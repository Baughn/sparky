using System;

namespace Sparky.Voxel;

/// <summary>
/// 3D block position following Vintage Story conventions.
/// <list type="bullet">
/// <item><description>X: East (+) / West (-)</description></item>
/// <item><description>Y: Up (+) / Down (-) — vertical axis</description></item>
/// <item><description>Z: South (+) / North (-)</description></item>
/// </list>
/// </summary>
public readonly record struct BlockPos(int X, int Y, int Z) {
    /// <summary>The origin (0, 0, 0).</summary>
    public static BlockPos Zero => new(0, 0, 0);

    /// <summary>
    /// Returns the neighboring block position in the given direction.
    /// </summary>
    public BlockPos Neighbor(BlockFacing facing) {
        var (dx, dy, dz) = facing.Normal();
        return new BlockPos(X + dx, Y + dy, Z + dz);
    }

    /// <summary>
    /// Returns this position offset by (dx, dy, dz).
    /// </summary>
    public BlockPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    /// <summary>
    /// Returns the Manhattan distance to another position.
    /// </summary>
    public int ManhattanDistance(BlockPos other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y) + Math.Abs(Z - other.Z);

    public override string ToString() => $"({X}, {Y}, {Z})";
}
