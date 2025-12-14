using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using VoxelGrid = Sparky.Game.Core.VoxelGrid;
using VoxelType = Sparky.Game.Core.VoxelType;
using Material = Sparky.Game.Core.Material;
using VoxelPos = Sparky.Game.Core.VoxelPos;
using SparkyBlockPos = Sparky.Game.Core.BlockPos;

namespace Sparky.VSIntegration;

/// <summary>
/// Circuit block entity that extends VS's microblock system with electrical simulation.
/// Inherits voxel storage and mesh generation from BlockEntityMicroBlock.
/// </summary>
public class BlockEntityCircuit : BlockEntityMicroBlock
{
    /// <summary>
    /// Network ID assigned by CircuitNetworkManager.
    /// </summary>
    public Guid NetworkId { get; internal set; }

    /// <summary>
    /// Maps VS block IDs to Sparky conductor materials.
    /// Populated on mod initialization with conductor block types.
    /// </summary>
    private static readonly Dictionary<int, Material> BlockIdToMaterial = new();

    /// <summary>
    /// Registers a VS block as a conductor material.
    /// Call during mod initialization after blocks are loaded.
    /// </summary>
    public static void RegisterConductor(int blockId, Material material)
    {
        BlockIdToMaterial[blockId] = material;
    }

    /// <summary>
    /// Clears all conductor registrations. Call on mod unload.
    /// </summary>
    public static void ClearConductorRegistrations()
    {
        BlockIdToMaterial.Clear();
    }

    /// <summary>
    /// Gets whether a VS block ID is a registered conductor.
    /// </summary>
    public static bool IsConductor(int blockId)
    {
        return BlockIdToMaterial.ContainsKey(blockId);
    }

    /// <summary>
    /// Gets the conductor material for a VS block ID, or null if not a conductor.
    /// </summary>
    public static Material? GetConductorMaterial(int blockId)
    {
        return BlockIdToMaterial.TryGetValue(blockId, out var mat) ? mat : null;
    }

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

    #region Serialization

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        if (NetworkId != Guid.Empty)
        {
            tree.SetBytes("networkId", NetworkId.ToByteArray());
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        var netIdBytes = tree.GetBytes("networkId");
        if (netIdBytes != null && netIdBytes.Length == 16)
        {
            NetworkId = new Guid(netIdBytes);
        }
    }

    #endregion

    #region Conductor Voxel Access

    /// <summary>
    /// Sets a single conductor voxel at the given local coordinates (0-15 each axis).
    /// </summary>
    public void SetConductorVoxel(int x, int y, int z, Material material)
    {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            throw new ArgumentOutOfRangeException("Voxel coordinates must be 0-15");

        // Find the block ID for this material
        int blockId = -1;
        foreach (var kvp in BlockIdToMaterial)
        {
            if (kvp.Value == material)
            {
                blockId = kvp.Key;
                break;
            }
        }

        if (blockId < 0)
        {
            Api?.Logger.Warning($"[Sparky] No block registered for material {material.Name}");
            return;
        }

        // Find or add this material to BlockIds
        byte materialIndex = GetOrAddMaterialIndex(blockId);

        // Set the voxel using inherited method
        bool changed = SetVoxel(new Vec3i(x, y, z), true, materialIndex, 1);

        if (changed)
        {
            // Mark mesh dirty for re-render and regenerate selection boxes
            MarkMeshDirty();
            RegenSelectionBoxes(Api.World, null);
            MarkDirty(true);
        }

        // Notify network manager on server side
        if (Api?.Side == EnumAppSide.Server)
        {
            var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.OnBlockVoxelChanged(Pos, x, y, z, VoxelType.Conductor, material);
        }
    }

    /// <summary>
    /// Removes a voxel at the given local coordinates.
    /// </summary>
    public void RemoveVoxel(int x, int y, int z)
    {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            return;

        // Clear the voxel using inherited method
        bool changed = SetVoxel(new Vec3i(x, y, z), false, 0, 1);

        if (changed)
        {
            // Mark mesh dirty for re-render and regenerate selection boxes
            MarkMeshDirty();
            RegenSelectionBoxes(Api.World, null);
            MarkDirty(true);
        }

        // Notify network manager on server side
        if (Api?.Side == EnumAppSide.Server)
        {
            var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.OnBlockVoxelChanged(Pos, x, y, z, VoxelType.Air, null);
        }
    }

    /// <summary>
    /// Gets the conductor material at the given local coordinates, or null if air/insulator.
    /// </summary>
    public Material? GetConductorAt(int x, int y, int z)
    {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            return null;

        // Find the voxel in our cuboids
        foreach (var cuboid in VoxelCuboids)
        {
            FromUint(cuboid, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1, out int matIdx);
            if (x >= x0 && x < x1 && y >= y0 && y < y1 && z >= z0 && z < z1)
            {
                if (BlockIds != null && matIdx < BlockIds.Length)
                {
                    return GetConductorMaterial(BlockIds[matIdx]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets or adds a material index for the given block ID.
    /// </summary>
    private byte GetOrAddMaterialIndex(int blockId)
    {
        if (BlockIds == null)
        {
            BlockIds = new int[] { blockId };
            return 0;
        }

        for (int i = 0; i < BlockIds.Length; i++)
        {
            if (BlockIds[i] == blockId)
                return (byte)i;
        }

        // Add new material
        if (BlockIds.Length >= 255)
        {
            Api?.Logger.Warning("[Sparky] Material palette full, reusing last index");
            return 254;
        }

        var newIds = new int[BlockIds.Length + 1];
        Array.Copy(BlockIds, newIds, BlockIds.Length);
        newIds[BlockIds.Length] = blockId;
        BlockIds = newIds;
        return (byte)(BlockIds.Length - 1);
    }

    #endregion

    #region Conversion to VoxelGrid

    /// <summary>
    /// Exports all conductor voxels to a VoxelGrid at the correct world positions.
    /// </summary>
    public void ExportToVoxelGrid(VoxelGrid grid, SparkyBlockPos sparkyBlockPos)
    {
        if (VoxelCuboids == null || BlockIds == null)
            return;

        foreach (var cuboid in VoxelCuboids)
        {
            FromUint(cuboid, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1, out int matIdx);

            // Get the material for this cuboid
            Material? material = null;
            if (matIdx < BlockIds.Length)
            {
                material = GetConductorMaterial(BlockIds[matIdx]);
            }

            // If not a conductor, skip (it's decorative/insulator)
            if (material == null)
                continue;

            // Export all voxels in this cuboid
            for (int y = y0; y < y1; y++)
            {
                for (int z = z0; z < z1; z++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        var voxelPos = VoxelPos.FromBlockLocal(sparkyBlockPos, x, y, z);
                        grid.SetVoxel(voxelPos, material);
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
