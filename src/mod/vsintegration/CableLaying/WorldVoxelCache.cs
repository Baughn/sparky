using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;
using Sparky.VSIntegration;
using VoxelPos = Sparky.Game.Core.VoxelPos;
using VSBlockPos = Vintagestory.API.MathTools.BlockPos;

namespace Sparky.VSIntegration.CableLaying;

/// <summary>
/// Caches world voxel data for cable pathfinding.
/// Converts VS blocks in a 7-block radius to a sparse octree of CacheVoxelState.
/// </summary>
public class WorldVoxelCache : IWorldVoxelCache {
    /// <summary>Cache radius in blocks for adjacency checking.</summary>
    public const int CacheRadius = 7;

    /// <summary>Inner radius for pathfinding (cables can only be routed within this).</summary>
    public const int PathfindingRadius = 6;

    private readonly SparseVoxelOctree<CacheVoxelState> _octree;
    private readonly VoxelPos _origin;
    private readonly IBlockAccessor _blockAccessor;
    private readonly VSBlockPos _centerBlock;
    private readonly int _radius;

    // Track cable conductor positions for clearing
    private readonly HashSet<VoxelPos> _cableConductors = new();

    /// <summary>
    /// Creates a new cache centered on the specified block.
    /// </summary>
    /// <param name="blockAccessor">VS block accessor for reading world data.</param>
    /// <param name="centerBlock">The center block position (where cable starts).</param>
    /// <param name="radius">Cache radius in blocks. Default is full CacheRadius (7).
    /// Use smaller radius (e.g., 1) for snap-only calculations.</param>
    public WorldVoxelCache(IBlockAccessor blockAccessor, VSBlockPos centerBlock, int radius = CacheRadius) {
        _blockAccessor = blockAccessor ?? throw new ArgumentNullException(nameof(blockAccessor));
        _centerBlock = centerBlock.Copy();
        _radius = radius;

        // Origin is center of the center block
        _origin = new VoxelPos(
            centerBlock.X * 16 + 8,
            centerBlock.Y * 16 + 8,
            centerBlock.Z * 16 + 8
        );

        // Default value is Empty (air)
        _octree = new SparseVoxelOctree<CacheVoxelState>(CacheVoxelState.Empty);

        RebuildCache();
    }

    /// <inheritdoc/>
    public VoxelPos Origin => _origin;

    /// <inheritdoc/>
    public CacheVoxelState GetState(VoxelPos pos) {
        return _octree.Get(pos);
    }

