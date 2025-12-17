using System;
using System.Collections.Generic;
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
public class CableLayingState {
    /// <summary>
    /// Current phase of cable laying.
    /// </summary>
    public enum Phase {
        /// <summary>Waiting for first click to select start position.</summary>
        Idle,
        /// <summary>Start selected, waiting for end position or path computation.</summary>
        StartSelected,
        /// <summary>Path computed and ready for placement.</summary>
        PathReady
    }

    /// <summary>
    /// Optional logging action for debugging. Set from VS integration code.
    /// </summary>
    public static Action<string>? Log { get; set; }

    private readonly CrossSection _crossSection;
    private Phase _currentPhase = Phase.Idle;
    private IReadOnlyList<VoxelPos>? _startPositions;
    private WorldVoxelCache? _cache;
    private CablePathfinder? _pathfinder;
    private PathResult? _currentPath;

    // Background pathfinding
    private Task<PathResult>? _pendingPathfind;
    private VoxelPos _lastGoalQueried;
    private readonly object _pathfindLock = new();

    // Snap position cache (for preview optimization)
    private VoxelPos? _lastSnapQueryPos;
    private IReadOnlyList<VoxelPos>? _cachedSnappedPositions;

    /// <summary>
    /// Creates a new cable laying state for the given cross-section.
    /// </summary>
    public CableLayingState(CrossSection crossSection) {
        _crossSection = crossSection;
    }

    /// <summary>Current phase of the cable laying process.</summary>
    public Phase CurrentPhase => _currentPhase;

    /// <summary>The selected start positions, if any.</summary>
    public IReadOnlyList<VoxelPos>? StartPositions => _startPositions;

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
    public void SelectStart(VoxelPos clickedPos, IBlockAccessor blockAccessor) {
        // Build cache centered on clicked position
        var centerBlock = new VSBlockPos(
            clickedPos.X / 16,
            clickedPos.Y / 16,
            clickedPos.Z / 16);
        _cache = new WorldVoxelCache(blockAccessor, centerBlock);

        // Find the best start positions within a few voxels of clicked position
        var positions = SnapPositionFinder.FindBestPosition(clickedPos, _cache, _crossSection, 0f);
        _startPositions = positions;

        // If snapping to existing cable, mark connected cable voxels as "our cable"
        // so pathfinder doesn't reject adjacency to them
        if (IsAdjacentToPreExistingConductor(positions)) {
            _cache.MarkConnectedConductorAsCable(positions[0], maxDistance: 4);
        }

        _pathfinder = new CablePathfinder(_cache, _crossSection);
        _currentPhase = Phase.StartSelected;
        _currentPath = null;
    }

    /// <summary>
    /// Checks if any of the positions are adjacent to pre-existing conductor.
    /// </summary>
    private bool IsAdjacentToPreExistingConductor(IReadOnlyList<VoxelPos> positions) {
        if (_cache == null)
            return false;

        foreach (var pos in positions) {
            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.PreExistingConductor))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the snapped start positions for preview purposes without committing to it.
    /// Use this in Idle phase to show where the cable would actually start.
    /// Results are cached when the query position hasn't moved.
    /// </summary>
    /// <param name="clickedPos">The position the player is targeting.</param>
    /// <param name="blockAccessor">Block accessor for checking world state.</param>
    /// <returns>The voxel positions where the cable would start.</returns>
    public IReadOnlyList<VoxelPos> GetSnappedStartPositions(
        VoxelPos clickedPos,
        IBlockAccessor blockAccessor) {
        // Return cached result if position hasn't moved
        if (_lastSnapQueryPos.HasValue && clickedPos == _lastSnapQueryPos.Value && _cachedSnappedPositions != null) {
            return _cachedSnappedPositions;
        }

        // Build small cache (1-block radius = 27 blocks vs 2744 for full cache)
        var centerBlock = new VSBlockPos(
            clickedPos.X / 16,
            clickedPos.Y / 16,
            clickedPos.Z / 16);
        var tempCache = new WorldVoxelCache(blockAccessor, centerBlock, radius: 1);

        var positions = SnapPositionFinder.FindBestPosition(clickedPos, tempCache, _crossSection, 0f);

        // Cache the result
        _lastSnapQueryPos = clickedPos;
        _cachedSnappedPositions = positions;

        return positions;
    }

    /// <summary>
    /// Updates the goal position and triggers pathfinding.
    /// Call this when the player's cursor moves.
    /// </summary>
    /// <param name="goal">The target voxel position.</param>
    public void UpdateGoal(VoxelPos goal) {
        // Allow updates in both StartSelected and PathReady phases
        if (_currentPhase == Phase.Idle || _startPositions == null || _pathfinder == null)
            return;

        // Don't recompute if goal hasn't changed
        if (goal == _lastGoalQueried)
            return;

        // Start background pathfinding (only if not already running)
        lock (_pathfindLock) {
            // Don't start new task if one is still running - wait for it to complete
            if (_pendingPathfind != null && !_pendingPathfind.IsCompleted)
                return;

            _lastGoalQueried = goal;
            var startPositions = _startPositions;
            var pathfinder = _pathfinder;

            _pendingPathfind = Task.Run(() => pathfinder.FindPath(startPositions, goal));
        }
    }

    /// <summary>
    /// Checks if pathfinding is complete and updates the current path.
    /// Call this each tick to poll for results.
    /// </summary>
    /// <returns>True if a new path result is available.</returns>
    public bool TryUpdatePath() {
        lock (_pathfindLock) {
            if (_pendingPathfind == null)
                return false;

            if (!_pendingPathfind.IsCompleted)
                return false;

            try {
                var result = _pendingPathfind.Result;
                _currentPath = result;
                _currentPhase = result.Type != PathResultType.NoProgress
                    ? Phase.PathReady
                    : Phase.StartSelected;
            } catch (Exception ex) {
                // Log the actual error instead of silently swallowing
                Log?.Invoke($"[CableLayingState] Pathfinding failed: {ex.Message}");
                _currentPath = null;
            } finally {
                _pendingPathfind = null;
            }

            return true;
        }
    }

    /// <summary>
    /// Resets the state machine to idle.
    /// </summary>
    public void Cancel() {
        _currentPhase = Phase.Idle;
        _startPositions = null;
        _cache = null;
        _pathfinder = null;
        _currentPath = null;
        _pendingPathfind = null;
    }

}
