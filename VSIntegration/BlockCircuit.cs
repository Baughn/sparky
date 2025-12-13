using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Sparky.VSIntegration;

/// <summary>
/// Block type for circuit blocks that contain voxel-based electrical conductors.
/// Supports per-voxel selection boxes for precise interaction.
/// </summary>
public class BlockCircuit : Block
{
    /// <summary>
    /// Enable per-voxel selection (like chiseled blocks).
    /// </summary>
    public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
    {
        return true;
    }

    /// <summary>
    /// Returns selection boxes for each non-air voxel in the block.
    /// </summary>
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var be = blockAccessor.GetBlockEntity(pos) as BlockEntityCircuit;
        if (be != null)
        {
            var boxes = be.GetVoxelSelectionBoxes();
            if (boxes.Length > 0)
            {
                return boxes;
            }
        }

        // Fallback to default full block selection
        return base.GetSelectionBoxes(blockAccessor, pos);
    }

    /// <summary>
    /// Returns collision boxes matching the selection boxes.
    /// </summary>
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        // For now, use the same boxes as selection
        return GetSelectionBoxes(blockAccessor, pos);
    }

    /// <summary>
    /// Handle block interaction - delegates to wire tool if held.
    /// </summary>
    public override bool OnBlockInteractStart(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        // Check if player is holding a wire tool
        var activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        var activeItem = activeSlot?.Itemstack?.Item;

        if (activeItem is ItemWireTool wireTool)
        {
            return wireTool.OnCircuitBlockInteract(world, byPlayer, blockSel);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    /// <summary>
    /// Returns the display name for this block.
    /// </summary>
    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
    {
        return "Circuit Block";
    }

    /// <summary>
    /// Called when the block is broken. Removes empty circuit blocks.
    /// </summary>
    public override void OnBlockBroken(
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCircuit;
        if (be != null)
        {
            // TODO: Drop materials based on voxel contents
            // For now, just remove the block
        }

        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
}
