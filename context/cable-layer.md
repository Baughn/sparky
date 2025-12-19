# Wire Tool Feature Plan

*Created: 2025-12-15*
*Updated: 2025-12-18*

---

## Preview System (Implemented)

Shows ghost voxels before placement. Visible to all players via server-side state sync.

### Architecture

```
VSIntegration/Preview/
├── PreviewState.cs           # Protobuf network messages
├── VoxelPreviewSystem.cs     # ModSystem: server sync + client tick + cable preview logic
├── VoxelPreviewRenderer.cs   # IRenderer: GPU mesh rendering
└── VoxelPreviewMesh.cs       # Mesh utilities for multi-voxel preview
```

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

### Rendering

- **Stage**: `EnumRenderStage.Opaque` (not OIT - works better for ghost rendering)
- **Shader**: VS StandardShader with vertex alpha
- **Texture**: From circuitblock's texture slot, mapped via atlas
- **Mesh**: Built dynamically with face culling for multi-voxel paths

### Key Implementation Notes

- Texture lookup deferred until first render (atlas not ready during init)
- Uses `rapi.BindTexture2d(textureId)` for texture binding
- Mesh origin in world coords; model matrix subtracts camera position
- Per-player state dictionary allows multiple concurrent previews
- Cable preview colors: green (complete path), yellow (partial), red (no progress)

---

## Cable Laying Feature (Implemented)

### Overview

Cable-laying modes added to ItemWireTool, allowing players to route cables between two points with automatic pathfinding. Mode selection via F key menu (like chisel tool).

### User Interaction

1. Player holds wire tool, presses F → mode selection menu appears (grid of 6 options)
2. In cable mode: ghost preview shows snapped cross-section at cursor
3. Player right-clicks starting point → start is locked, path preview appears as cursor moves
4. Player moves cursor → path preview updates (background pathfinding)
5. Player right-clicks end point → cable is placed
6. Cancel: switch away from wire tool or change mode

### Modes (F key menu, 6-item grid)

| Mode | Cross-Section | Description |
|------|---------------|-------------|
| Single Voxel | 1×1 | Original behavior (place/remove individual voxels) |
| Cable 1×1 | 1×1 | Pathfinding, single-voxel cable |
| Cable 1×2 | 1×2 | Light circuits |
| Cable 2×2 | 2×2 | Medium loads |
| Cable 2×3 | 2×3 | Heavy loads |
| Cable 3×5 | 3×5 | Main feeds |

### Preview System

**Pre-selection preview (every frame):**
- Single Voxel mode: Show ghost of voxel that would be placed on right-click
- Cable modes: Show ghost of starting cross-section at snapped position
- Uses `SnapPositionFinder` for optimal placement against surfaces

**Post-selection preview (background thread):**
- Only in cable modes after first right-click
- Shows full path from start to cursor position
- A* pathfinding runs on background thread
- Color indicates path status (green=complete, yellow=partial, red=blocked)

### Material Selection (Implemented)

Material cycling implemented - use scroll wheel or hotkeys to change material:
- Copper (default)
- Gold
- Lead
- Iron

Material stored per-item in `ItemStack.Attributes`.

### Architecture

```
Sparky.Core/Game/Core/CableLaying/   (VS-independent)
├── CacheVoxelState.cs       # Voxel state types for pathfinding
├── IWorldVoxelCache.cs      # Interface for world access
├── CrossSection.cs          # Cross-section types and orientation
├── CablePathfinder.cs       # A* with cross-section awareness
├── PathResult.cs            # Pathfinding result types
├── CableValidator.cs        # Acceptance criteria for tests
└── SnapPositionFinder.cs    # Start position snapping logic

VSIntegration/CableLaying/
├── WorldVoxelCache.cs       # VS-specific cache implementation
├── CableLayingState.cs      # State machine for two-click workflow
└── WireToolModeDialog.cs    # F key mode selection GUI
```

## Subsystem Details

### 1. WorldVoxelCache (Implemented)

Converts a configurable radius around start point into a sparse voxel octree for pathfinding.

