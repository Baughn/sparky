using Newtonsoft.Json.Linq;
using Sparky.VSIntegration.BehaviorSync;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Sparky.VSIntegration;

/// <summary>
/// Factory for creating and retrieving BEBehaviorCircuit instances at block positions.
/// Handles the complexity of adding circuits to both dedicated circuit blocks and
/// existing blocks (like stairs) via BlockEntityGeneric.
/// </summary>
public static class CircuitBlockFactory {
    /// <summary>
    /// Gets an existing circuit behavior at a position, or creates one dynamically.
    /// For replaceable blocks (air, grass), places a circuit block.
    /// For solid blocks, spawns a Generic BlockEntity if needed and adds the behavior.
    /// Returns null only if BlockEntity creation fails.
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
            blockEntity = world.BlockAccessor.GetBlockEntity(pos);
            behavior = blockEntity?.GetBehavior<BEBehaviorCircuit>();
            if (behavior == null) {
                api.Logger.Warning($"[Sparky] GetOrCreateAt({pos}): placed circuitblock but no BEBehaviorCircuit found");
            }
            return behavior;
        }

        // Solid block without BlockEntity - spawn a Generic BlockEntity first
        if (blockEntity == null) {
            api.Logger.Debug($"[Sparky] GetOrCreateAt({pos}): spawning Generic BlockEntity for {block.Code}");
            world.BlockAccessor.SpawnBlockEntity("Generic", pos);
            blockEntity = world.BlockAccessor.GetBlockEntity(pos);
            if (blockEntity == null) {
                api.Logger.Warning($"[Sparky] GetOrCreateAt({pos}): SpawnBlockEntity(Generic) failed for {block.Code}");
                return null;
            }
        }

        // Add circuit behavior to the BlockEntity
        api.Logger.Debug($"[Sparky] GetOrCreateAt({pos}): adding BEBehaviorCircuit to {block.Code} (BE type: {blockEntity.GetType().Name})");
        behavior = new BEBehaviorCircuit(blockEntity);
        behavior.Initialize(api, new JsonObject(new JObject()));
        blockEntity.Behaviors.Add(behavior);

        // Notify clients to add the behavior (server-side only)
        if (api.Side == EnumAppSide.Server) {
            var syncSystem = api.ModLoader.GetModSystem<BehaviorSyncSystem>();
            syncSystem?.NotifyBehaviorAdded(pos);
        }

        return behavior;
    }
}
