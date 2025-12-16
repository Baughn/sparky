using System;
using System.Threading.Tasks;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;
using Vintagestory.API.Common;
using VSBlockPos = Vintagestory.API.MathTools.BlockPos;

namespace Sparky.VSIntegration.CableLaying;

/// <summary>
/// State machine for cable laying workflow.
/// Tracks the two-click process: select start, then select end to place cable.
/// </summary>
public class CableLayingState
{
    /// <summary>
    /// Current phase of cable laying.
    /// </summary>
    public enum Phase
    {
        /// <summary>Waiting for first click to select start position.</summary>
        Idle,
        /// <summary>Start selected, waiting for end position or path computation.</summary>
        StartSelected,
        /// <summary>Path computed and ready for placement.</summary>
        PathReady
    }

    private readonly CrossSection _crossSection;
    private Phase _currentPhase = Phase.Idle;
    private VoxelPos? _startPosition;
    private VoxelDirection? _startDirection;
    private WorldVoxelCache? _cache;
    private CablePathfinder? _pathfinder;
    private PathResult? _currentPath;

    // Background pathfinding
    private Task<PathResult>? _pendingPathfind;
    private VoxelPos _lastGoalQueried;
    private readonly object _pathfindLock = new();

    // Snap position cache (for preview optimization)
    private VoxelPos? _lastSnapQueryPos;
    private VoxelPos _cachedSnappedPos;
    private VoxelDirection? _cachedSnappedDir;

    /// <summary>
    /// Creates a new cable laying state for the given cross-section.
    /// </summary>
    public CableLayingState(CrossSection crossSection)
    {
        _crossSection = crossSection;
    }

    /// <summary>Current phase of the cable laying process.</summary>
    public Phase CurrentPhase => _currentPhase;

    /// <summary>The selected start position, if any.</summary>
    public VoxelPos? StartPosition => _startPosition;

    /// <summary>The inherited direction from snapping to existing cable, if any.</summary>
    public VoxelDirection? StartDirection => _startDirection;

    /// <summary>The current computed path, if any.</summary>
    public PathResult? CurrentPath => _currentPath;

    /// <summary>The cross-section for this cable laying operation.</summary>
    public CrossSection CrossSection => _crossSection;

    /// <summary>
    /// Selects the start position for cable laying.
    /// Searches nearby positions to find the best starting point.
    /// </summary>
    /// <param name="clickedPos">The voxel position the player clicked.</param>
    /// <param name="blockAccessor">Block accessor for building the cache.</param>
    public void SelectStart(VoxelPos clickedPos, IBlockAccessor blockAccessor)
    {
        // Build cache centered on clicked position
        var centerBlock = new VSBlockPos(
            clickedPos.X / 16,
            clickedPos.Y / 16,
            clickedPos.Z / 16);
        _cache = new WorldVoxelCache(blockAccessor, centerBlock);

        // Find the best start position within 2 voxels of clicked position
        var (bestPos, bestDir) = FindBestStartPosition(clickedPos, _cache);

        // If snapping to existing cable, mark connected cable voxels as "our cable"
        // so pathfinder doesn't reject adjacency to them
        if (bestDir.HasValue)
        {
            _cache.MarkConnectedConductorAsCable(bestPos, maxDistance: 4);
        }

        _startPosition = bestPos;
        _startDirection = bestDir;
        _pathfinder = new CablePathfinder(_cache, _crossSection);
        _currentPhase = Phase.StartSelected;
        _currentPath = null;
    }

