using Vintagestory.API.Common;

namespace Sparky.VSIntegration;

/// <summary>
/// Block entity that hosts circuit behavior for solid blocks (stairs, slabs, etc.)
/// that don't have their own BlockEntity.
///
/// This is spawned by CircuitBlockFactory when placing cables in such blocks.
/// VS handles persistence automatically since this is a registered BE class.
/// </summary>
public class BlockEntityCircuitHost : BlockEntity {
    /// <summary>
    /// The block ID this host was created for. Used to detect when the block
    /// has been replaced without proper cleanup (e.g., WorldEdit, SetBlock).
    /// </summary>
    private int _originalBlockId;

    /// <summary>
    /// Check interval in milliseconds. Uses prime product (11*13=143 ticks * 50ms)
    /// to avoid synchronization with other periodic tasks.
    /// </summary>
    private const int CheckIntervalMs = 11 * 13 * 50; // 7150ms

    public override void CreateBehaviors(Block block, IWorldAccessor worldForResolve) {
        base.CreateBehaviors(block, worldForResolve);

        // Add BEBehaviorCircuit if not already present (from JSON or other source)
        if (GetBehavior<BEBehaviorCircuit>() == null) {
            var behavior = new BEBehaviorCircuit(this);
            Behaviors.Add(behavior);
        }
    }

    public override void Initialize(ICoreAPI api) {
        base.Initialize(api);

        // Store the block we were created for
        _originalBlockId = api.World.BlockAccessor.GetBlock(Pos).Id;

        // Register periodic check for orphaned hosts (block changed without cleanup).
        // BlockEntity.RegisterGameTickListener handles cleanup automatically on removal.
        RegisterGameTickListener(OnCheckBlock, CheckIntervalMs);
    }

    /// <summary>
    /// Periodic check to detect if our host block was replaced without proper cleanup.
    /// This catches cases like WorldEdit or direct SetBlock(0, pos) calls.
    /// </summary>
    private void OnCheckBlock(float dt) {
        var currentBlock = Api.World.BlockAccessor.GetBlock(Pos);

        if (currentBlock.Id != _originalBlockId) {
            Api.Logger.Debug($"[Sparky] BlockEntityCircuitHost at {Pos}: block changed from {_originalBlockId} to {currentBlock.Id}, cleaning up");
            OnBlockRemoved();
        }
    }

    public override void OnBlockRemoved() {
        Api?.Logger.Debug($"[Sparky] BlockEntityCircuitHost at {Pos}: block removed");

        base.OnBlockRemoved();

        // Explicitly remove ourselves from the chunk's BE dictionary.
        // This is needed because we're spawned on blocks that don't normally
        // have a BE, so VS doesn't automatically clean us up.
        GetBehavior<BEBehaviorCircuit>()?.OnBlockRemoved();
        // Api?.World.BlockAccessor.RemoveBlockEntity(Pos);
    }
}
