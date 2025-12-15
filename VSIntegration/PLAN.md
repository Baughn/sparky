# Wire Tool Feature Plan

*Created: 2025-12-15*

---

## Preview System (Implemented)

Shows ghost voxels before placement. Visible to all players via server-side state sync.

### Architecture

```
VSIntegration/Preview/
├── PreviewState.cs           # Protobuf network messages
├── VoxelPreviewSystem.cs     # ModSystem: server sync + client tick
├── VoxelPreviewRenderer.cs   # IRenderer: GPU mesh rendering
└── VoxelPreviewMesh.cs       # Mesh utilities (for future multi-voxel)
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

- **Stage**: `EnumRenderStage.OIT` (Order-Independent Transparency)
- **Shader**: VS StandardShader with 50% vertex alpha
- **Texture**: From circuitblock's "copper" texture slot
- **Mesh**: `CubeMeshUtil.GetCube()` scaled to 1/16 block, 0.999× for z-fighting

### Key Implementation Notes

- Texture lookup deferred until first render (atlas not ready during init)
- Uses `prog.Tex2D = atlasTextureId` (not `BindTexture2d`)
- Mesh origin in world coords; model matrix subtracts camera position
- Per-player state dictionary allows multiple concurrent previews

---

## Cable Laying Feature (Planned)

## Overview

Add cable-laying modes to ItemWireTool, allowing players to route cables between two points with automatic pathfinding. Mode selection via F key menu (like chisel tool).

## User Interaction

1. Player holds wire tool, presses F → mode selection menu appears (grid of 6 options)
2. In cable mode: ghost preview shows at cursor (starting point preview, updated every frame)
3. Player right-clicks starting point → start is locked, path preview appears as cursor moves
4. Player moves cursor → path preview updates (throttled to 100ms)
5. Player right-clicks end point → cable is placed
6. Cancel: switch away from wire tool

## Modes (F key menu, 6-item grid)

| Mode | Cross-Section | Description |
|------|---------------|-------------|
| Single Voxel | 1×1 | Current behavior (place/remove individual voxels) |
| Cable 1×1 | 1×1 | Pathfinding, single-voxel cable |
| Cable 1×2 | 1×2 | Light circuits |
| Cable 2×2 | 2×2 | Medium loads |
| Cable 2×3 | 2×3 | Heavy loads |
| Cable 3×5 | 3×5 | Main feeds |

## Preview System

**Pre-selection preview (every frame):**
- Single Voxel mode: Show ghost of voxel that would be placed on right-click
- Cable modes: Show ghost of starting cross-section at cursor position
- This is cheap - no pathfinding, just hit detection + cross-section placement

**Post-selection preview (100ms throttle):**
- Only in cable modes after first right-click
- Shows full path from start to cursor position
- A* pathfinding runs on background thread

## Material Selection

For now: hardcode to Copper.

Future: Hold conductor material in one hand, wire tool in other. Tool reads material from off-hand slot.

## Architecture

```
VSIntegration/CableLaying/
├── WorldVoxelCache.cs       # Convert world region to 4-state voxel grid
├── CablePathfinder.cs       # A* with cross-section awareness
├── CablePreviewRenderer.cs  # Ghost mesh rendering
├── CableValidator.cs        # Acceptance criteria assertions (for tests)
└── CableLayingMode.cs       # State machine, ItemWireTool integration
```

## Subsystem Details

### 1. WorldVoxelCache (Implemented)

Converts a 7-block radius around start point into a sparse voxel octree for pathfinding.

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

The `Unroutable` state was added to handle architectural gaps (stairs, fences). Since cables can only be placed inside Circuit blocks, air gaps in non-Circuit blocks should neither be occupied nor provide adjacency support.

**Conversion Rules:**

| Source Block Type | Conversion |
|-------------------|------------|
| Air / replaceable blocks | All voxels → Empty |
| `BlockEntityCircuit` | Conductors → PreExistingConductor, non-conductors → Insulation, unfilled → Empty |
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
    bool IsInPathfindingBounds(VoxelPos pos);  // 6-block radius
    bool IsInCacheBounds(VoxelPos pos);        // 7-block radius
    void SetCableConductor(VoxelPos pos);
    void ClearCableConductors();
    VoxelPos Origin { get; }
}
```

**Implementation Notes:**
- Cache radius: 7 blocks (outer ring for adjacency checks)
- Pathfinding radius: 6 blocks (cables can only be routed within this)
- Bounds are voxel-based: 6 blocks = 96 voxels, 7 blocks = 112 voxels
- `SetCableConductor`/`ClearCableConductors` for temporary path marking during A*

**Tests:** `Sparky.Tests/Game/CableLaying/WorldVoxelCacheTests.cs` (36 tests)

### 2. CablePathfinder (Implemented)

A* pathfinding accounting for cable cross-section with minimum turning radius.

