namespace Sparky.Game.Core.CableLaying;

/// <summary>
/// Finds the best snap position for cable placement.
/// Searches nearby positions to find optimal start points based on support quality.
/// Returns the actual voxel positions where the cable should start.
/// </summary>
public static class SnapPositionFinder {
    /// <summary>
    /// Finds the best start positions within 3 voxels of the clicked position.
    /// Returns the actual voxel positions that make up the cable's starting cross-section.
    ///
    /// Scoring rules:
    /// - Negative infinity if not all N×M voxels are in Empty
    /// - -1000 if not precisely max(N,M) voxels touching Insulator
    /// - -manhattan distance from clicked to geometric center of snap
    /// - +3 if adjacent to exactly N×M pre-existing conductor voxels
    /// - +2 for time-dependent direction preference
    /// </summary>
    /// <param name="clicked">The voxel position the player clicked.</param>
    /// <param name="cache">The world voxel cache for checking neighbors.</param>
    /// <param name="crossSection">The cable cross-section.</param>
    /// <param name="currentTime">Current time for direction preference.</param>
    /// <returns>The voxel positions where the cable should start.</returns>
    public static IReadOnlyList<VoxelPos> FindBestPosition(
        VoxelPos clicked,
        IWorldVoxelCache cache,
        CrossSection crossSection,
        float currentTime) {
        IReadOnlyList<VoxelPos> bestPositions = [clicked];
        int bestScore = int.MinValue;

        int maxSearch = 3;

        for (int dx = -maxSearch; dx <= maxSearch; dx++) {
            for (int dy = -maxSearch; dy <= maxSearch; dy++) {
                for (int dz = -maxSearch; dz <= maxSearch; dz++) {
                    var anchor = clicked.Offset(dx, dy, dz);

                    // Try all support directions for this position
                    foreach (var supportDir in VoxelDirectionExtensions.All) {
                        int score = ScorePositionWithSupport(
                            anchor, clicked, cache, crossSection, supportDir, currentTime,
                            out var positions);

                        if (score > bestScore) {
                            bestScore = score;
                            bestPositions = positions;
                        }
                    }
                }
            }
        }

        return bestPositions;
    }

    /// <summary>
    /// Scores a position with a specific support direction.
    /// Tries all combinations of travel direction and orientation.
    /// </summary>
    private static int ScorePositionWithSupport(
        VoxelPos anchor,
        VoxelPos target,
        IWorldVoxelCache cache,
        CrossSection crossSection,
        VoxelDirection supportDir,
        float currentTime,
        out IReadOnlyList<VoxelPos> chosenPositions) {
        chosenPositions = [anchor];

        // Get the two perpendicular axes for travel direction
        var (axis1, axis2) = supportDir.GetPerpendicularAxes();
        var travelDir1 = AxisToDirection(axis1);
        var travelDir2 = AxisToDirection(axis2);

        // Try all 4 combinations: 2 travel directions × 2 orientations
        var candidates = new[]
        {
            (travelDir1, CrossSectionOrientation.Flat, 0),
            (travelDir1, CrossSectionOrientation.Upright, 1),
            (travelDir2, CrossSectionOrientation.Flat, 2),
            (travelDir2, CrossSectionOrientation.Upright, 3)
        };

        int bestScore = int.MinValue;

        // Time-based preference: pick one of the 4 configurations to prefer
        int timePreference = (int)currentTime % 4;

        foreach (var (travelDir, orientation, index) in candidates) {
            var positions = crossSection.GetVoxelPositions(anchor, travelDir, orientation).ToList();
            int score = ScoreConfiguration(positions, target, cache, crossSection, supportDir);

            // Add time preference bonus
            if (index == timePreference)
                score += 2;

            if (score > bestScore) {
                bestScore = score;
                chosenPositions = positions;
            }
        }

        return bestScore;
    }

    /// <summary>
    /// Scores a specific configuration of voxel positions.
    /// </summary>
    private static int ScoreConfiguration(
        IReadOnlyList<VoxelPos> positions,
        VoxelPos target,
        IWorldVoxelCache cache,
        CrossSection crossSection,
        VoxelDirection supportDir) {
        int n = crossSection.Width;
        int m = crossSection.Height;
        int totalVoxels = n * m;
        int requiredContact = Math.Max(n, m);

        // Rule 1: All N×M voxels must be Empty (or CableConductor for self-overlap)
        foreach (var voxelPos in positions) {
            if (!cache.IsInPathfindingBounds(voxelPos))
                return int.MinValue;

            var state = cache.GetState(voxelPos);
            if (state != CacheVoxelState.Empty && state != CacheVoxelState.CableConductor)
                return int.MinValue;
        }

        int score = 0;

        // Rule 2: Exactly max(N,M) voxels must touch Insulator in the support direction
        int insulatorContact = 0;
        foreach (var voxelPos in positions) {
            var neighbor = voxelPos.Neighbor(supportDir);
            if (cache.GetState(neighbor) == CacheVoxelState.Insulation)
                insulatorContact++;
        }

        if (insulatorContact != requiredContact)
            score -= 1000;

        // Rule 3: -manhattan distance from target to geometric center
        var center = ComputeGeometricCenter(positions);
        int manhattanDist = Math.Abs(target.X - center.X) +
                           Math.Abs(target.Y - center.Y) +
                           Math.Abs(target.Z - center.Z);
        score -= manhattanDist;

        // Rule 4: +3 if adjacent to exactly N×M pre-existing conductor voxels
        int conductorAdjacent = CountAdjacentConductorVoxels(positions, cache);
        if (conductorAdjacent == totalVoxels)
            score += 3;

        return score;
    }

    /// <summary>
    /// Computes the geometric center of a list of voxel positions.
    /// </summary>
    private static VoxelPos ComputeGeometricCenter(IReadOnlyList<VoxelPos> positions) {
        int sumX = 0, sumY = 0, sumZ = 0;
        foreach (var p in positions) {
            sumX += p.X;
            sumY += p.Y;
            sumZ += p.Z;
        }
        return new VoxelPos(sumX / positions.Count, sumY / positions.Count, sumZ / positions.Count);
    }

    /// <summary>
    /// Counts how many voxels in the cross-section are adjacent to pre-existing conductor.
    /// </summary>
    private static int CountAdjacentConductorVoxels(IReadOnlyList<VoxelPos> positions, IWorldVoxelCache cache) {
        int count = 0;
        foreach (var pos in positions) {
            if (cache.AnyCardinalNeighbor(pos, CacheVoxelState.PreExistingConductor))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Converts an axis index (0=X, 1=Y, 2=Z) to a positive direction.
    /// </summary>
    private static VoxelDirection AxisToDirection(int axis) {
        return axis switch {
            0 => VoxelDirection.XPos,
            1 => VoxelDirection.YPos,
            2 => VoxelDirection.ZPos,
            _ => VoxelDirection.XPos
        };
    }
}
