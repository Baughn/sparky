using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Material = Sparky.Game.Core.Material;

namespace Sparky.VSIntegration.Preview;

/// <summary>
/// Utilities for building preview meshes from voxel positions.
/// </summary>
public static class VoxelPreviewMesh
{
    /// <summary>
    /// Size of one voxel in block units (1/16th of a block).
    /// </summary>
    public const float VoxelSize = 1f / 16f;

    /// <summary>
    /// Scale factor slightly less than 1 to avoid z-fighting with surfaces.
    /// </summary>
    private const float ZFightingScale = 0.999f;

    /// <summary>
    /// Builds a mesh for one or more preview voxels.
    /// Culls internal faces where adjacent voxels are both in the preview set.
    /// Vertices are built relative to the minimum voxel position (mesh origin).
    /// Uses a solid color approach (no texture) for simplicity.
    /// </summary>
    /// <param name="voxels">The voxels to render with their colors.</param>
    /// <returns>A MeshData ready for upload, or null if no voxels.</returns>
    public static MeshData? BuildVoxelMesh(IReadOnlyList<PreviewVoxel> voxels)
    {
        if (voxels.Count == 0)
            return null;

        // Compute mesh origin (minimum voxel) for relative positioning
        int minX = voxels.Min(v => v.X);
        int minY = voxels.Min(v => v.Y);
        int minZ = voxels.Min(v => v.Z);

        // Build a set of voxel positions for fast neighbor lookup
        var voxelSet = new HashSet<(int X, int Y, int Z)>();
        foreach (var v in voxels)
            voxelSet.Add((v.X, v.Y, v.Z));

        // Create the output mesh with UVs, RGBA, and Flags
        var mesh = new MeshData(24 * voxels.Count, 36 * voxels.Count, withUv: true, withRgba: true, withFlags: true);

        foreach (var voxel in voxels)
        {
            AddVoxelToMesh(mesh, voxel, voxelSet, minX, minY, minZ);
        }

        // Ensure Flags array is properly initialized (required for rendering)
        // Flag value 1 << 8 = 256 is a common default
        if (mesh.Flags != null)
        {
            for (int i = 0; i < mesh.VerticesCount; i++)
            {
                mesh.Flags[i] = 1 << 8;
            }
        }

        return mesh;
    }

    /// <summary>
    /// Adds a single voxel's faces to the mesh, culling faces adjacent to other preview voxels.
    /// Positions are relative to the mesh origin (minX, minY, minZ).
    /// </summary>
    private static void AddVoxelToMesh(
        MeshData mesh,
        PreviewVoxel voxel,
        HashSet<(int X, int Y, int Z)> voxelSet,
        int originX, int originY, int originZ)
    {
        // Position relative to mesh origin (for camera-relative rendering)
        float relX = (voxel.X - originX) * VoxelSize;
        float relY = (voxel.Y - originY) * VoxelSize;
        float relZ = (voxel.Z - originZ) * VoxelSize;

        // Check each face for neighbors
        // Shading values match CubeMeshUtil.DefaultBlockSideShadingsByFacing
        var faces = new (BlockFacing Face, int Dx, int Dy, int Dz, float Shading)[]
        {
            (BlockFacing.NORTH, 0, 0, -1, 0.6f),
            (BlockFacing.EAST, 1, 0, 0, 0.75f),
            (BlockFacing.SOUTH, 0, 0, 1, 0.6f),
            (BlockFacing.WEST, -1, 0, 0, 0.75f),
            (BlockFacing.UP, 0, 1, 0, 1.0f),
            (BlockFacing.DOWN, 0, -1, 0, 0.45f),
        };

        foreach (var (face, dx, dy, dz, shading) in faces)
        {
            // Skip face if neighbor exists in preview set
            if (voxelSet.Contains((voxel.X + dx, voxel.Y + dy, voxel.Z + dz)))
                continue;

            AddFaceToMesh(mesh, face, relX, relY, relZ, voxel.Rgba, shading);
        }
    }

