# VSIntegration Layer

*Last updated: 2025-12-16*

The VSIntegration folder bridges Vintage Story's game world and Sparky's electrical simulation system.

Whenever you make a change to this code, also change VSIntegration/CLAUDE.md.

See CABLE-LAYER.md for details on the cable-laying tool.

## Architecture Overview

```
Player Interaction          VS World Layer              Sparky Simulation Layer
─────────────────          ──────────────              ───────────────────────
ItemWireTool        →      BlockEntityCircuit   →      VoxelGrid (SVO)
(place/remove)             (microblock storage)        TopologyBuilder (prisms)
                           BlockCircuit                ISimulation (MNA solver)
                           CircuitNetworkManager
```

## Key Insight: Microblock Inheritance

`BlockEntityCircuit` extends VS's `BlockEntityMicroBlock`, inheriting:

- **Voxel storage** via `VoxelCuboids` (list of packed uint64 cuboids)
- **Material palette** via `BlockIds` array mapping indices → VS block IDs
- **Mesh generation** for rendering (handled by parent class)
- **Selection boxes** for per-voxel click targeting

This gives us chisel-block-style voxel editing for free. The electrical simulation layer is built on top of this foundation.

## Components

### BlockEntityCircuit.cs

Extends `BlockEntityMicroBlock` with electrical semantics:

- **`NetworkId`**: Guid linking this block to a `NetworkState` in CircuitNetworkManager
- **`BlockIdToMaterial`**: Static registry mapping VS block IDs → Sparky `Material` types
- **`SetConductorVoxel(x, y, z, material)`**: Places a voxel, updates mesh, notifies manager
- **`RemoveVoxel(x, y, z)`**: Removes a voxel, updates mesh, notifies manager
- **`ExportToVoxelGrid(grid, sparkyBlockPos)`**: Converts VS cuboids → Sparky VoxelGrid

The export process:
1. Iterates `VoxelCuboids` (inherited from BlockEntityMicroBlock)
2. Looks up each cuboid's material index in `BlockIds`
3. Checks if that VS block ID is a registered conductor via `BlockIdToMaterial`
4. If so, writes each voxel position to the Sparky `VoxelGrid`

### CircuitNetworkManager.cs

Server-side singleton managing all electrical networks. Each `NetworkState` contains:

| Field | Purpose |
|-------|---------|
| `VoxelGrid` | Sparky's SVO-based voxel storage |
| `TopologyBuilder` | Extracts prisms, finds conductor regions |
| `ISimulation` | MNA solver instance |
| `Blocks` | Set of VS BlockPos belonging to this network |
| `ChunkColumns` | For pause/resume when chunks unload |
| `IsPaused` | Simulation paused when chunks missing |

**Dirty block processing** (on game tick):
1. Collect all dirty blocks + their neighbors (for connectivity)
2. Find affected networks (may merge multiple networks)
3. Export each `BlockEntityCircuit` → merged `VoxelGrid`
4. Call `TopologyBuilder.BuildTopology(grid, components, simulation)`
5. Step the simulation

**Chunk coherence**: Networks pause when any of their chunks unload, resume when all chunks reload. Transient state (capacitor voltages, etc.) should be serialized on pause (TODO).

### BlockCircuit.cs

Block behavior class:
- Enables per-voxel selection via `DoParticalSelection() => true`
- Delegates selection/collision boxes to `BlockEntityCircuit`
- Routes interactions to `ItemWireTool` when player holds it

### ItemWireTool.cs

Player tool for building circuits:
- **Left-click**: Remove voxel from circuit block
- **Right-click**: Place conductor voxel or lay cable (in cable mode)
- **F key**: Open mode selection dialog
- **Modes**: SingleVoxel (default), Cable1x1, Cable1x2, Cable2x2, Cable2x3, Cable3x5

Uses `VoxelPositionHelper` (in `Sparky.Core/Game/Core/`) for pure-math hit-position calculations, including overflow handling when placement crosses block boundaries.

**Important: Singleton Pattern**
`Item` classes are singletons in VS - all players share the same `ItemWireTool` instance. Per-player/per-item state MUST NOT be stored as instance fields. Instead:
- **Per-item state** (mode, material): Stored in `ItemStack.Attributes` via `GetMode(slot)`/`SetMode(slot, mode, player)`
- **Per-player runtime state** (cable laying): Stored in `SparkyModSystem` dictionary via `GetCableState(playerUid)`

## Data Flow: Voxel Placement

