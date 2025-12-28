using System;
using System.Collections.Generic;
using System.Linq;
using Sparky.Mna.Api;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using VoxelGrid = Sparky.Game.Core.VoxelGrid;
using VoxelType = Sparky.Game.Core.VoxelType;
using Material = Sparky.Game.Core.Material;
using TopologyBuilder = Sparky.Game.Core.TopologyBuilder;
using Component = Sparky.Game.Core.Component;
using SparkyBlockPos = Sparky.Game.Core.BlockPos;

namespace Sparky.VSIntegration;

/// <summary>
/// Manages all electrical networks in the game world.
/// Handles network discovery, chunk coherence, and simulation ticking.
/// </summary>
public class CircuitNetworkManager {
    /// <summary>
    /// State for a single connected electrical network.
    /// </summary>
    public class NetworkState {
        public Guid Id { get; init; }
        public VoxelGrid Voxels { get; } = new();
        public TopologyBuilder Topology { get; } = new();
        public ISimulation Simulation { get; init; } = null!;
        public HashSet<BlockPos> Blocks { get; } = new();
        public HashSet<Vec3i> ChunkColumns { get; } = new();
        public bool IsPaused { get; set; }
        public byte[]? PausedSimState { get; set; }
    }

    private ICoreServerAPI? _sapi;
    private readonly Dictionary<Guid, NetworkState> _networks = new();
    private readonly Dictionary<BlockPos, Guid> _blockToNetwork = new();
    private readonly Dictionary<Vec3i, HashSet<Guid>> _chunkToNetworks = new();
    private readonly HashSet<Vec3i> _loadedChunks = new();
    private readonly HashSet<BlockPos> _dirtyBlocks = new();
    private long _tickListenerId;

    /// <summary>
    /// Simulation tick rate in milliseconds.
    /// </summary>
    public int TickIntervalMs { get; set; } = 50; // 20 Hz

    /// <summary>
    /// Simulation time step in seconds.
    /// </summary>
    public double TimeStep { get; set; } = 0.05; // 50ms

    /// <summary>
    /// Initializes the network manager with the server API.
    /// </summary>
    public void Initialize(ICoreServerAPI sapi) {
        _sapi = sapi;

        // Register chunk events
        sapi.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        sapi.Event.ChunkColumnUnloaded += OnChunkColumnUnloaded;

        // Register block break event to clean up orphaned CircuitHost BEs.
        // This is needed because dynamically-spawned BEs on blocks without EntityClass
        // don't get their OnBlockRemoved called by VS.
        sapi.Event.DidBreakBlock += OnDidBreakBlock;

        // Register game tick for simulation
        _tickListenerId = sapi.Event.RegisterGameTickListener(OnTick, TickIntervalMs);

        sapi.Logger.Notification("[Sparky] CircuitNetworkManager initialized");
    }

    /// <summary>
    /// Shuts down the network manager.
    /// </summary>
    public void Shutdown() {
        if (_sapi != null) {
            _sapi.Event.UnregisterGameTickListener(_tickListenerId);
            _sapi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
            _sapi.Event.ChunkColumnUnloaded -= OnChunkColumnUnloaded;
            _sapi.Event.DidBreakBlock -= OnDidBreakBlock;
        }

        _networks.Clear();
        _blockToNetwork.Clear();
        _chunkToNetworks.Clear();
        _loadedChunks.Clear();
        _dirtyBlocks.Clear();
    }

