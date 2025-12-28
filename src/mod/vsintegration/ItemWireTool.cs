using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Material = Sparky.Voxel.Material;
using Sparky.Mna.Topology.CableLaying;
using Sparky.Voxel;
using Sparky.VSIntegration.CableLaying;
using Sparky.VSIntegration.Preview;

namespace Sparky.VSIntegration;

/// <summary>
/// Operating mode for the wire tool.
/// </summary>
public enum WireToolMode {
    /// <summary>Default mode: place/remove single voxels.</summary>
    SingleVoxel,
    /// <summary>Cable laying with 1x1 cross-section.</summary>
    Cable1x1,
    /// <summary>Cable laying with 1x2 cross-section (light circuits).</summary>
    Cable1x2,
    /// <summary>Cable laying with 2x2 cross-section (medium loads).</summary>
    Cable2x2,
    /// <summary>Cable laying with 2x3 cross-section (heavy loads).</summary>
    Cable2x3,
    /// <summary>Cable laying with 3x5 cross-section (main feeds).</summary>
    Cable3x5
}

/// <summary>
/// Extension methods for WireToolMode.
/// </summary>
public static class WireToolModeExtensions {
    /// <summary>
    /// Gets the CrossSection for a cable mode, or null for SingleVoxel mode.
    /// </summary>
    public static CrossSection? GetCrossSection(this WireToolMode mode) => mode switch {
        WireToolMode.Cable1x1 => CrossSection.Size1x1,
        WireToolMode.Cable1x2 => CrossSection.Size1x2,
        WireToolMode.Cable2x2 => CrossSection.Size2x2,
        WireToolMode.Cable2x3 => CrossSection.Size2x3,
        WireToolMode.Cable3x5 => CrossSection.Size3x5,
        _ => null
    };

    /// <summary>
    /// Returns true if this is a cable laying mode (not SingleVoxel).
    /// </summary>
    public static bool IsCableMode(this WireToolMode mode) => mode != WireToolMode.SingleVoxel;

    /// <summary>
    /// Gets a display name for the mode.
    /// </summary>
    public static string GetDisplayName(this WireToolMode mode) => mode switch {
        WireToolMode.SingleVoxel => "Single Voxel",
        WireToolMode.Cable1x1 => "Cable 1×1",
        WireToolMode.Cable1x2 => "Cable 1×2",
        WireToolMode.Cable2x2 => "Cable 2×2",
        WireToolMode.Cable2x3 => "Cable 2×3",
        WireToolMode.Cable3x5 => "Cable 3×5",
        _ => mode.ToString()
    };
}

/// <summary>
/// Tool for placing and removing conductor voxels in circuit blocks.
/// Note: Item classes are singletons in VS, so all per-player/per-item state
/// must be stored in ItemStack.Attributes or ModSystem dictionaries.
/// </summary>
public class ItemWireTool : Item {
    /// <summary>
    /// Material selection cycle order.
    /// </summary>
    private static readonly Material[] Materials =
    {
        Material.Copper,
        Material.Gold,
        Material.Lead,
        Material.Iron
    };

    /// <summary>
    /// Gets the wire tool mode from the ItemStack attributes.
    /// </summary>
    public WireToolMode GetMode(ItemSlot slot) {
        return (WireToolMode)slot.Itemstack.Attributes.GetInt("wireToolMode", 0);
    }

    /// <summary>
    /// Sets the wire tool mode in ItemStack attributes and clears cable state.
    /// </summary>
    public void SetMode(ItemSlot slot, WireToolMode mode, IPlayer player) {
        var oldMode = GetMode(slot);
        if (oldMode == mode)
            return;

        slot.Itemstack.Attributes.SetInt("wireToolMode", (int)mode);
        slot.MarkDirty();

        // Clear any existing cable state when changing modes
        var modSystem = api.ModLoader.GetModSystem<SparkyModSystem>();
        modSystem.ClearCableState(player.PlayerUID);
    }

    /// <summary>
    /// Gets the selected material from the ItemStack attributes.
    /// </summary>
    public Material GetSelectedMaterial(ItemSlot slot) {
        int index = slot.Itemstack.Attributes.GetInt("selectedMaterialIndex", 0);
        return Materials[Math.Clamp(index, 0, Materials.Length - 1)];
    }

    /// <summary>
    /// Sets the selected material in the ItemStack attributes.
    /// </summary>
    public void SetSelectedMaterial(ItemSlot slot, Material material) {
        int index = Array.IndexOf(Materials, material);
        slot.Itemstack.Attributes.SetInt("selectedMaterialIndex", Math.Max(0, index));
        slot.MarkDirty();
    }

    /// <summary>
    /// Cycles to the next material.
    /// </summary>
    public void CycleNextMaterial(ItemSlot slot) {
        int index = slot.Itemstack.Attributes.GetInt("selectedMaterialIndex", 0);
        index = (index + 1) % Materials.Length;
        slot.Itemstack.Attributes.SetInt("selectedMaterialIndex", index);
        slot.MarkDirty();
    }

