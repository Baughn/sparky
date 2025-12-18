using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Sparky.VSIntegration;

/// <summary>
/// Block type for circuit blocks that contain voxel-based electrical conductors.
/// Supports per-voxel selection boxes for precise interaction.
/// </summary>
public class BlockCircuit : Block {
    /// <summary>
    /// Enable per-voxel selection (like chiseled blocks).
    /// </summary>
    public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos) {
        return true;
    }

    /// <summary>
    /// Returns selection boxes for voxels in the block.
    /// Uses behavior-provided per-voxel selection boxes.
    /// </summary>
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) {
        var behavior = blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorCircuit>();
        if (behavior != null) {
            var boxes = behavior.GetSelectionBoxes();
            if (boxes.Length > 0)
                return boxes;
        }

        // Fallback to default full block selection
        return base.GetSelectionBoxes(blockAccessor, pos);
    }

    /// <summary>
    /// Returns collision boxes matching the selection boxes.
    /// </summary>
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) {
        // For now, use the same boxes as selection
        return GetSelectionBoxes(blockAccessor, pos);
    }

    /// <summary>
    /// Handle block interaction - delegates to wire tool if held.
    /// In cable mode, returns false to let ItemWireTool.OnHeldInteractStart handle it.
    /// </summary>
    public override bool OnBlockInteractStart(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel) {
        // Check if player is holding a wire tool
        var activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot?.Itemstack?.Item is ItemWireTool wireTool) {
            // In cable mode, let the item's OnHeldInteractStart handle it.
            // Bug fix: previously this always called OnCircuitBlockInteract which
            // places single voxels, bypassing cable mode's two-click workflow.
            if (wireTool.GetMode(activeSlot).IsCableMode())
                return false;

            return wireTool.OnCircuitBlockInteract(world, byPlayer, blockSel, activeSlot);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    /// <summary>
    /// Returns the display name for this block.
    /// </summary>
    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) {
        return "Circuit Block";
    }

    /// <summary>
    /// Called when the block is broken. Removes empty circuit blocks.
    /// </summary>
    public override void OnBlockBroken(
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier = 1) {
        var behavior = world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorCircuit>();
        if (behavior != null) {
            // TODO: Drop materials based on voxel contents
            // For now, just remove the block
        }

        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
}