**Location:**
- `Sparky.Core/Game/Core/CableLaying/CacheVoxelState.cs` - Voxel state types
- `Sparky.Core/Game/Core/CableLaying/IWorldVoxelCache.cs` - Interface for pathfinder
- `VSIntegration/CableLaying/WorldVoxelCache.cs` - VS-dependent implementation

**Voxel States (5 states):**
```csharp
enum CacheVoxelStateValue : byte
{
    Empty,                // Air (Circuit block or air) - cable can occupy
    Insulation,           // Solid non-conductor - cable can be adjacent for support
    PreExistingConductor, // Existing circuit conductor - cable must NOT be adjacent
    CableConductor,       // Path being placed (during A*)
    Unroutable            // Non-air in non-Circuit block - can't occupy OR use for support
}
```

The `Unroutable` state handles architectural gaps (stairs, fences). Since cables can only be placed inside Circuit blocks, air gaps in non-Circuit blocks should neither be occupied nor provide adjacency support.

**Conversion Rules:**

| Source Block Type | Conversion |
|-------------------|------------|
| Air / replaceable blocks | All voxels → Empty |
| `BEBehaviorCircuit` | Conductors → PreExistingConductor, non-conductors → Insulation, unfilled → Empty |
| `BlockEntityMicroBlock` (non-circuit) | Filled voxels → Insulation, unfilled → Unroutable |
| Other solid blocks | All voxels → Insulation |

