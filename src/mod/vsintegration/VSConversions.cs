using Sparky.Voxel;

namespace Sparky.VSIntegration;

/// <summary>
/// Conversion utilities between Vintage Story types and Sparky core types.
/// </summary>
public static class VSConversions {
    /// <summary>
    /// Converts VS BlockFacing to VoxelDirection.
    /// </summary>
    public static VoxelDirection ToVoxelDirection(this Vintagestory.API.MathTools.BlockFacing face) {
        return face.Index switch {
            0 => VoxelDirection.ZNeg, // North
            1 => VoxelDirection.XPos, // East
            2 => VoxelDirection.ZPos, // South
            3 => VoxelDirection.XNeg, // West
            4 => VoxelDirection.YPos, // Up
            5 => VoxelDirection.YNeg, // Down
            _ => VoxelDirection.YPos
        };
    }
}
