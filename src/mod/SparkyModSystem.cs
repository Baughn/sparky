using System;
using System.Collections.Generic;
using Sparky.Game.Core.CableLaying;
using Sparky.VSIntegration;
using Sparky.VSIntegration.CableLaying;
using Sparky.VSIntegration.Debug;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

using Material = Sparky.Game.Core.Material;

namespace Sparky;

/// <summary>
/// Sparky mod - electrical circuit simulation for Vintage Story
/// </summary>
public class SparkyModSystem : ModSystem {
    private const string CHANNEL_NAME = "sparky";

    /// <summary>
    /// The circuit network manager (server-side only).
    /// </summary>
    public CircuitNetworkManager? NetworkManager { get; private set; }

    private IServerNetworkChannel? _serverChannel;
    private IClientNetworkChannel? _clientChannel;
    private ICoreClientAPI? _capi;
    private WireToolModeDialog? _modeDialog;

    // Per-player cable laying state (keyed by PlayerUID)
    private readonly Dictionary<string, CableLayingState> _playerCableStates = new();

    // Per-player cache debug state (keyed by PlayerUID)
    private readonly Dictionary<string, CacheDebugState> _playerCacheDebugStates = new();

    /// <summary>
    /// Gets the cable laying state for a player, or null if none exists.
    /// </summary>
    public CableLayingState? GetCableState(string playerUid) {
        _playerCableStates.TryGetValue(playerUid, out var state);
        return state;
    }

    /// <summary>
    /// Gets or creates a cable laying state for a player with the specified cross-section.
    /// </summary>
    public CableLayingState GetOrCreateCableState(string playerUid, CrossSection crossSection) {
        if (!_playerCableStates.TryGetValue(playerUid, out var state)) {
            state = new CableLayingState(crossSection);
            _playerCableStates[playerUid] = state;
        }
        return state;
    }

    /// <summary>
    /// Clears the cable laying state for a player.
    /// </summary>
    public void ClearCableState(string playerUid) {
        if (_playerCableStates.TryGetValue(playerUid, out var state)) {
            state.Cancel();
            _playerCableStates.Remove(playerUid);
        }
    }

    /// <summary>
    /// Gets the cache debug state for a player, or null if none exists.
    /// </summary>
    public CacheDebugState? GetCacheDebugState(string playerUid) {
        _playerCacheDebugStates.TryGetValue(playerUid, out var state);
        return state;
    }

    /// <summary>
    /// Gets or creates a cache debug state for a player.
    /// </summary>
    public CacheDebugState GetOrCreateCacheDebugState(string playerUid) {
        if (!_playerCacheDebugStates.TryGetValue(playerUid, out var state)) {
            state = new CacheDebugState();
            _playerCacheDebugStates[playerUid] = state;
        }
        return state;
    }

    /// <summary>
    /// Clears the cache debug state for a player.
    /// </summary>
    public void ClearCacheDebugState(string playerUid) {
        if (_playerCacheDebugStates.TryGetValue(playerUid, out var state)) {
            state.Clear();
            _playerCacheDebugStates.Remove(playerUid);
        }
    }

    /// <summary>
    /// Called on both client and server during initialization.
    /// </summary>
    public override void Start(ICoreAPI api) {
        base.Start(api);

        // Register block class
        api.RegisterBlockClass("BlockCircuit", typeof(BlockCircuit));

        // Register block entity behavior class
        api.RegisterBlockEntityBehaviorClass(BEBehaviorCircuit.BehaviorName, typeof(BEBehaviorCircuit));

        // Register item classes
        api.RegisterItemClass("ItemWireTool", typeof(ItemWireTool));
        api.RegisterItemClass("ItemCacheDebugTool", typeof(ItemCacheDebugTool));

        api.Logger.Notification("[Sparky] Mod classes registered");
    }

    /// <summary>
    /// Called after all assets are loaded. Register conductor blocks here.
    /// </summary>
    public override void AssetsFinalize(ICoreAPI api) {
        base.AssetsFinalize(api);

        // Register conductor blocks as Sparky materials
        RegisterConductorBlocks(api);

        // Note: Circuit behavior is NOT injected at startup.
        // It's added dynamically when cables are placed via GetOrCreateCircuitBehavior().
    }

