# VoxelGrid Storage Architecture

*Last updated: 2025-12-13 (incremental extension path)*

This document describes the optimized voxel storage system using a Sparse Voxel Octree (SVO) with lazy prism building.

## Key Definitions

**ConductorRegion**: A connected group of conductor prisms that share the same MNA node (same electrical potential). Non-resistive conductors merge into single regions when adjacent. Resistive conductors each get their own region and connect to neighbors via resistors.

**Prism**: A rectangular block of voxels with the same type and material, produced by greedy meshing. Bounded within a single 16³ block.

**Block**: A 16×16×16 chunk of voxel space, indexed by `BlockPos`. Prisms don't span block boundaries.

## Problem Statement

The original VoxelGrid implementation used a Dictionary<BlockPos, BlockVoxelData> where each block stored prisms. On every `SetVoxel` call:

1. Expand all prisms to a 4096-element voxel array
2. Modify one element
3. Rebuild all prisms via greedy meshing

This caused **O(4096)** work per voxel modification and allocated ~70KB per call.

**Benchmark (3×3×192 wire = 1720 voxels)**:
- Build time: 18,106 µs
- Memory: 117 MB allocations

## Solution: Two-Layer Architecture

### Layer 1: Sparse Voxel Octree (SVO)

File: `src/voxel/SparseVoxelOctree.cs`

The SVO provides O(log n) get/set operations with memory-efficient sparse storage.

```
World coordinates (global VoxelPos)
         ↓
    SVO Root (unbounded, grows dynamically)
         ↓
    Internal nodes (branch into 8 children)
         ↓
    Leaf nodes (single voxel or uniform region)
```

**Key features**:

1. **Dynamic root expansion**: Root grows to contain any coordinate (including negative)
2. **Uniform node collapse**: If all 8 children are identical leaves, collapse to single leaf
3. **Lazy allocation**: Empty regions = null pointers, no memory allocated
4. **Arbitrary coordinates**: Handles negative coords via signed integer division

**Octant indexing**:
```csharp
// Child index from position relative to node center
int childX = (x - originX) >= halfSize ? 1 : 0;
int childY = (y - originY) >= halfSize ? 1 : 0;
int childZ = (z - originZ) >= halfSize ? 1 : 0;
int childIndex = childX | (childY << 1) | (childZ << 2);
// Results in indices 0-7 for the 8 octants
```

**Root expansion** (when point is outside current bounds):
```csharp
// Double root size, place old root in appropriate octant
int octantX = targetX < _rootOriginX ? 1 : 0;  // Expand toward target
int newOriginX = _rootOriginX - octantX * halfSize;
// Old root goes to octant index = octantX | (octantY << 1) | (octantZ << 2)
```

### Layer 2: Incremental Prism Builder

File: `src/voxel/IncrementalPrismBuilder.cs`

Combines SVO storage with lazy per-block prism building.

```csharp
public class IncrementalPrismBuilder
{
    private readonly SparseVoxelOctree _svo = new();
    private readonly Dictionary<BlockPos, List<Prism>> _prismCache = new();
    private readonly HashSet<BlockPos> _dirtyBlocks = new();

    public void SetVoxel(VoxelPos pos, VoxelType type, Material? material)
    {
        _svo.Set(pos, type, material);
        _dirtyBlocks.Add(pos.Block);  // Mark block as needing prism rebuild
    }

    public IEnumerable<(BlockPos, Prism)> GetAllPrisms()
    {
        RebuildDirtyBlocks();  // Only rebuild what changed
        // Return cached prisms
    }
}
```

**Per-block rebuild**:
1. Extract 16³ voxels from SVO for the block
2. Run greedy meshing algorithm
3. Store resulting prisms in cache

**Greedy meshing algorithm**:
1. For each voxel type (Conductor, ResistiveConductor, Insulator):
2. Find unclaimed seed voxel
3. Grow in +X until hitting different type/material or boundary
4. Grow in +Y (maintaining X extent)
5. Grow in +Z (maintaining X and Y extent)
6. Mark all voxels in prism as claimed
7. Repeat until all voxels claimed

### Layer 3: VoxelGrid (Public API)

File: `src/voxel/VoxelGrid.cs`

Thin wrapper that delegates to IncrementalPrismBuilder. Maintains backward-compatible API.

```csharp
public class VoxelGrid
{
    private readonly IncrementalPrismBuilder _builder = new();

    public void SetVoxel(VoxelPos pos, VoxelType type) => _builder.SetVoxel(...);
    public VoxelType GetVoxelType(VoxelPos pos) => _builder.GetVoxelType(pos);
    public IEnumerable<(BlockPos, Prism)> GetAllPrisms() => _builder.GetAllPrisms();
}
```

## Performance Results

**Benchmark (3×3×192 wire = 1720 voxels)**:

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Build time (flood-fill) | 18,106 µs | 153 µs | 118× faster |
| Build time (randomized) | 18,686 µs | 236 µs | 79× faster |
| Memory allocation | 117 MB | 526 KB | 223× less |
| Topology full rebuild | 85 ms | 85 ms | - |
| Topology incremental | 85 ms | 7.7 ms | 11× faster |

**Large wire extension (3×3×20000 U-shaped wire = 180K voxels)**:

| Operation | Time |
|-----------|------|
| Initial build | ~600 ms |
| Add single voxel (full rebuild) | ~85 ms |
| Add single voxel (incremental) | <10 ms |

