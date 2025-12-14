using System;

namespace Sparky.Game.Core;

/// <summary>
/// Pure math functions for calculating voxel positions from hit positions and face normals.
/// Extracted from ItemWireTool to enable unit testing without VS dependencies.
/// </summary>
public static class VoxelPositionHelper
{
    /// <summary>
    /// Gets the voxel coordinates (0-15) of the voxel whose face was clicked.
    /// This is the voxel "behind" the clicked face.
    /// </summary>
    /// <param name="hitX">Hit X position in block-local coords (0-1)</param>
    /// <param name="hitY">Hit Y position in block-local coords (0-1)</param>
    /// <param name="hitZ">Hit Z position in block-local coords (0-1)</param>
    /// <param name="faceNormalX">Face normal X component (-1, 0, or 1)</param>
    /// <param name="faceNormalY">Face normal Y component (-1, 0, or 1)</param>
    /// <param name="faceNormalZ">Face normal Z component (-1, 0, or 1)</param>
    /// <returns>Voxel coordinates (0-15 each axis)</returns>
    public static (int X, int Y, int Z) GetClickedVoxel(
        double hitX, double hitY, double hitZ,
        float faceNormalX, float faceNormalY, float faceNormalZ)
    {
        // Offset hit position slightly inward from the face to get inside the clicked voxel
        // Face normal points outward, so subtract it to go inward
        const double inset = 0.01;
        double x = hitX - faceNormalX * inset;
        double y = hitY - faceNormalY * inset;
        double z = hitZ - faceNormalZ * inset;

        // Convert to voxel coordinates
        int vx = Math.Clamp((int)(x * 16), 0, 15);
        int vy = Math.Clamp((int)(y * 16), 0, 15);
        int vz = Math.Clamp((int)(z * 16), 0, 15);

        return (vx, vy, vz);
    }

    /// <summary>
    /// Gets the voxel coordinates for placing adjacent to a clicked face.
    /// This is the voxel "in front of" the clicked face.
    /// </summary>
    /// <param name="hitX">Hit X position in block-local coords (0-1)</param>
    /// <param name="hitY">Hit Y position in block-local coords (0-1)</param>
    /// <param name="hitZ">Hit Z position in block-local coords (0-1)</param>
    /// <param name="faceNormalX">Face normal X component (-1, 0, or 1)</param>
    /// <param name="faceNormalY">Face normal Y component (-1, 0, or 1)</param>
    /// <param name="faceNormalZ">Face normal Z component (-1, 0, or 1)</param>
    /// <returns>Voxel coordinates (0-15 each axis)</returns>
    public static (int X, int Y, int Z) GetAdjacentVoxel(
        double hitX, double hitY, double hitZ,
        float faceNormalX, float faceNormalY, float faceNormalZ)
    {
        var (x, y, z, _) = GetAdjacentVoxelWithOverflow(
            hitX, hitY, hitZ,
            faceNormalX, faceNormalY, faceNormalZ);
        return (x, y, z);
    }

    /// <summary>
    /// Gets the voxel coordinates for placing adjacent to a clicked face,
    /// and indicates if the position is outside the current block.
    /// If outside, coordinates are wrapped to the adjacent block's local space.
    /// </summary>
    /// <param name="hitX">Hit X position in block-local coords (0-1)</param>
    /// <param name="hitY">Hit Y position in block-local coords (0-1)</param>
    /// <param name="hitZ">Hit Z position in block-local coords (0-1)</param>
    /// <param name="faceNormalX">Face normal X component (-1, 0, or 1)</param>
    /// <param name="faceNormalY">Face normal Y component (-1, 0, or 1)</param>
    /// <param name="faceNormalZ">Face normal Z component (-1, 0, or 1)</param>
    /// <returns>Voxel coords (wrapped if outside) and whether it's outside the block</returns>
    public static (int X, int Y, int Z, bool OutsideBlock) GetAdjacentVoxelWithOverflow(
        double hitX, double hitY, double hitZ,
        float faceNormalX, float faceNormalY, float faceNormalZ)
    {
        // Offset hit position slightly outward from the face to get in the adjacent voxel
        const double outset = 0.01;
        double x = hitX + faceNormalX * outset;
        double y = hitY + faceNormalY * outset;
        double z = hitZ + faceNormalZ * outset;

        // Convert to voxel coordinates (without clamping first)
        int vx = (int)Math.Floor(x * 16);
        int vy = (int)Math.Floor(y * 16);
        int vz = (int)Math.Floor(z * 16);

        // Check if outside block bounds
        bool outside = vx < 0 || vx > 15 || vy < 0 || vy > 15 || vz < 0 || vz > 15;

        // Wrap coordinates to adjacent block's local space
        if (vx < 0) vx = 15;
        else if (vx > 15) vx = 0;
        if (vy < 0) vy = 15;
        else if (vy > 15) vy = 0;
        if (vz < 0) vz = 15;
        else if (vz > 15) vz = 0;

        return (vx, vy, vz, outside);
    }
}