    /// <summary>
    /// Gets the snapped start position for preview purposes without committing to it.
    /// Use this in Idle phase to show where the cable would actually start.
    /// Results are cached when the query position hasn't moved much.
    /// </summary>
    /// <param name="clickedPos">The position the player is targeting.</param>
    /// <param name="blockAccessor">Block accessor for checking world state.</param>
    /// <returns>The snapped position and inherited direction (if any).</returns>
    public (VoxelPos Position, VoxelDirection? Direction) GetSnappedStartPosition(
        VoxelPos clickedPos,
        IBlockAccessor blockAccessor)
    {
        // Return cached result if position hasn't moved much (within 1 voxel)
        if (_lastSnapQueryPos.HasValue)
        {
            int dist = Math.Abs(clickedPos.X - _lastSnapQueryPos.Value.X) +
                       Math.Abs(clickedPos.Y - _lastSnapQueryPos.Value.Y) +
                       Math.Abs(clickedPos.Z - _lastSnapQueryPos.Value.Z);
            if (dist == 0)
            {
                return (_cachedSnappedPos, _cachedSnappedDir);
            }
        }

        // Build small cache (1-block radius = 27 blocks vs 2744 for full cache)
        var centerBlock = new VSBlockPos(
            clickedPos.X / 16,
            clickedPos.Y / 16,
            clickedPos.Z / 16);
        var tempCache = new WorldVoxelCache(blockAccessor, centerBlock, radius: 1);

        var (snappedPos, snappedDir) = FindBestStartPosition(clickedPos, tempCache);

        // Cache the result
        _lastSnapQueryPos = clickedPos;
        _cachedSnappedPos = snappedPos;
        _cachedSnappedDir = snappedDir;

        return (snappedPos, snappedDir);
    }

    /// <summary>
    /// Updates the goal position and triggers pathfinding.
    /// Call this when the player's cursor moves.
    /// </summary>
    /// <param name="goal">The target voxel position.</param>
    public void UpdateGoal(VoxelPos goal)
    {
        // Allow updates in both StartSelected and PathReady phases
        if (_currentPhase == Phase.Idle || _startPosition == null || _pathfinder == null)
            return;

        // Don't recompute if goal hasn't changed
        if (goal == _lastGoalQueried)
            return;
        _lastGoalQueried = goal;

        // Start background pathfinding
        lock (_pathfindLock)
        {
            var start = _startPosition.Value;
            var dir = _startDirection;
            var pathfinder = _pathfinder;

            _pendingPathfind = Task.Run(() => pathfinder.FindPath(start, goal, dir));
        }
    }

    /// <summary>
    /// Checks if pathfinding is complete and updates the current path.
    /// Call this each tick to poll for results.
    /// </summary>
    /// <returns>True if a new path result is available.</returns>
    public bool TryUpdatePath()
    {
        lock (_pathfindLock)
        {
            if (_pendingPathfind == null)
                return false;

            if (!_pendingPathfind.IsCompleted)
                return false;

            try
            {
                var result = _pendingPathfind.Result;
                _currentPath = result;
                _currentPhase = result.Type != PathResultType.NoProgress
                    ? Phase.PathReady
                    : Phase.StartSelected;
            }
            catch
            {
                // Pathfinding failed, stay in StartSelected
                _currentPath = null;
            }
            finally
            {
                _pendingPathfind = null;
            }

            return true;
        }
    }

    /// <summary>
    /// Resets the state machine to idle.
    /// </summary>
    public void Cancel()
    {
        _currentPhase = Phase.Idle;
        _startPosition = null;
        _startDirection = null;
        _cache = null;
        _pathfinder = null;
        _currentPath = null;
        _pendingPathfind = null;
    }

    /// <summary>
    /// Finds the best start position within 2 voxels of the clicked position.
    /// Scores positions based on support quality and existing cable proximity.
    /// Ties are broken by Euclidean distance to clicked position (closer wins).
    /// </summary>
    private (VoxelPos Position, VoxelDirection? Direction) FindBestStartPosition(
        VoxelPos clicked,
        IWorldVoxelCache cache)
    {
        VoxelPos bestPos = clicked;
        VoxelDirection? bestDir = null;
        int bestScore = ScorePosition(clicked, cache, out var clickedDir);
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
                    int score = ScorePosition(pos, cache, out var dir);
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
    private int ScorePosition(VoxelPos pos, IWorldVoxelCache cache, out VoxelDirection? inheritedDirection)
    {
        inheritedDirection = null;

        // Must be empty or within bounds
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

        // Penalty for being adjacent to conductor without connection intent
        // (might cause short circuit)
        // This is handled by the pathfinder, so no penalty here

        return score;
    }

    /// <summary>
    /// Attempts to determine the travel direction from an adjacent existing cable.
    /// </summary>
    private VoxelDirection? FindCableDirection(VoxelPos pos, IWorldVoxelCache cache)
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
