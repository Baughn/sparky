# VSIntegration Layer

*Last updated: 2025-12-15*

The VSIntegration folder bridges Vintage Story's game world and Sparky's electrical simulation system.

Whenever you make a change to this code, also change VSIntegration/CLAUDE.md.

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
- **Right-click**: Place conductor voxel (with material selection)
- **Material cycling**: Copper, Gold, Lead, Iron

Uses `VoxelPositionHelper` (in `Sparky.Core/Game/Core/`) for pure-math hit-position calculations, including overflow handling when placement crosses block boundaries.

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

## Current Limitations

1. **Component support incomplete**: `BuildTopology()` receives `Enumerable.Empty<Component>()` - active components (sources, diodes) not yet integrated
2. **No simulation state sync**: Voltage/current data not synced to clients (voxel geometry syncs fine via inherited microblock system)
3. **Pause state serialization**: TODO - capacitor/inductor state not persisted on chunk unload
4. **Single network assumption**: `ProcessDirtyBlocks()` merges into one network; proper connected component detection needed for multiple isolated circuits
