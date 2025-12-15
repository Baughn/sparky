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

### 1. WorldVoxelCache

Converts a 6-block radius around start point into a temporary 3D voxel grid.

**Voxel States:**
```csharp
enum CacheVoxelState
{
    Empty,                // Air - cable can occupy
    Insulation,           // Solid non-conductor - cable can be adjacent
    PreExistingConductor, // Existing circuit - cable must NOT be adjacent
    CableConductor        // Path being placed (during A*)
}
```

**Conversion Rules:**

| Source Block Type | Conversion |
|-------------------|------------|
| `BlockEntityCircuit` | Copy voxels; conductors → PreExistingConductor, others → Insulation |
| `BlockEntityMicroBlock` (non-circuit) | Copy voxels as Insulation |
| Other blocks | Use VS "mostly solid" face API → shell of Insulation if solid |

**Cache Invalidation:**
- Start point changes → full rebuild
- Block placed/broken in region → invalidate affected block
- Use flag (not lock) to skip updates while pathfinder is running

**Implementation Notes:**
- Cache radius: 7 blocks (one larger than pathfinding radius, to detect conductors adjacent to edge voxels)
- Grid size: 7 blocks × 16 voxels = 112 voxels per axis (×2 for ±7 = 224)
- Storage: 3D array `CacheVoxelState[224, 224, 224]` (centered on start, ±7 blocks)
- Pathfinding limited to inner 6-block radius; outer ring is read-only for adjacency checks
- Origin tracking for coordinate translation

### 2. CablePathfinder

A* pathfinding accounting for cable cross-section.

**Node State:**
```csharp
record PathNode(
    VoxelPos Position,                    // Anchor corner of cross-section
    VoxelDirection IncomingDir,           // Direction we arrived from
    CrossSectionOrientation Orientation   // How cross-section is oriented
);
```

**Cost Function:**
- Base cost: 1 per voxel traveled
- Penalty: +N for distance from nearest insulation (discourages free-standing runs)
- Heuristic: Manhattan distance to goal

**Neighbor Generation:**
1. For each cardinal direction (6 directions)
2. Compute new orientation based on direction + "lay flat" rule
3. Check ALL voxels in cross-section at new position are valid:
   - Not Insulation (can't occupy solid)
   - Not PreExistingConductor
   - At least one adjacent to Insulation or CableConductor
4. Mark occupied voxels as CableConductor in working copy

**Corner Handling:**
At 90° turns, generate overlap region:
- Both the outgoing segment and incoming segment voxels are placed
- Overlap area equals full cross-section (ensures no weak spots)

**"Lay Flat" Rule:**
For non-square cross-sections:
- Larger dimension aligns parallel to nearest insulation surface
- For ambiguous cases (equidistant): prefer +X, then +Y, then +Z
- For vertical runs with no nearby wall: arbitrary (doesn't matter)

**Threading:**
- Run A* on background thread
- Use flag `_pathfinderRunning` to prevent cache updates during search
- Present results when done (may be stale if target moved, re-run if needed)

**Failure Handling:**
- No path found: Show red voxels at start point
- Target >6 blocks away: Show red indicator, message "Too far"

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
| Cable mode, start selected, path found | Ghost blocks along entire path |
| Cable mode, start selected, no path | Red indicator at start |

### 4. CableValidator

Test utility asserting acceptance criteria on generated cables.

```csharp
public static class CableValidator
{
    /// <summary>
    /// Validates all acceptance criteria for a generated cable.
    /// Call this in EVERY cable-related test.
    /// </summary>
    public static void ValidateCable(
        VoxelGrid grid,
        IReadOnlyList<VoxelPos> cableVoxels,
        CrossSection crossSection,
        WorldVoxelCache sourceCache);
}
```

**Acceptance Criteria (all must pass):**

1. **Prism Dimensions**: All prisms match cross-section
   - For 2×3: prisms are 2×3×N, 2×N×3, 3×2×N, N×2×3, 3×N×2, or N×3×2

2. **Connection Areas**: Inter-prism overlaps match cross-section
   - For 2×3: all adjacent prism pairs have 2×3 or 3×2 overlap area
   - Uses existing `TopologyBuilder` connection area calculation

3. **No Conductor Adjacency**: No cable voxel is cardinally adjacent to PreExistingConductor in the source cache

4. **No Conductor Adjacency (cardinal only)**: Cable voxels must not touch pre-existing conductors in ±X, ±Y, ±Z directions

5. **Insulation/Cable Adjacency**: Every cable voxel is adjacent (cardinal) to either:
   - Insulation in source cache, OR
   - Another cable voxel

6. **Wall Proximity ("Lay Flat")**: For cross-section W×H where W ≤ H:
   - The cable voxel furthest from any insulation surface is ≤ W voxels away
   - (For 2×3, no voxel is more than 2 voxels from a wall)

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

**Unit Tests (CablePathfinder):**
- Straight line path (each axis)
- Single 90° corner
- Multiple corners (L-shape, U-shape, S-shape)
- Around obstacle
- Through narrow gap (exactly cross-section width)
- Path not found (blocked)
- Path not found (too far)

**Unit Tests (WorldVoxelCache):**
- Empty world
- Circuit block with existing conductors
- VS microblock (non-circuit)
- Mixed block types
- Cache invalidation on block change

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
