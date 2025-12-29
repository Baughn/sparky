# Cable Laying System

The cable laying system provides A* pathfinding-based cable placement with cross-section awareness and a two-click workflow. The pathfinder is VS-independent and lives in `src/voxel/`, while VS integration code lives in `src/mod/vsintegration/CableLaying/`.

## Key Files

```
src/voxel/MnaTopology/CableLaying/      # VS-independent core
├── CacheVoxelState.cs                  # Voxel state enum for pathfinding
├── IWorldVoxelCache.cs                 # Interface for world access
├── CrossSection.cs                     # Cable cross-section types
├── CablePathfinder.cs                  # A* with cross-section awareness
├── PathResult.cs                       # Pathfinding result types
├── CableValidator.cs                   # Test utility for acceptance criteria
└── SnapPositionFinder.cs               # Start position snapping logic

src/mod/vsintegration/CableLaying/      # VS-specific integration
├── WorldVoxelCache.cs                  # VS-specific cache implementation
├── CableLayingState.cs                 # State machine for two-click workflow
└── WireToolModeDialog.cs               # F key mode selection GUI
```

## Architecture

The cable laying system separates VS-independent pathfinding logic from VS-specific world access:

```
ItemWireTool                VoxelPreviewSystem              CablePathfinder
(mode, clicks)              (preview updates)               (A* search)
     |                            |                              |
     v                            v                              v
CableLayingState  <-------> WorldVoxelCache  <----------> IWorldVoxelCache
(state machine)             (VS impl)                     (interface)
     |                            |
     v                            v
VoxelPlacementRequest       SparseVoxelOctree
(network message)           (voxel storage)
```

## Wire Tool Modes

The wire tool supports six modes, selectable via F key menu:

| Mode | Cross-Section | Description |
|------|---------------|-------------|
| SingleVoxel | 1x1 | Default: place/remove individual voxels |
| Cable1x1 | 1x1 | Pathfinding with single-voxel cable |
| Cable1x2 | 1x2 | Light circuits |
| Cable2x2 | 2x2 | Medium loads |
| Cable2x3 | 2x3 | Heavy loads |
| Cable3x5 | 3x5 | Main feeds |

## Two-Click Workflow

```
Phase.Idle ---RightClick---> Phase.StartSelected ---RightClick---> (place cable) ---> Phase.Idle
     ^                              |                                     |
     |                              v                                     |
     |                       Phase.PathReady  ---------------------------->
     |                              |                                     |
     <-----(switch tool/Cancel())---+-------------------------------------+
```

**Idle Phase**
- Ghost preview shows cross-section at snapped cursor position
- Uses 1-block WorldVoxelCache for efficient snap position calculation
- Time-based cycling through snap configurations

**StartSelected Phase**
- First click locks start position
- Full WorldVoxelCache (7-block radius) built around start
- Background pathfinding to cursor via `Task.Run()`
- Preview updates as cursor moves (polls for completed pathfinding)

**PathReady Phase**
- Path computed and ready
- Second click places cable via `VoxelPlacementRequest`
- Preview shows path with color indicating status

## Cross-Section Orientation

Non-square cross-sections (e.g., 2x3) need orientation tracking:

**Flat**: Width along first perpendicular axis, Height along second
**Upright**: Height along first perpendicular axis, Width along second

For example, a 2x3 cable traveling in +X:
- Flat: 2 voxels in Y, 3 voxels in Z
- Upright: 3 voxels in Y, 2 voxels in Z

The `GetOrientationForUpright()` method determines orientation based on the clicked surface, placing cables flat against walls.

## WorldVoxelCache

Converts VS blocks in a configurable radius to a sparse octree of `CacheVoxelState`:

**Voxel States**
```csharp
enum CacheVoxelState {
    Empty,                // Air - cable can occupy
    Insulation,           // Solid non-conductor - cable can be adjacent for support
    PreExistingConductor, // Existing conductor - cable must NOT be adjacent
    CableConductor,       // Path being placed (during A*)
    Unroutable            // Non-air in non-Circuit block - can't occupy or use for support
}
```

**Block Processing Rules**

| Block Type | Conversion |
|------------|------------|
| Air / replaceable | All voxels Empty |
| BEBehaviorCircuit | Conductors -> PreExistingConductor, non-conductors -> Insulation, unfilled -> Empty |
| BlockEntityMicroBlock (non-circuit) | Filled -> Insulation, unfilled -> Unroutable |
| Solid blocks | Coverage-based: >=90% Insulation, <=10% Empty, else Unroutable |

**Cache Bounds**
- Cache radius: 7 blocks (112 voxels)
- Pathfinding radius: 6 blocks (96 voxels)
- The outer ring is read-only for adjacency checks

**Temporary Path Marking**
- `SetCableConductor()`: Marks voxel as part of current path
- `ClearCableConductors()`: Clears before new pathfinding
- `MarkConnectedConductorAsCable()`: For extending existing cables

## CablePathfinder

A* pathfinding with cross-section awareness and minimum turning radius.

**Node State**
```csharp
record PathNode(
    VoxelPos Position,        // Anchor corner of cross-section
    VoxelDirection Direction, // Current travel direction
    int StepsSinceTurn        // For enforcing minimum turn distance
);
```

