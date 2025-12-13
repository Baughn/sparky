# VoxelGrid Storage Architecture

*Last updated: 2025-12-13*

This document describes the optimized voxel storage system using a Sparse Voxel Octree (SVO) with lazy prism building.

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

File: `Sparky.Core/Game/Core/SparseVoxelOctree.cs`

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

File: `Sparky.Core/Game/Core/IncrementalPrismBuilder.cs`

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

File: `Sparky/Game/Core/VoxelGrid.cs`

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
| Topology find regions | 73 µs | 68 µs | Similar |

**Why randomized is slower**: Random insertion order causes more SVO node splits and less optimal octree structure compared to flood-fill order which has better spatial locality.

## Supporting Classes

### SpatialHash<T>

File: `Sparky.Core/Game/Core/SpatialHash.cs`

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

## Future Improvements

1. **True incremental prism updates**: Currently rebuilds entire block on any change. Could use spatial hash to find affected prisms and only update locally.

2. **Cross-block prisms**: Prisms currently clip at block boundaries. Could allow prisms to span blocks for better compression of long wires.

3. **Parallel prism building**: Multiple dirty blocks could be rebuilt in parallel since they're independent.

4. **SVO serialization**: For persistence, could serialize SVO directly instead of expanding to voxels.
