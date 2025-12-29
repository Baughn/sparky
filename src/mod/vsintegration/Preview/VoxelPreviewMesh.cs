using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Material = Sparky.Voxel.Material;

namespace Sparky.VSIntegration.Preview;

/// <summary>
/// Utilities for building preview meshes from voxel positions.
/// </summary>
public static class VoxelPreviewMesh {
    /// <summary>
    /// Size of one voxel in block units (1/16th of a block).
    /// </summary>
    public const float VoxelSize = 1f / 16f;

    /// <summary>
    /// Scale factor slightly less than 1 to avoid z-fighting with surfaces.
    /// </summary>
    private const float ZFightingScale = 0.999f;

    /// <summary>
    /// Size of one sub-voxel (1/3 of a voxel) for Menger sponge rendering.
    /// </summary>
    private const float SubVoxelSize = VoxelSize / 3f;

    /// <summary>
    /// Shading for exterior faces (outer shell of Menger sponge).
    /// </summary>
    private const float ExteriorShading = 1.0f;

    /// <summary>
    /// Shading for interior faces (facing into the hollow).
    /// </summary>
    private const float InteriorShading = 0.6f;

    /// <summary>
    /// The 7 axial positions removed in a Menger sponge iteration 1.
    /// Center + 6 face-centers.
    /// </summary>
    private static readonly HashSet<(int, int, int)> RemovedPositions = new() {
        (1, 1, 1),  // center
        (0, 1, 1), (2, 1, 1),  // X-axis face centers
        (1, 0, 1), (1, 2, 1),  // Y-axis face centers
        (1, 1, 0), (1, 1, 2),  // Z-axis face centers
    };

    /// <summary>
    /// Builds a mesh for one or more preview voxels.
    /// Culls internal faces where adjacent voxels are both in the preview set.
    /// Vertices are built relative to the minimum voxel position (mesh origin).
    /// Uses a solid color approach (no texture) for simplicity.
    /// </summary>
    /// <param name="voxels">The voxels to render with their colors.</param>
    /// <returns>A MeshData ready for upload, or null if no voxels.</returns>
    public static MeshData? BuildVoxelMesh(IReadOnlyList<PreviewVoxel> voxels) {
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

        // 20 sub-voxels per voxel, 24 vertices and 36 indices per sub-voxel max
        var mesh = new MeshData(24 * 20 * voxels.Count, 36 * 20 * voxels.Count, withUv: true, withRgba: true, withFlags: true);

        foreach (var voxel in voxels) {
            AddVoxelToMesh(mesh, voxel, voxelSet, minX, minY, minZ);
        }

        // Ensure Flags array is properly initialized (required for rendering)
        // Flag value 1 << 8 = 256 is a common default
        if (mesh.Flags != null) {
            for (int i = 0; i < mesh.VerticesCount; i++) {
                mesh.Flags[i] = 1 << 8;
            }
        }

        return mesh;
    }

