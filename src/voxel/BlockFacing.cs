using System;

namespace Sparky.Voxel;

/// <summary>
/// Which face of a block (6 possibilities).
/// Follows Vintage Story conventions: Y is vertical (up/down).
/// </summary>
public enum BlockFacing {
    /// <summary>Negative Z direction.</summary>
    North = 0,

    /// <summary>Positive X direction.</summary>
    East = 1,

    /// <summary>Positive Z direction.</summary>
    South = 2,

    /// <summary>Negative X direction.</summary>
    West = 3,

    /// <summary>Positive Y direction (up).</summary>
    Up = 4,

    /// <summary>Negative Y direction (down).</summary>
    Down = 5,
}

/// <summary>
/// Extension methods for <see cref="BlockFacing"/>.
/// </summary>
public static class BlockFacingExtensions {
    private static readonly BlockFacing[] Opposites =
    {
        BlockFacing.South, // North -> South
        BlockFacing.West, // East -> West
        BlockFacing.North, // South -> North
        BlockFacing.East, // West -> East
        BlockFacing.Down, // Up -> Down
        BlockFacing.Up, // Down -> Up
    };

    /// <summary>
    /// Returns the opposite facing (e.g., North -> South, Up -> Down).
    /// </summary>
    public static BlockFacing Opposite(this BlockFacing facing) => Opposites[(int)facing];

    /// <summary>
    /// Returns true if this is a horizontal facing (North, East, South, West).
    /// </summary>
    public static bool IsHorizontal(this BlockFacing facing) => facing <= BlockFacing.West;

    /// <summary>
    /// Returns true if this is a vertical facing (Up, Down).
    /// </summary>
    public static bool IsVertical(this BlockFacing facing) => facing >= BlockFacing.Up;

    /// <summary>
    /// Returns the normal vector as (dx, dy, dz) integers.
    /// </summary>
    public static (int dx, int dy, int dz) Normal(this BlockFacing facing) =>
        facing switch {
            BlockFacing.North => (0, 0, -1),
            BlockFacing.East => (1, 0, 0),
            BlockFacing.South => (0, 0, 1),
            BlockFacing.West => (-1, 0, 0),
            BlockFacing.Up => (0, 1, 0),
            BlockFacing.Down => (0, -1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(facing)),
        };

    /// <summary>
    /// All six facings in order: North, East, South, West, Up, Down.
    /// </summary>
    public static readonly BlockFacing[] All =
    {
        BlockFacing.North,
        BlockFacing.East,
        BlockFacing.South,
        BlockFacing.West,
        BlockFacing.Up,
        BlockFacing.Down,
    };

    /// <summary>
    /// The four horizontal facings: North, East, South, West.
    /// </summary>
    public static readonly BlockFacing[] Horizontal =
    {
        BlockFacing.North,
        BlockFacing.East,
        BlockFacing.South,
        BlockFacing.West,
    };
}
