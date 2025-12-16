namespace Sparky.Game.Core.CableLaying;

/// <summary>
/// Finds the best snap position for cable placement.
/// Searches nearby positions to find optimal start points based on support quality.
/// </summary>
public static class SnapPositionFinder
{
    /// <summary>
    /// Finds the best start position within 2 voxels of the clicked position.
    /// Scores positions based on support quality and existing cable proximity.
    /// Ties are broken by Euclidean distance to clicked position (closer wins).
    /// </summary>
    /// <param name="clicked">The voxel position the player clicked.</param>
    /// <param name="cache">The world voxel cache for checking neighbors.</param>
    /// <param name="crossSection">The cable cross-section (for future cross-section-aware snapping).</param>
    /// <returns>The best snap position and inherited direction (if connecting to existing cable).</returns>
    public static (VoxelPos Position, VoxelDirection? Direction) FindBestStartPosition(
        VoxelPos clicked,
        IWorldVoxelCache cache,
        CrossSection crossSection)
    {
        VoxelPos bestPos = clicked;
        VoxelDirection? bestDir = null;
        int bestScore = ScorePosition(clicked, cache, crossSection, out var clickedDir);
        int bestDistSq = 0; // Distance squared from clicked (0 for clicked itself)
        if (clickedDir.HasValue)
            bestDir = clickedDir;

        // Search within 2 voxels
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                        continue;

                    var pos = clicked.Offset(dx, dy, dz);
                    int score = ScorePosition(pos, cache, crossSection, out var dir);
                    int distSq = dx * dx + dy * dy + dz * dz;

                    // Better score wins, or same score but closer to cursor
                    if (score > bestScore || (score == bestScore && distSq < bestDistSq))
                    {
                        bestScore = score;
                        bestDistSq = distSq;
                        bestPos = pos;
                        bestDir = dir;
                    }
                }
            }
        }

        return (bestPos, bestDir);
    }

    /// <summary>
    /// Scores a potential start position.
    /// Higher score = better position.
    /// </summary>
    /// <param name="pos">The position to score (this is the min-corner of the cross-section).</param>
    /// <param name="cache">The world voxel cache.</param>
    /// <param name="crossSection">The cable cross-section.</param>
    /// <param name="inheritedDirection">Output: direction from adjacent existing cable, if any.</param>
    /// <returns>Score value (higher is better, negative means invalid).</returns>
    public static int ScorePosition(
        VoxelPos pos,
        IWorldVoxelCache cache,
        CrossSection crossSection,
        out VoxelDirection? inheritedDirection)
    {
        inheritedDirection = null;

        // Must be empty and within bounds
        if (!cache.IsInPathfindingBounds(pos))
            return -1000;

        var state = cache.GetState(pos);
        if (state != CacheVoxelState.Empty && state != CacheVoxelState.CableConductor)
            return -1000;

        int score = 0;

        // Bonus for having insulation support
        if (cache.AnyCardinalNeighbor(pos, CacheVoxelState.Insulation))
            score += 100;

        // Bonus for being adjacent to existing cable (for connection)
        if (cache.AnyCardinalNeighbor(pos, CacheVoxelState.PreExistingConductor))
        {
            score += 50;

            // Try to determine direction from adjacent conductor
            inheritedDirection = FindCableDirection(pos, cache);
        }

        return score;
    }

    /// <summary>
    /// Attempts to determine the travel direction from an adjacent existing cable.
    /// </summary>
    private static VoxelDirection? FindCableDirection(VoxelPos pos, IWorldVoxelCache cache)
    {
        // Find which direction has the existing conductor
        foreach (var dir in VoxelDirectionExtensions.All)
        {
            var neighbor = pos.Neighbor(dir);
            if (cache.GetState(neighbor) == CacheVoxelState.PreExistingConductor)
            {
                // The new cable should continue in the direction AWAY from the existing cable
                return dir.Opposite();
            }
        }
        return null;
    }
}
