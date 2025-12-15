using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Material = Sparky.Game.Core.Material;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;
using Sparky.VSIntegration.CableLaying;

namespace Sparky.VSIntegration;

/// <summary>
/// Operating mode for the wire tool.
/// </summary>
public enum WireToolMode
{
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
public static class WireToolModeExtensions
{
    /// <summary>
    /// Gets the CrossSection for a cable mode, or null for SingleVoxel mode.
    /// </summary>
    public static CrossSection? GetCrossSection(this WireToolMode mode) => mode switch
    {
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
    public static string GetDisplayName(this WireToolMode mode) => mode switch
    {
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
public class ItemWireTool : Item
{
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
    public WireToolMode GetMode(ItemSlot slot)
    {
        return (WireToolMode)slot.Itemstack.Attributes.GetInt("wireToolMode", 0);
    }

    /// <summary>
    /// Sets the wire tool mode in ItemStack attributes and clears cable state.
    /// </summary>
    public void SetMode(ItemSlot slot, WireToolMode mode, IPlayer player)
    {
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
    public Material GetSelectedMaterial(ItemSlot slot)
    {
        int index = slot.Itemstack.Attributes.GetInt("selectedMaterialIndex", 0);
        return Materials[Math.Clamp(index, 0, Materials.Length - 1)];
    }

    /// <summary>
    /// Sets the selected material in the ItemStack attributes.
    /// </summary>
    public void SetSelectedMaterial(ItemSlot slot, Material material)
    {
        int index = Array.IndexOf(Materials, material);
        slot.Itemstack.Attributes.SetInt("selectedMaterialIndex", Math.Max(0, index));
        slot.MarkDirty();
    }

    /// <summary>
    /// Cycles to the next material.
    /// </summary>
    public void CycleNextMaterial(ItemSlot slot)
    {
        int index = slot.Itemstack.Attributes.GetInt("selectedMaterialIndex", 0);
        index = (index + 1) % Materials.Length;
        slot.Itemstack.Attributes.SetInt("selectedMaterialIndex", index);
        slot.MarkDirty();
    }

    /// <summary>
    /// Cycles to the previous material.
    /// </summary>
    public void CyclePreviousMaterial(ItemSlot slot)
    {
        int index = slot.Itemstack.Attributes.GetInt("selectedMaterialIndex", 0);
        index = (index - 1 + Materials.Length) % Materials.Length;
        slot.Itemstack.Attributes.SetInt("selectedMaterialIndex", index);
        slot.MarkDirty();
    }

    /// <summary>
    /// Handle left-click (attack) - removes voxels from circuit blocks.
    /// </summary>
    public override void OnHeldAttackStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handling)
    {
        if (blockSel == null)
        {
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
            return;
        }

        var world = byEntity.World;
        var block = world.BlockAccessor.GetBlock(blockSel.Position);

        // If targeting a circuit block, remove voxel instead of breaking block
        if (block is BlockCircuit)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityCircuit;
            if (be != null)
            {
                var (localX, localY, localZ) = GetClickedVoxel(blockSel);
                be.RemoveVoxel(localX, localY, localZ);

                // If block is now empty, remove it
                if (be.VoxelCuboids == null || be.VoxelCuboids.Count == 0)
                {
                    world.BlockAccessor.SetBlock(0, blockSel.Position);
                }

                handling = EnumHandHandling.PreventDefault;
                return;
            }
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
        ref EnumHandHandling handling)
    {
        if (blockSel == null)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        var world = byEntity.World;
        var player = (byEntity as EntityPlayer)?.Player;
        if (player == null)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        // Get mode and material from ItemStack.Attributes
        var mode = GetMode(slot);
        var material = GetSelectedMaterial(slot);

        api?.Logger.Debug($"[Sparky WireTool] OnHeldInteractStart: mode={mode}, isCableMode={mode.IsCableMode()}, side={world.Side}");

        // Handle cable laying modes - only on client side
        // The client manages the state machine and places blocks (which syncs to server)
        if (mode.IsCableMode())
        {
            if (world.Side == EnumAppSide.Client && api != null)
            {
                // Get or create per-player cable state from ModSystem
                var modSystem = api.ModLoader.GetModSystem<SparkyModSystem>();
                var crossSection = mode.GetCrossSection()!.Value;
                var cableState = modSystem.GetOrCreateCableState(player.PlayerUID, crossSection);

                HandleCableModeInteract(world, blockSel, cableState, material, ref handling);
            }
            else
            {
                // Server side: just prevent default, client handles everything
                handling = EnumHandHandling.PreventDefault;
            }
            return;
        }

        // SingleVoxel mode - original behavior
        var pos = blockSel.Position;
        var block = world.BlockAccessor.GetBlock(pos);

        // If targeting a circuit block, place voxel
        if (block is BlockCircuit)
        {
            OnCircuitBlockInteract(world, player, blockSel, slot);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        // If targeting a replaceable block (air, grass, etc), place new circuit block
        if (block.Replaceable >= 6000)
        {
            PlaceNewCircuitBlock(world, blockSel, byEntity, slot);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        // If targeting a solid block, try to place on the adjacent face
        var adjacentPos = blockSel.Position.AddCopy(blockSel.Face);
        var be = GetOrCreateCircuitBlock(world, adjacentPos);
        if (be != null)
        {
            var (localX, localY, localZ) = GetVoxelPositionOnFace(blockSel);
            be.SetConductorVoxel(localX, localY, localZ, material);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
    }

    /// <summary>
    /// Handles right-click in cable laying mode.
    /// </summary>
    private void HandleCableModeInteract(
        IWorldAccessor world,
        BlockSelection blockSel,
        CableLayingState cableState,
        Material material,
        ref EnumHandHandling handling)
    {
        // Calculate clicked voxel position
        var voxelPos = GetTargetVoxelPos(blockSel);

        api?.Logger.Debug($"[Sparky WireTool] HandleCableModeInteract: phase={cableState.CurrentPhase}, voxelPos={voxelPos}, side={world.Side}");

        switch (cableState.CurrentPhase)
        {
            case CableLayingState.Phase.Idle:
                // First click: select start position
                api?.Logger.Debug($"[Sparky WireTool] Selecting start at {voxelPos}");
                cableState.SelectStart(voxelPos, world.BlockAccessor);
                api?.Logger.Debug($"[Sparky WireTool] After SelectStart: phase={cableState.CurrentPhase}");
                handling = EnumHandHandling.PreventDefault;
                break;

            case CableLayingState.Phase.StartSelected:
            case CableLayingState.Phase.PathReady:
                // Second click: place cable if we have a valid path
                api?.Logger.Debug($"[Sparky WireTool] Second click: hasPath={cableState.CurrentPath != null}, pathCount={cableState.CurrentPath?.Path.Count ?? 0}");
                if (cableState.CurrentPath?.Path.Count > 0)
                {
                    api?.Logger.Debug($"[Sparky WireTool] Placing cable path with {cableState.CurrentPath.Value.Path.Count} voxels");
                    PlaceCablePath(world, cableState.CurrentPath.Value, material);
                    cableState.Cancel();
                }
                else
                {
                    api?.Logger.Debug("[Sparky WireTool] No valid path to place");
                }
                handling = EnumHandHandling.PreventDefault;
                break;
        }
    }

    /// <summary>
    /// Gets the global voxel position for a block selection.
    /// </summary>
    private VoxelPos GetTargetVoxelPos(BlockSelection blockSel)
    {
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
    /// Places all voxels in a cable path using batch operations for efficiency.
    /// </summary>
    private void PlaceCablePath(IWorldAccessor world, PathResult path, Material material)
    {
        // Group voxels by block
        var voxelsByBlock = new Dictionary<(int X, int Y, int Z), List<(int X, int Y, int Z)>>();

        foreach (var voxel in path.Path)
        {
            // Handle negative coordinates properly for block position
            var blockX = voxel.X >= 0 ? voxel.X / 16 : (voxel.X - 15) / 16;
            var blockY = voxel.Y >= 0 ? voxel.Y / 16 : (voxel.Y - 15) / 16;
            var blockZ = voxel.Z >= 0 ? voxel.Z / 16 : (voxel.Z - 15) / 16;
            var localX = ((voxel.X % 16) + 16) % 16;
            var localY = ((voxel.Y % 16) + 16) % 16;
            var localZ = ((voxel.Z % 16) + 16) % 16;

            var blockKey = (blockX, blockY, blockZ);
            if (!voxelsByBlock.TryGetValue(blockKey, out var list))
            {
                list = new List<(int, int, int)>();
                voxelsByBlock[blockKey] = list;
            }
            list.Add((localX, localY, localZ));
        }

        // Place voxels in each block using batch method
        foreach (var (blockKey, voxels) in voxelsByBlock)
        {
            var blockPos = new Vintagestory.API.MathTools.BlockPos(blockKey.X, blockKey.Y, blockKey.Z);
            var be = GetOrCreateCircuitBlock(world, blockPos);
            if (be == null)
                continue;

            // Convert to batch format with material
            var batchVoxels = voxels.Select(v => (v.X, v.Y, v.Z, material));
            be.SetConductorVoxelsBatch(batchVoxels);
        }
    }

    /// <summary>
    /// Gets an existing circuit block entity, or creates a new circuit block if the position is replaceable.
    /// Returns null if the position is occupied by a non-circuit, non-replaceable block.
    /// </summary>
    private BlockEntityCircuit? GetOrCreateCircuitBlock(IWorldAccessor world, Vintagestory.API.MathTools.BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);

        if (block is BlockCircuit)
        {
            return world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCircuit;
        }

        if (block.Replaceable >= 6000)
        {
            var circuitBlock = world.GetBlock(new AssetLocation("sparky:circuitblock"));
            if (circuitBlock == null)
            {
                api?.Logger.Warning("Could not find sparky:circuitblock");
                return null;
            }

            world.BlockAccessor.SetBlock(circuitBlock.BlockId, pos);
            return world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCircuit;
        }

        return null;
    }

    /// <summary>
    /// Called when right-clicking an existing circuit block. Places a conductor voxel.
    /// </summary>
    public bool OnCircuitBlockInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemSlot slot)
    {
        // Check if adjacent voxel would be outside this block
        var (localX, localY, localZ, outsideBlock) = GetAdjacentVoxelWithOverflow(blockSel);

        // Determine target block position
        var targetPos = outsideBlock
            ? blockSel.Position.AddCopy(blockSel.Face)
            : blockSel.Position;

        var be = GetOrCreateCircuitBlock(world, targetPos);
        if (be == null) return false;

        var material = GetSelectedMaterial(slot);
        be.SetConductorVoxel(localX, localY, localZ, material);
        return true;
    }

    /// <summary>
    /// Places a new circuit block and sets the initial voxel.
    /// </summary>
    private void PlaceNewCircuitBlock(IWorldAccessor world, BlockSelection blockSel, EntityAgent byEntity, ItemSlot slot)
    {
        var be = GetOrCreateCircuitBlock(world, blockSel.Position);
        if (be == null) return;

        // Place voxel adjacent to the clicked face (in front of it)
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;
        var (localX, localY, localZ) = VoxelPositionHelper.GetAdjacentVoxel(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
        var material = GetSelectedMaterial(slot);
        be.SetConductorVoxel(localX, localY, localZ, material);
    }

    /// <summary>
    /// Gets the voxel coordinates (0-15) of the voxel whose face was clicked.
    /// This is the voxel "behind" the clicked face.
    /// </summary>
    private (int X, int Y, int Z) GetClickedVoxel(BlockSelection blockSel)
    {
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
    private (int X, int Y, int Z, bool OutsideBlock) GetAdjacentVoxelWithOverflow(BlockSelection blockSel)
    {
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;
        return VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
    }

    /// <summary>
    /// Gets the voxel position when clicking a solid block to place in an adjacent circuit block.
    /// The voxel should be on the face of the circuit block that touches the solid block.
    /// </summary>
    private (int X, int Y, int Z) GetVoxelPositionOnFace(BlockSelection solidBlockSel)
    {
        var face = solidBlockSel.Face;
        var hit = solidBlockSel.HitPosition;

        // The hit position is on the solid block's face.
        // We need to map this to the adjacent block's coordinate space.
        // The voxel should be on the face of the adjacent block that touches the solid block.
        // That's the OPPOSITE face from where we clicked.

        // Map hit coordinates to the adjacent block's space
        double adjX = face.Axis == EnumAxis.X ? (face.Normali.X > 0 ? 0.0 : 1.0) : hit.X;
        double adjY = face.Axis == EnumAxis.Y ? (face.Normali.Y > 0 ? 0.0 : 1.0) : hit.Y;
        double adjZ = face.Axis == EnumAxis.Z ? (face.Normali.Z > 0 ? 0.0 : 1.0) : hit.Z;

        // Use GetClickedVoxel with the opposite face normal to get the voxel ON the face
        // (not adjacent to it)
        return VoxelPositionHelper.GetClickedVoxel(
            adjX, adjY, adjZ,
            -face.Normalf.X, -face.Normalf.Y, -face.Normalf.Z);
    }

    /// <summary>
    /// Returns info text shown in the hotbar.
    /// Note: ItemStack doesn't have direct slot access here, so we show mode from attributes.
    /// </summary>
    public override string GetHeldItemName(ItemStack itemStack)
    {
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
        bool withDebugInfo)
    {
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