```
1. Player right-clicks with ItemWireTool
   ↓
2. ItemWireTool.OnCircuitBlockInteract()
   - Calculates target voxel from hit position
   - Handles block boundary overflow
   ↓
3. BlockEntityCircuit.SetConductorVoxel(x, y, z, material)
   - Gets VS block ID for material from BlockIdToMaterial registry
   - Calls inherited SetVoxel() to update VoxelCuboids
   - Marks mesh dirty, regenerates selection boxes
   - Notifies CircuitNetworkManager.OnBlockVoxelChanged()
   ↓
4. CircuitNetworkManager adds to _dirtyBlocks
   ↓
5. On next game tick: ProcessDirtyBlocks()
   - Merges affected blocks into single VoxelGrid
   - TopologyBuilder.BuildTopology() extracts prisms, finds regions
   - Updates ISimulation with nodes and resistors
   ↓
6. Simulation.Step(dt) runs MNA solver each tick
```

## Coordinate Systems

| System | Range | Used By |
|--------|-------|---------|
| VS BlockPos | World blocks | BlockEntityCircuit.Pos, CircuitNetworkManager |
| Sparky BlockPos | Same as VS | VoxelGrid, TopologyBuilder |
| Local voxel (x,y,z) | 0-15 per axis | SetConductorVoxel, VS microblock storage |
| Global VoxelPos | Unbounded | Sparky VoxelGrid, SVO |

Conversion: `VoxelPos.FromBlockLocal(sparkyBlockPos, localX, localY, localZ)`

## Integration with Voxel/Prism System

The translation happens at `ExportToVoxelGrid()`:

**VS Side (inherited storage)**:
- `VoxelCuboids`: List<uint> of packed cuboids (min/max coords + material index)
- `BlockIds`: int[] mapping material indices → VS block IDs

**Sparky Side (after export)**:
- `VoxelGrid`: Uses `IncrementalPrismBuilder` internally
  - `SparseVoxelOctree` for O(log n) voxel access
  - Lazy prism building per dirty 16³ block
- `TopologyBuilder`: Extracts prisms, runs union-find for conductor regions

The prism system's incremental updates (see `context/voxel-storage.md`) mean that adding a single voxel doesn't require rebuilding the entire network topology - only affected blocks get reprocessed.

## Conductor Registration

During mod initialization (`SparkyModSystem.AssetsFinalize`):

```csharp
BlockEntityCircuit.RegisterConductor(blockId, Material.Copper);
```

This populates `BlockIdToMaterial`, enabling `ExportToVoxelGrid()` to distinguish conductor voxels from decorative/insulator blocks.

## Preview System

The `Preview/` folder implements ghost voxel rendering for wire tool placement feedback.

### Architecture

```
VSIntegration/Preview/
├── PreviewState.cs           # Protobuf network messages
├── VoxelPreviewSystem.cs     # ModSystem: server sync + client tick
├── VoxelPreviewRenderer.cs   # IRenderer: GPU mesh rendering
└── VoxelPreviewMesh.cs       # Multi-voxel mesh building with face culling
```

### Rendering Approach

**Key learnings from debugging:**

1. **Render Stage**: Use `EnumRenderStage.Opaque`, not `OIT` (Order-Independent Transparency)
2. **Texture Binding**: Use `rapi.BindTexture2d(textureId)`, not `prog.Tex2D = textureId`
3. **Face Culling**: Disable during render with `rapi.GlDisableCullFace()` for preview visibility
4. **MeshData Setup**: Must include all arrays:
   ```csharp
   new MeshData(vertexCount, indexCount, withUv: true, withRgba: true, withFlags: true)
   ```
5. **Flags Array**: Initialize with `1 << 8` for each vertex (standard VS flag value)
6. **UV Coordinates**: Use `CubeMeshUtil.CubeUvCoords` for proper face UVs, then call `meshData.SetTexPos(texPos)` to map to atlas

### Data Flow

```
Client tick (50Hz)         Server                    All Clients
────────────────           ──────                    ───────────
Player aims wire tool  →   PreviewUpdateRequest  →   Stores state
                           (client→server)           ↓
                                                     20Hz broadcast
                                                     ↓
                           PreviewState          ←   Updates renderer
                           (server→all clients)      ↓
                                                     OnRenderFrame
```

## Per-Player State Management

Vintage Story's `Item` classes are singletons - all players share the same instance. This requires careful state management for multiplayer:

### State Storage Patterns

| State Type | Storage Location | Example |
|------------|------------------|---------|
| Per-item persistent | `ItemStack.Attributes` | Tool mode, selected material |
| Per-player runtime | `SparkyModSystem` dictionary | CableLayingState |

### ItemStack.Attributes Pattern