    /// <inheritdoc/>
    public bool AllEmpty(VoxelPos min, VoxelPos max) {
        for (int z = min.Z; z < max.Z; z++) {
            for (int y = min.Y; y < max.Y; y++) {
                for (int x = min.X; x < max.X; x++) {
                    if (_octree.Get(new VoxelPos(x, y, z)) != CacheVoxelState.Empty)
                        return false;
                }
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public bool AnyCardinalNeighbor(VoxelPos pos, CacheVoxelState state) {
        foreach (var dir in VoxelDirectionExtensions.All) {
            var neighbor = pos.Neighbor(dir);
            if (_octree.Get(neighbor) == state)
                return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public bool IsInPathfindingBounds(VoxelPos pos) {
        int dx = Math.Abs(pos.X - _origin.X);
        int dy = Math.Abs(pos.Y - _origin.Y);
        int dz = Math.Abs(pos.Z - _origin.Z);

        // Pathfinding radius is 6 blocks = 96 voxels
        int maxDist = PathfindingRadius * 16;
        return dx < maxDist && dy < maxDist && dz < maxDist;
    }

    /// <inheritdoc/>
    public bool IsInCacheBounds(VoxelPos pos) {
        int dx = Math.Abs(pos.X - _origin.X);
        int dy = Math.Abs(pos.Y - _origin.Y);
        int dz = Math.Abs(pos.Z - _origin.Z);

        // Cache radius is 7 blocks = 112 voxels
        int maxDist = CacheRadius * 16;
        return dx < maxDist && dy < maxDist && dz < maxDist;
    }

    /// <inheritdoc/>
    public void SetCableConductor(VoxelPos pos) {
        _octree.Set(pos, CacheVoxelState.CableConductor);
        _cableConductors.Add(pos);
    }

    /// <inheritdoc/>
    public void ClearCableConductors() {
        foreach (var pos in _cableConductors) {
            _octree.Set(pos, CacheVoxelState.Empty);
        }
        _cableConductors.Clear();
    }

    /// <summary>
    /// Flood-fills from a position, converting connected PreExistingConductor voxels to CableConductor.
    /// Used when extending an existing cable - marks the connected cable as "ours" so pathfinder
    /// doesn't reject adjacency to it.
    /// </summary>
    /// <param name="startPos">Position to start flood-fill from (should be adjacent to existing cable).</param>
    /// <param name="maxDistance">Maximum Manhattan distance to flood-fill.</param>
    /// <returns>Number of voxels converted.</returns>
    public int MarkConnectedConductorAsCable(VoxelPos startPos, int maxDistance = 4) {
        int converted = 0;
        var visited = new HashSet<VoxelPos>();
        var queue = new Queue<VoxelPos>();

        // Find initial conductor neighbors to start flood-fill
        foreach (var dir in VoxelDirectionExtensions.All) {
            var neighbor = startPos.Neighbor(dir);
            if (_octree.Get(neighbor) == CacheVoxelState.PreExistingConductor) {
                queue.Enqueue(neighbor);
            }
        }

        while (queue.Count > 0) {
            var pos = queue.Dequeue();

            if (visited.Contains(pos))
                continue;
            visited.Add(pos);

            // Check distance limit
            int dist = Math.Abs(pos.X - startPos.X) + Math.Abs(pos.Y - startPos.Y) + Math.Abs(pos.Z - startPos.Z);
            if (dist > maxDistance)
                continue;

            // Convert PreExistingConductor to CableConductor
            if (_octree.Get(pos) == CacheVoxelState.PreExistingConductor) {
                _octree.Set(pos, CacheVoxelState.CableConductor);
                _cableConductors.Add(pos);
                converted++;

                // Add neighbors to queue
                foreach (var dir in VoxelDirectionExtensions.All) {
                    var neighbor = pos.Neighbor(dir);
                    if (!visited.Contains(neighbor)) {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return converted;
    }

    /// <inheritdoc/>
    public int DistanceToInsulation(VoxelPos pos, int maxDistance) {
        // Expanding search by Manhattan distance
        for (int d = 1; d <= maxDistance; d++) {
            // Check all positions at Manhattan distance d
            for (int dx = -d; dx <= d; dx++) {
                for (int dy = -(d - Math.Abs(dx)); dy <= d - Math.Abs(dx); dy++) {
                    int remainingDist = d - Math.Abs(dx) - Math.Abs(dy);
                    // Two possible dz values for this Manhattan distance (positive and negative)
                    foreach (int dz in new[] { remainingDist, -remainingDist }) {
                        if (remainingDist == 0 && dz != 0)
                            continue; // Avoid duplicate at dz=0
                        var checkPos = pos.Offset(dx, dy, dz);
                        if (_octree.Get(checkPos) == CacheVoxelState.Insulation)
                            return d;
                    }
                }
            }
        }
        return maxDistance + 1;
    }

    /// <summary>
    /// Rebuilds the entire cache from the world.
    /// </summary>
    public void RebuildCache() {
        _octree.Clear();
        _cableConductors.Clear();

        // Iterate all blocks in the cache radius
        for (int bz = -_radius; bz <= _radius; bz++) {
            for (int by = -_radius; by <= _radius; by++) {
                for (int bx = -_radius; bx <= _radius; bx++) {
                    var blockPos = _centerBlock.AddCopy(bx, by, bz);
                    ProcessBlock(blockPos);
                }
            }
        }
    }

    /// <summary>
    /// Processes a single block and adds its voxels to the cache.
    /// </summary>
    private void ProcessBlock(VSBlockPos blockPos) {
        var block = _blockAccessor.GetBlock(blockPos);
        var be = _blockAccessor.GetBlockEntity(blockPos);

        // Air or replaceable blocks → all Empty (default, nothing to set)
        if (block.BlockId == 0 || block.Replaceable >= 6000) {
            return;
        }

        // Circuit behavior → conductors become PreExistingConductor, non-conductors → Insulation
        var behavior = be?.GetBehavior<BEBehaviorCircuit>();
        if (behavior != null) {
            ProcessCircuitBehavior(blockPos, behavior);
            return;
        }

        // Circuit block → conductors become PreExistingConductor, non-conductors → Insulation, unfilled → Empty
        if (be is BlockEntityCircuit circuit) {
            ProcessCircuitBlock(blockPos, circuit);
            return;
        }

        // Non-circuit microblock → filled voxels become Insulation, unfilled → Unroutable
        if (be is BlockEntityMicroBlock microblock) {
            ProcessMicroBlock(blockPos, microblock);
            return;
        }

        // Regular solid block → all voxels become Insulation
        // For simplicity, treat any non-air, non-replaceable block as fully solid
        ProcessSolidBlock(blockPos, block);
    }

    /// <summary>
    /// Processes a circuit block. Conductors → PreExistingConductor, non-conductors → Insulation.
    /// Unfilled areas remain Empty (can be occupied by cable).
    /// </summary>
    private void ProcessCircuitBlock(VSBlockPos blockPos, BlockEntityCircuit circuit) {
        if (circuit.VoxelCuboids == null || circuit.BlockIds == null)
            return;

        ApplyCircuitCuboids(
            _octree,
            blockPos.X * 16,
            blockPos.Y * 16,
            blockPos.Z * 16,
            circuit.VoxelCuboids,
            circuit.BlockIds,
            BlockEntityCircuit.IsConductor);
    }

    /// <summary>
    /// Processes a circuit behavior. Conductors → PreExistingConductor, non-conductors → Insulation.
    /// Unfilled areas remain Empty (can be occupied by cable).
    /// </summary>
    private void ProcessCircuitBehavior(VSBlockPos blockPos, BEBehaviorCircuit behavior) {
        if (behavior.ConductorCuboids.Count == 0 || behavior.ConductorBlockIds.Length == 0)
            return;

        ApplyCircuitCuboids(
            _octree,
            blockPos.X * 16,
            blockPos.Y * 16,
            blockPos.Z * 16,
            behavior.ConductorCuboids,
            behavior.ConductorBlockIds,
            BEBehaviorCircuit.IsConductor);
    }

    /// <summary>
    /// Applies circuit cuboids to the cache octree.
    /// </summary>
    public static void ApplyCircuitCuboids(
        SparseVoxelOctree<CacheVoxelState> octree,
        int baseX,
        int baseY,
        int baseZ,
        IReadOnlyList<uint> cuboids,
        int[] blockIds,
        System.Func<int, bool> isConductor) {
        if (cuboids == null || cuboids.Count == 0 || blockIds == null)
            return;

        foreach (var cuboid in cuboids) {
            BEBehaviorCircuit.FromUint(cuboid,
                out int x0, out int y0, out int z0,
                out int x1, out int y1, out int z1,
                out int matIdx);

            bool conductor = matIdx < blockIds.Length && isConductor(blockIds[matIdx]);
            var state = conductor ? CacheVoxelState.PreExistingConductor : CacheVoxelState.Insulation;

            for (int z = z0; z < z1; z++) {
                for (int y = y0; y < y1; y++) {
                    for (int x = x0; x < x1; x++) {
                        var pos = new VoxelPos(baseX + x, baseY + y, baseZ + z);
                        octree.Set(pos, state);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processes a non-circuit microblock. Filled → Insulation, unfilled → Unroutable.
    /// </summary>
    private void ProcessMicroBlock(VSBlockPos blockPos, BlockEntityMicroBlock microblock) {
        if (microblock.VoxelCuboids == null)
            return;

        int baseX = blockPos.X * 16;
        int baseY = blockPos.Y * 16;
        int baseZ = blockPos.Z * 16;

        // First mark all voxels as Unroutable (can't route through this block)
        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 16; y++) {
                for (int x = 0; x < 16; x++) {
                    var pos = new VoxelPos(baseX + x, baseY + y, baseZ + z);
                    _octree.Set(pos, CacheVoxelState.Unroutable);
                }
            }
        }

        // Then mark filled voxels as Insulation
        foreach (var cuboid in microblock.VoxelCuboids) {
            BlockEntityMicroBlock.FromUint(cuboid,
                out int x0, out int y0, out int z0,
                out int x1, out int y1, out int z1,
                out int _);

            for (int z = z0; z < z1; z++) {
                for (int y = y0; y < y1; y++) {
                    for (int x = x0; x < x1; x++) {
                        var pos = new VoxelPos(baseX + x, baseY + y, baseZ + z);
                        _octree.Set(pos, CacheVoxelState.Insulation);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processes a regular solid block. All voxels become Insulation.
    /// </summary>
    private void ProcessSolidBlock(VSBlockPos blockPos, Block block) {
        int baseX = blockPos.X * 16;
        int baseY = blockPos.Y * 16;
        int baseZ = blockPos.Z * 16;

        // For now, treat all non-air, non-replaceable, non-microblock blocks as fully solid
        // This includes stairs, fences, etc. - they become 16x16x16 Insulation blocks
        // Future enhancement: use collision boxes for more accurate representation
        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 16; y++) {
                for (int x = 0; x < 16; x++) {
                    var pos = new VoxelPos(baseX + x, baseY + y, baseZ + z);
                    _octree.Set(pos, CacheVoxelState.Insulation);
                }
            }
        }
    }
}
