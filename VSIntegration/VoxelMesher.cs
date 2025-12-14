using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using VoxelType = Sparky.Game.Core.VoxelType;
using Material = Sparky.Game.Core.Material;

namespace Sparky.VSIntegration;

/// <summary>
/// Generates mesh data for circuit block voxels using greedy meshing and TesselateShape.
/// </summary>
public static class VoxelMesher
{
    /// <summary>
    /// Material index to texture path mapping.
    /// </summary>
    private static readonly Dictionary<byte, string> MaterialTextures = new()
    {
        { 1, "sparky:block/copper" },
        { 2, "sparky:block/gold" },
        { 3, "sparky:block/lead" },
        { 4, "sparky:block/iron" }
    };

    /// <summary>
    /// Cached voxel shape (loaded once per session).
    /// </summary>
    private static Shape? _cachedVoxelShape;

    /// <summary>
    /// Generates a mesh for the given voxel data using greedy meshing and TesselateShape.
    /// </summary>
    public static MeshData GenerateMesh(byte[] voxelData, ITesselatorAPI tesselator, ICoreClientAPI capi)
    {
        // Load and cache the voxel shape
        if (_cachedVoxelShape == null)
        {
            var shapeAsset = capi.Assets.TryGet(new AssetLocation("sparky:shapes/block/voxel.json"));
            if (shapeAsset != null)
            {
                _cachedVoxelShape = shapeAsset.ToObject<Shape>();
            }
        }

        if (_cachedVoxelShape == null)
        {
            capi.Logger.Error("[Sparky] Failed to load voxel shape");
            return new MeshData(1, 1);
        }

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

                    // Create mesh for this prism using TesselateShape
                    var prismMesh = CreatePrismMesh(
                        tesselator, capi, _cachedVoxelShape,
                        x, y, z, sizeX, sizeY, sizeZ, materialIdx);

                    if (prismMesh != null)
                    {
                        if (combinedMesh == null)
                        {
                            combinedMesh = prismMesh;
                        }
                        else
                        {
                            combinedMesh.AddMeshData(prismMesh);
                        }
                    }
                }
            }
        }

        return combinedMesh ?? new MeshData(1, 1);
    }

    /// <summary>
    /// Creates a mesh for a single prism using TesselateShape.
    /// </summary>
    private static MeshData? CreatePrismMesh(
        ITesselatorAPI tesselator,
        ICoreClientAPI capi,
        Shape voxelShape,
        int x, int y, int z,
        int sizeX, int sizeY, int sizeZ,
        byte materialIdx)
    {
        // Get texture path for this material
        if (!MaterialTextures.TryGetValue(materialIdx, out var texturePath))
        {
            texturePath = "sparky:block/copper"; // Default fallback
        }

        // Create texture source
        var texSource = new MaterialTextureSource(capi, texturePath);

        // Tesselate the shape
        tesselator.TesselateShape(
            "sparky-voxel",
            voxelShape,
            out MeshData mesh,
            texSource);

        if (mesh == null || mesh.VerticesCount == 0)
        {
            return null;
        }

        // Scale to prism size (shape is 16x16x16, we want sizeX x sizeY x sizeZ voxels)
        // Each voxel is 1/16 of a block
        float scaleX = sizeX / 16f;
        float scaleY = sizeY / 16f;
        float scaleZ = sizeZ / 16f;

        mesh.Scale(new Vec3f(0, 0, 0), scaleX, scaleY, scaleZ);

        // Translate to position (x, y, z are in voxel coordinates 0-15)
        float posX = x / 16f;
        float posY = y / 16f;
        float posZ = z / 16f;

        mesh.Translate(posX, posY, posZ);

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

/// <summary>
/// Simple texture source that returns a single texture for all texture codes.
/// Textures must be pre-loaded into the atlas during mod initialization.
/// </summary>
internal class MaterialTextureSource : ITexPositionSource
{
    private readonly TextureAtlasPosition _texPos;
    public Size2i AtlasSize { get; }

    public MaterialTextureSource(ICoreClientAPI capi, string texturePath)
    {
        AtlasSize = capi.BlockTextureAtlas.Size;

        var texLoc = new AssetLocation(texturePath);
        _texPos = capi.BlockTextureAtlas[texLoc];

        // Fallback to unknown texture if not found
        if (_texPos == null)
        {
            capi.Logger.Warning($"[Sparky] Texture not found in atlas: {texturePath}, using unknown texture");
            _texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
        }
    }

    public TextureAtlasPosition this[string textureCode] => _texPos;
}