    /// <summary>
    /// Called when a block is broken by a player. Handles cleanup for dynamically-spawned
    /// CircuitHost block entities that VS doesn't know to remove (because the block type
    /// doesn't declare EntityClass).
    /// </summary>
    private void OnDidBreakBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel) {
        if (blockSel == null || _sapi == null) return;

        var pos = blockSel.Position;
        var blockEntity = _sapi.World.BlockAccessor.GetBlockEntity(pos);
        if (blockEntity == null) return;

        // Check if there's an orphaned CircuitHost at this position
        if (blockEntity is BlockEntityCircuitHost circuitHost) {
            _sapi.Logger.Debug($"[Sparky] OnDidBreakBlock: cleaning up orphaned CircuitHost at {pos}");
            circuitHost.OnBlockRemoved();
            return;
        }

        // Also check for BEBehaviorCircuit on any block entity whose block doesn't declare EntityClass
        // (meaning VS won't call OnBlockRemoved automatically)
        var behavior = blockEntity.GetBehavior<BEBehaviorCircuit>();
        if (behavior != null) {
            var block = _sapi.World.GetBlock(oldblockId);
            if (block?.EntityClass == null) {
                _sapi.Logger.Debug($"[Sparky] OnDidBreakBlock: cleaning up orphaned BEBehaviorCircuit at {pos} (block {block?.Code} has no EntityClass)");
                behavior.OnBlockRemoved();
                _sapi.World.BlockAccessor.RemoveBlockEntity(pos);
            }
        }
    }

    #region Block Registration

    /// <summary>
    /// Registers a circuit block with the manager.
    /// Called from BEBehaviorCircuit.Initialize().
    /// </summary>
    public void RegisterBlock(BlockPos vsPos, BEBehaviorCircuit behavior) {
        // Check if this block already has a network assignment
        if (behavior.NetworkId != Guid.Empty && _networks.ContainsKey(behavior.NetworkId)) {
            _blockToNetwork[vsPos] = behavior.NetworkId;
            return;
        }

        // New block - mark dirty for topology rebuild
        _dirtyBlocks.Add(vsPos);
    }

    /// <summary>
    /// Unregisters a circuit block from the manager.
    /// Called from BEBehaviorCircuit.OnBlockRemoved().
    /// </summary>
    public void UnregisterBlock(BlockPos vsPos) {
        if (_blockToNetwork.TryGetValue(vsPos, out var netId)) {
            _blockToNetwork.Remove(vsPos);

            if (_networks.TryGetValue(netId, out var network)) {
                network.Blocks.Remove(vsPos);

                // If network is now empty, remove it
                if (network.Blocks.Count == 0) {
                    RemoveNetwork(netId);
                } else {
                    // Mark remaining blocks dirty to detect network splits
                    foreach (var block in network.Blocks) {
                        _dirtyBlocks.Add(block);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Called when a block entity is unloaded (chunk unload).
    /// </summary>
    public void OnBlockUnloaded(BlockPos vsPos) {
        // The block is still registered, but the chunk is unloading
        // This will be handled by OnChunkColumnUnloaded
    }

    /// <summary>
    /// Called when a voxel in a block changes.
    /// </summary>
    public void OnBlockVoxelChanged(BlockPos vsPos, int lx, int ly, int lz, VoxelType type, Material? material) {
        _dirtyBlocks.Add(vsPos);
    }

    /// <summary>
    /// Called when multiple voxels in a block change at once (e.g., cable placement).
    /// </summary>
    public void OnBlockVoxelsChangedBatch(BlockPos vsPos) {
        _dirtyBlocks.Add(vsPos);
    }

    #endregion

    #region Chunk Events

    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks) {
        var columnKey = new Vec3i(chunkCoord.X, 0, chunkCoord.Y);
        _loadedChunks.Add(columnKey);

        // Check if any paused networks can resume
        if (_chunkToNetworks.TryGetValue(columnKey, out var networks)) {
            foreach (var netId in networks.ToList()) {
                TryResumeNetwork(netId);
            }
        }
    }

    private void OnChunkColumnUnloaded(Vec3i chunkCoord) {
        var columnKey = new Vec3i(chunkCoord.X, 0, chunkCoord.Z);
        _loadedChunks.Remove(columnKey);

        // Pause all networks that have blocks in this chunk
        if (_chunkToNetworks.TryGetValue(columnKey, out var networks)) {
            foreach (var netId in networks.ToList()) {
                PauseNetwork(netId);
            }
        }
    }

    #endregion

    #region Network Pause/Resume

    private void PauseNetwork(Guid networkId) {
        if (!_networks.TryGetValue(networkId, out var network))
            return;

        if (network.IsPaused)
            return;

        network.IsPaused = true;

        // TODO: Serialize transient simulation state (capacitor voltages, inductor currents)
        // network.PausedSimState = SerializeSimulationState(network.Simulation);

        _sapi?.Logger.Debug($"[Sparky] Network {networkId} paused - chunk unloaded");
    }

    private void TryResumeNetwork(Guid networkId) {
        if (!_networks.TryGetValue(networkId, out var network))
            return;

        if (!network.IsPaused)
            return;

        // Check if ALL chunks are now loaded
        foreach (var chunk in network.ChunkColumns) {
            if (!_loadedChunks.Contains(chunk))
                return; // Still missing chunks
        }

        // All chunks loaded - restore state and resume
        if (network.PausedSimState != null) {
            // TODO: RestoreSimulationState(network.Simulation, network.PausedSimState);
            network.PausedSimState = null;
        }

        network.IsPaused = false;
        _sapi?.Logger.Debug($"[Sparky] Network {networkId} resumed - all chunks loaded");
    }

    private void RemoveNetwork(Guid networkId) {
        if (!_networks.TryGetValue(networkId, out var network))
            return;

        // Remove from chunk mappings
        foreach (var chunk in network.ChunkColumns) {
            if (_chunkToNetworks.TryGetValue(chunk, out var networks)) {
                networks.Remove(networkId);
                if (networks.Count == 0) {
                    _chunkToNetworks.Remove(chunk);
                }
            }
        }

        _networks.Remove(networkId);
        _sapi?.Logger.Debug($"[Sparky] Network {networkId} removed");
    }

    #endregion

    #region Simulation Tick

    private void OnTick(float dt) {
        // 1. Process dirty blocks
        if (_dirtyBlocks.Count > 0) {
            ProcessDirtyBlocks();
            _dirtyBlocks.Clear();
        }

        // 2. Step active simulations
        foreach (var network in _networks.Values) {
            if (!network.IsPaused) {
                try {
                    network.Simulation.Step(TimeStep);
                } catch (Exception ex) {
                    _sapi?.Logger.Error($"[Sparky] Simulation error in network {network.Id}: {ex.Message}");
                }
            }
        }

        // 3. TODO: Sync visual state to clients (throttled)
    }

    #endregion

    #region Topology Rebuild

    private void ProcessDirtyBlocks() {
        if (_sapi == null || _dirtyBlocks.Count == 0)
            return;

        // Identify affected networks
        var affectedNetworks = new HashSet<Guid>();
        foreach (var pos in _dirtyBlocks) {
            if (_blockToNetwork.TryGetValue(pos, out var netId))
                affectedNetworks.Add(netId);

            // Check neighbors for potential merges
            foreach (var neighbor in GetNeighborBlockPositions(pos)) {
                if (_blockToNetwork.TryGetValue(neighbor, out netId))
                    affectedNetworks.Add(netId);
            }
        }

        // Collect all blocks from affected networks + new blocks
        var allBlocks = new HashSet<BlockPos>(_dirtyBlocks);
        foreach (var netId in affectedNetworks) {
            if (_networks.TryGetValue(netId, out var existingNet)) {
                foreach (var blockPos in existingNet.Blocks) {
                    allBlocks.Add(blockPos);
                }
            }
        }

        // Build merged voxel grid
        var mergedGrid = new VoxelGrid();
        foreach (var blockPos in allBlocks) {
            var be = _sapi.World.BlockAccessor.GetBlockEntity(blockPos);
            var behavior = be?.GetBehavior<BEBehaviorCircuit>();
            if (behavior == null)
                continue;

            var sparkyBlockPos = BEBehaviorCircuit.ToSparkyBlockPos(blockPos);
            behavior.ExportToVoxelGrid(mergedGrid, sparkyBlockPos);
        }

        // Skip if no voxels
        if (mergedGrid.VoxelCount == 0) {
            // Clean up old networks
            foreach (var netId in affectedNetworks) {
                RemoveNetwork(netId);
            }
            return;
        }

        // For simplicity in this initial implementation, create/update a single network
        // TODO: Proper connected component detection for multiple networks
        var network = GetOrCreateNetwork(affectedNetworks, allBlocks);

        // Update network's voxel grid
        network.Voxels.Clear();
        foreach (var (pos, voxel) in mergedGrid.GetAllVoxels()) {
            if (voxel.Material != null)
                network.Voxels.SetVoxel(pos, voxel.Material);
            else
                network.Voxels.SetVoxel(pos, voxel.Type);
        }

        // Rebuild topology
        var regions = network.Topology.BuildTopology(
            network.Voxels,
            Enumerable.Empty<Component>(), // TODO: Component support
            network.Simulation);

        // Update block-to-network mapping
        foreach (var block in allBlocks) {
            _blockToNetwork[block] = network.Id;

            var be = _sapi.World.BlockAccessor.GetBlockEntity(block);
            var behavior = be?.GetBehavior<BEBehaviorCircuit>();
            if (behavior != null) {
                behavior.NetworkId = network.Id;
            }
        }

        // Update chunk mappings
        UpdateNetworkChunkMappings(network);

        _sapi.Logger.Debug($"[Sparky] Rebuilt topology for network {network.Id}: {network.Voxels.VoxelCount} voxels, {allBlocks.Count} blocks");
    }

    private NetworkState GetOrCreateNetwork(HashSet<Guid> affectedNetworks, HashSet<BlockPos> blocks) {
        // Reuse existing network if there's exactly one
        if (affectedNetworks.Count == 1) {
            var netId = affectedNetworks.First();
            if (_networks.TryGetValue(netId, out var existing)) {
                existing.Blocks.Clear();
                foreach (var block in blocks)
                    existing.Blocks.Add(block);
                return existing;
            }
        }

        // Remove old networks
        foreach (var netId in affectedNetworks) {
            RemoveNetwork(netId);
        }

        // Create new network
        var newNetwork = new NetworkState {
            Id = Guid.NewGuid(),
            Simulation = new SimulationManager()
        };

        foreach (var block in blocks)
            newNetwork.Blocks.Add(block);

        _networks[newNetwork.Id] = newNetwork;
        return newNetwork;
    }

    private void UpdateNetworkChunkMappings(NetworkState network) {
        // Clear old mappings
        foreach (var chunk in network.ChunkColumns.ToList()) {
            if (_chunkToNetworks.TryGetValue(chunk, out var networks)) {
                networks.Remove(network.Id);
            }
        }
        network.ChunkColumns.Clear();

        // Add new mappings
        foreach (var block in network.Blocks) {
            var chunkX = block.X >> 5; // Divide by 32 (chunk size)
            var chunkZ = block.Z >> 5;
            var chunk = new Vec3i(chunkX, 0, chunkZ);

            network.ChunkColumns.Add(chunk);

            if (!_chunkToNetworks.TryGetValue(chunk, out var networks)) {
                networks = new HashSet<Guid>();
                _chunkToNetworks[chunk] = networks;
            }
            networks.Add(network.Id);
        }

        // Check if network should be paused
        foreach (var chunk in network.ChunkColumns) {
            if (!_loadedChunks.Contains(chunk)) {
                PauseNetwork(network.Id);
                break;
            }
        }
    }

    private static IEnumerable<BlockPos> GetNeighborBlockPositions(BlockPos pos) {
        yield return pos.NorthCopy();
        yield return pos.SouthCopy();
        yield return pos.EastCopy();
        yield return pos.WestCopy();
        yield return pos.UpCopy();
        yield return pos.DownCopy();
    }

    #endregion

    #region Public Queries

    /// <summary>
    /// Gets the network state for a block, if any.
    /// </summary>
    public NetworkState? GetNetworkForBlock(BlockPos pos) {
        if (_blockToNetwork.TryGetValue(pos, out var netId)) {
            _networks.TryGetValue(netId, out var network);
            return network;
        }
        return null;
    }

    /// <summary>
    /// Gets all active networks.
    /// </summary>
    public IEnumerable<NetworkState> GetAllNetworks() => _networks.Values;

    /// <summary>
    /// Gets network count.
    /// </summary>
    public int NetworkCount => _networks.Count;

    #endregion
}