```csharp
// Get mode from the specific ItemStack
public WireToolMode GetMode(ItemSlot slot)
{
    return (WireToolMode)slot.Itemstack.Attributes.GetInt("wireToolMode", 0);
}

// Set mode on the ItemStack (persists with the item)
public void SetMode(ItemSlot slot, WireToolMode mode, IPlayer player)
{
    slot.Itemstack.Attributes.SetInt("wireToolMode", (int)mode);
    slot.MarkDirty();
}
```

### ModSystem Dictionary Pattern

```csharp
// In SparkyModSystem.cs
private readonly Dictionary<string, CableLayingState> _playerCableStates = new();

public CableLayingState? GetCableState(string playerUid)
{
    _playerCableStates.TryGetValue(playerUid, out var state);
    return state;
}
```

This allows multiple players to:
- Have different wire tool settings on different wire tool items
- Lay cables simultaneously without interfering with each other
- Switch tools without losing cable laying progress (runtime state clears on mode change)

## Cable Laying System

The `CableLaying/` folder implements pathfinding-based cable placement.

### Architecture

```
Sparky.Core/Game/Core/CableLaying/   (VS-independent)
├── CacheVoxelState.cs       # Voxel state enum for pathfinding
├── IWorldVoxelCache.cs      # Interface for world access
├── CrossSection.cs          # Cable cross-section types (1x1, 2x2, etc.)
├── CablePathfinder.cs       # A* with cross-section awareness
├── PathResult.cs            # Pathfinding result types
└── CableValidator.cs        # Acceptance criteria for tests

VSIntegration/CableLaying/
├── WorldVoxelCache.cs       # VS-specific cache implementation
├── CableLayingState.cs      # State machine for two-click workflow
└── WireToolModeDialog.cs    # F key mode selection GUI
```

### State Storage

- **Mode/Material**: `ItemStack.Attributes` (per-item)
- **CableLayingState**: `SparkyModSystem._playerCableStates` (per-player, keyed by PlayerUID)
- **Preview**: Rendered client-side, synced to server via `VoxelPreviewSystem`

### Wire Tool Modes

| Mode | Cross-Section | Description |
|------|---------------|-------------|
| SingleVoxel | 1×1 | Original behavior (place/remove individual voxels) |
| Cable1x1 | 1×1 | Pathfinding, single-voxel cable |
| Cable1x2 | 1×2 | Light circuits |
| Cable2x2 | 2×2 | Medium loads |
| Cable2x3 | 2×3 | Heavy loads |
| Cable3x5 | 3×5 | Main feeds |

### Two-Click Workflow

1. Player holds wire tool, presses F → mode selection menu
2. In cable mode: ghost preview shows cross-section at cursor
3. Right-click → locks start point, path preview appears
4. Move cursor → path preview updates (background pathfinding)
5. Right-click again → cable placed via `SetConductorVoxelsBatch()`

### State Machine (CableLayingState)

```
Phase.Idle ──RightClick──> Phase.StartSelected ──RightClick──> (place) ──> Phase.Idle
     ^                            │
     └────(switch tool/cancel)────┘
```

- **Idle**: Show cross-section preview at cursor
- **StartSelected**: Background pathfinding to cursor, show path preview
- **PathReady**: Path computed, ready for placement

### Pathfinding Algorithm

`CablePathfinder` uses A* with cross-section awareness and distance-based support.

**Distance-Based Support (Corner Routing)**

Cable voxels can be up to `2 × CrossSection.Height` voxels away from insulation:

| Cross-Section | Height | Max Distance |
|--------------|--------|--------------|
| 1×1 | 1 | 2 voxels |
| 1×2, 2×2 | 2 | 4 voxels |
| 2×3 | 3 | 6 voxels |
| 3×5 | 5 | 10 voxels |

This allows routing around exterior corners of floating objects. Cost penalty discourages unnecessary distance:

```
stepCost = 1.0 + max(0, distanceToInsulation - 1) × DistancePenalty
```

Where `DistancePenalty = 3.0`. Surface routes (distance 1) have base cost 1.0; corner routes are more expensive but possible.

**Key constraints:**
- No 180° turns (reverse direction)
- Minimum `W × H` voxels between 90° turns (cross-section area)
- No adjacency to `PreExistingConductor` (short circuit prevention)
- Start position requires adjacent insulation (no starting from corners)
- Support chain: cable-to-cable adjacency counts as distance 1

## Current Limitations

1. **Component support incomplete**: `BuildTopology()` receives `Enumerable.Empty<Component>()` - active components (sources, diodes) not yet integrated
2. **No simulation state sync**: Voltage/current data not synced to clients (voxel geometry syncs fine via inherited microblock system)
3. **Pause state serialization**: TODO - capacitor/inductor state not persisted on chunk unload
4. **Single network assumption**: `ProcessDirtyBlocks()` merges into one network; proper connected component detection needed for multiple isolated circuits
