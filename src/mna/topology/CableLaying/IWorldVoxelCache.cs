using Sparky.Voxel;

namespace Sparky.Mna.Topology.CableLaying;

/// <summary>
/// Interface for the world voxel cache used by the cable pathfinder.
/// Provides efficient access to voxel states in a region around a start point.
/// </summary>
public interface IWorldVoxelCache {
    /// <summary>
    /// Gets the state of a single voxel.
    /// </summary>
    /// <param name="pos">The voxel position in world coordinates.</param>
    /// <returns>The voxel state. Returns <see cref="CacheVoxelState.Empty"/> for positions outside cache bounds.</returns>
    CacheVoxelState GetState(VoxelPos pos);

    /// <summary>
    /// Checks if all voxels in a region are empty.
    /// </summary>
    /// <param name="min">Minimum corner (inclusive).</param>
    /// <param name="max">Maximum corner (exclusive).</param>
    /// <returns>True if all voxels in the region are <see cref="CacheVoxelState.Empty"/>.</returns>
    bool AllEmpty(VoxelPos min, VoxelPos max);

    /// <summary>
    /// Checks if any cardinal neighbor (6 directions) has the specified state.
    /// </summary>
    /// <param name="pos">The center position.</param>
    /// <param name="state">The state to look for.</param>
    /// <returns>True if at least one cardinal neighbor has the specified state.</returns>
    bool AnyCardinalNeighbor(VoxelPos pos, CacheVoxelState state);

    /// <summary>
    /// Checks if a position is within the inner pathfinding bounds (6-block radius).
    /// Cables can only be routed within this region.
    /// </summary>
    bool IsInPathfindingBounds(VoxelPos pos);

    /// <summary>
    /// Checks if a position is within the outer cache bounds (7-block radius).
    /// The outer ring is read-only for adjacency checks.
    /// </summary>
    bool IsInCacheBounds(VoxelPos pos);

    /// <summary>
    /// Marks a voxel as part of the cable path being constructed.
    /// Used during A* search to track the temporary path.
    /// </summary>
    void SetCableConductor(VoxelPos pos);

    /// <summary>
    /// Clears all temporary cable conductor markers.
    /// Call before starting a new pathfinding operation.
    /// </summary>
    void ClearCableConductors();

    /// <summary>
    /// Gets the origin (center) of the cache in world voxel coordinates.
    /// </summary>
    VoxelPos Origin { get; }

    /// <summary>
    /// Finds the minimum Manhattan distance from a position to any insulation voxel.
    /// Used for extended support range checking (corners, etc.).
    /// </summary>
    /// <param name="pos">The position to check.</param>
    /// <param name="maxDistance">Maximum search distance.</param>
    /// <returns>Distance to nearest insulation (1 = adjacent), or maxDistance+1 if none within range.</returns>
    int DistanceToInsulation(VoxelPos pos, int maxDistance);
}
