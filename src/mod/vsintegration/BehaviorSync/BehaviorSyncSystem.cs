using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Sparky.VSIntegration.BehaviorSync;

/// <summary>
/// ModSystem that synchronizes dynamically-added BEBehaviorCircuit instances
/// between server and clients.
///
/// Problem: When a behavior is dynamically added to BlockEntityGeneric on the server,
/// the client doesn't receive it. VS's FromTreeAttributes only calls behaviors already
/// in the Behaviors list.
///
/// Solution: Send a packet when behavior is added, client adds it locally, then
/// normal MarkDirty sync populates the data.
/// </summary>
public class BehaviorSyncSystem : ModSystem {
    public const string ChannelName = "sparky-behavior";

    private ICoreServerAPI? _sapi;
    private IServerNetworkChannel? _serverChannel;

    private ICoreClientAPI? _capi;
    private IClientNetworkChannel? _clientChannel;

    public override bool ShouldLoad(EnumAppSide side) => true;

    #region Server Side

    public override void StartServerSide(ICoreServerAPI api) {
        _sapi = api;

        _serverChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<BehaviorAddedPacket>();

        api.Logger.Debug("[Sparky] BehaviorSyncSystem server initialized");
    }

    /// <summary>
    /// Notifies all clients that a BEBehaviorCircuit was added at the given position.
    /// Call this immediately after dynamically adding the behavior on the server.
    /// </summary>
    public void NotifyBehaviorAdded(BlockPos pos) {
        if (_serverChannel == null) return;

        var packet = new BehaviorAddedPacket(pos.X, pos.Y, pos.Z);
        _serverChannel.BroadcastPacket(packet);

        _sapi?.Logger.Debug($"[Sparky] BehaviorSyncSystem: broadcast BehaviorAddedPacket for {pos}");
    }

    #endregion

    #region Client Side

    // Pending behavior additions that need retry (BE didn't exist yet)
    private readonly List<(BlockPos Pos, int RetriesLeft)> _pendingAdditions = new();
    private long _retryTickListenerId;

    public override void StartClientSide(ICoreClientAPI api) {
        _capi = api;

        _clientChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<BehaviorAddedPacket>()
            .SetMessageHandler<BehaviorAddedPacket>(OnBehaviorAdded);

        // Register tick listener for retry processing
        _retryTickListenerId = api.Event.RegisterGameTickListener(ProcessPendingAdditions, 100);

        api.Logger.Debug("[Sparky] BehaviorSyncSystem client initialized");
    }

    private void OnBehaviorAdded(BehaviorAddedPacket packet) {
        if (_capi == null) return;

        var pos = new BlockPos(packet.X, packet.Y, packet.Z);

        if (!TryAddBehavior(pos)) {
            // Queue for retry - BE might not exist yet
            _pendingAdditions.Add((pos.Copy(), 10)); // Retry up to 10 times (1 second)
            _capi.Logger.Debug($"[Sparky] BehaviorSyncSystem: queued {pos} for retry (BE not ready)");
        }
    }

    private void ProcessPendingAdditions(float dt) {
        if (_capi == null || _pendingAdditions.Count == 0) return;

        for (int i = _pendingAdditions.Count - 1; i >= 0; i--) {
            var (pos, retriesLeft) = _pendingAdditions[i];

            if (TryAddBehavior(pos)) {
                _pendingAdditions.RemoveAt(i);
            } else if (retriesLeft <= 1) {
                _capi.Logger.Warning($"[Sparky] BehaviorSyncSystem: giving up on {pos} after retries");
                _pendingAdditions.RemoveAt(i);
            } else {
                _pendingAdditions[i] = (pos, retriesLeft - 1);
            }
        }
    }

    private bool TryAddBehavior(BlockPos pos) {
        if (_capi == null) return false;

        var be = _capi.World.BlockAccessor.GetBlockEntity(pos);
        if (be == null) {
            return false; // BE doesn't exist yet
        }

        // Check if behavior already exists
        if (be.GetBehavior<BEBehaviorCircuit>() != null) {
            _capi.Logger.Debug($"[Sparky] BehaviorSyncSystem: behavior already exists at {pos}");
            return true; // Already done
        }

        // Add the behavior
        _capi.Logger.Debug($"[Sparky] BehaviorSyncSystem: adding BEBehaviorCircuit to {pos} (BE type: {be.GetType().Name})");

        var behavior = new BEBehaviorCircuit(be);
        behavior.Initialize(_capi, new JsonObject(new JObject()));
        be.Behaviors.Add(behavior);

        // The tree attributes sync that follows (from server's MarkDirty) will
        // populate the behavior's data via FromTreeAttributes.
        // We also mark dirty to ensure the block gets re-rendered.
        be.MarkDirty(true);
        return true;
    }

    #endregion

    public override void Dispose() {
        if (_capi != null) {
            _capi.Event.UnregisterGameTickListener(_retryTickListenerId);
        }
        _pendingAdditions.Clear();
        base.Dispose();
    }
}