**Storage:**
- Uses existing `SparseVoxelOctree<CacheVoxelState>` (generalized from VoxelGrid's octree)
- Uniform region collapsing: a fully solid block = 1 octree node, not 4096 voxels
- Memory efficient for sparse worlds

**Interface:**
```csharp
interface IWorldVoxelCache
{
    CacheVoxelState GetState(VoxelPos pos);
    bool AllEmpty(VoxelPos min, VoxelPos max);
    bool AnyCardinalNeighbor(VoxelPos pos, CacheVoxelState state);
    bool IsInPathfindingBounds(VoxelPos pos);
    bool IsInCacheBounds(VoxelPos pos);
    void SetCableConductor(VoxelPos pos);
    void ClearCableConductors();
    void MarkConnectedConductorAsCable(VoxelPos start, int maxDistance);  // For snapping to existing cables
    VoxelPos Origin { get; }
    int DistanceToInsulation(VoxelPos pos, int maxDistance);
}
```

**Implementation Notes:**
- Configurable radius (default 7 blocks, can use 1 block for preview snapping)
- Pathfinding radius: 6 blocks (cables can only be routed within this)
- Bounds are voxel-based: 6 blocks = 96 voxels, 7 blocks = 112 voxels
- `SetCableConductor`/`ClearCableConductors` for temporary path marking during A*
- `MarkConnectedConductorAsCable` allows snapping to existing cables without triggering conductor adjacency rejection

**Tests:** `Sparky.Tests/Game/CableLaying/WorldVoxelCacheTests.cs` (36 tests)

### 2. CablePathfinder (Implemented)

A* pathfinding accounting for cable cross-section with minimum turning radius.

**Location:**
- `Sparky.Core/Game/Core/CableLaying/CrossSection.cs` - Cross-section types and orientation
- `Sparky.Core/Game/Core/CableLaying/PathResult.cs` - Result types
- `Sparky.Core/Game/Core/CableLaying/CablePathfinder.cs` - Main A* implementation

**Tests:** `Sparky.Tests/Game/CableLaying/CablePathfinderTests.cs` (21 tests)

**Start Point Snapping:**
- Uses `SnapPositionFinder` to find optimal start position within 3 voxels of click
- If adjacent to PreExistingConductor: marks connected conductor as CableConductor to allow continuation
- Scoring considers: insulator contact, distance from click, existing cable adjacency

**Minimum Turning Radius:**
- Minimum straight-line distance between 90° turns = `width × height` voxels
- Prevents overlapping corners and produces realistic cable paths

| Cross-section | Min turn distance |
|---------------|-------------------|
| 1×1 | 1 (no constraint) |
| 1×2 | 2 voxels |
| 2×2 | 4 voxels |
| 2×3 | 6 voxels |
| 3×5 | 15 voxels |

**Node State:**
```csharp
record PathNode(
    VoxelPos Position,         // Anchor corner of cross-section
    VoxelDirection Direction,  // Current travel direction
    int StepsSinceTurn         // For enforcing minimum turn distance
);
```

**Cost Function:**
- Base cost: 1 per voxel traveled
- Turn penalty: +0.1 for each 90° turn (prefers straight paths)
- Distance penalty: +3.0 per voxel distance from insulation (beyond adjacent)
  - `stepCost = 1.0 + max(0, distToInsulation - 1) * 3.0`
  - Allows corner routing while preferring surface routes
- Heuristic: Manhattan distance to goal

**Neighbor Generation:**
1. For each cardinal direction (6 directions):
   - **Continue straight**: always considered
   - **90° turn**: only if `StepsSinceTurn >= minTurnDistance`
   - **180° reverse**: never allowed
2. Compute cross-section orientation based on direction + "lay flat" rule
3. Check ALL voxels in cross-section at new position:
   - Must be Empty (not Insulation, PreExistingConductor, or Unroutable)
   - At least one voxel within `2 × Height` distance to Insulation or CableConductor (extended support range for corner routing)
   - No voxel adjacent to PreExistingConductor (short circuit prevention)
4. Mark occupied voxels as CableConductor in cache (for support chain)

**Corner Handling:**
At 90° turns, place overlap region:
- Union of incoming and outgoing cross-sections at turn point
- Ensures full electrical cross-section through the corner
- Minimum turn distance prevents overlapping corners

**"Lay Flat" Rule:**
For non-square cross-sections (e.g., 2×3):
- Larger dimension aligns parallel to nearest insulation surface
- Ambiguous cases (equidistant): prefer alignment with +X, then +Y, then +Z
- Vertical runs with no nearby wall: arbitrary orientation

**Result Types:**
```csharp
enum PathResultType { Complete, Partial, NoProgress }

record PathResult(
    PathResultType Type,
    IReadOnlyList<VoxelPos> Path,  // All voxels in cable
    VoxelPos EndPosition           // Where path ends (may not be goal)
);
```

- **Complete**: reached goal exactly
- **Partial**: closest reachable point (player can finish with 1×1 tool)
- **NoProgress**: completely blocked (invalid start position?)

**Search Behavior:**
- Track "best path so far" (closest to goal by Manhattan distance)
- If goal unreachable, continue until search space exhausted
- Return best path found, even if partial
- Acceptable since search space is small (~96³ max) and runs off-thread

**Threading:**
- Run A* on background thread via `Task.Run()`
- Results polled each tick via `TryUpdatePath()`
- Results may be stale if world changed; re-run if needed

### 3. SnapPositionFinder (Implemented)

Finds optimal starting position for cable placement.

**Location:** `Sparky.Core/Game/Core/CableLaying/SnapPositionFinder.cs`

**Tests:** `Sparky.Tests/Game/CableLaying/SnapPositionTests.cs`

**Algorithm:**
The clicked block face determines the cable orientation:
- **Upright direction**: Face normal (perpendicular to surface)
- **Support direction**: Opposite of upright (toward the insulating block)
- **Cable orientation**: Height (larger dimension) lies along surface, Width (smaller dimension) sticks out

Steps:
1. Derive support direction from the clicked face
2. Search within 3 voxels of clicked position
3. For each candidate position, try all 4 travel directions perpendicular to support
4. Derive orientation from travel direction + upright direction using `GetOrientationForUpright()`
5. Score each configuration based on:
   - All voxels must be Empty (negative infinity if not)
   - Exactly `max(N,M)` voxels must touch Insulator (-1000 penalty if not)
   - Manhattan distance from click to geometric center (negative)
   - +3 bonus if adjacent to exactly N×M pre-existing conductor voxels
   - +2 bonus for time-based direction preference (cycles through 4 configurations)

**Purpose:**
- Ensures cables always start from a valid supported position
- Allows snapping to existing cable ends for continuation
- Provides smooth preview even when cursor is slightly off-target
- Time-based preference lets user wait for desired orientation

### 4. CablePreviewRenderer (Implemented)

Integrated into `VoxelPreviewSystem.cs` rather than a separate file. Renders ghost blocks showing proposed cable path.

**Implementation:**
- Uses same `VoxelPreviewRenderer` as single voxel preview
- `BuildCablePreview()` in VoxelPreviewSystem handles cable-specific logic
- `BuildPositionsPreview()` for cross-section preview in Idle phase
- `BuildPathPreview()` for full path preview in StartSelected/PathReady phases

**Preview States:**
| State | What to render |
|-------|----------------|
| Single Voxel mode, no selection | Single ghost voxel at cursor |
| Cable mode, Idle phase | Ghost cross-section at snapped cursor position |
| Cable mode, StartSelected, Complete path | Ghost blocks along entire path (green tint) |
| Cable mode, StartSelected, Partial path | Ghost blocks to closest point (yellow tint) |
| Cable mode, StartSelected, NoProgress | Red indicator at start |

### 5. CableValidator (Implemented)

Test utility asserting acceptance criteria on generated cables.

**Location:** `Sparky.Core/Game/Core/CableLaying/CableValidator.cs`

```csharp
public static class CableValidator
{
    /// <summary>
    /// Validates all acceptance criteria for a cable path.
    /// Call this in EVERY cable-related test.
    /// </summary>
    public static void ValidatePath(
        IReadOnlyList<VoxelPos> path,
        CrossSection crossSection,
        IWorldVoxelCache cache);
}
```

**Implemented Criteria (checked in every pathfinder test):**

1. **No Conductor Adjacency** (Criterion 3): No cable voxel is cardinally adjacent to PreExistingConductor in the source cache

2. **Support Adjacency** (Criterion 4): Every cable voxel is adjacent (cardinal) to either:
   - Insulation in source cache, OR
   - Another cable voxel

3. **Wall Proximity (Extended Support)** (Criterion 5): Cable voxels can be up to `2 × Height` from insulation:
   - For 1×1: max 2 voxels away (allows corner routing)
   - For 2×3: max 6 voxels away
   - Surface routes preferred via distance penalty in cost function

4. **Minimum Turn Distance** (Criterion 6): Between any two 90° turns, there are at least `W × H` voxels of straight cable
   - For 2×3: at least 6 voxels between turns
   - Ensures corners don't overlap and cable has realistic bend radius

**Deferred Criteria (for integration tests with VoxelGrid):**

5. **Prism Dimensions** (Criterion 1): All prisms match cross-section
   - For 2×3: prisms are 2×3×N, 2×N×3, 3×2×N, N×2×3, 3×N×2, or N×3×2
   - Requires TopologyBuilder integration

6. **Connection Areas** (Criterion 2): Inter-prism overlaps match cross-section
   - For 2×3: all adjacent prism pairs have 2×3 or 3×2 overlap area
   - Uses existing `TopologyBuilder.CalculateContactArea()`

### 6. CableLayingState (Implemented)

State machine for cable laying workflow. Per-player instance stored in `SparkyModSystem`.

**Location:** `VSIntegration/CableLaying/CableLayingState.cs`

**States:**
```
Phase.Idle ──RightClick──> Phase.StartSelected ──RightClick──> (place cable) ──> Phase.Idle
     ^                            │                                   │
     │                            ↓                                   │
     │                     Phase.PathReady ──────────────────────────→│
     │                            │                                   │
     └────(switch tool/Cancel())──┴───────────────────────────────────┘
```

**Key Methods:**
- `SelectStart(voxelPos, blockAccessor)`: Builds cache, initializes pathfinder
- `GetSnappedStartPositions(pos, blockAccessor)`: For Idle phase preview (uses small 1-block cache)
- `UpdateGoal(pos)`: Triggers background pathfinding if position changed
- `TryUpdatePath()`: Polls for completed pathfinding, returns true if new result
- `Cancel()`: Resets to Idle phase

**Threading:**
- Background pathfinding via `Task.Run()`
- Lock prevents concurrent pathfind requests
- Results polled each tick, not awaited

### 7. WireToolModeDialog (Implemented)

Mode selection dialog opened with F key.

**Location:** `VSIntegration/CableLaying/WireToolModeDialog.cs`

**Features:**
- 2×3 grid of buttons for 6 modes
- Standard VS dialog styling
- ESC or title bar close to cancel
- Click mode button to select and close

## Changes to Existing Code (All Implemented)

### BlockCircuit.cs ✓

Block-level interaction handler respects wire tool mode:
- In cable mode, returns `false` to defer to `ItemWireTool.OnHeldInteractStart`
- In single-voxel mode, calls `OnCircuitBlockInteract` directly
- Bug fix (2025-12-16): Previously always called `OnCircuitBlockInteract`, bypassing cable mode's two-click workflow

### BEBehaviorCircuit.cs ✓

Added batch method for efficient multi-voxel placement:
```csharp
public void SetConductorVoxelsBatch(IEnumerable<(int X, int Y, int Z, Material Material)> voxels)
```

### CircuitNetworkManager.cs ✓

Added batch notification:
```csharp
public void OnBlockVoxelsChangedBatch(BlockPos vsPos)
```

### ItemWireTool.cs ✓

- Added `WireToolMode` enum with 6 modes
- Added `WireToolModeExtensions` for cross-section mapping
- Per-item state stored in `ItemStack.Attributes` (mode, material)
- Per-player cable state retrieved from `SparkyModSystem`
- `HandleCableModeInteract()` for two-click cable placement
- `PlaceCablePath()` groups voxels by block, uses batch placement
- `GetOrCreateCircuitBlock()` creates circuit blocks as needed

### SparkyModSystem.cs ✓

Added per-player cable state management:
```csharp
private readonly Dictionary<string, CableLayingState> _playerCableStates;
public CableLayingState GetOrCreateCableState(string playerUid, CrossSection crossSection);
public void ClearCableState(string playerUid);
```

## Test Plan

Every test involving cable generation must call `CableValidator.ValidatePath()`.

**Unit Tests (CablePathfinder):** ✓ Implemented
- Straight line path (each axis)
- Single 90° corner with minimum turn distance respected
- Turn rejected when under minimum distance
- Around obstacle
- Through narrow gap (exactly cross-section width)
- Unconstrained start (no nearby cable)
- Constrained start (initial direction)
- Partial path returned when goal unreachable
- NoProgress when completely blocked (no support)
- 180° turn never generated
- Different cross-section sizes (1×1, 2×2, 2×3, 3×5)
- Conductor avoidance validation

**Unit Tests (WorldVoxelCache):** ✓ Implemented
- CacheVoxelState equality and hashing
- GetState for empty/set positions
- AllEmpty region checks
- AnyCardinalNeighbor detection (6 directions)
- Bounds checking (pathfinding vs cache)
- SetCableConductor/ClearCableConductors tracking
- Octree uniform region collapsing

**Unit Tests (SnapPositionFinder):** ✓ Implemented
- Snapping to surfaces
- Snapping to existing cable ends
- Score-based selection

**Integration Tests:** (TODO)
- Place cable, verify simulation connectivity
- Place cable near existing circuit, verify no connection
- Place cable through multiple VS blocks

## Open Questions (All Resolved)

1. ~~Cross-section selection UI~~ → F key menu (resolved)
2. ~~Material selection~~ → Per-item, cyclable via ItemStack.Attributes (resolved)
3. ~~Cost per voxel~~ → Later (resolved)

## Performance Budget

- Cache build: <50ms for 7-block radius, <5ms for 1-block preview cache
- A* search: <100ms typical, 6-block limit prevents runaway
- Pre-selection preview: Every frame (cheap, uses small 1-block cache with result caching)
- Post-selection preview: Background thread, polled each tick
- Cable placement: Batched by block, single topology rebuild per block
