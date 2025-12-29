# Vintage Story Integration

The `src/mod/vsintegration/` folder bridges Vintage Story's game world and Sparky's electrical simulation system. It provides behavior-based voxel storage, network management, and multiplayer-safe placement via server-authoritative network messages.

## Key Files

```
src/mod/
├── SparkyModSystem.cs                    # Main ModSystem entry point
└── vsintegration/
    ├── BEBehaviorCircuit.cs              # Block entity behavior for circuit voxels
    ├── BlockCircuit.cs                   # Block class with per-voxel selection
    ├── BlockEntityCircuitHost.cs         # BE for hosting circuits in solid blocks
    ├── CircuitBlockFactory.cs            # Creates circuit behaviors at positions
    ├── CircuitNetworkManager.cs          # Server-side network/simulation manager
    ├── MaterialRegistry.cs               # Conductor material loading from block attributes
    ├── ItemWireTool.cs                   # Player tool for placing/removing voxels
    ├── VoxelPlacementSystem.cs           # Server-side placement request handler
    ├── VSConversions.cs                  # Coordinate conversion utilities
    ├── BehaviorSync/
    │   ├── BehaviorSyncPacket.cs         # Network packet for behavior sync
    │   └── BehaviorSyncSystem.cs         # Syncs dynamically-added behaviors
    ├── CableLaying/
    │   ├── CableLayingState.cs           # State machine for two-click cable workflow
    │   ├── WorldVoxelCache.cs            # VS-specific world voxel cache
    │   └── WireToolModeDialog.cs         # F key mode selection GUI
    ├── Preview/
    │   ├── PreviewState.cs               # Protobuf network messages
    │   ├── VoxelPreviewSystem.cs         # Preview sync and cable preview logic
    │   ├── VoxelPreviewRenderer.cs       # GPU mesh rendering
    │   └── VoxelPreviewMesh.cs           # Multi-voxel mesh building
    ├── PlayerState/
    │   ├── PlayerStatePacket.cs          # Per-player state sync packet
    │   ├── PlayerStateKey.cs             # State key definitions
    │   └── PlayerStateManager.cs         # Player state synchronization
    └── Debug/
        ├── CacheDebugState.cs            # Debug visualization state
        └── ItemCacheDebugTool.cs         # Developer tool for cache inspection
```

## Architecture

```
Player Interaction          VS World Layer              Sparky Simulation Layer
-----------------          --------------              -----------------------
ItemWireTool        ->     BEBehaviorCircuit    ->     VoxelGrid (SVO)
(place/remove)             (behavior storage)          TopologyBuilder (prisms)
                           BlockCircuit (behavior)     ISimulation (MNA solver)
                           CircuitNetworkManager
```

## SparkyModSystem

The main `ModSystem` entry point (`src/mod/SparkyModSystem.cs`) handles:

**Registration (Start)**
- Block class: `BlockCircuit`
- Block entity behavior: `sparky:circuit` (BEBehaviorCircuit)
- Block entity class: `CircuitHost` (BlockEntityCircuitHost)
- Item classes: `ItemWireTool`, `ItemCacheDebugTool`

**Asset Finalization (AssetsFinalize)**
- Loads conductor materials from block attributes via `MaterialRegistry.Load()`
- Registers conductors with `BEBehaviorCircuit.RegisterConductor()`

**Server Initialization (StartServerSide)**
- Creates `CircuitNetworkManager` for simulation management
- Registers cleanup handlers for stale circuit block entities

**Client Initialization (StartClientSide)**
- Registers F key hotkey for wire tool mode dialog
- Hooks up logging callbacks for pathfinder debugging

**Per-Player State Management**
- `_playerCableStates`: CableLayingState per player for cable laying workflow
- `_playerPreviewTargets`: Single voxel preview positions
- `_playerCacheDebugStates`: Debug visualization state

## BEBehaviorCircuit

Block entity behavior that provides voxel-based conductor storage. Attaches to `BlockEntityGeneric` or `BlockEntityCircuitHost`.