**Location:**
- `Sparky.Core/Game/Core/CableLaying/CrossSection.cs` - Cross-section types and orientation
- `Sparky.Core/Game/Core/CableLaying/PathResult.cs` - Result types
- `Sparky.Core/Game/Core/CableLaying/CablePathfinder.cs` - Main A* implementation

**Tests:** `Sparky.Tests/Game/CableLaying/CablePathfinderTests.cs` (21 tests)

**Start Point Snapping:**
- When player selects start, search within 1-2 voxels for existing cable end of same cross-section
- If found: snap to it, inherit travel direction (constrained start)
- If not found: unconstrained start (any direction valid)

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
- Heuristic: Manhattan distance to goal

**Neighbor Generation:**
1. For each cardinal direction (6 directions):
   - **Continue straight**: always considered
   - **90° turn**: only if `StepsSinceTurn >= minTurnDistance`
   - **180° reverse**: never allowed
2. Compute cross-section orientation based on direction + "lay flat" rule
3. Check ALL voxels in cross-section at new position:
   - Must be Empty (not Insulation, PreExistingConductor, or Unroutable)
   - At least one voxel adjacent to Insulation or CableConductor (support)
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
- Run A* on background thread
- Cache updates deferred while pathfinder running
- Results may be stale if world changed; re-run if needed

### 3. CablePreviewRenderer

Renders ghost blocks showing proposed cable path.

**Requirements:**
- World-relative mesh (not anchored to block entity)
- Translucent/ghost appearance
- Two modes: pre-selection (cross-section only) and post-selection (full path)
- Visible to all players (server-side state)

**Implementation Approach:**
- Use VS `IRenderer` interface for custom rendering
- Build mesh once when path changes (list of cube faces)
- Store world-space vertices
- Each frame: multiply by view-projection matrix, draw with transparency

**Preview States:**
| State | What to render |
|-------|----------------|
| Single Voxel mode, no selection | Single ghost voxel at cursor |
| Cable mode, no selection | Ghost cross-section at cursor |
| Cable mode, start selected, Complete path | Ghost blocks along entire path (green tint) |
| Cable mode, start selected, Partial path | Ghost blocks to closest point + red endpoint indicator |
| Cable mode, start selected, NoProgress | Red indicator at start |

### 4. CableValidator (Implemented)

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

3. **Wall Proximity ("Lay Flat")** (Criterion 5): For cross-section W×H where W ≤ H:
   - The cable voxel furthest from any insulation surface is ≤ W voxels away
   - (For 2×3, no voxel is more than 2 voxels from a wall)

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

### 5. CableLayingMode

State machine integrated with ItemWireTool.

**States:**
```
Idle ──RightClick──> StartSelected ──RightClick──> (place cable) ──> Idle
  ^                        |
  └────(switch tool)───────┘
```

**Mode Menu:**
- F key opens VS-style mode selection dialog
- 2×3 grid showing all 6 modes with icons
- Click to select, ESC to cancel

## Changes to Existing Code

### BlockEntityCircuit.cs

Add batch method:
```csharp
/// <summary>
/// Sets multiple conductor voxels in a batch. More efficient than individual calls.
/// </summary>
public void SetConductorVoxelsBatch(IEnumerable<(int X, int Y, int Z, Material Material)> voxels)
{
    bool anyChanged = false;
    foreach (var (x, y, z, material) in voxels)
    {
        // Set voxel without triggering updates
        anyChanged = true;
    }

    if (anyChanged)
    {
        MarkMeshDirty();
        RegenSelectionBoxes(Api.World, null);
        MarkDirty(true);

        // Single notification to network manager
        if (Api?.Side == EnumAppSide.Server)
        {
            var modSystem = Api.ModLoader.GetModSystem<SparkyModSystem>();
            modSystem?.NetworkManager?.OnBlockVoxelsChangedBatch(Pos);
        }
    }
}
```

### CircuitNetworkManager.cs

Add batch notification:
```csharp
/// <summary>
/// Called when multiple voxels in a block change at once.
/// </summary>
public void OnBlockVoxelsChangedBatch(BlockPos vsPos)
{
    _dirtyBlocks.Add(vsPos);
}
```

### ItemWireTool.cs

- Add `WireToolMode` enum
- Add mode menu handling (F key)
- Integrate CableLayingMode state machine
- Update preview rendering based on mode

## Test Plan

Every test involving cable generation must call `CableValidator.ValidateCable()`.

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

**Integration Tests:**
- Place cable, verify simulation connectivity
- Place cable near existing circuit, verify no connection
- Place cable through multiple VS blocks

## Open Questions

1. ~~Cross-section selection UI~~ → F key menu (resolved)
2. ~~Material selection~~ → Off-hand material, hardcode copper for now (resolved)
3. ~~Cost per voxel~~ → Later (resolved)

## Performance Budget

- Cache build: <50ms for 6-block radius
- A* search: <100ms typical, 6-block limit prevents runaway
- Pre-selection preview: Every frame (cheap, no pathfinding)
- Post-selection preview: 100ms throttle for pathfinding
- Cable placement: Batched, single topology rebuild