    /// <summary>
    /// Registers conductor blocks with the circuit simulation system.
    /// </summary>
    private void RegisterConductorBlocks(ICoreAPI api) {
        // Clear any previous registrations
        BEBehaviorCircuit.ClearConductorRegistrations();

        // Load materials from JSON configuration
        MaterialRegistry.Load(api);

        // Register each conductor from the registry
        foreach (var conductor in MaterialRegistry.Conductors) {
            var block = api.World.GetBlock(new AssetLocation(conductor.BlockCode));
            if (block != null) {
                BEBehaviorCircuit.RegisterConductor(block.BlockId, conductor.Material);
                api.Logger.Debug($"[Sparky] Registered conductor: {conductor.BlockCode} -> {conductor.Material.Name}");
            } else {
                api.Logger.Warning($"[Sparky] Conductor block not found: {conductor.BlockCode}");
            }
        }

        api.Logger.Notification($"[Sparky] Registered {MaterialRegistry.Conductors.Count} conductor blocks from JSON");
    }

    /// <summary>
    /// Called on the server during initialization.
    /// </summary>
    public override void StartServerSide(ICoreServerAPI api) {
        base.StartServerSide(api);

        // Initialize network manager
        NetworkManager = new CircuitNetworkManager();
        NetworkManager.Initialize(api);

        api.Event.SaveGameLoaded += () => CleanupStaleCircuitBlockEntities(api);
        api.Event.ChunkColumnLoaded += (_, chunks) => CleanupStaleCircuitBlockEntities(api, chunks);

        // Register network channel for future use
        _serverChannel = api.Network.RegisterChannel(CHANNEL_NAME);

        api.Logger.Notification("[Sparky] Server-side initialization complete");
    }

    private static void CleanupStaleCircuitBlockEntities(ICoreServerAPI api) {
        var chunks = api.WorldManager.AllLoadedChunks;
        if (chunks == null || chunks.Count == 0)
            return;

        CleanupStaleCircuitBlockEntities(api, chunks.Values);
    }

    private static void CleanupStaleCircuitBlockEntities(ICoreServerAPI api, IEnumerable<IWorldChunk> chunks) {
        foreach (var chunk in chunks) {
            var blockEntities = chunk?.BlockEntities;
            if (blockEntities == null || blockEntities.Count == 0)
                continue;

            List<Vintagestory.API.MathTools.BlockPos>? toRemove = null;
            foreach (var kvp in blockEntities) {
                var behavior = kvp.Value?.GetBehavior<BEBehaviorCircuit>();
                if (behavior == null)
                    continue;

                // Keep behaviors that have conductors (dynamically added or otherwise)
                if (behavior.HasConductors)
                    continue;

                // Remove empty circuit behaviors on blocks that don't natively support them
                var block = api.World.BlockAccessor.GetBlock(kvp.Key);
                if (!BEBehaviorCircuit.BlockSupportsCircuitBehavior(block)) {
                    toRemove ??= new List<Vintagestory.API.MathTools.BlockPos>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove == null)
                continue;

            foreach (var pos in toRemove) {
                api.World.BlockAccessor.RemoveBlockEntity(pos);
            }
        }
    }

    /// <summary>
    /// Called on the client during initialization.
    /// </summary>
    public override void StartClientSide(ICoreClientAPI api) {
        base.StartClientSide(api);
        _capi = api;

        // Register network channel for future use
        _clientChannel = api.Network.RegisterChannel(CHANNEL_NAME);

        // Register F key hotkey for wire tool mode selection
        api.Input.RegisterHotKey(
            "wiretoolmode",
            "Wire Tool Mode",
            GlKeys.F,
            HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("wiretoolmode", OnWireToolModeKey);

        // Hook up pathfinder logging
        CablePathfinder.Log = msg => api.Logger.Debug(msg);

        // Hook up cache debug logging
        CacheDebugState.Log = msg => api.Logger.Debug(msg);

        api.Logger.Notification("[Sparky] Client-side initialization complete");
    }

    private bool OnWireToolModeKey(KeyCombination comb) {
        if (_capi == null)
            return false;

        var player = _capi.World.Player;
        if (player == null)
            return false;

        // Only show menu when holding wire tool
        var slot = player.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack?.Item is not ItemWireTool wireTool)
            return false;

        // Toggle dialog
        if (_modeDialog?.IsOpened() == true) {
            _modeDialog.TryClose();
            return true;
        }

        _modeDialog = new WireToolModeDialog(_capi, mode => {
            wireTool.SetMode(slot, mode, player);
            _capi.ShowChatMessage($"Wire tool mode: {mode.GetDisplayName()}");
        });
        _modeDialog.TryOpen();
        return true;
    }

    /// <summary>
    /// Called when the mod is being unloaded.
    /// </summary>
    public override void Dispose() {
        NetworkManager?.Shutdown();
        NetworkManager = null;

        base.Dispose();
    }
}
