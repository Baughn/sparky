using Sparky.VSIntegration.BehaviorSync;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Sparky.VSIntegration;

/// <summary>
/// Factory for creating and retrieving BEBehaviorCircuit instances at block positions.
/// Handles the complexity of adding circuits to both dedicated circuit blocks and
/// solid blocks (like stairs) via BlockEntityCircuitHost.
/// </summary>
public static class CircuitBlockFactory {
    /// <summary>
    /// Gets an existing circuit behavior at a position, or creates one.
    /// For replaceable blocks (air, grass), places a circuit block.
    /// For solid blocks without a BE, spawns BlockEntityCircuitHost.
    /// Returns null if block already has a BE without circuit behavior.
    /// </summary>
    public static BEBehaviorCircuit? GetOrCreateAt(IWorldAccessor world, BlockPos pos) {
        var api = world.Api;
        var blockEntity = world.BlockAccessor.GetBlockEntity(pos);
        var block = world.BlockAccessor.GetBlock(pos);

        api.Logger.Debug($"[Sparky] GetOrCreateAt({pos}): block={block.Code}, hasBlockEntity={blockEntity != null}, beType={blockEntity?.GetType().Name}");

        // Check if existing block entity has circuit behavior
        var behavior = blockEntity?.GetBehavior<BEBehaviorCircuit>();
        if (behavior != null) {
            api.Logger.Debug($"[Sparky] GetOrCreateAt({pos}): found existing behavior with {behavior.ConductorCuboids.Count} cuboids");
            return behavior;
        }

        // Check if block is replaceable - place a circuit block
        if (block.Replaceable >= 6000) {
            var circuitBlock = world.GetBlock(new AssetLocation("sparky:circuitblock"));
            if (circuitBlock == null) {
                api.Logger.Warning($"[Sparky] GetOrCreateAt({pos}): circuitblock asset not found");
                return null;
            }

            world.BlockAccessor.SetBlock(circuitBlock.BlockId, pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            blockEntity = world.BlockAccessor.GetBlockEntity(pos);
            behavior = blockEntity?.GetBehavior<BEBehaviorCircuit>();
            if (behavior == null) {
                api.Logger.Warning($"[Sparky] GetOrCreateAt({pos}): placed circuitblock but no BEBehaviorCircuit found");
            }
            return behavior;
        }

        // Block already has a BE (without circuit behavior) - reject placement
        // We only support adding circuits to blocks with no BE at all
        if (blockEntity != null) {
            api.Logger.Debug($"[Sparky] GetOrCreateAt({pos}): cannot place circuit in {block.Code} - block already has BlockEntity ({blockEntity.GetType().Name})");
            return null;
        }

        // Solid block without BlockEntity - spawn our CircuitHost
        api.Logger.Debug($"[Sparky] GetOrCreateAt({pos}): spawning CircuitHost for {block.Code}");
        world.BlockAccessor.SpawnBlockEntity("CircuitHost", pos);
        blockEntity = world.BlockAccessor.GetBlockEntity(pos);
        if (blockEntity == null) {
            api.Logger.Warning($"[Sparky] GetOrCreateAt({pos}): SpawnBlockEntity(CircuitHost) failed for {block.Code}");
            return null;
        }

        // CircuitHost.CreateBehaviors() adds the behavior automatically
        behavior = blockEntity.GetBehavior<BEBehaviorCircuit>();
        if (behavior == null) {
            api.Logger.Warning($"[Sparky] GetOrCreateAt({pos}): CircuitHost created but no BEBehaviorCircuit found");
            return null;
        }

        // Notify clients to add the behavior (server-side only)
        // This handles the timing window before VS syncs the BE creation
        if (api.Side == EnumAppSide.Server) {
            var syncSystem = api.ModLoader.GetModSystem<BehaviorSyncSystem>();
            syncSystem?.NotifyBehaviorAdded(pos);
        }

        return behavior;
    }
}