**Storage**
- `ConductorCuboids`: List of packed uint cuboids (matches BlockEntityMicroBlock format)
- `ConductorBlockIds`: Material palette mapping index to VS block ID
- `NetworkId`: Guid linking to a NetworkState in CircuitNetworkManager

**Cuboid Packing Format**
```
bits 0-3:   minX
bits 4-7:   minY
bits 8-11:  minZ
bits 12-15: maxX-1
bits 16-19: maxY-1
bits 20-23: maxZ-1
bits 24-31: material index
```

**Key Methods**
- `SetConductorVoxel(x, y, z, material)`: Sets a single voxel
- `SetConductorVoxelsBatch()`: Efficient batch placement for cables
- `RemoveVoxel(x, y, z)`: Removes a voxel
- `ExportToVoxelGrid(grid, sparkyBlockPos)`: Converts to Sparky VoxelGrid

**Static Conductor Registry**
- `RegisterConductor(blockId, material)`: Maps VS block ID to Sparky Material
- `IsConductor(blockId)`: Checks if block is a conductor
- `GetConductorMaterial(blockId)`: Gets Material for block ID

**Rendering**
- Mesh generated via `BlockEntityMicroBlock.CreateMesh()` static helper
- Per-voxel selection boxes built from packed cuboids

## CircuitNetworkManager

Server-side singleton managing all electrical networks. Each `NetworkState` contains:

| Field | Purpose |
|-------|---------|
| `Id` | Unique network identifier (Guid) |
| `Simulation` | VoxelSimulation instance (MNA solver) |
| `Blocks` | Set of VS BlockPos belonging to this network |
| `ChunkColumns` | For pause/resume when chunks unload |
| `IsPaused` | Simulation paused when chunks missing |

**Dirty Block Processing (OnTick)**
1. Collect all dirty blocks and their neighbors
2. Find affected networks (may merge multiple networks)
3. Export each BEBehaviorCircuit to merged VoxelGrid
4. Call `Simulation.RebuildTopology()`
5. Step all non-paused simulations

**Chunk Coherence**
- Networks pause when any chunk unloads
- Networks resume when all chunks reload
- Transient state (capacitor voltages) not yet serialized on pause

## Network Protocol

All voxel placement goes through server-authoritative network messages:

```
Client                              Server                          Other Clients
------                              ------                          -------------
1. Player interacts
   |
2. Calculate voxel position
   |
3. SendVoxelPlacement() ------>    4. VoxelPlacementSystem.OnVoxelPlacementRequest()
                                      - Validates player distance (~15 blocks)
                                      - Groups voxels by block position
                                      - CircuitBlockFactory.GetOrCreateAt()
                                      - SetConductorVoxelsBatch() or RemoveVoxel()
                                      |
                                   5. BEBehaviorCircuit notifies
                                      CircuitNetworkManager.OnBlockVoxelsChangedBatch()
                                      |
                                   6. ProcessDirtyBlocks() on game tick
                                      - RebuildTopology()
                                      - Steps simulation
                                      |
                                   7. VS auto-syncs block entity -----> Block entity update
                                      via MarkDirty(true)                (voxels visible)
```

**Network Messages** (defined in PreviewState.cs):
- `VoxelPlacementRequest`: List of VoxelPlacement + IsRemoval flag
- `VoxelPlacement`: Global X/Y/Z coordinates + material index
- `PreviewUpdateRequest`: Preview voxels from client to server
- `PreviewState`: Preview voxels broadcast to all clients

**Channel Names**:
- `sparky-voxels`: Voxel placement and preview sync
- `sparky-behavior`: Behavior sync for dynamically-added behaviors

## Behavior Sync System

Solves a VS quirk: when behaviors are dynamically added to BlockEntityGeneric on the server, clients don't receive them automatically.

**Problem**
- VS's `FromTreeAttributes` only calls behaviors already in the Behaviors list
- Dynamically-added behaviors on server aren't created on client

**Solution** (BehaviorSyncSystem)
1. Server broadcasts `BehaviorAddedPacket` when behavior is added
2. Client receives packet and adds behavior locally
3. Normal `MarkDirty` sync populates behavior data via `FromTreeAttributes`
4. Retry logic handles timing window when BE doesn't exist yet