    /// <summary>
    /// Adds a voxel as a Menger sponge (iteration 1) to the mesh.
    /// Generates 20 sub-voxels, culling faces between adjacent sub-voxels
    /// and adjacent preview voxels.
    /// </summary>
    private static void AddVoxelToMesh(
        MeshData mesh,
        PreviewVoxel voxel,
        HashSet<(int X, int Y, int Z)> voxelSet,
        int originX, int originY, int originZ) {

        // Position relative to mesh origin
        float relX = (voxel.X - originX) * VoxelSize;
        float relY = (voxel.Y - originY) * VoxelSize;
        float relZ = (voxel.Z - originZ) * VoxelSize;

        // Build the set of sub-voxels that exist (excluding removed positions)
        // Also exclude sub-voxels on edges where adjacent preview voxels exist
        var subVoxelSet = new HashSet<(int, int, int)>();

        for (int sx = 0; sx < 3; sx++)
        for (int sy = 0; sy < 3; sy++)
        for (int sz = 0; sz < 3; sz++) {
            if (RemovedPositions.Contains((sx, sy, sz)))
                continue;
            subVoxelSet.Add((sx, sy, sz));
        }

        // For each direction, if an adjacent voxel exists, add "virtual" sub-voxels
        // at positions 3/-1 to enable face culling at voxel boundaries
        if (voxelSet.Contains((voxel.X - 1, voxel.Y, voxel.Z))) {
            for (int sy = 0; sy < 3; sy++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((2, sy, sz)))
                    subVoxelSet.Add((-1, sy, sz));
        }
        if (voxelSet.Contains((voxel.X + 1, voxel.Y, voxel.Z))) {
            for (int sy = 0; sy < 3; sy++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((0, sy, sz)))
                    subVoxelSet.Add((3, sy, sz));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y - 1, voxel.Z))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((sx, 2, sz)))
                    subVoxelSet.Add((sx, -1, sz));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y + 1, voxel.Z))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((sx, 0, sz)))
                    subVoxelSet.Add((sx, 3, sz));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y, voxel.Z - 1))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sy = 0; sy < 3; sy++)
                if (!RemovedPositions.Contains((sx, sy, 2)))
                    subVoxelSet.Add((sx, sy, -1));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y, voxel.Z + 1))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sy = 0; sy < 3; sy++)
                if (!RemovedPositions.Contains((sx, sy, 0)))
                    subVoxelSet.Add((sx, sy, 3));
        }

        // Generate sub-voxels
        for (int sx = 0; sx < 3; sx++)
        for (int sy = 0; sy < 3; sy++)
        for (int sz = 0; sz < 3; sz++) {
            if (RemovedPositions.Contains((sx, sy, sz)))
                continue;
            AddSubVoxelToMesh(mesh, relX, relY, relZ, sx, sy, sz, voxel.Rgba, subVoxelSet);
        }
    }

    /// <summary>
    /// Applies shading multiplier to RGB channels while preserving alpha.
    /// Input/output in VS ARGB format (same as ColorUtil.ToRgba).
    /// </summary>
    private static int ApplyShading(int argbColor, float shading) {
        int a = (argbColor >> 24) & 0xFF;
        int r = (int)(((argbColor >> 16) & 0xFF) * shading);
        int g = (int)(((argbColor >> 8) & 0xFF) * shading);
        int b = (int)((argbColor & 0xFF) * shading);

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    /// <summary>
    /// Determines if a sub-voxel face is interior (facing a removed position).
    /// </summary>
    private static bool IsInteriorFace(int sx, int sy, int sz, int dx, int dy, int dz) {
        return RemovedPositions.Contains((sx + dx, sy + dy, sz + dz));
    }

    /// <summary>
    /// Adds a single sub-voxel's faces to the mesh with Menger sponge shading.
    /// </summary>
    private static void AddSubVoxelToMesh(
        MeshData mesh,
        float voxelRelX, float voxelRelY, float voxelRelZ,
        int sx, int sy, int sz,
        int rgba,
        HashSet<(int, int, int)> subVoxelSet) {

        // Sub-voxel position relative to parent voxel corner
        float subX = voxelRelX + sx * SubVoxelSize;
        float subY = voxelRelY + sy * SubVoxelSize;
        float subZ = voxelRelZ + sz * SubVoxelSize;

        var faces = new (BlockFacing Face, int Dx, int Dy, int Dz)[] {
            (BlockFacing.NORTH, 0, 0, -1),
            (BlockFacing.EAST, 1, 0, 0),
            (BlockFacing.SOUTH, 0, 0, 1),
            (BlockFacing.WEST, -1, 0, 0),
            (BlockFacing.UP, 0, 1, 0),
            (BlockFacing.DOWN, 0, -1, 0),
        };

        foreach (var (face, dx, dy, dz) in faces) {
            // Skip face if adjacent sub-voxel exists in the set
            if (subVoxelSet.Contains((sx + dx, sy + dy, sz + dz)))
                continue;

            // Determine shading: interior faces are darker
            float shading = IsInteriorFace(sx, sy, sz, dx, dy, dz)
                ? InteriorShading
                : ExteriorShading;

            AddSubVoxelFaceToMesh(mesh, face, subX, subY, subZ, rgba, shading);
        }
    }

    /// <summary>
    /// Adds a single sub-voxel face quad to the mesh.
    /// </summary>
    private static void AddSubVoxelFaceToMesh(
        MeshData mesh,
        BlockFacing face,
        float x, float y, float z,
        int argbColor,
        float shading) {
        int baseVertex = mesh.VerticesCount;

        int faceIndex = face.Index;
        int vertexOffset = faceIndex * 4 * 3;
        int uvOffset = faceIndex * 4 * 2;

        int shadedColor = ApplyShading(argbColor, shading);

        float halfSubVoxel = SubVoxelSize * 0.5f;

        for (int i = 0; i < 4; i++) {
            float vx = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 0];
            float vy = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 1];
            float vz = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 2];

            // Scale from -1..1 to sub-voxel size, apply z-fighting scale
            float wx = x + halfSubVoxel + (vx * halfSubVoxel * ZFightingScale);
            float wy = y + halfSubVoxel + (vy * halfSubVoxel * ZFightingScale);
            float wz = z + halfSubVoxel + (vz * halfSubVoxel * ZFightingScale);

            float u = CubeMeshUtil.CubeUvCoords[uvOffset + i * 2 + 0];
            float v = CubeMeshUtil.CubeUvCoords[uvOffset + i * 2 + 1];

            mesh.AddVertex(wx, wy, wz, u, v, shadedColor);
        }

        mesh.AddIndex(baseVertex + 0);
        mesh.AddIndex(baseVertex + 1);
        mesh.AddIndex(baseVertex + 2);
        mesh.AddIndex(baseVertex + 0);
        mesh.AddIndex(baseVertex + 2);
        mesh.AddIndex(baseVertex + 3);
    }

    /// <summary>
    /// Gets the display color for a material.
    /// </summary>
    /// <param name="material">The conductor material.</param>
    /// <param name="alpha">Alpha value 0-255.</param>
    /// <returns>Color in VS ARGB format (same as ColorUtil.ToRgba).</returns>
    public static int GetMaterialColor(Material material, byte alpha = 255) {
        // Get RGB values for material
        var (r, g, b) = material.Name switch {
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
    public static Vec3d VoxelToWorld(int voxelX, int voxelY, int voxelZ) {
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
    public static Vec3d ComputeMeshOrigin(IReadOnlyList<PreviewVoxel> voxels) {
        if (voxels.Count == 0)
            return new Vec3d(0, 0, 0);

        int minX = voxels.Min(v => v.X);
        int minY = voxels.Min(v => v.Y);
        int minZ = voxels.Min(v => v.Z);

        return VoxelToWorld(minX, minY, minZ);
    }
}
