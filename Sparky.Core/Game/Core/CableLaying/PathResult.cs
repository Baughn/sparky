namespace Sparky.Game.Core.CableLaying;

/// <summary>
/// The type of pathfinding result.
/// </summary>
public enum PathResultType
{
    /// <summary>Path reaches the goal exactly.</summary>
    Complete,

    /// <summary>Path gets closer to goal but cannot reach it. Player can finish with 1×1 tool.</summary>
    Partial,

    /// <summary>No progress possible from start position (blocked or invalid).</summary>
    NoProgress
}

/// <summary>
/// Result of a cable pathfinding operation.
/// </summary>
/// <param name="Type">The result type (Complete, Partial, or NoProgress).</param>
/// <param name="Path">All voxel positions in the cable path.</param>
/// <param name="EndPosition">Where the path ends (may not be the goal for Partial results).</param>
/// <param name="DistanceToGoal">Manhattan distance from end position to goal.</param>
public readonly record struct PathResult(
    PathResultType Type,
    IReadOnlyList<VoxelPos> Path,
    VoxelPos EndPosition,
    int DistanceToGoal)
{
    /// <summary>Creates a Complete result.</summary>
    public static PathResult Complete(IReadOnlyList<VoxelPos> path, VoxelPos goal) =>
        new(PathResultType.Complete, path, goal, 0);

    /// <summary>Creates a Partial result.</summary>
    public static PathResult Partial(IReadOnlyList<VoxelPos> path, VoxelPos endPos, VoxelPos goal) =>
        new(PathResultType.Partial, path, endPos, ManhattanDistance(endPos, goal));

    /// <summary>Creates a NoProgress result.</summary>
    public static PathResult NoProgress(VoxelPos start, VoxelPos goal) =>
        new(PathResultType.NoProgress, Array.Empty<VoxelPos>(), start, ManhattanDistance(start, goal));

    private static int ManhattanDistance(VoxelPos a, VoxelPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
}