    /// <summary>
    /// Adds a single face quad to the mesh.
    /// </summary>
    private static void AddFaceToMesh(
        MeshData mesh,
        BlockFacing face,
        float x, float y, float z,
        int argbColor,
        float shading)
    {
        int baseVertex = mesh.VerticesCount;

        // Get face vertices from CubeMeshUtil (these are in -1 to 1 range, centered)
        int faceIndex = face.Index;
        int vertexOffset = faceIndex * 4 * 3; // 4 vertices per face, 3 coords each
        int uvOffset = faceIndex * 4 * 2;     // 4 vertices per face, 2 UV coords each

        // Apply shading to color (multiply RGB, keep alpha)
        int shadedColor = ApplyShading(argbColor, shading);

        // Add 4 vertices for this face
        // Voxel center offset (half voxel size)
        float halfVoxel = VoxelSize * 0.5f;

        for (int i = 0; i < 4; i++)
        {
            // Get vertex position from cube template (-1 to 1 range)
            float vx = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 0];
            float vy = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 1];
            float vz = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 2];

            // Scale from -1..1 to 0..VoxelSize, apply z-fighting scale around center
            float wx = x + halfVoxel + (vx * halfVoxel * ZFightingScale);
            float wy = y + halfVoxel + (vy * halfVoxel * ZFightingScale);
            float wz = z + halfVoxel + (vz * halfVoxel * ZFightingScale);

            // Get UV coordinates from cube template (0 to 1 range)
            float u = CubeMeshUtil.CubeUvCoords[uvOffset + i * 2 + 0];
            float v = CubeMeshUtil.CubeUvCoords[uvOffset + i * 2 + 1];

            mesh.AddVertex(wx, wy, wz, u, v, shadedColor);
        }

        // Add 2 triangles (6 indices) for this face
        mesh.AddIndex(baseVertex + 0);
        mesh.AddIndex(baseVertex + 1);
        mesh.AddIndex(baseVertex + 2);
        mesh.AddIndex(baseVertex + 0);
        mesh.AddIndex(baseVertex + 2);
        mesh.AddIndex(baseVertex + 3);
    }

    /// <summary>
    /// Applies shading multiplier to RGB channels while preserving alpha.
    /// Input/output in VS ARGB format (same as ColorUtil.ToRgba).
    /// </summary>
    private static int ApplyShading(int argbColor, float shading)
    {
        int a = (argbColor >> 24) & 0xFF;
        int r = (int)(((argbColor >> 16) & 0xFF) * shading);
        int g = (int)(((argbColor >> 8) & 0xFF) * shading);
        int b = (int)((argbColor & 0xFF) * shading);

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    /// <summary>
    /// Gets the display color for a material.
    /// </summary>
    /// <param name="material">The conductor material.</param>
    /// <param name="alpha">Alpha value 0-255.</param>
    /// <returns>Color in VS ARGB format (same as ColorUtil.ToRgba).</returns>
    public static int GetMaterialColor(Material material, byte alpha = 128)
    {
        // Get RGB values for material
        var (r, g, b) = material.Name switch
        {
            "Copper" => (0xB8, 0x73, 0x33),
            "Gold" => (0xFF, 0xD7, 0x00),
            "Lead" => (0x5C, 0x62, 0x74),
            "Iron" => (0x8B, 0x8B, 0x8B),
            _ => (0xFF, 0xFF, 0xFF)
        };

        // VS uses ARGB format: (a << 24) | (r << 16) | (g << 8) | b
        return (alpha << 24) | (r << 16) | (g << 8) | b;
    }

    /// <summary>
    /// Converts a global voxel position to world coordinates (block corner).
    /// </summary>
    public static Vec3d VoxelToWorld(int voxelX, int voxelY, int voxelZ)
    {
        return new Vec3d(
            voxelX * VoxelSize,
            voxelY * VoxelSize,
            voxelZ * VoxelSize
        );
    }

    /// <summary>
    /// Computes the mesh origin (minimum corner) for a set of voxels.
    /// Used for camera-relative rendering.
    /// </summary>
    public static Vec3d ComputeMeshOrigin(IReadOnlyList<PreviewVoxel> voxels)
    {
        if (voxels.Count == 0)
            return new Vec3d(0, 0, 0);

        int minX = voxels.Min(v => v.X);
        int minY = voxels.Min(v => v.Y);
        int minZ = voxels.Min(v => v.Z);

        return VoxelToWorld(minX, minY, minZ);
    }
}
