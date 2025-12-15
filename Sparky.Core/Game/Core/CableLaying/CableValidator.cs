namespace Sparky.Game.Core.CableLaying;

/// <summary>
/// Test utility for validating acceptance criteria on generated cable paths.
/// Call this in EVERY cable-related test to ensure path quality.
/// </summary>
public static class CableValidator
{
    /// <summary>
    /// Validates all acceptance criteria for a cable path.
    /// Throws descriptive exceptions on validation failure.
    /// </summary>
    /// <param name="path">The cable voxel positions from pathfinder.</param>
    /// <param name="crossSection">The cable cross-section size.</param>
    /// <param name="cache">The world voxel cache used during pathfinding.</param>
    /// <exception cref="CableValidationException">Thrown when any criterion fails.</exception>
    public static void ValidatePath(
        IReadOnlyList<VoxelPos> path,
        CrossSection crossSection,
        IWorldVoxelCache cache)
    {
        if (path.Count == 0)
            return; // Empty path is valid (NoProgress result)

        var pathSet = new HashSet<VoxelPos>(path);

        ValidateNoConductorAdjacency(path, cache);
        ValidateSupportAdjacency(path, pathSet, cache);
        ValidateWallProximity(path, crossSection, cache);
        ValidateMinimumTurnDistance(path, crossSection);
    }

    /// <summary>
    /// Criterion 3: No cable voxel is cardinally adjacent to PreExistingConductor.
    /// </summary>
    private static void ValidateNoConductorAdjacency(
        IReadOnlyList<VoxelPos> path,
        IWorldVoxelCache cache)
    {
        foreach (var pos in path)
        {
            foreach (var dir in VoxelDirectionExtensions.All)
            {
                var neighbor = pos.Neighbor(dir);
                if (cache.GetState(neighbor) == CacheVoxelState.PreExistingConductor)
                {
                    throw new CableValidationException(
                        $"Conductor adjacency violation: cable voxel {pos} is adjacent to " +
                        $"PreExistingConductor at {neighbor}");
                }
            }
        }
    }

    /// <summary>
    /// Criterion 4: Every cable voxel is adjacent (cardinal) to either
    /// Insulation in source cache OR another cable voxel.
    /// </summary>
    private static void ValidateSupportAdjacency(
        IReadOnlyList<VoxelPos> path,
        HashSet<VoxelPos> pathSet,
        IWorldVoxelCache cache)
    {
        foreach (var pos in path)
        {
            bool hasSupport = false;

            foreach (var dir in VoxelDirectionExtensions.All)
            {
                var neighbor = pos.Neighbor(dir);

                // Support from insulation
                if (cache.GetState(neighbor) == CacheVoxelState.Insulation)
                {
                    hasSupport = true;
                    break;
                }

                // Support from another cable voxel
                if (pathSet.Contains(neighbor))
                {
                    hasSupport = true;
                    break;
                }
            }

            if (!hasSupport)
            {
                throw new CableValidationException(
                    $"Support adjacency violation: cable voxel {pos} has no adjacent " +
                    $"Insulation or other cable voxel");
            }
        }
    }

    /// <summary>
    /// Criterion 5: Wall Proximity ("Lay Flat")
    /// For cross-section W×H where W ≤ H, the cable voxel furthest from
    /// any insulation surface is ≤ W voxels away.
    /// </summary>
    private static void ValidateWallProximity(
        IReadOnlyList<VoxelPos> path,
        CrossSection crossSection,
        IWorldVoxelCache cache)
    {
        int maxAllowedDistance = crossSection.Width;

        foreach (var pos in path)
        {
            int minDist = FindDistanceToInsulation(pos, cache, maxAllowedDistance + 1);

            if (minDist > maxAllowedDistance)
            {
                throw new CableValidationException(
                    $"Wall proximity violation: cable voxel {pos} is {minDist} voxels " +
                    $"from nearest insulation (max allowed: {maxAllowedDistance} for " +
                    $"{crossSection} cross-section)");
            }
        }
    }

