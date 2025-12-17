namespace Sparky.Game.Core;

/// <summary>
/// 3D voxel position at sub-block resolution.
/// <para>
/// Each VS block contains a 16×16×16 grid of voxels. This struct represents
/// absolute world coordinates at voxel resolution:
/// <list type="bullet">
/// <item><description>World voxel X = Block.X * 16 + local X (0-15)</description></item>
/// <item><description>World voxel Y = Block.Y * 16 + local Y (0-15)</description></item>
/// <item><description>World voxel Z = Block.Z * 16 + local Z (0-15)</description></item>
/// </list>
/// </para>
/// </summary>
/// <remarks>
/// VoxelPos is the primary coordinate system for the voxel-based connectivity model.
/// Adjacent conductor voxels automatically connect - topology is determined by physical adjacency.
/// </remarks>
public readonly record struct VoxelPos(int X, int Y, int Z) {
    /// <summary>The origin (0, 0, 0).</summary>
    public static VoxelPos Zero => new(0, 0, 0);

    /// <summary>Number of voxels per block axis.</summary>
    public const int VoxelsPerBlock = 16;

    /// <summary>
    /// Creates a VoxelPos from block coordinates and local offset.
    /// </summary>
    public static VoxelPos FromBlockLocal(BlockPos block, int localX, int localY, int localZ) =>
        new(
            block.X * VoxelsPerBlock + localX,
            block.Y * VoxelsPerBlock + localY,
            block.Z * VoxelsPerBlock + localZ
        );

    /// <summary>
    /// Gets the containing block position.
    /// </summary>
    public BlockPos Block => new(
        X >= 0 ? X / VoxelsPerBlock : (X - VoxelsPerBlock + 1) / VoxelsPerBlock,
        Y >= 0 ? Y / VoxelsPerBlock : (Y - VoxelsPerBlock + 1) / VoxelsPerBlock,
        Z >= 0 ? Z / VoxelsPerBlock : (Z - VoxelsPerBlock + 1) / VoxelsPerBlock
    );

    /// <summary>
    /// Gets the local position within the containing block (0-15 each axis).
    /// </summary>
    public (int X, int Y, int Z) Local {
        get {
            var block = Block;
            return (
                X - block.X * VoxelsPerBlock,
                Y - block.Y * VoxelsPerBlock,
                Z - block.Z * VoxelsPerBlock
            );
        }
    }

    /// <summary>
    /// Returns the neighboring voxel in the given direction.
    /// </summary>
    public VoxelPos Neighbor(VoxelDirection dir) => dir switch {
        VoxelDirection.XPos => new(X + 1, Y, Z),
        VoxelDirection.XNeg => new(X - 1, Y, Z),
        VoxelDirection.YPos => new(X, Y + 1, Z),
        VoxelDirection.YNeg => new(X, Y - 1, Z),
        VoxelDirection.ZPos => new(X, Y, Z + 1),
        VoxelDirection.ZNeg => new(X, Y, Z - 1),
        _ => this
    };

    /// <summary>
    /// Returns this position offset by (dx, dy, dz).
    /// </summary>
    public VoxelPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public override string ToString() => $"Voxel({X}, {Y}, {Z})";
}

/// <summary>
/// The 6 directions a voxel can connect to neighbors.
/// </summary>
public enum VoxelDirection {
    XPos,  // +X (East)
    XNeg,  // -X (West)
    YPos,  // +Y (Up)
    YNeg,  // -Y (Down)
    ZPos,  // +Z (South)
    ZNeg   // -Z (North)
}

/// <summary>
/// Extension methods for VoxelDirection.
/// </summary>
public static class VoxelDirectionExtensions {
    /// <summary>All 6 directions.</summary>
    public static readonly VoxelDirection[] All =
    [
        VoxelDirection.XPos, VoxelDirection.XNeg,
        VoxelDirection.YPos, VoxelDirection.YNeg,
        VoxelDirection.ZPos, VoxelDirection.ZNeg
    ];

    /// <summary>
    /// Returns the opposite direction.
    /// </summary>
    public static VoxelDirection Opposite(this VoxelDirection dir) => dir switch {
        VoxelDirection.XPos => VoxelDirection.XNeg,
        VoxelDirection.XNeg => VoxelDirection.XPos,
        VoxelDirection.YPos => VoxelDirection.YNeg,
        VoxelDirection.YNeg => VoxelDirection.YPos,
        VoxelDirection.ZPos => VoxelDirection.ZNeg,
        VoxelDirection.ZNeg => VoxelDirection.ZPos,
        _ => dir
    };

    /// <summary>
    /// Returns the unit offset for this direction.
    /// </summary>
    public static (int X, int Y, int Z) Offset(this VoxelDirection dir) => dir switch {
        VoxelDirection.XPos => (1, 0, 0),
        VoxelDirection.XNeg => (-1, 0, 0),
        VoxelDirection.YPos => (0, 1, 0),
        VoxelDirection.YNeg => (0, -1, 0),
        VoxelDirection.ZPos => (0, 0, 1),
        VoxelDirection.ZNeg => (0, 0, -1),
        _ => (0, 0, 0)
    };

    /// <summary>
    /// Returns the axis index (0=X, 1=Y, 2=Z) for this direction.
    /// </summary>
    public static int Axis(this VoxelDirection dir) => dir switch {
        VoxelDirection.XPos or VoxelDirection.XNeg => 0,
        VoxelDirection.YPos or VoxelDirection.YNeg => 1,
        VoxelDirection.ZPos or VoxelDirection.ZNeg => 2,
        _ => 0
    };

    /// <summary>
    /// Returns true if this is a positive direction (+X, +Y, +Z).
    /// </summary>
    public static bool IsPositive(this VoxelDirection dir) => dir switch {
        VoxelDirection.XPos or VoxelDirection.YPos or VoxelDirection.ZPos => true,
        _ => false
    };
}
