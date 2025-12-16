namespace Sparky.Game.Core.CableLaying;

/// <summary>
/// A* pathfinder for cable routing with cross-section awareness.
/// </summary>
/// <remarks>
/// Key features:
/// - Handles cable cross-sections (1×1 to 3×5)
/// - Minimum turning radius based on cross-section area
/// - Returns partial paths when goal is unreachable
/// - Marks path voxels in cache for support chain validation
/// </remarks>
public class CablePathfinder
{
    private readonly IWorldVoxelCache _cache;
    private readonly CrossSection _crossSection;

    /// <summary>Small penalty for turns to prefer straight paths.</summary>
    private const float TurnPenalty = 0.1f;

    /// <summary>
    /// Penalty per voxel of distance from insulation surface.
    /// High enough to prefer surface routes, but allows corner routing.
    /// </summary>
    private const float DistancePenalty = 3.0f;

    /// <summary>
    /// Optional logging action. Set this to receive debug logs from pathfinding.
    /// </summary>
    public static Action<string>? Log { get; set; }

    public CablePathfinder(IWorldVoxelCache cache, CrossSection crossSection)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _crossSection = crossSection;
    }

    /// <summary>
    /// Finds a path from start to goal.
    /// </summary>
    /// <param name="start">Starting voxel position (anchor corner of cross-section).</param>
    /// <param name="goal">Goal voxel position.</param>
    /// <param name="initialDirection">
    /// If not null, constrains the starting direction (for connecting to existing cable).
    /// </param>
    /// <returns>PathResult with the best path found.</returns>
    public PathResult FindPath(VoxelPos start, VoxelPos goal, VoxelDirection? initialDirection = null)
    {
        Log?.Invoke($"[Pathfinder] FindPath: start={start}, goal={goal}, initialDir={initialDirection}, crossSection={_crossSection}");

        _cache.ClearCableConductors();

        // Priority queue: (priority, node)
        var openSet = new PriorityQueue<SearchNode, float>();
        var cameFrom = new Dictionary<SearchNode, SearchNode>();
        var gScore = new Dictionary<SearchNode, float>();
        var visited = new HashSet<SearchNode>();

        // Track best partial result
        SearchNode? bestNode = null;
        int bestDistance = int.MaxValue;
        float bestGScore = float.MaxValue;

        // Initialize with starting nodes
        if (initialDirection.HasValue)
        {
            // Constrained start - single direction
            var startNode = new SearchNode(start, initialDirection.Value, 0);
            if (TryPlaceCrossSection(start, initialDirection.Value, isStart: true))
            {
                Log?.Invoke($"[Pathfinder] Start node added: dir={initialDirection.Value}");
                openSet.Enqueue(startNode, Heuristic(start, goal));
                gScore[startNode] = 0;
            }
            else
            {
                Log?.Invoke($"[Pathfinder] Start failed TryPlaceCrossSection: dir={initialDirection.Value}");
            }
        }
        else
        {
            // Unconstrained start - try all directions
            int validStarts = 0;
            foreach (var dir in VoxelDirectionExtensions.All)
            {
                var startNode = new SearchNode(start, dir, 0);
                if (TryPlaceCrossSection(start, dir, isStart: true))
                {
                    validStarts++;
                    openSet.Enqueue(startNode, Heuristic(start, goal));
                    gScore[startNode] = 0;
                }
            }
            Log?.Invoke($"[Pathfinder] Unconstrained start: {validStarts}/6 directions valid");
        }

        // If no valid start positions, return NoProgress
        if (openSet.Count == 0)
        {
            Log?.Invoke($"[Pathfinder] NoProgress: no valid start positions");
            return PathResult.NoProgress(start, goal);
        }

        int iterations = 0;
        int neighborsRejectedByVisited = 0;
        int neighborsRejectedByPlacement = 0;
        int neighborsAccepted = 0;

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            iterations++;

            if (visited.Contains(current))
                continue;
            visited.Add(current);

            // Track best partial result
            int distToGoal = ManhattanDistance(current.Position, goal);
            float currentG = gScore.GetValueOrDefault(current, float.MaxValue);

            if (distToGoal < bestDistance ||
                (distToGoal == bestDistance && currentG < bestGScore))
            {
                bestDistance = distToGoal;
                bestGScore = currentG;
                bestNode = current;
            }

            // Check if we've reached the goal
            if (IsAtGoal(current.Position, goal))
            {
                var path = ReconstructPath(cameFrom, current);
                Log?.Invoke($"[Pathfinder] Complete: iterations={iterations}, pathLength={path.Count}, visited={visited.Count}");
                return PathResult.Complete(path, goal);
            }

            // Check bounds
            if (!_cache.IsInPathfindingBounds(current.Position))
            {
                // Spammy: Log?.Invoke($"[Pathfinder] Node {current.Position} outside bounds, skipping");
                continue;
            }

            // Generate neighbors
            foreach (var neighbor in GetNeighbors(current))
            {
                if (visited.Contains(neighbor))
                {
                    neighborsRejectedByVisited++;
                    continue;
                }

                // Try to place cross-section at neighbor position
                var (canPlace, distToInsulation) = CanPlaceCrossSection(neighbor.Position, neighbor.Direction);
                if (!canPlace)
                {
                    neighborsRejectedByPlacement++;
                    continue;
                }

                neighborsAccepted++;

                // Base cost + distance penalty (0 if adjacent, increases for further distances)
                float stepCost = 1.0f + Math.Max(0, distToInsulation - 1) * DistancePenalty;
                float tentativeG = currentG + stepCost;
                if (neighbor.Direction != current.Direction)
                    tentativeG += TurnPenalty;

                if (tentativeG < gScore.GetValueOrDefault(neighbor, float.MaxValue))
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    float f = tentativeG + Heuristic(neighbor.Position, goal);
                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        Log?.Invoke($"[Pathfinder] Search exhausted: iterations={iterations}, visited={visited.Count}, accepted={neighborsAccepted}, rejectedVisited={neighborsRejectedByVisited}, rejectedPlacement={neighborsRejectedByPlacement}");

        // No complete path found - return best partial
        if (bestNode.HasValue && bestDistance < ManhattanDistance(start, goal))
        {
            // Reconstruct path to best node
            _cache.ClearCableConductors();
            var path = ReconstructPathAndMark(cameFrom, bestNode.Value);
            Log?.Invoke($"[Pathfinder] Partial: pathLength={path.Count}, bestDist={bestDistance}, endPos={bestNode.Value.Position}");
            return PathResult.Partial(path, bestNode.Value.Position, goal);
        }

        Log?.Invoke($"[Pathfinder] NoProgress: bestDist={bestDistance}, startDist={ManhattanDistance(start, goal)}");
        return PathResult.NoProgress(start, goal);
    }

    /// <summary>
    /// Checks if the current position is at or covers the goal.
    /// </summary>
    private bool IsAtGoal(VoxelPos current, VoxelPos goal)
    {
        // For simplicity, check if goal is within one voxel of current
        // A more sophisticated check would verify the cross-section covers the goal
        return ManhattanDistance(current, goal) <= 1;
    }

    /// <summary>
    /// Gets valid neighbor nodes from the current node.
    /// </summary>
    private IEnumerable<SearchNode> GetNeighbors(SearchNode current)
    {
        int minTurnDist = _crossSection.MinTurnDistance;

        foreach (var dir in VoxelDirectionExtensions.All)
        {
            // Never allow 180° turns
            if (dir == current.Direction.Opposite())
                continue;

            // Check minimum turn distance for 90° turns
            bool isTurn = dir != current.Direction;
            if (isTurn && current.StepsSinceTurn < minTurnDist)
                continue;

            var (dx, dy, dz) = dir.Offset();
            var newPos = current.Position.Offset(dx, dy, dz);
            int newSteps = isTurn ? 0 : current.StepsSinceTurn + 1;

            yield return new SearchNode(newPos, dir, newSteps);
        }
    }

    /// <summary>
    /// Checks if a cross-section can be placed at the given position.
    /// Returns validity and minimum distance to insulation for cost calculation.
    /// </summary>
    /// <returns>Tuple of (valid, minDistanceToInsulation). Distance is 1 if adjacent.</returns>
    private (bool Valid, int MinDistance) CanPlaceCrossSection(VoxelPos anchor, VoxelDirection direction)
    {
        var orientation = DetermineOrientation(anchor, direction);
        int maxDist = 2 * _crossSection.Height;
        int minDistance = int.MaxValue;

        foreach (var pos in _crossSection.GetVoxelPositions(anchor, direction, orientation))
        {
            var state = _cache.GetState(pos);

            // Must be empty (can occupy)
            if (state != CacheVoxelState.Empty && state != CacheVoxelState.CableConductor)
                return (false, 0);

            // Check for adjacent PreExistingConductor (short circuit)
            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.PreExistingConductor))
                return (false, 0);

            // Find distance to support (insulation or cable)
            int dist = _cache.DistanceToInsulation(pos, maxDist);

            // Cable conductor neighbor counts as distance 1 (direct support)
            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.CableConductor))
                dist = Math.Min(dist, 1);

            minDistance = Math.Min(minDistance, dist);
        }

        // Valid if any voxel is within extended support range
        bool valid = minDistance <= maxDist;
        return (valid, minDistance);
    }

    /// <summary>
    /// Tries to place a cross-section at the given position, marking voxels in cache.
    /// Used for the start position to establish initial cable markers.
    /// Start positions require adjacent insulation (distance 1) for stability.
    /// </summary>
    private bool TryPlaceCrossSection(VoxelPos anchor, VoxelDirection direction, bool isStart)
    {
        var orientation = DetermineOrientation(anchor, direction);
        var positions = _crossSection.GetVoxelPositions(anchor, direction, orientation).ToList();
        int maxDist = 2 * _crossSection.Height;
        int minDistance = int.MaxValue;

        // First pass: validate
        foreach (var pos in positions)
        {
            var state = _cache.GetState(pos);

            if (state != CacheVoxelState.Empty && state != CacheVoxelState.CableConductor)
            {
                Log?.Invoke($"[Pathfinder] TryPlace FAIL: pos={pos} state={state} (need Empty/CableConductor)");
                return false;
            }

            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.PreExistingConductor))
            {
                Log?.Invoke($"[Pathfinder] TryPlace FAIL: pos={pos} adjacent to PreExistingConductor");
                return false;
            }

            // Find distance to support
            int dist = _cache.DistanceToInsulation(pos, maxDist);
            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.CableConductor))
                dist = Math.Min(dist, 1);

            minDistance = Math.Min(minDistance, dist);
        }

        // For start, require adjacent insulation (distance 1) - no starting from corners
        if (isStart && !positions.Any(p => _cache.AnyCardinalNeighbor(p, CacheVoxelState.Insulation)))
        {
            Log?.Invoke($"[Pathfinder] TryPlace FAIL: isStart but no adjacent insulation. minDist={minDistance}");
            return false;
        }

        // Must have support within extended range
        if (minDistance > maxDist)
        {
            Log?.Invoke($"[Pathfinder] TryPlace FAIL: no support within range. minDist={minDistance}, maxDist={maxDist}");
            return false;
        }

        // Second pass: mark
        foreach (var pos in positions)
        {
            _cache.SetCableConductor(pos);
        }

        return true;
    }

    /// <summary>
    /// Determines the best orientation for the cross-section based on nearby insulation.
    /// Implements the "lay flat" rule: larger dimension aligns with nearest wall.
    /// </summary>
    private CrossSectionOrientation DetermineOrientation(VoxelPos anchor, VoxelDirection direction)
    {
        if (_crossSection.IsSquare)
            return CrossSectionOrientation.Flat; // Doesn't matter for square

        var (firstAxis, secondAxis) = direction.GetPerpendicularAxes();

        // Check distance to insulation along each perpendicular axis
        int distFirst = DistanceToInsulation(anchor, firstAxis);
        int distSecond = DistanceToInsulation(anchor, secondAxis);

        // "Lay flat" means Width (smaller) perpendicular to nearest surface,
        // Height (larger) parallel to it (spread along the surface).
        // Flat: Width on first axis, Height on second
        // Upright: Height on first axis, Width on second

        if (distFirst <= distSecond)
        {
            // Insulation closer on first axis - put width there (perpendicular)
            return CrossSectionOrientation.Flat;
        }
        else
        {
            // Insulation closer on second axis - put width there
            return CrossSectionOrientation.Upright;
        }
    }

    /// <summary>
    /// Finds distance to nearest insulation along an axis.
    /// </summary>
    private int DistanceToInsulation(VoxelPos pos, int axis)
    {
        int maxCheck = _crossSection.Height + 2; // Don't search too far

        for (int dist = 1; dist <= maxCheck; dist++)
        {
            var checkPos = axis switch
            {
                0 => pos.Offset(dist, 0, 0),
                1 => pos.Offset(0, dist, 0),
                _ => pos.Offset(0, 0, dist)
            };

            if (_cache.GetState(checkPos) == CacheVoxelState.Insulation)
                return dist;

            checkPos = axis switch
            {
                0 => pos.Offset(-dist, 0, 0),
                1 => pos.Offset(0, -dist, 0),
                _ => pos.Offset(0, 0, -dist)
            };

            if (_cache.GetState(checkPos) == CacheVoxelState.Insulation)
                return dist;
        }

        return maxCheck + 1; // Far away
    }

    /// <summary>
    /// Reconstructs the path from start to the given node.
    /// </summary>
    private List<VoxelPos> ReconstructPath(Dictionary<SearchNode, SearchNode> cameFrom, SearchNode end)
    {
        var path = new List<VoxelPos>();
        var current = end;

        while (true)
        {
            var orientation = DetermineOrientation(current.Position, current.Direction);
            foreach (var pos in _crossSection.GetVoxelPositions(current.Position, current.Direction, orientation))
            {
                if (!path.Contains(pos))
                    path.Add(pos);
            }

            if (!cameFrom.TryGetValue(current, out var prev))
                break;
            current = prev;
        }

        return path;
    }

    /// <summary>
    /// Reconstructs the path and marks voxels as CableConductor in cache.
    /// </summary>
    private List<VoxelPos> ReconstructPathAndMark(Dictionary<SearchNode, SearchNode> cameFrom, SearchNode end)
    {
        var path = ReconstructPath(cameFrom, end);
        foreach (var pos in path)
        {
            _cache.SetCableConductor(pos);
        }
        return path;
    }

    private static float Heuristic(VoxelPos from, VoxelPos to) =>
        ManhattanDistance(from, to);

    private static int ManhattanDistance(VoxelPos a, VoxelPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    /// <summary>
    /// Internal search node for A*.
    /// </summary>
    private readonly record struct SearchNode(
        VoxelPos Position,
        VoxelDirection Direction,
        int StepsSinceTurn);
}
