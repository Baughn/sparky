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
    public override void CreateBehaviors(Block block, IWorldAccessor worldForResolve) {
        base.CreateBehaviors(block, worldForResolve);

        // Add BEBehaviorCircuit if not already present (from JSON or other source)
        if (GetBehavior<BEBehaviorCircuit>() == null) {
            var behavior = new BEBehaviorCircuit(this);
            Behaviors.Add(behavior);
        }
    }
}
