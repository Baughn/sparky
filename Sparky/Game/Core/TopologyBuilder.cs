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

    /// <summary>
    /// Builds MNA topology from voxels and components.
    /// </summary>
    public Dictionary<VoxelPos, ConductorRegion> BuildTopology(
        VoxelGrid voxels,
        IEnumerable<Component> components,
        ISimulation sim)
    {
        using var _ = sim.BeginBulkUpdate();

        // Step 1: Find all connected conductor regions via prism adjacency
        var regions = FindConductorRegions(voxels);

        // Step 2: Create MNA nodes for each region
        var componentList = new List<Component>(components);
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
        var allocatedNodes = new List<NodeId>();
        foreach (var region in GetUniqueRegions(regions))
        {
            if (groundRegions.Contains(region))
            {
                region.NodeId = sim.Ground;
            }
            else
            {
                var node = sim.CreateNode();
                region.NodeId = node;
                allocatedNodes.Add(node);
            }
        }

        // Step 2.5: Create resistors between adjacent resistive regions
        CreateInterRegionResistors(voxels, regions, sim);

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
                    var isolatedNode = sim.CreateNode();
                    allocatedNodes.Add(isolatedNode);
                    nodeId = isolatedNode;
                }

                terminalNodes[terminal.Name] = nodeId.Value;
            }

            component.CreateMnaComponents(sim, terminalNodes);
        }

        return regions;
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
    /// </summary>
    /// <param name="grid">The voxel grid (needed to check prism types).</param>
    /// <param name="regions">The conductor regions map.</param>
    /// <param name="sim">The simulation to add resistors to.</param>
    /// <param name="resistancePerFace">Resistance per voxel face contact.</param>
    public void CreateInterRegionResistors(
        VoxelGrid grid,
        Dictionary<VoxelPos, ConductorRegion> regions,
        ISimulation sim,
        double resistancePerFace = DefaultWireResistance)
    {
        // Track which region pairs we've already connected to avoid duplicates
        var connectedPairs = new HashSet<(ConductorRegion, ConductorRegion)>();

        // Get all unique regions
        var uniqueRegions = new HashSet<ConductorRegion>(regions.Values);

        // For each pair of regions, check if any of their prisms are adjacent
        // Create resistors when AT LEAST ONE region is resistive
        foreach (var regionA in uniqueRegions)
        {
            foreach (var regionB in uniqueRegions)
            {
                if (regionA == regionB)
                    continue;

                // At least one region must be resistive (wire-to-wire or wire-to-terminal)
                if (!regionA.IsResistive && !regionB.IsResistive)
                    continue;

                // Skip if already connected (in either order)
                if (connectedPairs.Contains((regionA, regionB)) ||
                    connectedPairs.Contains((regionB, regionA)))
                    continue;

                // Check all prism pairs for adjacency
                int totalContactArea = 0;
                foreach (var (blockA, prismA) in regionA.Prisms)
                {
                    foreach (var (blockB, prismB) in regionB.Prisms)
                    {
                        var area = CalculateContactArea(prismA, prismB, blockA, blockB);
                        totalContactArea += area;
                    }
                }

                if (totalContactArea > 0)
                {
                    // Create resistor: R = resistancePerFace / contactArea (parallel resistors)
                    var resistance = resistancePerFace / totalContactArea;
                    var resistorId = sim.AddResistor(regionA.NodeId, regionB.NodeId, resistance);

                    // Track on both regions for current queries
                    regionA.AdjacentResistors.Add(resistorId);
                    regionB.AdjacentResistors.Add(resistorId);

                    connectedPairs.Add((regionA, regionB));
                }
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
}