    /// <summary>
    /// Cycles to the previous material.
    /// </summary>
    public void CyclePreviousMaterial(ItemSlot slot) {
        int index = slot.Itemstack.Attributes.GetInt("selectedMaterialIndex", 0);
        index = (index - 1 + Materials.Length) % Materials.Length;
        slot.Itemstack.Attributes.SetInt("selectedMaterialIndex", index);
        slot.MarkDirty();
    }

    /// <summary>
    /// Gets the material index for network serialization.
    /// </summary>
    private int GetMaterialIndex(Material material) {
        return Array.IndexOf(Materials, material);
    }

    /// <summary>
    /// Sends a voxel placement/removal request to the server.
    /// </summary>
    private void SendVoxelPlacement(VoxelPlacementRequest request) {
        var previewSystem = api?.ModLoader.GetModSystem<VoxelPreviewSystem>();
        previewSystem?.SendVoxelPlacement(request);
    }

    /// <summary>
    /// Handle left-click (attack) - removes voxels from circuit blocks.
    /// </summary>
    public override void OnHeldAttackStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handling) {
        if (blockSel == null) {
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
            return;
        }

        var world = byEntity.World;

        // Server side: prevent default, client sends network message
        var block = world.BlockAccessor.GetBlock(blockSel.Position);

        if (world.Side == EnumAppSide.Server) {
            if (IsCircuitHost(world, blockSel.Position, block)) {
                handling = EnumHandHandling.PreventDefault;
            }
            return;
        }

        // Client side: send removal request to server
        if (IsCircuitHost(world, blockSel.Position, block)) {
            var (localX, localY, localZ) = GetClickedVoxel(blockSel);

            // Convert to global voxel coordinates
            var globalX = blockSel.Position.X * 16 + localX;
            var globalY = blockSel.Position.Y * 16 + localY;
            var globalZ = blockSel.Position.Z * 16 + localZ;

            var request = new VoxelPlacementRequest {
                IsRemoval = true,
                Voxels = new List<VoxelPlacement>
                {
                    new VoxelPlacement(globalX, globalY, globalZ)
                }
            };
            SendVoxelPlacement(request);

            handling = EnumHandHandling.PreventDefault;
            return;
        }

        base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
    }

    /// <summary>
    /// Handle right-click (interact) - places voxels or handles cable laying.
    /// </summary>
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling) {
        if (blockSel == null) {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        var world = byEntity.World;
        var player = (byEntity as EntityPlayer)?.Player;
        if (player == null) {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        // Server side: always prevent default, client handles all placement
        // Block changes sync automatically via VS's block change system
        if (world.Side == EnumAppSide.Server) {
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        // Client side: handle all placement logic
        var mode = GetMode(slot);
        var material = GetSelectedMaterial(slot);

        api?.Logger.Debug($"[Sparky WireTool] OnHeldInteractStart: mode={mode}, isCableMode={mode.IsCableMode()}, side={world.Side}");

        if (api == null) {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        var modSystem = api.ModLoader.GetModSystem<SparkyModSystem>();

        // Handle cable laying modes
        if (mode.IsCableMode()) {
            var crossSection = mode.GetCrossSection()!.Value;
            var cableState = modSystem.GetOrCreateCableState(player.PlayerUID, crossSection);

            HandleCableModeInteract(world, blockSel, cableState, material, ref handling);
            return;
        }

        // SingleVoxel mode - use preview's pre-calculated position
        var target = modSystem.GetPreviewTarget(player.PlayerUID);
        if (target == null) {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        var matIndex = GetMaterialIndex(material);
        var request = new VoxelPlacementRequest {
            Voxels = [new VoxelPlacement(target.Value.X, target.Value.Y, target.Value.Z, matIndex)]
        };
        SendVoxelPlacement(request);
        handling = EnumHandHandling.PreventDefault;
    }

    private static bool IsCircuitHost(IWorldAccessor world, Vintagestory.API.MathTools.BlockPos pos, Block block) {
        if (block is BlockCircuit)
            return true;

        return world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorCircuit>() != null;
    }

    /// <summary>
    /// Handles right-click in cable laying mode.
    /// </summary>
    private void HandleCableModeInteract(
        IWorldAccessor world,
        BlockSelection blockSel,
        CableLayingState cableState,
        Material material,
        ref EnumHandHandling handling) {
        // Calculate clicked voxel position and face direction
        var voxelPos = GetTargetVoxelPos(blockSel);
        var uprightDir = blockSel.Face.ToVoxelDirection();
        float currentTime = (float)world.ElapsedMilliseconds / 1000f;

        api?.Logger.Debug($"[Sparky WireTool] HandleCableModeInteract: phase={cableState.CurrentPhase}, voxelPos={voxelPos}, side={world.Side}");

        switch (cableState.CurrentPhase) {
            case CableLayingState.Phase.Idle:
                // First click: use same snapping logic as preview to get consistent positions
                var snappedPositions = cableState.GetSnappedStartPositions(voxelPos, world.BlockAccessor, uprightDir, currentTime);
                api?.Logger.Debug($"[Sparky WireTool] Selecting start at snapped positions (count={snappedPositions.Count})");
                cableState.SelectStart(snappedPositions, world.BlockAccessor);
                api?.Logger.Debug($"[Sparky WireTool] After SelectStart: phase={cableState.CurrentPhase}");
                handling = EnumHandHandling.PreventDefault;
                break;

            case CableLayingState.Phase.StartSelected:
            case CableLayingState.Phase.PathReady:
                // Second click: place cable if we have a valid path, then reset
                api?.Logger.Debug($"[Sparky WireTool] Second click: hasPath={cableState.CurrentPath != null}, pathCount={cableState.CurrentPath?.Path.Count ?? 0}");
                if (cableState.CurrentPath?.Path.Count > 0) {
                    api?.Logger.Debug($"[Sparky WireTool] Placing cable path with {cableState.CurrentPath.Value.Path.Count} voxels");
                    PlaceCablePath(world, cableState.CurrentPath.Value, material);
                } else {
                    api?.Logger.Debug("[Sparky WireTool] No valid path to place");
                }
                // Always reset to idle on second click
                cableState.Cancel();
                handling = EnumHandHandling.PreventDefault;
                break;
        }
    }

    /// <summary>
    /// Gets the global voxel position for a block selection.
    /// </summary>
    private VoxelPos GetTargetVoxelPos(BlockSelection blockSel) {
        var (localX, localY, localZ, outsideBlock) = GetAdjacentVoxelWithOverflow(blockSel);

        var blockPos = outsideBlock
            ? blockSel.Position.AddCopy(blockSel.Face)
            : blockSel.Position;

        return new VoxelPos(
            blockPos.X * 16 + localX,
            blockPos.Y * 16 + localY,
            blockPos.Z * 16 + localZ);
    }

    /// <summary>
    /// Sends a cable path placement request to the server.
    /// </summary>
    private void PlaceCablePath(IWorldAccessor world, PathResult path, Material material) {
        var matIndex = GetMaterialIndex(material);
        var request = new VoxelPlacementRequest {
            Voxels = path.Path.Select(v => new VoxelPlacement(v.X, v.Y, v.Z, matIndex)).ToList()
        };
        SendVoxelPlacement(request);
    }

    /// <summary>
    /// Called when right-clicking an existing circuit block. Sends placement request to server.
    /// </summary>
    public bool OnCircuitBlockInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemSlot slot) {
        // Server side: prevent default, client sends network message
        if (world.Side == EnumAppSide.Server)
            return true;

        // Client side: send placement request
        var (localX, localY, localZ, outsideBlock) = GetAdjacentVoxelWithOverflow(blockSel);
        var targetPos = outsideBlock ? blockSel.Position.AddCopy(blockSel.Face) : blockSel.Position;
        var globalX = targetPos.X * 16 + localX;
        var globalY = targetPos.Y * 16 + localY;
        var globalZ = targetPos.Z * 16 + localZ;

        var material = GetSelectedMaterial(slot);
        var matIndex = GetMaterialIndex(material);

        var request = new VoxelPlacementRequest {
            Voxels = new List<VoxelPlacement> { new VoxelPlacement(globalX, globalY, globalZ, matIndex) }
        };
        SendVoxelPlacement(request);
        return true;
    }

    /// <summary>
    /// Gets the voxel coordinates (0-15) of the voxel whose face was clicked.
    /// This is the voxel "behind" the clicked face.
    /// </summary>
    private (int X, int Y, int Z) GetClickedVoxel(BlockSelection blockSel) {
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;
        return VoxelPositionHelper.GetClickedVoxel(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
    }

    /// <summary>
    /// Gets the voxel coordinates for placing adjacent to a clicked face,
    /// and indicates if the position is outside the current block.
    /// If outside, coordinates are wrapped to the adjacent block's local space.
    /// </summary>
    private (int X, int Y, int Z, bool OutsideBlock) GetAdjacentVoxelWithOverflow(BlockSelection blockSel) {
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;
        return VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
    }

    /// <summary>
    /// Returns info text shown in the hotbar.
    /// Note: ItemStack doesn't have direct slot access here, so we show mode from attributes.
    /// </summary>
    public override string GetHeldItemName(ItemStack itemStack) {
        var mode = (WireToolMode)itemStack.Attributes.GetInt("wireToolMode", 0);
        int matIndex = itemStack.Attributes.GetInt("selectedMaterialIndex", 0);
        var material = Materials[Math.Clamp(matIndex, 0, Materials.Length - 1)];
        return $"Wire Tool ({material.Name}) - {mode.GetDisplayName()}";
    }

    /// <summary>
    /// Returns detailed info for the handbook.
    /// </summary>
    public override void GetHeldItemInfo(
        ItemSlot inSlot,
        StringBuilder dsc,
        IWorldAccessor world,
        bool withDebugInfo) {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var mode = GetMode(inSlot);
        var material = GetSelectedMaterial(inSlot);
        dsc.AppendLine($"Mode: {mode.GetDisplayName()}");
        dsc.AppendLine($"Material: {material.Name}");
        dsc.AppendLine("Right click: Place conductor voxel");
        dsc.AppendLine("Left click: Remove voxel");
        dsc.AppendLine("F key: Change mode");
    }
}