**Cost Function**
- Base cost: 1.0 per voxel traveled
- Turn penalty: +0.1 for each 90-degree turn
- Distance penalty: `+3.0 * max(0, distanceToInsulation - 1)`
- Heuristic: Manhattan distance to goal

**Constraints**
1. No 180-degree turns (reverse direction)
2. Minimum straight-line distance between 90-degree turns = `max(Width, Height) + 1`
3. All cross-section voxels must be Empty
4. Support requirement: at least one voxel within `2 * Height` distance of Insulation or CableConductor
5. No adjacency to PreExistingConductor (short circuit prevention)

**Minimum Turn Distance by Cross-Section**

| Cross-Section | Min Turn Distance |
|---------------|-------------------|
| 1x1 | 2 voxels |
| 1x2 | 3 voxels |
| 2x2 | 3 voxels |
| 2x3 | 4 voxels |
| 3x5 | 6 voxels |

**Extended Support Range**

Cable voxels can be up to `2 * Height` voxels away from insulation, enabling corner routing:

| Cross-Section | Max Distance from Insulation |
|---------------|------------------------------|
| 1x1 | 2 voxels |
| 1x2, 2x2 | 4 voxels |
| 2x3 | 6 voxels |
| 3x5 | 10 voxels |

Surface routes (distance 1) have base cost; corner routes incur distance penalty.

**Corner Handling**

At 90-degree turns, the pathfinder places the union of incoming and outgoing cross-sections:
- Ensures full electrical cross-section through corners
- Minimum turn distance prevents overlapping corners

## SnapPositionFinder

Finds optimal starting position for cable placement within 3 voxels of click:

**Algorithm**
1. Derive support direction from clicked face
2. Search 3-voxel radius around click
3. For each candidate, try all 4 travel directions perpendicular to support
4. Score configurations based on:
   - All voxels must be Empty (disqualify if not)
   - Must have exactly `max(W,H)` voxels touching Insulator
   - Manhattan distance from click (closer is better)
   - +3 bonus for adjacency to existing conductor (cable extension)
   - +2 bonus for time-based direction preference

**Purpose**
- Ensures cables start from valid supported positions
- Allows snapping to existing cable ends for continuation
- Time-based preference lets user wait for desired orientation

## CableLayingState

State machine for per-player cable laying workflow:

**State**
- `CurrentPhase`: Idle, StartSelected, or PathReady
- `CrossSection`: The cable size being laid
- `StartPositions`: Voxels where cable starts
- `CurrentPath`: Latest pathfinding result

**Key Methods**
- `GetSnappedStartPositions()`: Preview-only snap calculation (uses 1-block cache)
- `SelectStart()`: Commits start position, builds full cache
- `UpdateGoal()`: Triggers background pathfinding if goal changed
- `TryUpdatePath()`: Polls for completed pathfinding result
- `Cancel()`: Resets to Idle phase

**Background Pathfinding**
- A* runs on background thread via `Task.Run()`
- Lock prevents concurrent pathfind requests
- Results polled each tick, not awaited
- Stale results acceptable (world may have changed)

## Preview Integration

The preview system shows cable placement before committing:

**Idle Phase Preview**
- `BuildPositionsPreview()`: Shows snapped cross-section at cursor
- Uses small 1-block cache (27 blocks vs 2744 for full cache)
- Cache reused when center block unchanged

**Path Preview**
- `BuildPathPreview()`: Shows full path with color coding
- Green: Complete path (reaches goal)
- Yellow: Partial path (closest reachable point)
- Red: No progress (completely blocked)

## CableValidator

Test utility that validates pathfinding results against acceptance criteria:

**Validated Criteria**
1. **No Conductor Adjacency**: No cable voxel adjacent to PreExistingConductor
2. **Support Adjacency**: Every voxel adjacent to Insulation or CableConductor
3. **Wall Proximity**: All voxels within `2 * Height` of insulation
4. **Minimum Turn Distance**: Required straight-line between 90-degree turns

**Deferred Criteria** (require TopologyBuilder integration)
5. Prism dimensions match cross-section
6. Inter-prism overlaps match cross-section

## Material Selection

Material cycling via ItemStack.Attributes:

```csharp
private static readonly Material[] Materials = {
    Material.Copper,
    Material.Gold,
    Material.Lead,
    Material.Iron
};
```

- `GetSelectedMaterial(slot)`: Gets current material
- `CycleNextMaterial(slot)`: Cycles forward
- `CyclePreviousMaterial(slot)`: Cycles backward

## Performance Budget

| Operation | Target |
|-----------|--------|
| Cache build (7-block) | <50ms |
| Cache build (1-block preview) | <5ms |
| A* search | <100ms typical |
| Pre-selection preview | Every frame (uses cached small cache) |
| Post-selection preview | Background thread, polled each tick |
| Cable placement | Batched by block |

## WireToolModeDialog

Mode selection dialog opened with F key:

- 2x3 grid of buttons for 6 modes
- Standard VS dialog styling
- ESC or click outside to cancel
- Click mode button to select and close

```csharp
var modes = new[] {
    (WireToolMode.SingleVoxel, "Single Voxel"),
    (WireToolMode.Cable1x1, "Cable 1x1"),
    (WireToolMode.Cable1x2, "Cable 1x2"),
    (WireToolMode.Cable2x2, "Cable 2x2"),
    (WireToolMode.Cable2x3, "Cable 2x3"),
    (WireToolMode.Cable3x5, "Cable 3x5")
};
```
