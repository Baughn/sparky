using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core;

/// <summary>
/// Builds MNA topology from a voxel grid and components.
/// </summary>
/// <remarks>
/// The topology builder performs these steps:
/// 1. Find connected conductor regions by analyzing prism adjacency (not voxel flood-fill)
/// 2. Map component terminals to their connected nodes
/// 3. Create MNA components between terminal nodes
///
/// Supports incremental updates: if only a few blocks changed, only those regions
/// are rebuilt rather than the entire topology.
/// </remarks>
public class TopologyBuilder
{
    /// <summary>
    /// Represents a connected region of conductor prisms.
    /// All prisms in a region share the same MNA node.
    /// </summary>
    public class ConductorRegion
    {
        public NodeId NodeId { get; set; }
        public HashSet<VoxelPos> Voxels { get; } = new();
        internal List<(BlockPos Block, Prism Prism)> Prisms { get; } = new();

        /// <summary>
        /// Whether this region contains any resistive conductor prisms.
        /// </summary>
        public bool IsResistive { get; internal set; }

        /// <summary>
        /// Resistor IDs connecting this region to adjacent resistive regions.
        /// Used to query current through wires.
        /// </summary>
        public List<ResistorId> AdjacentResistors { get; } = new();
    }

    /// <summary>
    /// Default resistance per voxel face contact (ohms).
    /// </summary>
    public const double DefaultWireResistance = 0.01;

    // Persistent state for incremental updates
    private Dictionary<VoxelPos, ConductorRegion>? _cachedRegions;
    private readonly Dictionary<BlockPos, HashSet<ConductorRegion>> _blockToRegions = new();
    private readonly SpatialHash<(ConductorRegion Region, BlockPos Block, Prism Prism)> _prismIndex = new(16);
    private readonly Dictionary<(ConductorRegion, ConductorRegion), ResistorId> _regionPairResistors = new();
    private long _lastBuiltVersion = -1;

    /// <summary>
    /// Builds MNA topology from voxels and components.
    /// Uses incremental updates if only a few blocks changed.
    /// </summary>
    public Dictionary<VoxelPos, ConductorRegion> BuildTopology(
        VoxelGrid voxels,
        IEnumerable<Component> components,
        ISimulation sim)
    {
        var componentList = new List<Component>(components);

        // Check if we can skip rebuild (no changes since last build)
        // Use version number instead of dirty blocks (which can be cleared by prism access)
        if (_cachedRegions != null && voxels.Version == _lastBuiltVersion)
        {
            // No voxel changes - just update components if needed
            UpdateComponentsOnly(componentList, sim);
            return _cachedRegions;
        }

        // Check if we can do an incremental update
        // Use merge detection: if new prisms would connect multiple existing regions,
        // fall back to full rebuild (merge case is complex to handle incrementally)
        if (_cachedRegions != null && _lastBuiltVersion >= 0)
        {
            // Save dirty blocks before any operations that might clear them
            var dirtyBlocks = new HashSet<BlockPos>(voxels.DirtyBlocks);

            // IMPORTANT: Save old prisms BEFORE WouldMergeRegions triggers rebuild
            // GetCachedPrisms returns OLD prisms before any rebuild
            var oldPrismsByBlock = new Dictionary<BlockPos, IReadOnlyList<Prism>>();
            foreach (var block in dirtyBlocks)
            {
                oldPrismsByBlock[block] = voxels.GetCachedPrisms(block);
            }

            if (dirtyBlocks.Count > 0 && !WouldMergeRegions(voxels, dirtyBlocks))
            {
                return BuildTopologyIncremental(voxels, componentList, sim, dirtyBlocks, oldPrismsByBlock);
            }
        }

        // Full rebuild
        return BuildTopologyFull(voxels, componentList, sim);
    }

    /// <summary>
    /// Checks if the new prisms in dirty blocks would merge multiple existing regions.
    /// </summary>
    /// <remarks>
    /// Returns true if new prisms touch more than one existing region, indicating
    /// a merge that requires full rebuild to handle correctly.
    /// </remarks>
    private bool WouldMergeRegions(VoxelGrid voxels, HashSet<BlockPos> dirtyBlocks)
    {
        if (dirtyBlocks.Count == 0)
            return false;

        var touchedRegions = new HashSet<ConductorRegion>();

        foreach (var dirtyBlock in dirtyBlocks)
        {
            // Get new prisms in this block (triggers rebuild if needed)
            var newPrisms = voxels.GetPrismsInBlock(dirtyBlock);

            foreach (var prism in newPrisms)
            {
                // Only check NON-resistive conductor prisms for merges
                // Resistive prisms NEVER merge - each is its own region with resistors to neighbors
                // So a resistive prism touching multiple regions doesn't cause a merge
                if (prism.Type != VoxelType.Conductor)
                    continue;

                // Check boundary voxels of this prism for adjacent non-resistive regions
                CheckPrismBoundaryForNonResistiveRegions(dirtyBlock, prism, touchedRegions);

                // Early exit if we've found multiple non-resistive regions
                if (touchedRegions.Count > 1)
                    return true;
            }
        }

        return touchedRegions.Count > 1;
    }