**Why randomized is slower**: Random insertion order causes more SVO node splits and less optimal octree structure compared to flood-fill order which has better spatial locality.

## Incremental Topology Updates

File: `src/mna/topology/TopologyBuilder.cs`

The TopologyBuilder maintains persistent state between `BuildTopology()` calls to enable incremental updates when only a few voxels change.

### Persistent State

```csharp
private Dictionary<VoxelPos, ConductorRegion>? _cachedRegions;
private readonly Dictionary<BlockPos, HashSet<ConductorRegion>> _blockToRegions;
private readonly SpatialHash<(ConductorRegion, BlockPos, Prism)> _prismIndex;
private readonly Dictionary<(ConductorRegion, ConductorRegion), ResistorId> _regionPairResistors;
private long _lastBuiltVersion;
```

### Version Tracking

The `VoxelGrid.Version` property increments on every `SetVoxel` call. TopologyBuilder uses this to detect staleness:
- If `Version == _lastBuiltVersion`: Skip rebuild entirely
- Otherwise: Check for incremental or full rebuild

### Merge Detection Algorithm

Multi-block incremental updates use merge detection to ensure correctness:

```
1. Save old prisms BEFORE triggering rebuild (critical for voxel comparison)
2. For each dirty block:
   a. Get new prisms (triggers rebuild)
   b. For NON-resistive prisms only: check boundary faces for adjacent non-resistive regions
3. If non-resistive prisms touch >1 distinct non-resistive region: FALL BACK to full rebuild
4. Otherwise: proceed with incremental update
```

**Important**: Resistive conductors never merge (each is its own region with resistors to neighbors),
so they're excluded from merge detection. Only non-resistive conductors can merge into a single region.

| Scenario | Regions Touched | Action |
|----------|-----------------|--------|
| Add non-resistive voxel to existing non-resistive region | 1 | Incremental |
| Add resistive voxel (any neighbors) | N/A | Incremental (resistive never merges) |
| Add isolated voxel | 0 | Incremental |
| Add non-resistive voxel bridging 2+ non-resistive regions | 2+ | Full rebuild |
| Remove voxel (potential split) | 0-1 | Incremental or full rebuild |

### Extension Fast Path

When adding voxels to a large existing region that extends beyond the dirty area:

```
1. If region extends beyond expanded dirty blocks:
   a. Compare old voxels (from saved prisms) vs new voxels
   b. If only ADDING (no voxels removed) and single affected region:
      → Use ExtendExistingRegion (O(dirty blocks) instead of O(all blocks))
   c. Otherwise: full rebuild
```

This enables O(1) topology updates for adding voxels to very large wires (180K+ voxels).

### Incremental Update Flow

```
1. Remove components from simulation (before removing nodes)
2. Expand dirty blocks to include neighbors (for cross-block connectivity)
3. Find all regions with prisms in expanded area
4. Check if regions extend beyond expanded area → use extension path if safe
5. Remove resistors and nodes for affected regions
6. Rebuild prisms for expanded blocks
7. Run union-find to create new regions
8. Add new regions to indexes and simulation
9. Create resistors between adjacent regions
10. Recreate components with new node mappings
```

## Supporting Classes

### SpatialHash<T>

File: `src/voxel/SpatialHash.cs`

Generic spatial hash grid for O(1) proximity queries. Currently unused but available for future incremental prism updates.

```csharp
public class SpatialHash<T>
{
    public void Add(T item, VoxelPos min, VoxelPos max);  // AABB bounds
    public IEnumerable<T> Query(VoxelPos pos);            // Items at point
    public IEnumerable<T> QueryDistinct(VoxelPos min, VoxelPos max);  // Items in region
}
```

### OctreeLeaf

Record struct returned by `SparseVoxelOctree.GetLeafNodes()` for iterating uniform regions:

```csharp
public readonly record struct OctreeLeaf(
    VoxelPos Origin,    // Corner with smallest coordinates
    int Size,           // Cubic region size (power of 2)
    VoxelType Type,
    Material? Material
);
```

## Test Coverage

- `SparseVoxelOctreeTests.cs` - 17 tests covering:
  - Basic get/set operations
  - Negative coordinates
  - Uniform node collapse
  - Large scale operations

- `SpatialHashTests.cs` - 19 tests covering:
  - Point and region queries
  - Cross-cell items
  - Negative coordinates

- `IncrementalPrismBuilderTests.cs` - 19 tests covering:
  - Prism coalescing
  - Cache invalidation
  - Cross-block behavior

- `TopologyBuilderTests.cs` - 37 tests covering:
  - Basic topology construction
  - Resistive wire modeling
  - Incremental region splits and merges
  - Cross-block modifications
  - Consistency between incremental and full rebuilds
  - Large wire construction with various build orders

## Future Improvements

1. **True incremental prism updates**: Currently rebuilds entire block on any change. Could use spatial hash to find affected prisms and only update locally.

2. **Cross-block prisms**: Prisms currently clip at block boundaries. Could allow prisms to span blocks for better compression of long wires.

3. **Parallel prism building**: Multiple dirty blocks could be rebuilt in parallel since they're independent.

4. **SVO serialization**: For persistence, could serialize SVO directly instead of expanding to voxels.

5. **Incremental merge handling**: Currently falls back to full rebuild when merging regions. Could implement proper merge logic to stay incremental.
