using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

using VoxelGrid = Sparky.Game.Core.VoxelGrid;
using VoxelType = Sparky.Game.Core.VoxelType;
using Material = Sparky.Game.Core.Material;
using VoxelPos = Sparky.Game.Core.VoxelPos;
using SparkyBlockPos = Sparky.Game.Core.BlockPos;

namespace Sparky.VSIntegration;

/// <summary>
/// Block entity that stores 16x16x16 voxel data for electrical conductors.
/// Each voxel can be Air, Conductor, ResistiveConductor, or Insulator,
/// with an optional material type (Copper, Gold, Lead, Iron).
/// </summary>
public class BlockEntityCircuit : BlockEntity
{
    /// <summary>
    /// Packed voxel storage: 4096 bytes for a 16x16x16 grid.
    /// Byte layout: bits 0-1 = VoxelType, bits 2-5 = MaterialIndex
    /// </summary>
    private byte[] _voxelData = new byte[4096];

    /// <summary>
    /// Network ID assigned by CircuitNetworkManager.
    /// </summary>
    public Guid NetworkId { get; internal set; }

    /// <summary>
    /// Cached mesh for client-side rendering.
    /// </summary>
    private MeshData? _cachedMesh;

    /// <summary>
    /// True if mesh needs regeneration.
    /// </summary>
    private bool _meshDirty = true;

    /// <summary>
    /// Material index lookup for packing/unpacking.
    /// </summary>
    private static readonly Dictionary<Material, byte> MaterialToIndex = new()
    {
        { Material.Copper, 1 },
        { Material.Gold, 2 },
        { Material.Lead, 3 },
        { Material.Iron, 4 }
    };

    private static readonly Dictionary<byte, Material> IndexToMaterial = new()
    {
        { 1, Material.Copper },
        { 2, Material.Gold },
        { 3, Material.Lead },
        { 4, Material.Iron }
    };

