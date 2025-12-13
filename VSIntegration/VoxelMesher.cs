using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using VoxelType = Sparky.Game.Core.VoxelType;
using Material = Sparky.Game.Core.Material;

namespace Sparky.VSIntegration;

/// <summary>
/// Generates mesh data for circuit block voxels using greedy meshing.
/// </summary>
public static class VoxelMesher
{
    /// <summary>
    /// Material colors (ARGB format for VS).
    /// </summary>
    private static readonly Dictionary<byte, int> MaterialColors = new()
    {
        { 0, unchecked((int)0x00000000) }, // Air (transparent)
        { 1, unchecked((int)0xFFB87333) }, // Copper - #B87333
        { 2, unchecked((int)0xFFFFD700) }, // Gold - #FFD700
        { 3, unchecked((int)0xFF7F7F7F) }, // Lead - #7F7F7F
        { 4, unchecked((int)0xFF8B7355) }, // Iron - #8B7355 (rusty iron color)
    };

    /// <summary>
    /// Material index lookup matching BlockEntityCircuit.
    /// </summary>
    private static readonly Dictionary<Material, byte> MaterialToIndex = new()
    {
        { Material.Copper, 1 },
        { Material.Gold, 2 },
        { Material.Lead, 3 },
        { Material.Iron, 4 }
    };