    /// <summary>
    /// Finds the distance to the nearest insulation voxel.
    /// </summary>
    private static int FindDistanceToInsulation(VoxelPos pos, IWorldVoxelCache cache, int maxSearch)
    {
        // BFS to find nearest insulation
        var visited = new HashSet<VoxelPos> { pos };
        var queue = new Queue<(VoxelPos Pos, int Dist)>();
        queue.Enqueue((pos, 0));

        while (queue.Count > 0)
        {
            var (current, dist) = queue.Dequeue();

            if (dist >= maxSearch)
                return maxSearch;

            foreach (var dir in VoxelDirectionExtensions.All)
            {
                var neighbor = current.Neighbor(dir);

                if (visited.Contains(neighbor))
                    continue;
                visited.Add(neighbor);

                if (cache.GetState(neighbor) == CacheVoxelState.Insulation)
                    return dist + 1;

                queue.Enqueue((neighbor, dist + 1));
            }
        }

        return maxSearch;
    }

    /// <summary>
    /// Criterion 6: Minimum Turn Distance
    /// Between any two 90° turns, there are at least W×H voxels of straight cable.
    /// </summary>
    private static void ValidateMinimumTurnDistance(
        IReadOnlyList<VoxelPos> path,
        CrossSection crossSection)
    {
        if (path.Count < 3)
            return; // Can't have turns with < 3 voxels

        int minTurnDistance = crossSection.MinTurnDistance;

        // Find the path's direction changes by analyzing the voxel sequence
        // This is approximate - we look for direction changes in the path skeleton
        var turns = FindTurns(path);

        for (int i = 1; i < turns.Count; i++)
        {
            int distance = turns[i] - turns[i - 1];
            if (distance < minTurnDistance)
            {
                throw new CableValidationException(
                    $"Minimum turn distance violation: turns at indices {turns[i - 1]} and " +
                    $"{turns[i]} are {distance} voxels apart (minimum: {minTurnDistance} for " +
                    $"{crossSection} cross-section)");
            }
        }
    }

    /// <summary>
    /// Finds turn positions in the path by detecting direction changes.
    /// Returns indices in the path where turns occur.
    /// </summary>
    private static List<int> FindTurns(IReadOnlyList<VoxelPos> path)
    {
        var turns = new List<int>();

        if (path.Count < 3)
            return turns;

        // For cross-section cables, we need to find the "spine" -
        // the sequence of anchor points. For simplicity, we'll detect
        // direction changes by looking at consecutive positions.

        VoxelDirection? lastDir = null;

        for (int i = 1; i < path.Count; i++)
        {
            var dir = GetDirection(path[i - 1], path[i]);
            if (dir == null)
                continue; // Not adjacent, skip

            if (lastDir.HasValue && dir != lastDir && dir != lastDir.Value.Opposite())
            {
                // This is a 90° turn
                turns.Add(i - 1);
            }

            lastDir = dir;
        }

        return turns;
    }

    /// <summary>
    /// Gets the direction from one voxel to an adjacent voxel, or null if not adjacent.
    /// </summary>
    private static VoxelDirection? GetDirection(VoxelPos from, VoxelPos to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int dz = to.Z - from.Z;

        // Must be exactly 1 unit apart in one axis
        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) != 1)
            return null;

        if (dx == 1) return VoxelDirection.XPos;
        if (dx == -1) return VoxelDirection.XNeg;
        if (dy == 1) return VoxelDirection.YPos;
        if (dy == -1) return VoxelDirection.YNeg;
        if (dz == 1) return VoxelDirection.ZPos;
        if (dz == -1) return VoxelDirection.ZNeg;

        return null;
    }
}

/// <summary>
/// Exception thrown when cable validation fails.
/// </summary>
public class CableValidationException : Exception
{
    public CableValidationException(string message) : base(message) { }
}
