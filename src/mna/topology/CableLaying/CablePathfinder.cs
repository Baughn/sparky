using Sparky.Voxel;

namespace Sparky.Mna.Topology.CableLaying;

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
public class CablePathfinder {
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
    /// Maximum iterations before giving up. Prevents runaway searches.
    /// 50000 is enough for ~96 voxel radius pathfinding bounds.
    /// </summary>
    private const int MaxIterations = 50000;

    /// <summary>
    /// Optional logging action. Set this to receive debug logs from pathfinding.
    /// </summary>
    public static Action<string>? Log { get; set; }

    public CablePathfinder(IWorldVoxelCache cache, CrossSection crossSection) {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _crossSection = crossSection;
    }

    /// <summary>
    /// Finds a path from start positions to goal.
    /// </summary>
    /// <param name="startPositions">The starting voxel positions (the cable cross-section).</param>
    /// <param name="goal">Goal voxel position.</param>
    /// <returns>PathResult with the best path found, starting with the exact input positions.</returns>
    public PathResult FindPath(IReadOnlyList<VoxelPos> startPositions, VoxelPos goal) {
        if (startPositions.Count == 0)
            return PathResult.NoProgress(new VoxelPos(0, 0, 0), goal);

        var anchor = startPositions[0];
        Log?.Invoke($"[Pathfinder] FindPath: startPositions={startPositions.Count}, anchor={anchor}, goal={goal}, crossSection={_crossSection}");

        _cache.ClearCableConductors();

        // Early exit if goal is way outside pathfinding bounds (no point searching)
        if (!_cache.IsInPathfindingBounds(goal)) {
            Log?.Invoke($"[Pathfinder] Goal outside pathfinding bounds, returning NoProgress");
            return PathResult.NoProgress(anchor, goal);
        }

        // Find all configurations that match the positions
        var configs = InferAllConfigurations(startPositions);
        if (configs.Count == 0) {
            Log?.Invoke($"[Pathfinder] Could not infer any configuration from positions, returning NoProgress");
            return PathResult.NoProgress(anchor, goal);
        }

        // Priority queue: (priority, node)
        var openSet = new PriorityQueue<SearchNode, float>();
        var cameFrom = new Dictionary<SearchNode, SearchNode>();
        var gScore = new Dictionary<SearchNode, float>();
        var visited = new HashSet<SearchNode>();

        // Track best partial result
        SearchNode? bestNode = null;
        int bestDistance = int.MaxValue;
        float bestGScore = float.MaxValue;

        // Try all matching configurations as start nodes (validates and marks positions)
        int validStarts = 0;
        foreach (var (direction, orientation) in configs) {
            var (success, _) = TryPlaceCrossSection(anchor, direction, isStart: true);
            if (success) {
                var startNode = new SearchNode(anchor, direction, orientation, 0);
                openSet.Enqueue(startNode, Heuristic(anchor, goal));
                gScore[startNode] = 0;
                validStarts++;
            }
        }

        Log?.Invoke($"[Pathfinder] Tried {configs.Count} configs, {validStarts} valid starts");

        if (validStarts == 0) {
            Log?.Invoke($"[Pathfinder] NoProgress: no valid start positions");
            return PathResult.NoProgress(anchor, goal);
        }

        int iterations = 0;
        int neighborsRejectedByVisited = 0;
        int neighborsRejectedByPlacement = 0;
        int neighborsAccepted = 0;

        while (openSet.Count > 0) {
            var current = openSet.Dequeue();
            iterations++;

            // Prevent runaway searches
            if (iterations > MaxIterations) {
                Log?.Invoke($"[Pathfinder] Hit iteration limit ({MaxIterations}), returning best partial");
                break;
            }

            if (visited.Contains(current))
                continue;
            visited.Add(current);

            // Track best partial result
            int distToGoal = ManhattanDistance(current.Position, goal);
            float currentG = gScore.GetValueOrDefault(current, float.MaxValue);

            if (distToGoal < bestDistance ||
                (distToGoal == bestDistance && currentG < bestGScore)) {
                bestDistance = distToGoal;
                bestGScore = currentG;
                bestNode = current;
            }

            // Check if we've reached the goal
            if (IsAtGoal(current.Position, goal)) {
                var path = ReconstructPath(cameFrom, current);
                Log?.Invoke($"[Pathfinder] Complete: iterations={iterations}, pathLength={path.Count}, visited={visited.Count}");
                return PathResult.Complete(path, goal);
            }

            // Check bounds
            if (!_cache.IsInPathfindingBounds(current.Position)) {
                // Spammy: Log?.Invoke($"[Pathfinder] Node {current.Position} outside bounds, skipping");
                continue;
            }

            // Generate neighbors
            foreach (var neighbor in GetNeighbors(current)) {
                if (visited.Contains(neighbor)) {
                    neighborsRejectedByVisited++;
                    continue;
                }

                // Try to place cross-section at neighbor position
                var (canPlace, distToInsulation) = CanPlaceCrossSection(neighbor.Position, neighbor.Direction, neighbor.Orientation);
                if (!canPlace) {
                    neighborsRejectedByPlacement++;
                    continue;
                }

                neighborsAccepted++;

                // Base cost + distance penalty (0 if adjacent, increases for further distances)
                float stepCost = 1.0f + Math.Max(0, distToInsulation - 1) * DistancePenalty;
                float tentativeG = currentG + stepCost;
                if (neighbor.Direction != current.Direction)
                    tentativeG += TurnPenalty;

                if (tentativeG < gScore.GetValueOrDefault(neighbor, float.MaxValue)) {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    float f = tentativeG + Heuristic(neighbor.Position, goal);
                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        Log?.Invoke($"[Pathfinder] Search exhausted: iterations={iterations}, visited={visited.Count}, accepted={neighborsAccepted}, rejectedVisited={neighborsRejectedByVisited}, rejectedPlacement={neighborsRejectedByPlacement}");

        // No complete path found - return best partial
        if (bestNode.HasValue && bestDistance < ManhattanDistance(anchor, goal)) {
            // Reconstruct path to best node
            _cache.ClearCableConductors();
            var path = ReconstructPathAndMark(cameFrom, bestNode.Value);
            Log?.Invoke($"[Pathfinder] Partial: pathLength={path.Count}, bestDist={bestDistance}, endPos={bestNode.Value.Position}");
            return PathResult.Partial(path, bestNode.Value.Position, goal);
        }

        Log?.Invoke($"[Pathfinder] NoProgress: bestDist={bestDistance}, startDist={ManhattanDistance(anchor, goal)}");
        return PathResult.NoProgress(anchor, goal);
    }

    /// <summary>
    /// Infers all matching travel direction and orientation combinations from a set of voxel positions.
    /// For 1x1 cross-sections, all 6 directions match (since they produce the same single position).
    /// For larger cross-sections, typically only one or two configurations match.
    /// </summary>
    private List<(VoxelDirection Direction, CrossSectionOrientation Orientation)> InferAllConfigurations(
        IReadOnlyList<VoxelPos> positions) {
        var result = new List<(VoxelDirection, CrossSectionOrientation)>();

        if (positions.Count == 0)
            return result;

        var positionSet = positions.ToHashSet();
        var anchor = positions[0];

        foreach (var dir in VoxelDirectionExtensions.All) {
            foreach (var orient in new[] { CrossSectionOrientation.Flat, CrossSectionOrientation.Upright }) {
                var generated = _crossSection.GetVoxelPositions(anchor, dir, orient).ToHashSet();
                if (generated.SetEquals(positionSet))
                    result.Add((dir, orient));
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if the current position is at or covers the goal.
    /// </summary>
    private bool IsAtGoal(VoxelPos current, VoxelPos goal) {
        // For simplicity, check if goal is within one voxel of current
        // A more sophisticated check would verify the cross-section covers the goal
        return ManhattanDistance(current, goal) <= 1;
    }

    /// <summary>
    /// Gets valid neighbor nodes from the current node.
    /// </summary>
    private IEnumerable<SearchNode> GetNeighbors(SearchNode current) {
        int minTurnDist = _crossSection.MinTurnDistance;

        foreach (var dir in VoxelDirectionExtensions.All) {
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

            // Compute orientation: preserve for straight moves, compute continuity for turns
            var newOrient = isTurn
                ? GetOrientationAfterTurn(current.Direction, current.Orientation, dir)
                : current.Orientation;

            yield return new SearchNode(newPos, dir, newOrient, newSteps);
        }
    }

    /// <summary>
    /// Checks if a cross-section can be placed at the given position.
    /// Returns validity and minimum distance to insulation for cost calculation.
    /// </summary>
    /// <returns>Tuple of (valid, minDistanceToInsulation). Distance is 1 if adjacent.</returns>
    private (bool Valid, int MinDistance) CanPlaceCrossSection(VoxelPos anchor, VoxelDirection direction, CrossSectionOrientation orientation) {
        int maxDist = 2 * _crossSection.Height;
        int minDistance = int.MaxValue;

        foreach (var pos in _crossSection.GetVoxelPositions(anchor, direction, orientation)) {
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
    /// <returns>Success flag and the orientation used.</returns>
    private (bool Success, CrossSectionOrientation Orientation) TryPlaceCrossSection(VoxelPos anchor, VoxelDirection direction, bool isStart) {
        var orientation = DetermineOrientation(anchor, direction);
        var positions = _crossSection.GetVoxelPositions(anchor, direction, orientation).ToList();
        int maxDist = 2 * _crossSection.Height;
        int minDistance = int.MaxValue;

        // First pass: validate
        foreach (var pos in positions) {
            var state = _cache.GetState(pos);

            if (state != CacheVoxelState.Empty && state != CacheVoxelState.CableConductor) {
                Log?.Invoke($"[Pathfinder] TryPlace FAIL: pos={pos} state={state} (need Empty/CableConductor)");
                return (false, orientation);
            }

            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.PreExistingConductor)) {
                Log?.Invoke($"[Pathfinder] TryPlace FAIL: pos={pos} adjacent to PreExistingConductor");
                return (false, orientation);
            }

            // Find distance to support
            int dist = _cache.DistanceToInsulation(pos, maxDist);
            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.CableConductor))
                dist = Math.Min(dist, 1);

            minDistance = Math.Min(minDistance, dist);
        }

        // For start, require adjacent insulation (distance 1) - no starting from corners
        if (isStart && !positions.Any(p => _cache.AnyCardinalNeighbor(p, CacheVoxelState.Insulation))) {
            Log?.Invoke($"[Pathfinder] TryPlace FAIL: isStart but no adjacent insulation. minDist={minDistance}");
            return (false, orientation);
        }

        // Must have support within extended range
        if (minDistance > maxDist) {
            Log?.Invoke($"[Pathfinder] TryPlace FAIL: no support within range. minDist={minDistance}, maxDist={maxDist}");
            return (false, orientation);
        }

        // Second pass: mark
        foreach (var pos in positions) {
            _cache.SetCableConductor(pos);
        }

        return (true, orientation);
    }

    /// <summary>
    /// Determines the best orientation for the cross-section based on nearby insulation.
    /// Implements the "lay flat" rule: larger dimension aligns with nearest wall.
    /// </summary>
    private CrossSectionOrientation DetermineOrientation(VoxelPos anchor, VoxelDirection direction) {
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

        if (distFirst <= distSecond) {
            // Insulation closer on first axis - put width there (perpendicular)
            return CrossSectionOrientation.Flat;
        } else {
            // Insulation closer on second axis - put width there
            return CrossSectionOrientation.Upright;
        }
    }

    /// <summary>
    /// Finds distance to nearest insulation along an axis.
    /// </summary>
    private int DistanceToInsulation(VoxelPos pos, int axis) {
        int maxCheck = _crossSection.Height + 2; // Don't search too far

        for (int dist = 1; dist <= maxCheck; dist++) {
            var checkPos = axis switch {
                0 => pos.Offset(dist, 0, 0),
                1 => pos.Offset(0, dist, 0),
                _ => pos.Offset(0, 0, dist)
            };

            if (_cache.GetState(checkPos) == CacheVoxelState.Insulation)
                return dist;

            checkPos = axis switch {
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
    private List<VoxelPos> ReconstructPath(Dictionary<SearchNode, SearchNode> cameFrom, SearchNode end) {
        var path = new List<VoxelPos>();
        var current = end;

        while (true) {
            // Use the orientation stored in the SearchNode (computed during search for continuity)
            foreach (var pos in _crossSection.GetVoxelPositions(current.Position, current.Direction, current.Orientation)) {
                if (!path.Contains(pos))
                    path.Add(pos);
            }

            if (!cameFrom.TryGetValue(current, out var prev))
                break;

            // At turns, add corner voxels to ensure W×H connectivity with both segments
            if (current.Direction != prev.Direction) {
                AddCornerVoxels(path, prev, current);
            }

            current = prev;
        }

        return path;
    }

    /// <summary>
    /// Adds corner voxels at a turn to ensure proper W×H connectivity.
    /// Fills the bounding box of the incoming and outgoing cross-sections.
    /// </summary>
    private void AddCornerVoxels(List<VoxelPos> path, SearchNode incoming, SearchNode outgoing) {
        // Get the voxels for both cross-sections at the turn point
        var incomingVoxels = _crossSection.GetVoxelPositions(
            incoming.Position, incoming.Direction, incoming.Orientation).ToList();
        var outgoingVoxels = _crossSection.GetVoxelPositions(
            outgoing.Position, outgoing.Direction, outgoing.Orientation).ToList();

        // Compute bounding box of the union
        var allVoxels = incomingVoxels.Concat(outgoingVoxels).ToList();

        int minX = allVoxels.Min(v => v.X);
        int maxX = allVoxels.Max(v => v.X);
        int minY = allVoxels.Min(v => v.Y);
        int maxY = allVoxels.Max(v => v.Y);
        int minZ = allVoxels.Min(v => v.Z);
        int maxZ = allVoxels.Max(v => v.Z);

        // Fill the bounding box (creates a solid corner block)
        for (int x = minX; x <= maxX; x++) {
            for (int y = minY; y <= maxY; y++) {
                for (int z = minZ; z <= maxZ; z++) {
                    var pos = new VoxelPos(x, y, z);
                    if (!path.Contains(pos))
                        path.Add(pos);
                }
            }
        }
    }

    /// <summary>
    /// Reconstructs the path and marks voxels as CableConductor in cache.
    /// </summary>
    private List<VoxelPos> ReconstructPathAndMark(Dictionary<SearchNode, SearchNode> cameFrom, SearchNode end) {
        var path = ReconstructPath(cameFrom, end);
        foreach (var pos in path) {
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
        CrossSectionOrientation Orientation,
        int StepsSinceTurn);

    /// <summary>
    /// Computes the orientation after a 90° turn that maintains cross-section continuity.
    /// The shared perpendicular axis between old and new directions must have the same dimension.
    /// </summary>
    private CrossSectionOrientation GetOrientationAfterTurn(
        VoxelDirection oldDir,
        CrossSectionOrientation oldOrient,
        VoxelDirection newDir) {
        if (_crossSection.IsSquare)
            return CrossSectionOrientation.Flat; // Doesn't matter for square

        var (oldFirst, oldSecond) = oldDir.GetPerpendicularAxes();
        var (newFirst, newSecond) = newDir.GetPerpendicularAxes();

        // Find the shared perpendicular axis
        int sharedAxis;
        if (oldFirst == newFirst || oldFirst == newSecond)
            sharedAxis = oldFirst;
        else
            sharedAxis = oldSecond;

        // Determine what dimension the old orientation had on the shared axis
        bool oldSharedIsFirst = (sharedAxis == oldFirst);
        int oldDimOnShared = oldSharedIsFirst
            ? (oldOrient == CrossSectionOrientation.Flat ? _crossSection.Width : _crossSection.Height)
            : (oldOrient == CrossSectionOrientation.Flat ? _crossSection.Height : _crossSection.Width);

        // Determine what orientation the new direction needs to match that dimension
        bool newSharedIsFirst = (sharedAxis == newFirst);

        // For new direction with Flat: first axis has Width, second has Height
        // For new direction with Upright: first axis has Height, second has Width
        if (newSharedIsFirst) {
            // Shared axis is the first perpendicular axis of new direction
            // Flat → Width on first, Upright → Height on first
            return (oldDimOnShared == _crossSection.Width)
                ? CrossSectionOrientation.Flat
                : CrossSectionOrientation.Upright;
        } else {
            // Shared axis is the second perpendicular axis of new direction
            // Flat → Height on second, Upright → Width on second
            return (oldDimOnShared == _crossSection.Height)
                ? CrossSectionOrientation.Flat
                : CrossSectionOrientation.Upright;
        }
    }
}