## CircuitBlockFactory

Factory for creating BEBehaviorCircuit at positions:

1. **Existing behavior**: Return it
2. **Replaceable block** (air, grass): Place `sparky:circuitblock`
3. **Solid block without BE**: Spawn `BlockEntityCircuitHost`
4. **Block with existing BE** (without circuit behavior): Reject

## BlockEntityCircuitHost

Block entity for hosting circuit behavior on solid blocks (stairs, slabs, etc.) that don't normally have block entities.

- Created by `CircuitBlockFactory` via `SpawnBlockEntity()`
- Adds `BEBehaviorCircuit` in `CreateBehaviors()`
- Periodic check (every ~7s) detects if host block was replaced
- Cleans up on block removal

## Item Singleton Pattern

VS Item classes are singletons - all players share the same instance. Per-player state requires careful storage:

| State Type | Storage Location | Example |
|------------|------------------|---------|
| Per-item persistent | `ItemStack.Attributes` | Tool mode, selected material |
| Per-player runtime | `SparkyModSystem` dictionary | CableLayingState |

**ItemStack.Attributes Pattern**
```csharp
public WireToolMode GetMode(ItemSlot slot) {
    return (WireToolMode)slot.Itemstack.Attributes.GetInt("wireToolMode", 0);
}
```

**ModSystem Dictionary Pattern**
```csharp
private readonly Dictionary<string, CableLayingState> _playerCableStates = new();

public CableLayingState? GetCableState(string playerUid) {
    _playerCableStates.TryGetValue(playerUid, out var state);
    return state;
}
```

## Conductor Registration

Conductors are registered by scanning all blocks for `sparky` attributes during `AssetsFinalize`:

```json
{
  "attributes": {
    "sparky": {
      "conductor": true,
      "material": "Copper",
      "resistivity": 0.001,
      "previewColor": "#B87333",
      "displayName": "Copper Wire"
    }
  }
}
```

This allows other mods to add conductors by simply adding attributes to their block definitions.

## Coordinate Systems

| System | Range | Used By |
|--------|-------|---------|
| VS BlockPos | World blocks | BEBehaviorCircuit.Pos, CircuitNetworkManager |
| Sparky BlockPos | Same as VS | VoxelGrid, TopologyBuilder |
| Local voxel (x,y,z) | 0-15 per axis | SetConductorVoxel, behavior storage |
| Global VoxelPos | Unbounded | Sparky VoxelGrid, SVO |

**Conversion**: `VoxelPos.FromBlockLocal(sparkyBlockPos, localX, localY, localZ)`

## Preview System

The preview system renders ghost voxels showing where placement will occur.

**Components**
- `VoxelPreviewSystem`: ModSystem handling preview sync
- `VoxelPreviewRenderer`: IRenderer for GPU mesh rendering
- `VoxelPreviewMesh`: Multi-voxel mesh building with face culling

**Data Flow**
```
Client tick (50Hz)         Server                    All Clients
----------------           ------                    -----------
Player aims wire tool  ->  PreviewUpdateRequest  ->  Stores state
                           (client->server)          |
                                                     v
                                                    20Hz broadcast
                                                     |
                           PreviewState          <-  Updates renderer
                           (server->all clients)     |
                                                     v
                                                    OnRenderFrame
```

**Rendering Approach**
- Render stage: `EnumRenderStage.Opaque`
- Texture: From circuitblock via atlas lookup
- Mesh: Built dynamically with face culling between adjacent voxels
- Model matrix: Camera-relative positioning

**Preview Colors**
- Green tint: Complete path (cable reaches goal)
- Yellow tint: Partial path (closest reachable point)
- Red: No progress (blocked)

## Current Limitations

1. **Component support incomplete**: Active components (sources, diodes) not yet integrated with voxel system
2. **No simulation state sync**: Voltage/current data not synced to clients
3. **Pause state serialization**: Capacitor/inductor state not persisted on chunk unload
4. **Single network merging**: `ProcessDirtyBlocks()` merges into one network; proper connected component detection needed