    /// <summary>
    /// Gets the number of non-air voxels in this block.
    /// </summary>
    public int VoxelCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < 4096; i++)
            {
                if ((_voxelData[i] & 0x03) != 0)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Sets a voxel at the given local coordinates (0-15 each axis).
    /// </summary>
    public void SetVoxel(int x, int y, int z, VoxelType type, Material? material = null)
    {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            throw new ArgumentOutOfRangeException("Voxel coordinates must be 0-15");

        int index = GetIndex(x, y, z);
        byte packed = PackVoxel(type, material);

        if (_voxelData[index] != packed)
        {
            _voxelData[index] = packed;
            _meshDirty = true;
            MarkDirty(redrawOnClient: true);

            Api?.Logger.Debug($"[Sparky] SetVoxel({x},{y},{z}) type={type} material={material?.Name ?? "null"} packed={packed}");

            // Notify network manager on server side
            if (Api?.Side == EnumAppSide.Server)
            {
                var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
                modSystem?.NetworkManager?.OnBlockVoxelChanged(Pos, x, y, z, type, material);
            }
        }
    }

    /// <summary>
    /// Gets the voxel type and material at the given local coordinates.
    /// </summary>
    public (VoxelType Type, Material? Material) GetVoxel(int x, int y, int z)
    {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            return (VoxelType.Air, null);

        int index = GetIndex(x, y, z);
        return UnpackVoxel(_voxelData[index]);
    }

    /// <summary>
    /// Gets the raw voxel data array (for network sync).
    /// </summary>
    public byte[] GetRawVoxelData() => _voxelData;

    /// <summary>
    /// Sets the raw voxel data array (for network sync).
    /// </summary>
    public void SetRawVoxelData(byte[] data)
    {
        if (data.Length != 4096)
            throw new ArgumentException("Voxel data must be exactly 4096 bytes");

        Array.Copy(data, _voxelData, 4096);
        _meshDirty = true;
    }

    #region Index Calculation

    private static int GetIndex(int x, int y, int z)
    {
        return (y << 8) | (z << 4) | x;
    }

    #endregion

    #region Packing/Unpacking

    private static byte PackVoxel(VoxelType type, Material? material)
    {
        byte typeBits = (byte)((int)type & 0x03);
        byte materialBits = 0;

        if (material != null && MaterialToIndex.TryGetValue(material, out byte idx))
        {
            materialBits = (byte)(idx << 2);
        }

        return (byte)(typeBits | materialBits);
    }

    private static (VoxelType Type, Material? Material) UnpackVoxel(byte packed)
    {
        var type = (VoxelType)(packed & 0x03);
        byte materialIdx = (byte)((packed >> 2) & 0x0F);

        Material? material = null;
        if (materialIdx != 0 && IndexToMaterial.TryGetValue(materialIdx, out var mat))
        {
            material = mat;
        }

        return (type, material);
    }

    #endregion

    #region Serialization

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBytes("voxelData", _voxelData);

        if (NetworkId != Guid.Empty)
        {
            tree.SetBytes("networkId", NetworkId.ToByteArray());
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        var data = tree.GetBytes("voxelData");
        if (data != null && data.Length == 4096)
        {
            Array.Copy(data, _voxelData, 4096);
        }

        var netIdBytes = tree.GetBytes("networkId");
        if (netIdBytes != null && netIdBytes.Length == 16)
        {
            NetworkId = new Guid(netIdBytes);
        }

        _meshDirty = true;
    }

    #endregion

    #region Lifecycle

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side == EnumAppSide.Server)
        {
            // Register with network manager
            var modSystem = api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.RegisterBlock(Pos, this);
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (Api?.Side == EnumAppSide.Server)
        {
            // Unregister from network manager
            var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.UnregisterBlock(Pos);
        }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        if (Api?.Side == EnumAppSide.Server)
        {
            // Notify network manager of chunk unload
            var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.OnBlockUnloaded(Pos);
        }
    }

    #endregion

    #region Rendering

    /// <summary>
    /// Called by the chunk tesselator to generate mesh data.
    /// Runs on a background thread - must be thread-safe.
    /// </summary>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (_meshDirty || _cachedMesh == null)
        {
            _cachedMesh = VoxelMesher.GenerateMesh(_voxelData, Api);
            _meshDirty = false;

            // Debug: log mesh generation results
            Api?.Logger.Debug($"[Sparky] OnTesselation: VoxelCount={VoxelCount}, MeshVerts={_cachedMesh?.VerticesCount}, MeshIndices={_cachedMesh?.IndicesCount}");
        }

        if (_cachedMesh != null && _cachedMesh.VerticesCount > 0)
        {
            mesher.AddMeshData(_cachedMesh);
            return true; // Skip default block mesh
        }

        return false;
    }

    /// <summary>
    /// Generates selection boxes for each non-air voxel.
    /// Used by BlockCircuit.GetSelectionBoxes().
    /// </summary>
    public Cuboidf[] GetVoxelSelectionBoxes()
    {
        var boxes = new List<Cuboidf>();
        const float scale = 1f / 16f;

        for (int y = 0; y < 16; y++)
        {
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    var (type, _) = GetVoxel(x, y, z);
                    if (type != VoxelType.Air)
                    {
                        boxes.Add(new Cuboidf(
                            x * scale, y * scale, z * scale,
                            (x + 1) * scale, (y + 1) * scale, (z + 1) * scale
                        ));
                    }
                }
            }
        }

        return boxes.ToArray();
    }

    #endregion

    #region Conversion to/from VoxelGrid

    /// <summary>
    /// Exports all voxels to a VoxelGrid at the correct world positions.
    /// </summary>
    public void ExportToVoxelGrid(VoxelGrid grid, SparkyBlockPos sparkyBlockPos)
    {
        for (int y = 0; y < 16; y++)
        {
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    var (type, material) = GetVoxel(x, y, z);
                    if (type != VoxelType.Air)
                    {
                        var voxelPos = VoxelPos.FromBlockLocal(sparkyBlockPos, x, y, z);
                        if (material != null)
                        {
                            grid.SetVoxel(voxelPos, material);
                        }
                        else
                        {
                            grid.SetVoxel(voxelPos, type);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Converts a VS BlockPos to a Sparky BlockPos.
    /// </summary>
    public static SparkyBlockPos ToSparkyBlockPos(BlockPos vsPos)
    {
        return new SparkyBlockPos(vsPos.X, vsPos.Y, vsPos.Z);
    }

    #endregion
}