    /// <summary>
    /// Checks the boundary faces of a prism for adjacent NON-resistive regions.
    /// Used for merge detection - only non-resistive regions can merge.
    /// </summary>
    private void CheckPrismBoundaryForNonResistiveRegions(
        BlockPos block,
        Prism prism,
        HashSet<ConductorRegion> touchedRegions)
    {
        var end = prism.End;

        // Check -X face
        if (prism.LocalX > 0 || block.X > int.MinValue)
        {
            int checkX = prism.LocalX - 1;
            var checkBlock = prism.LocalX == 0 ? block.Neighbor(BlockFacing.West) : block;
            int localX = prism.LocalX == 0 ? 15 : checkX;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int y = prism.LocalY; y < end.Y; y++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, localX, y, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region) && !region.IsResistive)
                        touchedRegions.Add(region);
                }
            }
        }

        // Check +X face
        {
            int checkX = end.X;
            var checkBlock = end.X == 16 ? block.Neighbor(BlockFacing.East) : block;
            int localX = end.X == 16 ? 0 : checkX;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int y = prism.LocalY; y < end.Y; y++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, localX, y, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region) && !region.IsResistive)
                        touchedRegions.Add(region);
                }
            }
        }

        // Check -Y face
        if (prism.LocalY > 0 || block.Y > int.MinValue)
        {
            int checkY = prism.LocalY - 1;
            var checkBlock = prism.LocalY == 0 ? block.Neighbor(BlockFacing.Down) : block;
            int localY = prism.LocalY == 0 ? 15 : checkY;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, localY, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region) && !region.IsResistive)
                        touchedRegions.Add(region);
                }
            }
        }

        // Check +Y face
        {
            int checkY = end.Y;
            var checkBlock = end.Y == 16 ? block.Neighbor(BlockFacing.Up) : block;
            int localY = end.Y == 16 ? 0 : checkY;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, localY, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region) && !region.IsResistive)
                        touchedRegions.Add(region);
                }
            }
        }

        // Check -Z face
        if (prism.LocalZ > 0 || block.Z > int.MinValue)
        {
            int checkZ = prism.LocalZ - 1;
            var checkBlock = prism.LocalZ == 0 ? block.Neighbor(BlockFacing.North) : block;
            int localZ = prism.LocalZ == 0 ? 15 : checkZ;

            for (int y = prism.LocalY; y < end.Y; y++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, y, localZ);
                    if (_cachedRegions!.TryGetValue(pos, out var region) && !region.IsResistive)
                        touchedRegions.Add(region);
                }
            }
        }

        // Check +Z face
        {
            int checkZ = end.Z;
            var checkBlock = end.Z == 16 ? block.Neighbor(BlockFacing.South) : block;
            int localZ = end.Z == 16 ? 0 : checkZ;

            for (int y = prism.LocalY; y < end.Y; y++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, y, localZ);
                    if (_cachedRegions!.TryGetValue(pos, out var region) && !region.IsResistive)
                        touchedRegions.Add(region);
                }
            }
        }
    }

    /// <summary>
    /// Checks the boundary faces of a prism for adjacent regions.
    /// </summary>
    private void CheckPrismBoundaryForRegions(
        BlockPos block,
        Prism prism,
        HashSet<ConductorRegion> touchedRegions)
    {
        var end = prism.End;

        // Check -X face (x = LocalX - 1)
        if (prism.LocalX > 0 || block.X > int.MinValue)
        {
            int checkX = prism.LocalX - 1;
            var checkBlock = prism.LocalX == 0 ? block.Neighbor(BlockFacing.West) : block;
            int localX = prism.LocalX == 0 ? 15 : checkX;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int y = prism.LocalY; y < end.Y; y++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, localX, y, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region))
                        touchedRegions.Add(region);
                }
            }
        }

        // Check +X face (x = end.X)
        {
            int checkX = end.X;
            var checkBlock = end.X == 16 ? block.Neighbor(BlockFacing.East) : block;
            int localX = end.X == 16 ? 0 : checkX;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int y = prism.LocalY; y < end.Y; y++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, localX, y, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region))
                        touchedRegions.Add(region);
                }
            }
        }

        // Check -Y face (y = LocalY - 1)
        if (prism.LocalY > 0 || block.Y > int.MinValue)
        {
            int checkY = prism.LocalY - 1;
            var checkBlock = prism.LocalY == 0 ? block.Neighbor(BlockFacing.Down) : block;
            int localY = prism.LocalY == 0 ? 15 : checkY;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, localY, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region))
                        touchedRegions.Add(region);
                }
            }
        }

        // Check +Y face (y = end.Y)
        {
            int checkY = end.Y;
            var checkBlock = end.Y == 16 ? block.Neighbor(BlockFacing.Up) : block;
            int localY = end.Y == 16 ? 0 : checkY;

            for (int z = prism.LocalZ; z < end.Z; z++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, localY, z);
                    if (_cachedRegions!.TryGetValue(pos, out var region))
                        touchedRegions.Add(region);
                }
            }
        }

        // Check -Z face (z = LocalZ - 1)
        if (prism.LocalZ > 0 || block.Z > int.MinValue)
        {
            int checkZ = prism.LocalZ - 1;
            var checkBlock = prism.LocalZ == 0 ? block.Neighbor(BlockFacing.North) : block;
            int localZ = prism.LocalZ == 0 ? 15 : checkZ;

            for (int y = prism.LocalY; y < end.Y; y++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, y, localZ);
                    if (_cachedRegions!.TryGetValue(pos, out var region))
                        touchedRegions.Add(region);
                }
            }
        }

        // Check +Z face (z = end.Z)
        {
            int checkZ = end.Z;
            var checkBlock = end.Z == 16 ? block.Neighbor(BlockFacing.South) : block;
            int localZ = end.Z == 16 ? 0 : checkZ;

            for (int y = prism.LocalY; y < end.Y; y++)
            {
                for (int x = prism.LocalX; x < end.X; x++)
                {
                    var pos = VoxelPos.FromBlockLocal(checkBlock, x, y, localZ);
                    if (_cachedRegions!.TryGetValue(pos, out var region))
                        touchedRegions.Add(region);
                }
            }
        }
    }

    /// <summary>
    /// Checks if two prisms have the same bounds (same position and size).
    /// </summary>
    private static bool PrismsMatch(Prism a, Prism b)
    {
        return a.LocalX == b.LocalX && a.LocalY == b.LocalY && a.LocalZ == b.LocalZ &&
               a.SizeX == b.SizeX && a.SizeY == b.SizeY && a.SizeZ == b.SizeZ &&
               a.Type == b.Type;
    }

    /// <summary>
    /// Extends an existing region with new voxels from dirty blocks.
    /// Used when adding voxels to an existing region that extends beyond the dirty area.
    /// </summary>
    private Dictionary<VoxelPos, ConductorRegion> ExtendExistingRegion(
        VoxelGrid voxels,
        List<Component> componentList,
        ISimulation sim,
        HashSet<BlockPos> dirtyBlocks,
        ConductorRegion existingRegion)
    {
        using var _ = sim.BeginBulkUpdate();

        // For each dirty block, find new voxels that need to be added
        foreach (var block in dirtyBlocks)
        {
            // Get old voxels (from cached prisms)
            var oldVoxels = new HashSet<VoxelPos>();
            foreach (var oldPrism in voxels.GetCachedPrisms(block))
            {
                if (oldPrism.Type != VoxelType.Conductor && oldPrism.Type != VoxelType.ResistiveConductor)
                    continue;
                var end = oldPrism.End;
                for (int z = oldPrism.LocalZ; z < end.Z; z++)
                    for (int y = oldPrism.LocalY; y < end.Y; y++)
                        for (int x = oldPrism.LocalX; x < end.X; x++)
                            oldVoxels.Add(VoxelPos.FromBlockLocal(block, x, y, z));
            }

            // Remove old prisms from indexes (they might have changed shape)
            foreach (var oldPrism in voxels.GetCachedPrisms(block))
            {
                _prismIndex.Remove((existingRegion, block, oldPrism));
            }

            // Also remove old prisms from region's prism list
            existingRegion.Prisms.RemoveAll(p => p.Block == block);

            // Add new prisms to region
            foreach (var newPrism in voxels.GetPrismsInBlock(block))
            {
                // Skip non-conductor prisms
                if (newPrism.Type != VoxelType.Conductor && newPrism.Type != VoxelType.ResistiveConductor)
                    continue;

                // Add prism to region
                existingRegion.Prisms.Add((block, newPrism));
                if (newPrism.Type == VoxelType.ResistiveConductor)
                    existingRegion.IsResistive = true;

                // Add new voxels to region (skip already-present ones)
                var end = newPrism.End;
                for (int z = newPrism.LocalZ; z < end.Z; z++)
                {
                    for (int y = newPrism.LocalY; y < end.Y; y++)
                    {
                        for (int x = newPrism.LocalX; x < end.X; x++)
                        {
                            var voxelPos = VoxelPos.FromBlockLocal(block, x, y, z);
                            if (!oldVoxels.Contains(voxelPos))
                            {
                                existingRegion.Voxels.Add(voxelPos);
                                _cachedRegions![voxelPos] = existingRegion;
                            }
                        }
                    }
                }

                // Add to prism index
                var (min, max) = GetPrismWorldBounds(block, newPrism);
                _prismIndex.Add((existingRegion, block, newPrism), min, max);
            }

            // Update block-to-regions index
            if (!_blockToRegions.TryGetValue(block, out var regionsInBlock))
            {
                regionsInBlock = new HashSet<ConductorRegion>();
                _blockToRegions[block] = regionsInBlock;
            }
            regionsInBlock.Add(existingRegion);
        }

        // Update components
        foreach (var component in componentList)
        {
            component.RemoveMnaComponents(sim);

            var terminalNodes = new Dictionary<string, NodeId>();
            foreach (var terminal in component.Terminals)
            {
                NodeId? nodeId = null;
                foreach (var voxel in terminal.Voxels)
                {
                    if (_cachedRegions!.TryGetValue(voxel, out var region))
                    {
                        nodeId = region.NodeId;
                        break;
                    }
                }

                if (!nodeId.HasValue)
                {
                    nodeId = sim.CreateNode();
                }

                terminalNodes[terminal.Name] = nodeId.Value;
            }

            component.CreateMnaComponents(sim, terminalNodes);
        }

        _lastBuiltVersion = voxels.Version;
        return _cachedRegions!;
    }

    /// <summary>
    /// Creates resistors between a new prism and adjacent prisms in OTHER regions.
    /// </summary>
    private void CreateResistorsForNewPrism(
        BlockPos block,
        Prism prism,
        ConductorRegion region,
        ISimulation sim)
    {
        var (min, max) = GetPrismWorldBounds(block, prism);

        // Query for adjacent prisms
        var expandedMin = new VoxelPos(min.X - 1, min.Y - 1, min.Z - 1);
        var expandedMax = new VoxelPos(max.X + 1, max.Y + 1, max.Z + 1);

        // Track contact areas per other region
        var otherRegionContacts = new Dictionary<ConductorRegion, int>();

        foreach (var (otherRegion, otherBlock, otherPrism) in _prismIndex.QueryDistinct(expandedMin, expandedMax))
        {
            // Skip same region (no resistors within a region)
            if (otherRegion == region)
                continue;

            // Check if prisms are actually adjacent
            var contactArea = CalculateContactArea(prism, otherPrism, block, otherBlock);
            if (contactArea <= 0)
                continue;

            if (!otherRegionContacts.TryGetValue(otherRegion, out var existing))
                existing = 0;
            otherRegionContacts[otherRegion] = existing + contactArea;
        }

        // Create resistors to other regions
        foreach (var (otherRegion, contactArea) in otherRegionContacts)
        {
            // Skip if both regions are non-resistive (pure conductors = same node)
            if (!region.IsResistive && !otherRegion.IsResistive)
                continue;

            var pair = region.GetHashCode() < otherRegion.GetHashCode()
                ? (region, otherRegion)
                : (otherRegion, region);

            // Skip if resistor already exists
            if (_regionPairResistors.ContainsKey(pair))
                continue;

            var resistance = DefaultWireResistance / contactArea;
            var resistorId = sim.AddResistor(region.NodeId, otherRegion.NodeId, resistance);
            _regionPairResistors[pair] = resistorId;
            region.AdjacentResistors.Add(resistorId);
            otherRegion.AdjacentResistors.Add(resistorId);
        }
    }

    /// <summary>
    /// Updates only the MNA components without rebuilding topology.
    /// Used when voxels haven't changed but components might have.
    /// </summary>
    private void UpdateComponentsOnly(List<Component> componentList, ISimulation sim)
    {
        using var _ = sim.BeginBulkUpdate();

        foreach (var component in componentList)
        {
            component.RemoveMnaComponents(sim);

            var terminalNodes = new Dictionary<string, NodeId>();
            foreach (var terminal in component.Terminals)
            {
                NodeId? nodeId = null;
                foreach (var voxel in terminal.Voxels)
                {
                    if (_cachedRegions!.TryGetValue(voxel, out var region))
                    {
                        nodeId = region.NodeId;
                        break;
                    }
                }

                if (!nodeId.HasValue)
                {
                    nodeId = sim.CreateNode();
                }

                terminalNodes[terminal.Name] = nodeId.Value;
            }

            component.CreateMnaComponents(sim, terminalNodes);
        }
    }

    /// <summary>
    /// Performs a full topology rebuild from scratch.
    /// </summary>
    private Dictionary<VoxelPos, ConductorRegion> BuildTopologyFull(
        VoxelGrid voxels,
        List<Component> componentList,
        ISimulation sim)
    {
        using var _ = sim.BeginBulkUpdate();

        // Clear persistent state
        _blockToRegions.Clear();
        _prismIndex.Clear();
        _regionPairResistors.Clear();

        // Step 1: Find all connected conductor regions via prism adjacency
        var regions = FindConductorRegions(voxels);

        // Build block-to-regions index
        foreach (var region in GetUniqueRegions(regions))
        {
            foreach (var (block, prism) in region.Prisms)
            {
                if (!_blockToRegions.TryGetValue(block, out var regionsInBlock))
                {
                    regionsInBlock = new HashSet<ConductorRegion>();
                    _blockToRegions[block] = regionsInBlock;
                }
                regionsInBlock.Add(region);
            }
        }

        // Step 2: Create MNA nodes for each region
        var groundRegions = new HashSet<ConductorRegion>();

        foreach (var component in componentList)
        {
            if (component.Type == ComponentType.Ground)
            {
                foreach (var terminal in component.Terminals)
                {
                    foreach (var voxel in terminal.Voxels)
                    {
                        if (regions.TryGetValue(voxel, out var region))
                        {
                            groundRegions.Add(region);
                        }
                    }
                }
            }
        }

        // Assign nodes to regions
        foreach (var region in GetUniqueRegions(regions))
        {
            if (groundRegions.Contains(region))
            {
                region.NodeId = sim.Ground;
            }
            else
            {
                region.NodeId = sim.CreateNode();
            }
        }

        // Step 2.5: Create resistors between adjacent resistive regions
        CreateInterRegionResistorsFull(voxels, regions, sim);

        // Step 3: Create MNA components
        foreach (var component in componentList)
        {
            component.RemoveMnaComponents(sim);

            var terminalNodes = new Dictionary<string, NodeId>();
            foreach (var terminal in component.Terminals)
            {
                NodeId? nodeId = null;
                foreach (var voxel in terminal.Voxels)
                {
                    if (regions.TryGetValue(voxel, out var region))
                    {
                        nodeId = region.NodeId;
                        break;
                    }
                }

                if (!nodeId.HasValue)
                {
                    nodeId = sim.CreateNode();
                }

                terminalNodes[terminal.Name] = nodeId.Value;
            }

            component.CreateMnaComponents(sim, terminalNodes);
        }

        // Cache results
        _cachedRegions = regions;
        _lastBuiltVersion = voxels.Version;

        return regions;
    }

    /// <summary>
    /// Performs an incremental topology update for changed blocks only.
    /// </summary>
    /// <remarks>
    /// Uses merge detection to ensure correctness: only called when new prisms
    /// don't bridge multiple existing regions.
    /// </remarks>
    private Dictionary<VoxelPos, ConductorRegion> BuildTopologyIncremental(
        VoxelGrid voxels,
        List<Component> componentList,
        ISimulation sim,
        HashSet<BlockPos> dirtyBlocks,
        Dictionary<BlockPos, IReadOnlyList<Prism>> oldPrismsByBlock)
    {
        using var _ = sim.BeginBulkUpdate();

        // Expand to include neighbors (for cross-block connections)
        var expandedDirty = new HashSet<BlockPos>(dirtyBlocks);
        foreach (var block in dirtyBlocks)
        {
            foreach (var dir in BlockFacingExtensions.All)
            {
                expandedDirty.Add(block.Neighbor(dir));
            }
        }

        // Find all regions affected by these blocks
        var affectedRegions = new HashSet<ConductorRegion>();
        foreach (var block in expandedDirty)
        {
            if (_blockToRegions.TryGetValue(block, out var regionsInBlock))
            {
                foreach (var region in regionsInBlock)
                {
                    affectedRegions.Add(region);
                }
            }
        }

        // Check if any affected region extends beyond the dirty area
        // If so, we need to be careful:
        // - If only ADDING voxels to one region, we can extend it without full rebuild
        // - If potentially SPLITTING a region (removals), we need full rebuild
        bool regionExtendsBeyondDirty = false;
        foreach (var region in affectedRegions)
        {
            foreach (var (block, _) in region.Prisms)
            {
                if (!expandedDirty.Contains(block))
                {
                    regionExtendsBeyondDirty = true;
                    break;
                }
            }
            if (regionExtendsBeyondDirty) break;
        }

        if (regionExtendsBeyondDirty)
        {
            // Check if we're only adding (not removing voxels)
            // Note: prism shapes might change due to greedy meshing, but
            // as long as no voxels are removed, we can safely extend
            bool onlyAdding = true;
            foreach (var block in dirtyBlocks)
            {
                // Get old voxels from saved prisms (captured before rebuild)
                var oldVoxels = new HashSet<VoxelPos>();
                if (oldPrismsByBlock.TryGetValue(block, out var oldPrisms))
                {
                    foreach (var oldPrism in oldPrisms)
                    {
                        if (oldPrism.Type != VoxelType.Conductor && oldPrism.Type != VoxelType.ResistiveConductor)
                            continue;
                        var end = oldPrism.End;
                        for (int z = oldPrism.LocalZ; z < end.Z; z++)
                            for (int y = oldPrism.LocalY; y < end.Y; y++)
                                for (int x = oldPrism.LocalX; x < end.X; x++)
                                    oldVoxels.Add(VoxelPos.FromBlockLocal(block, x, y, z));
                    }
                }

                // Get new voxels (already rebuilt by WouldMergeRegions)
                var newVoxels = new HashSet<VoxelPos>();
                foreach (var newPrism in voxels.GetPrismsInBlock(block))
                {
                    if (newPrism.Type != VoxelType.Conductor && newPrism.Type != VoxelType.ResistiveConductor)
                        continue;
                    var end = newPrism.End;
                    for (int z = newPrism.LocalZ; z < end.Z; z++)
                        for (int y = newPrism.LocalY; y < end.Y; y++)
                            for (int x = newPrism.LocalX; x < end.X; x++)
                                newVoxels.Add(VoxelPos.FromBlockLocal(block, x, y, z));
                }

                // Check if any old voxels were removed
                foreach (var oldVoxel in oldVoxels)
                {
                    if (!newVoxels.Contains(oldVoxel))
                    {
                        onlyAdding = false;
                        break;
                    }
                }
                if (!onlyAdding) break;
            }

            if (onlyAdding && affectedRegions.Count == 1)
            {
                // Safe to extend the existing region without full rebuild
                return ExtendExistingRegion(voxels, componentList, sim, dirtyBlocks, affectedRegions.First());
            }

            // Not safe - fall back to full rebuild
            return BuildTopologyFull(voxels, componentList, sim);
        }

        // First, remove components that reference nodes we're about to delete
        // This must happen BEFORE removing nodes to avoid NodeInUseException
        foreach (var component in componentList)
        {
            component.RemoveMnaComponents(sim);
        }

        // Remove old resistors for affected regions
        var resistorsToRemove = new List<(ConductorRegion, ConductorRegion)>();
        foreach (var (pair, resistorId) in _regionPairResistors)
        {
            if (affectedRegions.Contains(pair.Item1) || affectedRegions.Contains(pair.Item2))
            {
                sim.RemoveResistor(resistorId);
                // Clean up AdjacentResistors on BOTH regions (including non-affected ones)
                // to prevent stale resistor IDs from causing InvalidComponentException
                pair.Item1.AdjacentResistors.Remove(resistorId);
                pair.Item2.AdjacentResistors.Remove(resistorId);
                resistorsToRemove.Add(pair);
            }
        }
        foreach (var pair in resistorsToRemove)
        {
            _regionPairResistors.Remove(pair);
        }

        // Remove affected regions from indexes
        foreach (var region in affectedRegions)
        {
            // Remove from voxel map
            foreach (var voxel in region.Voxels)
            {
                _cachedRegions!.Remove(voxel);
            }

            // Remove from block map
            foreach (var (block, _) in region.Prisms)
            {
                if (_blockToRegions.TryGetValue(block, out var regionsInBlock))
                {
                    regionsInBlock.Remove(region);
                }
            }

            // Remove from prism index
            foreach (var (block, prism) in region.Prisms)
            {
                _prismIndex.Remove((region, block, prism));
            }

            // Remove node from simulation
            if (region.NodeId != sim.Ground)
            {
                sim.RemoveNode(region.NodeId);
            }
        }

        // Collect all prisms in expanded dirty blocks for re-union
        var prismsToRebuild = new List<(BlockPos Block, Prism Prism, bool IsResistive)>();
        foreach (var block in expandedDirty)
        {
            // Trigger prism rebuild for dirty blocks
            var (_, newPrisms) = voxels.RebuildBlockIncremental(block);
            foreach (var prism in newPrisms)
            {
                if (prism.Type == VoxelType.Conductor)
                {
                    prismsToRebuild.Add((block, prism, false));
                }
                else if (prism.Type == VoxelType.ResistiveConductor)
                {
                    prismsToRebuild.Add((block, prism, true));
                }
            }
        }

        // Re-run union-find on the affected prisms
        var newRegions = BuildRegionsFromPrisms(prismsToRebuild);

        // Determine ground regions
        var groundRegions = new HashSet<ConductorRegion>();
        foreach (var component in componentList)
        {
            if (component.Type == ComponentType.Ground)
            {
                foreach (var terminal in component.Terminals)
                {
                    foreach (var voxel in terminal.Voxels)
                    {
                        // Check if this voxel is in a new region
                        foreach (var region in newRegions.Values)
                        {
                            if (region.Voxels.Contains(voxel))
                            {
                                groundRegions.Add(region);
                            }
                        }
                    }
                }
            }
        }

        // Assign nodes to new regions and add to indexes
        foreach (var region in GetUniqueRegions(newRegions))
        {
            if (groundRegions.Contains(region))
            {
                region.NodeId = sim.Ground;
            }
            else
            {
                region.NodeId = sim.CreateNode();
            }

            // Add to voxel map
            foreach (var voxel in region.Voxels)
            {
                _cachedRegions![voxel] = region;
            }

            // Add to block map
            foreach (var (block, prism) in region.Prisms)
            {
                if (!_blockToRegions.TryGetValue(block, out var regionsInBlock))
                {
                    regionsInBlock = new HashSet<ConductorRegion>();
                    _blockToRegions[block] = regionsInBlock;
                }
                regionsInBlock.Add(region);

                // Add to prism index
                var (min, max) = GetPrismWorldBounds(block, prism);
                _prismIndex.Add((region, block, prism), min, max);
            }
        }

        // Create resistors for new adjacent region pairs
        CreateInterRegionResistorsIncremental(newRegions, sim);

        // Update components
        UpdateComponentsOnly(componentList, sim);

        // Update version
        _lastBuiltVersion = voxels.Version;

        return _cachedRegions!;
    }

    /// <summary>
    /// Builds regions from a list of prisms using union-find.
    /// </summary>
    private Dictionary<VoxelPos, ConductorRegion> BuildRegionsFromPrisms(
        List<(BlockPos Block, Prism Prism, bool IsResistive)> allPrisms)
    {
        if (allPrisms.Count == 0)
            return new Dictionary<VoxelPos, ConductorRegion>();

        // Union-find to group connected prisms
        var parent = new int[allPrisms.Count];
        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        int Find(int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(int x, int y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py)
                parent[px] = py;
        }

        bool ShouldUnion(int i, int j)
        {
            return !allPrisms[i].IsResistive && !allPrisms[j].IsResistive;
        }

        // Group by block for efficient adjacency checking
        var prismsByBlock = new Dictionary<BlockPos, List<int>>();
        for (int i = 0; i < allPrisms.Count; i++)
        {
            var block = allPrisms[i].Block;
            if (!prismsByBlock.TryGetValue(block, out var list))
            {
                list = new List<int>();
                prismsByBlock[block] = list;
            }
            list.Add(i);
        }

        // Check within-block adjacency
        foreach (var (_, indices) in prismsByBlock)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    var pi = allPrisms[indices[i]].Prism;
                    var pj = allPrisms[indices[j]].Prism;
                    if (PrismsTouch(pi, pj) && ShouldUnion(indices[i], indices[j]))
                    {
                        Union(indices[i], indices[j]);
                    }
                }
            }
        }

        // Check cross-block adjacency
        foreach (var (block, indices) in prismsByBlock)
        {
            foreach (var dir in BlockFacingExtensions.All)
            {
                var neighborBlock = block.Neighbor(dir);
                if (!prismsByBlock.TryGetValue(neighborBlock, out var neighborIndices))
                    continue;

                foreach (var i in indices)
                {
                    var prismA = allPrisms[i].Prism;
                    if (!PrismAtBlockBoundary(prismA, dir))
                        continue;

                    foreach (var j in neighborIndices)
                    {
                        var prismB = allPrisms[j].Prism;
                        if (PrismsConnectAcrossBlocks(prismA, prismB, dir) && ShouldUnion(i, j))
                        {
                            Union(i, j);
                        }
                    }
                }
            }
        }

        // Build regions from union-find result
        var regionsByRoot = new Dictionary<int, ConductorRegion>();
        for (int i = 0; i < allPrisms.Count; i++)
        {
            var root = Find(i);
            if (!regionsByRoot.TryGetValue(root, out var region))
            {
                region = new ConductorRegion();
                regionsByRoot[root] = region;
            }
            region.Prisms.Add((allPrisms[i].Block, allPrisms[i].Prism));
            if (allPrisms[i].IsResistive)
            {
                region.IsResistive = true;
            }
        }

        // Build voxel-to-region map
        var result = new Dictionary<VoxelPos, ConductorRegion>();
        foreach (var region in regionsByRoot.Values)
        {
            foreach (var (block, prism) in region.Prisms)
            {
                var end = prism.End;
                for (int z = prism.LocalZ; z < end.Z; z++)
                {
                    for (int y = prism.LocalY; y < end.Y; y++)
                    {
                        for (int x = prism.LocalX; x < end.X; x++)
                        {
                            var voxelPos = VoxelPos.FromBlockLocal(block, x, y, z);
                            result[voxelPos] = region;
                            region.Voxels.Add(voxelPos);
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Creates resistors for new regions (incremental update).
    /// </summary>
    private void CreateInterRegionResistorsIncremental(
        Dictionary<VoxelPos, ConductorRegion> newRegions,
        ISimulation sim,
        double resistancePerFace = DefaultWireResistance)
    {
        var uniqueNewRegions = new HashSet<ConductorRegion>(newRegions.Values);
        if (uniqueNewRegions.Count == 0)
            return;

        // Add new prisms to spatial index
        foreach (var region in uniqueNewRegions)
        {
            foreach (var (block, prism) in region.Prisms)
            {
                var (min, max) = GetPrismWorldBounds(block, prism);
                try
                {
                    _prismIndex.Add((region, block, prism), min, max);
                }
                catch (ArgumentException)
                {
                    // Already added during region building
                }
            }
        }

        // Find adjacent regions (including existing ones) for new regions
        var pairContactAreas = new Dictionary<(ConductorRegion, ConductorRegion), int>();

        (ConductorRegion, ConductorRegion) OrderPair(ConductorRegion a, ConductorRegion b)
        {
            return a.GetHashCode() <= b.GetHashCode() ? (a, b) : (b, a);
        }

        foreach (var region in uniqueNewRegions)
        {
            foreach (var (block, prism) in region.Prisms)
            {
                var (min, max) = GetPrismWorldBounds(block, prism);
                var expandedMin = new VoxelPos(min.X - 1, min.Y - 1, min.Z - 1);
                var expandedMax = new VoxelPos(max.X + 1, max.Y + 1, max.Z + 1);

                foreach (var (otherRegion, otherBlock, otherPrism) in _prismIndex.QueryDistinct(expandedMin, expandedMax))
                {
                    if (otherRegion == region)
                        continue;

                    if (!region.IsResistive && !otherRegion.IsResistive)
                        continue;

                    // Skip if already have a resistor
                    var pair = OrderPair(region, otherRegion);
                    if (_regionPairResistors.ContainsKey(pair))
                        continue;

                    var area = CalculateContactArea(prism, otherPrism, block, otherBlock);
                    if (area > 0)
                    {
                        if (!pairContactAreas.TryGetValue(pair, out var existing))
                            existing = 0;
                        pairContactAreas[pair] = existing + area;
                    }
                }
            }
        }

        // Create resistors
        foreach (var ((regionA, regionB), totalContactArea) in pairContactAreas)
        {
            // When both regions are new, contact area is counted twice (once from each direction).
            // When one region is new and the other is existing, it's only counted once.
            // Divide by 2 only when both are new to get the actual contact area.
            var bothNew = uniqueNewRegions.Contains(regionA) && uniqueNewRegions.Contains(regionB);
            var actualArea = bothNew ? totalContactArea / 2 : totalContactArea;

            if (actualArea > 0)
            {
                var resistance = resistancePerFace / actualArea;
                var resistorId = sim.AddResistor(regionA.NodeId, regionB.NodeId, resistance);

                regionA.AdjacentResistors.Add(resistorId);
                regionB.AdjacentResistors.Add(resistorId);
                _regionPairResistors[(regionA, regionB)] = resistorId;
            }
        }
    }

    /// <summary>
    /// Finds all connected conductor regions using prism adjacency.
    /// </summary>
    /// <remarks>
    /// Union rules:
    /// - Conductor + Conductor: merge (equipotential)
    /// - Conductor + ResistiveConductor: merge (wire connects to terminal)
    /// - ResistiveConductor + ResistiveConductor: separate (resistor between them)
    /// </remarks>
    public Dictionary<VoxelPos, ConductorRegion> FindConductorRegions(VoxelGrid grid)
    {
        // Collect all conductor prisms (both pure and resistive) with their block positions
        var allPrisms = new List<(BlockPos Block, Prism Prism, bool IsResistive)>();
        foreach (var (block, prism) in grid.GetAllPrisms())
        {
            if (prism.Type == VoxelType.Conductor)
            {
                allPrisms.Add((block, prism, false));
            }
            else if (prism.Type == VoxelType.ResistiveConductor)
            {
                allPrisms.Add((block, prism, true));
            }
        }

        if (allPrisms.Count == 0)
            return new Dictionary<VoxelPos, ConductorRegion>();

        // Union-find to group connected prisms
        var parent = new int[allPrisms.Count];
        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        int Find(int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(int x, int y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py)
                parent[px] = py;
        }

        // Should these two prisms be unioned? Only if NEITHER is resistive.
        bool ShouldUnion(int i, int j)
        {
            // Resistive prisms never merge - each gets its own node with resistors to neighbors
            return !allPrisms[i].IsResistive && !allPrisms[j].IsResistive;
        }

        // Check adjacency between all pairs of prisms
        // Optimization: group by block first, then check within-block and cross-block
        var prismsByBlock = new Dictionary<BlockPos, List<int>>();
        for (int i = 0; i < allPrisms.Count; i++)
        {
            var block = allPrisms[i].Block;
            if (!prismsByBlock.TryGetValue(block, out var list))
            {
                list = new List<int>();
                prismsByBlock[block] = list;
            }
            list.Add(i);
        }

        // Check within-block adjacency
        foreach (var (_, indices) in prismsByBlock)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    var pi = allPrisms[indices[i]].Prism;
                    var pj = allPrisms[indices[j]].Prism;
                    if (PrismsTouch(pi, pj) && ShouldUnion(indices[i], indices[j]))
                    {
                        Union(indices[i], indices[j]);
                    }
                }
            }
        }

        // Check cross-block adjacency (prisms at block boundaries)
        foreach (var (block, indices) in prismsByBlock)
        {
            // Check each of 6 neighbor blocks
            foreach (var dir in BlockFacingExtensions.All)
            {
                var neighborBlock = block.Neighbor(dir);
                if (!prismsByBlock.TryGetValue(neighborBlock, out var neighborIndices))
                    continue;

                foreach (var i in indices)
                {
                    var prismA = allPrisms[i].Prism;
                    if (!PrismAtBlockBoundary(prismA, dir))
                        continue;

                    foreach (var j in neighborIndices)
                    {
                        var prismB = allPrisms[j].Prism;
                        if (PrismsConnectAcrossBlocks(prismA, prismB, dir) && ShouldUnion(i, j))
                        {
                            Union(i, j);
                        }
                    }
                }
            }
        }

        // Build regions from union-find result
        var regionsByRoot = new Dictionary<int, ConductorRegion>();
        for (int i = 0; i < allPrisms.Count; i++)
        {
            var root = Find(i);
            if (!regionsByRoot.TryGetValue(root, out var region))
            {
                region = new ConductorRegion();
                regionsByRoot[root] = region;
            }
            region.Prisms.Add((allPrisms[i].Block, allPrisms[i].Prism));
            if (allPrisms[i].IsResistive)
            {
                region.IsResistive = true;
            }
        }

        // Build voxel-to-region map by expanding prisms
        var result = new Dictionary<VoxelPos, ConductorRegion>();
        foreach (var region in regionsByRoot.Values)
        {
            foreach (var (block, prism) in region.Prisms)
            {
                var end = prism.End;
                for (int z = prism.LocalZ; z < end.Z; z++)
                {
                    for (int y = prism.LocalY; y < end.Y; y++)
                    {
                        for (int x = prism.LocalX; x < end.X; x++)
                        {
                            var voxelPos = VoxelPos.FromBlockLocal(block, x, y, z);
                            result[voxelPos] = region;
                            region.Voxels.Add(voxelPos);
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if two prisms in the same block touch (share a face).
    /// </summary>
    private static bool PrismsTouch(Prism a, Prism b)
    {
        var aEnd = a.End;
        var bEnd = b.End;

        // Check if they overlap in 2 dimensions and are adjacent in the third

        // Adjacent in X?
        if ((a.LocalX == bEnd.X || aEnd.X == b.LocalX) &&
            RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y) &&
            RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z))
            return true;

        // Adjacent in Y?
        if ((a.LocalY == bEnd.Y || aEnd.Y == b.LocalY) &&
            RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
            RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z))
            return true;

        // Adjacent in Z?
        if ((a.LocalZ == bEnd.Z || aEnd.Z == b.LocalZ) &&
            RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
            RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if prism is at the block boundary in the given direction.
    /// </summary>
    private static bool PrismAtBlockBoundary(Prism p, BlockFacing facing)
    {
        return facing switch
        {
            BlockFacing.West => p.LocalX == 0,           // -X
            BlockFacing.East => p.LocalX + p.SizeX == 16, // +X
            BlockFacing.Down => p.LocalY == 0,           // -Y
            BlockFacing.Up => p.LocalY + p.SizeY == 16,   // +Y
            BlockFacing.North => p.LocalZ == 0,          // -Z
            BlockFacing.South => p.LocalZ + p.SizeZ == 16, // +Z
            _ => false
        };
    }

    /// <summary>
    /// Checks if two prisms connect across a block boundary.
    /// prismA is in the block, prismB is in the neighbor block in direction facing.
    /// </summary>
    private static bool PrismsConnectAcrossBlocks(Prism a, Prism b, BlockFacing facing)
    {
        var aEnd = a.End;
        var bEnd = b.End;

        return facing switch
        {
            // A is at +X boundary, B is at -X boundary (local X = 0)
            BlockFacing.East => aEnd.X == 16 && b.LocalX == 0 &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.West => a.LocalX == 0 && bEnd.X == 16 &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.Up => aEnd.Y == 16 && b.LocalY == 0 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.Down => a.LocalY == 0 && bEnd.Y == 16 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.South => aEnd.Z == 16 && b.LocalZ == 0 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y),

            BlockFacing.North => a.LocalZ == 0 && bEnd.Z == 16 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y),

            _ => false
        };
    }

    /// <summary>
    /// Checks if two ranges [a1, a2) and [b1, b2) overlap.
    /// </summary>
    private static bool RangesOverlap(int a1, int a2, int b1, int b2)
    {
        return a1 < b2 && b1 < a2;
    }

    /// <summary>
    /// Calculates the overlap size of two ranges [a1, a2) and [b1, b2).
    /// Returns 0 if no overlap.
    /// </summary>
    private static int RangeOverlapSize(int a1, int a2, int b1, int b2)
    {
        var overlapStart = Math.Max(a1, b1);
        var overlapEnd = Math.Min(a2, b2);
        return Math.Max(0, overlapEnd - overlapStart);
    }

    /// <summary>
    /// Calculates the contact area (number of voxel faces) between two touching prisms.
    /// Returns 0 if they don't touch.
    /// </summary>
    private static int CalculateContactArea(Prism a, Prism b, BlockPos blockA, BlockPos blockB)
    {
        var aEnd = a.End;
        var bEnd = b.End;

        // Same block - check within-block adjacency
        if (blockA == blockB)
        {
            // Adjacent in X?
            if (a.LocalX == bEnd.X || aEnd.X == b.LocalX)
            {
                int overlapY = RangeOverlapSize(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y);
                int overlapZ = RangeOverlapSize(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z);
                if (overlapY > 0 && overlapZ > 0)
                    return overlapY * overlapZ;
            }

            // Adjacent in Y?
            if (a.LocalY == bEnd.Y || aEnd.Y == b.LocalY)
            {
                int overlapX = RangeOverlapSize(a.LocalX, aEnd.X, b.LocalX, bEnd.X);
                int overlapZ = RangeOverlapSize(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z);
                if (overlapX > 0 && overlapZ > 0)
                    return overlapX * overlapZ;
            }

            // Adjacent in Z?
            if (a.LocalZ == bEnd.Z || aEnd.Z == b.LocalZ)
            {
                int overlapX = RangeOverlapSize(a.LocalX, aEnd.X, b.LocalX, bEnd.X);
                int overlapY = RangeOverlapSize(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y);
                if (overlapX > 0 && overlapY > 0)
                    return overlapX * overlapY;
            }

            return 0;
        }

        // Different blocks - check cross-block adjacency
        // Determine which direction blockB is from blockA
        var dx = blockB.X - blockA.X;
        var dy = blockB.Y - blockA.Y;
        var dz = blockB.Z - blockA.Z;

        // Must be exactly adjacent in one direction
        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) != 1)
            return 0;

        if (dx == 1 && aEnd.X == 16 && b.LocalX == 0)
        {
            int overlapY = RangeOverlapSize(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y);
            int overlapZ = RangeOverlapSize(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z);
            return overlapY * overlapZ;
        }
        if (dx == -1 && a.LocalX == 0 && bEnd.X == 16)
        {
            int overlapY = RangeOverlapSize(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y);
            int overlapZ = RangeOverlapSize(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z);
            return overlapY * overlapZ;
        }
        if (dy == 1 && aEnd.Y == 16 && b.LocalY == 0)
        {
            int overlapX = RangeOverlapSize(a.LocalX, aEnd.X, b.LocalX, bEnd.X);
            int overlapZ = RangeOverlapSize(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z);
            return overlapX * overlapZ;
        }
        if (dy == -1 && a.LocalY == 0 && bEnd.Y == 16)
        {
            int overlapX = RangeOverlapSize(a.LocalX, aEnd.X, b.LocalX, bEnd.X);
            int overlapZ = RangeOverlapSize(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z);
            return overlapX * overlapZ;
        }
        if (dz == 1 && aEnd.Z == 16 && b.LocalZ == 0)
        {
            int overlapX = RangeOverlapSize(a.LocalX, aEnd.X, b.LocalX, bEnd.X);
            int overlapY = RangeOverlapSize(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y);
            return overlapX * overlapY;
        }
        if (dz == -1 && a.LocalZ == 0 && bEnd.Z == 16)
        {
            int overlapX = RangeOverlapSize(a.LocalX, aEnd.X, b.LocalX, bEnd.X);
            int overlapY = RangeOverlapSize(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y);
            return overlapX * overlapY;
        }

        return 0;
    }

    /// <summary>
    /// Creates resistors between adjacent resistive conductor regions.
    /// Uses spatial indexing for O(n) complexity instead of O(n²).
    /// </summary>
    /// <param name="grid">The voxel grid (needed to check prism types).</param>
    /// <param name="regions">The conductor regions map.</param>
    /// <param name="sim">The simulation to add resistors to.</param>
    /// <param name="resistancePerFace">Resistance per voxel face contact.</param>
    private void CreateInterRegionResistorsFull(
        VoxelGrid grid,
        Dictionary<VoxelPos, ConductorRegion> regions,
        ISimulation sim,
        double resistancePerFace = DefaultWireResistance)
    {
        // Get all unique regions
        var uniqueRegions = new HashSet<ConductorRegion>(regions.Values);
        if (uniqueRegions.Count == 0)
            return;

        // Build spatial index of all prisms for O(1) neighbor queries
        // Store in persistent _prismIndex for incremental updates
        foreach (var region in uniqueRegions)
        {
            foreach (var (block, prism) in region.Prisms)
            {
                var (min, max) = GetPrismWorldBounds(block, prism);
                _prismIndex.Add((region, block, prism), min, max);
            }
        }

        // Accumulate contact area per region pair
        // Key: ordered pair (smaller hash first) to avoid duplicates
        var pairContactAreas = new Dictionary<(ConductorRegion, ConductorRegion), int>();

        (ConductorRegion, ConductorRegion) OrderPair(ConductorRegion a, ConductorRegion b)
        {
            return a.GetHashCode() <= b.GetHashCode() ? (a, b) : (b, a);
        }

        // For each prism, query nearby prisms and check adjacency
        foreach (var region in uniqueRegions)
        {
            foreach (var (block, prism) in region.Prisms)
            {
                // Expand bounds by 1 voxel to find touching prisms
                var (min, max) = GetPrismWorldBounds(block, prism);
                var expandedMin = new VoxelPos(min.X - 1, min.Y - 1, min.Z - 1);
                var expandedMax = new VoxelPos(max.X + 1, max.Y + 1, max.Z + 1);

                foreach (var (otherRegion, otherBlock, otherPrism) in _prismIndex.QueryDistinct(expandedMin, expandedMax))
                {
                    // Skip same region
                    if (otherRegion == region)
                        continue;

                    // At least one region must be resistive
                    if (!region.IsResistive && !otherRegion.IsResistive)
                        continue;

                    // Calculate contact area
                    var area = CalculateContactArea(prism, otherPrism, block, otherBlock);
                    if (area > 0)
                    {
                        var pair = OrderPair(region, otherRegion);
                        if (!pairContactAreas.TryGetValue(pair, out var existing))
                            existing = 0;
                        pairContactAreas[pair] = existing + area;
                    }
                }
            }
        }

        // Create resistors for each adjacent region pair
        // Note: contact areas are counted twice (once from each side), so divide by 2
        foreach (var ((regionA, regionB), totalContactArea) in pairContactAreas)
        {
            var actualArea = totalContactArea / 2; // Each contact counted from both sides
            if (actualArea > 0)
            {
                var resistance = resistancePerFace / actualArea;
                var resistorId = sim.AddResistor(regionA.NodeId, regionB.NodeId, resistance);

                regionA.AdjacentResistors.Add(resistorId);
                regionB.AdjacentResistors.Add(resistorId);

                // Store for incremental updates
                _regionPairResistors[(regionA, regionB)] = resistorId;
            }
        }
    }

    private static IEnumerable<ConductorRegion> GetUniqueRegions(
        Dictionary<VoxelPos, ConductorRegion> regions)
    {
        var seen = new HashSet<ConductorRegion>();
        foreach (var region in regions.Values)
        {
            if (seen.Add(region))
            {
                yield return region;
            }
        }
    }

    /// <summary>
    /// Gets the world-space AABB bounds of a prism.
    /// </summary>
    private static (VoxelPos Min, VoxelPos Max) GetPrismWorldBounds(BlockPos block, Prism prism)
    {
        var min = VoxelPos.FromBlockLocal(block, prism.LocalX, prism.LocalY, prism.LocalZ);
        var max = new VoxelPos(
            min.X + prism.SizeX - 1,
            min.Y + prism.SizeY - 1,
            min.Z + prism.SizeZ - 1);
        return (min, max);
    }
}