    /// <summary>
    /// Generates a mesh for the given voxel data using greedy meshing.
    /// Thread-safe - can be called from tesselation thread.
    /// </summary>
    public static MeshData GenerateMesh(byte[] voxelData, ICoreAPI? api)
    {
        // Use VS's CubeMeshUtil to create properly formatted cube meshes
        MeshData? combinedMesh = null;

        // Track which voxels have been claimed by a prism
        var claimed = new bool[4096];

        for (int y = 0; y < 16; y++)
        {
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int index = GetIndex(x, y, z);
                    if (claimed[index]) continue;

                    var (type, materialIdx) = UnpackVoxel(voxelData[index]);
                    if (type == VoxelType.Air) continue;

                    // Greedy mesh: grow a rectangular prism
                    var (sizeX, sizeY, sizeZ) = GrowPrism(
                        voxelData, claimed, x, y, z, type, materialIdx);

                    // Mark all voxels in this prism as claimed
                    for (int dy = 0; dy < sizeY; dy++)
                    {
                        for (int dz = 0; dz < sizeZ; dz++)
                        {
                            for (int dx = 0; dx < sizeX; dx++)
                            {
                                claimed[GetIndex(x + dx, y + dy, z + dz)] = true;
                            }
                        }
                    }

                    // Create cube mesh using VS utilities
                    var cubeMesh = CreateColoredCube(x, y, z, sizeX, sizeY, sizeZ, materialIdx);

                    if (combinedMesh == null)
                    {
                        combinedMesh = cubeMesh;
                    }
                    else
                    {
                        combinedMesh.AddMeshData(cubeMesh);
                    }
                }
            }
        }

        return combinedMesh ?? new MeshData(1, 1);
    }

    /// <summary>
    /// Creates a colored cube mesh at the specified position.
    /// </summary>
    private static MeshData CreateColoredCube(int x, int y, int z, int sizeX, int sizeY, int sizeZ, byte materialIdx)
    {
        const float scale = 1f / 16f;
        float x0 = x * scale;
        float y0 = y * scale;
        float z0 = z * scale;
        float x1 = (x + sizeX) * scale;
        float y1 = (y + sizeY) * scale;
        float z1 = (z + sizeZ) * scale;

        // Get color for this material
        int color = MaterialColors.GetValueOrDefault(materialIdx, unchecked((int)0xFFFFFFFF));
        byte r = (byte)((color >> 16) & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)(color & 0xFF);
        byte a = (byte)((color >> 24) & 0xFF);

        // Create a simple cube mesh manually with proper VS format
        var mesh = new MeshData(24, 36, false, true, true, false);

        // Define the 8 corners of the cube
        float[][] corners = new float[][]
        {
            new[] { x0, y0, z0 }, // 0: left bottom back
            new[] { x1, y0, z0 }, // 1: right bottom back
            new[] { x1, y0, z1 }, // 2: right bottom front
            new[] { x0, y0, z1 }, // 3: left bottom front
            new[] { x0, y1, z0 }, // 4: left top back
            new[] { x1, y1, z0 }, // 5: right top back
            new[] { x1, y1, z1 }, // 6: right top front
            new[] { x0, y1, z1 }, // 7: left top front
        };

        // 6 faces, each with 4 vertices (corner indices)
        int[][] faces = new int[][]
        {
            new[] { 3, 2, 1, 0 }, // bottom (y-)
            new[] { 4, 5, 6, 7 }, // top (y+)
            new[] { 0, 1, 5, 4 }, // back (z-)
            new[] { 2, 3, 7, 6 }, // front (z+)
            new[] { 3, 0, 4, 7 }, // left (x-)
            new[] { 1, 2, 6, 5 }, // right (x+)
        };

        // UV coordinates for each vertex of a face
        float[][] uvs = new float[][]
        {
            new[] { 0f, 0f },
            new[] { 1f, 0f },
            new[] { 1f, 1f },
            new[] { 0f, 1f },
        };

        int vertexIndex = 0;
        foreach (var face in faces)
        {
            for (int i = 0; i < 4; i++)
            {
                var corner = corners[face[i]];
                var uv = uvs[i];

                // Position
                mesh.xyz[vertexIndex * 3 + 0] = corner[0];
                mesh.xyz[vertexIndex * 3 + 1] = corner[1];
                mesh.xyz[vertexIndex * 3 + 2] = corner[2];

                // UV
                mesh.Uv[vertexIndex * 2 + 0] = uv[0];
                mesh.Uv[vertexIndex * 2 + 1] = uv[1];

                // Color
                mesh.Rgba[vertexIndex * 4 + 0] = r;
                mesh.Rgba[vertexIndex * 4 + 1] = g;
                mesh.Rgba[vertexIndex * 4 + 2] = b;
                mesh.Rgba[vertexIndex * 4 + 3] = a;

                vertexIndex++;
            }
        }
        mesh.VerticesCount = 24;

        // Add indices for each face (2 triangles per face)
        int indexOffset = 0;
        for (int face = 0; face < 6; face++)
        {
            int baseVert = face * 4;
            mesh.Indices[indexOffset++] = baseVert + 0;
            mesh.Indices[indexOffset++] = baseVert + 1;
            mesh.Indices[indexOffset++] = baseVert + 2;
            mesh.Indices[indexOffset++] = baseVert + 0;
            mesh.Indices[indexOffset++] = baseVert + 2;
            mesh.Indices[indexOffset++] = baseVert + 3;
        }
        mesh.IndicesCount = 36;

        return mesh;
    }

    /// <summary>
    /// Grows a rectangular prism starting from (x, y, z) with the same type and material.
    /// </summary>
    private static (int SizeX, int SizeY, int SizeZ) GrowPrism(
        byte[] voxelData, bool[] claimed,
        int x, int y, int z,
        VoxelType type, byte materialIdx)
    {
        // Grow in +X
        int sizeX = 1;
        while (x + sizeX < 16)
        {
            int nextIdx = GetIndex(x + sizeX, y, z);
            if (claimed[nextIdx]) break;
            var (nextType, nextMat) = UnpackVoxel(voxelData[nextIdx]);
            if (nextType != type || nextMat != materialIdx) break;
            sizeX++;
        }

        // Grow in +Z (maintaining X extent)
        int sizeZ = 1;
        while (z + sizeZ < 16)
        {
            bool canGrow = true;
            for (int dx = 0; dx < sizeX; dx++)
            {
                int nextIdx = GetIndex(x + dx, y, z + sizeZ);
                if (claimed[nextIdx]) { canGrow = false; break; }
                var (nextType, nextMat) = UnpackVoxel(voxelData[nextIdx]);
                if (nextType != type || nextMat != materialIdx) { canGrow = false; break; }
            }
            if (!canGrow) break;
            sizeZ++;
        }

        // Grow in +Y (maintaining X and Z extent)
        int sizeY = 1;
        while (y + sizeY < 16)
        {
            bool canGrow = true;
            for (int dz = 0; dz < sizeZ && canGrow; dz++)
            {
                for (int dx = 0; dx < sizeX && canGrow; dx++)
                {
                    int nextIdx = GetIndex(x + dx, y + sizeY, z + dz);
                    if (claimed[nextIdx]) { canGrow = false; break; }
                    var (nextType, nextMat) = UnpackVoxel(voxelData[nextIdx]);
                    if (nextType != type || nextMat != materialIdx) { canGrow = false; break; }
                }
            }
            if (!canGrow) break;
            sizeY++;
        }

        return (sizeX, sizeY, sizeZ);
    }

    #region Helper Methods

    private static int GetIndex(int x, int y, int z)
    {
        return (y << 8) | (z << 4) | x;
    }

    private static (VoxelType Type, byte MaterialIdx) UnpackVoxel(byte packed)
    {
        var type = (VoxelType)(packed & 0x03);
        byte materialIdx = (byte)((packed >> 2) & 0x0F);
        return (type, materialIdx);
    }

    #endregion
}
