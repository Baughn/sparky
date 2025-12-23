using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Sparky.VSIntegration.Debug;

/// <summary>
/// Creative-mode debug tool that visualizes WorldVoxelCache state.
/// Right-click to add blocks to visualization; previews appear 3 blocks above.
/// </summary>
public class ItemCacheDebugTool : Item {
    /// <summary>
    /// Handle right-click - add clicked block to cache visualization.
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

        // Server side: prevent default
        if (world.Side == EnumAppSide.Server) {
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        // Client side: add block to debug state
        var modSystem = api.ModLoader.GetModSystem<SparkyModSystem>();
        var debugState = modSystem.GetOrCreateCacheDebugState(player.PlayerUID);
        debugState.AddBlock(blockSel.Position, world.BlockAccessor);

        handling = EnumHandHandling.PreventDefault;
    }

    public override void GetHeldItemInfo(
        ItemSlot inSlot,
        StringBuilder dsc,
        IWorldAccessor world,
        bool withDebugInfo) {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine("Right-click: Add block to cache visualization");
        dsc.AppendLine("Preview appears 3 blocks above clicked block");
        dsc.AppendLine("Switch away to clear previews");
        dsc.AppendLine();
        dsc.AppendLine("Colors:");
        dsc.AppendLine("  Blue/gray: Insulation");
        dsc.AppendLine("  Orange: PreExistingConductor");
        dsc.AppendLine("  Green: CableConductor");
        dsc.AppendLine("  Red: Unroutable");
    }
}
