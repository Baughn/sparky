using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
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
/// Circuit behavior that can be attached to any block entity.
/// Provides self-contained conductor voxel storage and mesh rendering.
/// </summary>
public class BEBehaviorCircuit : BlockEntityBehavior {
    /// <summary>
    /// Network ID assigned by CircuitNetworkManager.
    /// </summary>
    public Guid NetworkId { get; internal set; }

    /// <summary>
    /// Packed conductor cuboid data. Format matches BlockEntityMicroBlock:
    /// bits 0-3: minX, bits 4-7: minY, bits 8-11: minZ,
    /// bits 12-15: maxX-1, bits 16-19: maxY-1, bits 20-23: maxZ-1,
    /// bits 24-31: material index
    /// </summary>
    public List<uint> ConductorCuboids { get; private set; } = new();

    /// <summary>
    /// Material palette mapping index → VS block ID.
    /// </summary>
    public int[] ConductorBlockIds { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// Cached mesh for rendering conductors. Null until first tesselation.
    /// </summary>
    private MeshData? _conductorMesh;

    /// <summary>
    /// Whether mesh needs regeneration on next tesselation.
    /// </summary>
    private bool _meshDirty = true;

    /// <summary>
    /// Cached selection boxes for per-voxel targeting.
    /// </summary>
    private Cuboidf[] _selectionBoxes = Array.Empty<Cuboidf>();

    /// <summary>
    /// Whether selection boxes need regeneration.
    /// </summary>
    private bool _selectionDirty = true;

    #region Static Conductor Registry

    /// <summary>
    /// Maps VS block IDs to Sparky conductor materials.
    /// Populated on mod initialization with conductor block types.
    /// </summary>
    private static readonly Dictionary<int, Material> BlockIdToMaterial = new();

    /// <summary>
    /// Registers a VS block as a conductor material.
    /// Call during mod initialization after blocks are loaded.
    /// </summary>
    public static void RegisterConductor(int blockId, Material material) {
        BlockIdToMaterial[blockId] = material;
    }

    /// <summary>
    /// Clears all conductor registrations. Call on mod unload.
    /// </summary>
    public static void ClearConductorRegistrations() {
        BlockIdToMaterial.Clear();
    }

    /// <summary>
    /// Gets whether a VS block ID is a registered conductor.
    /// </summary>
    public static bool IsConductor(int blockId) {
        return BlockIdToMaterial.ContainsKey(blockId);
    }

    /// <summary>
    /// Gets the conductor material for a VS block ID, or null if not a conductor.
    /// </summary>
    public static Material? GetConductorMaterial(int blockId) {
        return BlockIdToMaterial.TryGetValue(blockId, out var mat) ? mat : null;
    }

    /// <summary>
    /// Gets the VS block ID for a given material, or -1 if not found.
    /// </summary>
    public static int GetBlockIdForMaterial(Material material) {
        foreach (var kvp in BlockIdToMaterial) {
            if (kvp.Value == material) {
                return kvp.Key;
            }
        }
        return -1;
    }

    #endregion

    #region Constructor

    public BEBehaviorCircuit(BlockEntity blockentity) : base(blockentity) {
    }

    #endregion

    #region Lifecycle

    public override void Initialize(ICoreAPI api, JsonObject properties) {
        base.Initialize(api, properties);

        // Register with network manager on server
        if (api.Side == EnumAppSide.Server) {
            var modSystem = api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.RegisterBlock(Pos, this);
        }

        // Mark mesh for generation on client
        if (api.Side == EnumAppSide.Client && ConductorCuboids.Count > 0) {
            _meshDirty = true;
        }
    }

    public override void OnBlockRemoved() {
        base.OnBlockRemoved();

        // Unregister from network manager on server
        if (Api?.Side == EnumAppSide.Server) {
            var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.UnregisterBlock(Pos);
        }
    }

    public override void OnBlockUnloaded() {
        base.OnBlockUnloaded();

        // Notify network manager of chunk unload on server
        if (Api?.Side == EnumAppSide.Server) {
            var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.OnBlockUnloaded(Pos);
        }
    }

    #endregion

    #region Serialization

    public override void ToTreeAttributes(ITreeAttribute tree) {
        base.ToTreeAttributes(tree);

        if (ConductorCuboids.Count > 0) {
            // Store cuboids as uint array (reinterpreted as int for VS API)
            tree["sparky_cuboids"] = new IntArrayAttribute(
                ConductorCuboids.Select(c => unchecked((int)c)).ToArray()
            );
            tree["sparky_blockIds"] = new IntArrayAttribute(ConductorBlockIds);
        }

        if (NetworkId != Guid.Empty) {
            tree.SetBytes("sparky_networkId", NetworkId.ToByteArray());
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve) {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        // Load cuboids
        var cuboidsAttr = tree["sparky_cuboids"] as IntArrayAttribute;
        if (cuboidsAttr != null) {
            ConductorCuboids = cuboidsAttr.value.Select(i => unchecked((uint)i)).ToList();
        } else {
            ConductorCuboids = new List<uint>();
        }

        // Load block IDs
        var blockIdsAttr = tree["sparky_blockIds"] as IntArrayAttribute;
        if (blockIdsAttr != null) {
            ConductorBlockIds = blockIdsAttr.value;
        } else {
            ConductorBlockIds = Array.Empty<int>();
        }

        // Load network ID
        var netIdBytes = tree.GetBytes("sparky_networkId");
        if (netIdBytes != null && netIdBytes.Length == 16) {
            NetworkId = new Guid(netIdBytes);
        } else {
            NetworkId = Guid.Empty;
        }

        // Mark mesh for regeneration
        _meshDirty = true;
        _conductorMesh = null;
        _selectionDirty = true;
        _selectionBoxes = Array.Empty<Cuboidf>();
    }

    #endregion

    #region Conductor Voxel Access

    /// <summary>
    /// Sets multiple conductor voxels at once. More efficient than individual calls.
    /// </summary>
    public void SetConductorVoxelsBatch(IEnumerable<(int X, int Y, int Z, Material Material)> voxels) {
        bool anyChanged = false;

        foreach (var (x, y, z, material) in voxels) {
            if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
                continue;

            if (SetVoxelInternal(x, y, z, material)) {
                anyChanged = true;
            }
        }

        if (anyChanged) {
            OnVoxelsChanged();

            // Notify network manager on server
            if (Api?.Side == EnumAppSide.Server) {
                var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
                modSystem?.NetworkManager?.OnBlockVoxelsChangedBatch(Pos);
            }
        }
    }

    /// <summary>
    /// Sets a single conductor voxel at the given local coordinates (0-15 each axis).
    /// </summary>
    public void SetConductorVoxel(int x, int y, int z, Material material) {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            throw new ArgumentOutOfRangeException("Voxel coordinates must be 0-15");

        if (SetVoxelInternal(x, y, z, material)) {
            OnVoxelsChanged();

            // Notify network manager on server
            if (Api?.Side == EnumAppSide.Server) {
                var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
                modSystem?.NetworkManager?.OnBlockVoxelChanged(Pos, x, y, z, VoxelType.Conductor, material);
            }
        }
    }

    /// <summary>
    /// Removes a voxel at the given local coordinates.
    /// </summary>
    public void RemoveVoxel(int x, int y, int z) {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            return;

        if (RemoveVoxelInternal(x, y, z)) {
            OnVoxelsChanged();

            // Notify network manager on server
            if (Api?.Side == EnumAppSide.Server) {
                var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
                modSystem?.NetworkManager?.OnBlockVoxelChanged(Pos, x, y, z, VoxelType.Air, null);
            }
        }
    }

    /// <summary>
    /// Gets the conductor material at the given local coordinates, or null if air/insulator.
    /// </summary>
    public Material? GetConductorAt(int x, int y, int z) {
        if (x < 0 || x > 15 || y < 0 || y > 15 || z < 0 || z > 15)
            return null;

        foreach (var cuboid in ConductorCuboids) {
            FromUint(cuboid, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1, out int matIdx);
            if (x >= x0 && x < x1 && y >= y0 && y < y1 && z >= z0 && z < z1) {
                if (matIdx < ConductorBlockIds.Length) {
                    return GetConductorMaterial(ConductorBlockIds[matIdx]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true if there are any conductor voxels in this behavior.
    /// </summary>
    public bool HasConductors => ConductorCuboids.Count > 0;

    #endregion

    #region Internal Voxel Operations

    /// <summary>
    /// Sets a voxel internally without triggering updates.
    /// Returns true if the voxel was actually changed.
    /// </summary>
    private bool SetVoxelInternal(int x, int y, int z, Material material) {
        int blockId = GetBlockIdForMaterial(material);
        if (blockId < 0) {
            Api?.Logger.Warning($"[Sparky] No block registered for material {material.Name}");
            return false;
        }

        // First remove any existing voxel at this position
        RemoveVoxelInternal(x, y, z);

        // Get or add material index
        byte matIdx = GetOrAddMaterialIndex(blockId);

        // Add new 1x1x1 cuboid
        uint cuboid = ToUint(x, y, z, x + 1, y + 1, z + 1, matIdx);
        ConductorCuboids.Add(cuboid);

        return true;
    }

    /// <summary>
    /// Removes a voxel internally without triggering updates.
    /// Returns true if a voxel was actually removed.
    /// </summary>
    private bool RemoveVoxelInternal(int x, int y, int z) {
        bool removed = false;
        var newCuboids = new List<uint>();

        foreach (var cuboid in ConductorCuboids) {
            FromUint(cuboid, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1, out int matIdx);

            // Check if this cuboid contains the target voxel
            if (x >= x0 && x < x1 && y >= y0 && y < y1 && z >= z0 && z < z1) {
                removed = true;

                // For simplicity, if this is a 1x1x1 cuboid, just remove it
                // For larger cuboids, we'd need to split - but we only create 1x1x1 for now
                if (x1 - x0 == 1 && y1 - y0 == 1 && z1 - z0 == 1) {
                    // Don't add to newCuboids - effectively removes it
                    continue;
                }

                // TODO: Handle splitting larger cuboids if we ever create them
                // For now, just remove the entire cuboid
                Api?.Logger.Warning("[Sparky] Removing voxel from multi-voxel cuboid - entire cuboid removed");
                continue;
            }

            newCuboids.Add(cuboid);
        }

        if (removed) {
            ConductorCuboids = newCuboids;
        }

        return removed;
    }

    /// <summary>
    /// Called after voxels change to update mesh and mark dirty.
    /// </summary>
    private void OnVoxelsChanged() {
        _meshDirty = true;
        _conductorMesh = null;
        _selectionDirty = true;
        Blockentity.MarkDirty(true);
    }

    /// <summary>
    /// Gets or adds a material index for the given block ID.
    /// </summary>
    private byte GetOrAddMaterialIndex(int blockId) {
        for (int i = 0; i < ConductorBlockIds.Length; i++) {
            if (ConductorBlockIds[i] == blockId)
                return (byte)i;
        }

        // Add new material
        if (ConductorBlockIds.Length >= 255) {
            Api?.Logger.Warning("[Sparky] Material palette full, reusing last index");
            return 254;
        }

        var newIds = new int[ConductorBlockIds.Length + 1];
        Array.Copy(ConductorBlockIds, newIds, ConductorBlockIds.Length);
        newIds[ConductorBlockIds.Length] = blockId;
        ConductorBlockIds = newIds;
        return (byte)(ConductorBlockIds.Length - 1);
    }

    #endregion

    #region Cuboid Packing (matches BlockEntityMicroBlock format)

    /// <summary>
    /// Packs cuboid bounds and material index into a uint.
    /// </summary>
    public static uint ToUint(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, int material) {
        return (uint)(minX | (minY << 4) | (minZ << 8) |
                      ((maxX - 1) << 12) | ((maxY - 1) << 16) | ((maxZ - 1) << 20) |
                      (material << 24));
    }

    /// <summary>
    /// Unpacks cuboid bounds and material index from a uint.
    /// </summary>
    public static void FromUint(uint val, out int x0, out int y0, out int z0,
                                out int x1, out int y1, out int z1, out int material) {
        x0 = (int)(val & 0xF);
        y0 = (int)((val >> 4) & 0xF);
        z0 = (int)((val >> 8) & 0xF);
        x1 = (int)(((val >> 12) & 0xF) + 1);
        y1 = (int)(((val >> 16) & 0xF) + 1);
        z1 = (int)(((val >> 20) & 0xF) + 1);
        material = (int)((val >> 24) & 0xFF);
    }

    #endregion

    #region Rendering

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator) {
        if (ConductorCuboids.Count == 0) {
            return false; // No conductors, don't skip block's own mesh
        }

        if (_meshDirty || _conductorMesh == null) {
            RegenMesh();
            _meshDirty = false;
        }

        if (_conductorMesh != null) {
            mesher.AddMeshData(_conductorMesh);
        }

        return false; // Don't skip the block's own mesh
    }

    /// <summary>
    /// Regenerates the conductor mesh from cuboid data.
    /// </summary>
    private void RegenMesh() {
        if (Api?.Side != EnumAppSide.Client || ConductorCuboids.Count == 0) {
            _conductorMesh = null;
            return;
        }

        if (ConductorBlockIds.Length == 0) {
            _conductorMesh = null;
            return;
        }

        var capi = Api as ICoreClientAPI;
        if (capi == null) {
            _conductorMesh = null;
            return;
        }

        // Use BlockEntityMicroBlock's static mesh generator
        _conductorMesh = BlockEntityMicroBlock.CreateMesh(
            capi,
            ConductorCuboids,
            ConductorBlockIds,
            null, // No decor
            Pos
        );
    }

    #endregion

    #region Selection Boxes

    /// <summary>
    /// Gets selection boxes for all conductor cuboids.
    /// </summary>
    public Cuboidf[] GetSelectionBoxes() {
        if (!_selectionDirty)
            return _selectionBoxes;

        _selectionBoxes = BuildSelectionBoxes(ConductorCuboids);
        _selectionDirty = false;
        return _selectionBoxes;
    }

    /// <summary>
    /// Builds selection boxes from packed cuboids.
    /// </summary>
    public static Cuboidf[] BuildSelectionBoxes(IReadOnlyList<uint> cuboids) {
        if (cuboids == null || cuboids.Count == 0)
            return Array.Empty<Cuboidf>();

        const float inv16 = 1f / 16f;
        var boxes = new Cuboidf[cuboids.Count];
        for (int i = 0; i < cuboids.Count; i++) {
            FromUint(cuboids[i], out int x0, out int y0, out int z0, out int x1, out int y1, out int z1, out _);
            boxes[i] = new Cuboidf(
                x0 * inv16, y0 * inv16, z0 * inv16,
                x1 * inv16, y1 * inv16, z1 * inv16
            );
        }

        return boxes;
    }

    #endregion

    #region Conversion to VoxelGrid

    /// <summary>
    /// Exports all conductor voxels to a VoxelGrid at the correct world positions.
    /// </summary>
    public void ExportToVoxelGrid(VoxelGrid grid, SparkyBlockPos sparkyBlockPos) {
        if (ConductorCuboids.Count == 0 || ConductorBlockIds.Length == 0)
            return;

        foreach (var cuboid in ConductorCuboids) {
            FromUint(cuboid, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1, out int matIdx);

            // Get the material for this cuboid
            Material? material = null;
            if (matIdx < ConductorBlockIds.Length) {
                material = GetConductorMaterial(ConductorBlockIds[matIdx]);
            }

            // If not a conductor, skip
            if (material == null)
                continue;

            // Export all voxels in this cuboid
            for (int y = y0; y < y1; y++) {
                for (int z = z0; z < z1; z++) {
                    for (int x = x0; x < x1; x++) {
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
    public static SparkyBlockPos ToSparkyBlockPos(BlockPos vsPos) {
        return new SparkyBlockPos(vsPos.X, vsPos.Y, vsPos.Z);
    }

    #endregion
}
