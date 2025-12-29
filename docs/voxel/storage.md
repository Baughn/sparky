# Voxel Storage

Sparky uses a two-layer sparse storage architecture for voxel data: a Sparse Voxel Octree (SVO) for O(log n) random access, and an incremental prism builder for efficient geometry extraction. This design avoids the O(4096) expand-modify-rebuild cycle that would occur with naive block-based storage.

## Key Files

```
src/voxel/
├── VoxelGrid.cs             # Public API facade
├── SparseVoxelOctree.cs     # Generic octree with dynamic root expansion
├── IncrementalPrismBuilder.cs # SVO + lazy per-block prism caching
├── VoxelType.cs             # Air, Conductor, ResistiveConductor, Insulator
├── VoxelPos.cs              # World-space voxel coordinates
├── BlockPos.cs              # Block-space coordinates (16x16x16 chunks)
├── Prism.cs                 # Axis-aligned voxel region
├── Material.cs              # Conductor material properties (resistivity)
```

## Architecture

### Layer 1: Sparse Voxel Octree

`SparseVoxelOctree<T>` provides O(log n) get/set operations with memory-efficient sparse storage.

```
World coordinates (global VoxelPos)
         |
    SVO Root (unbounded, grows dynamically)
         |
    Internal nodes (branch into 8 children)
         |
    Leaf nodes (single voxel or uniform region)
```

Key features:

- **Dynamic root expansion**: Root doubles in size to contain any coordinate, including negative values
- **Uniform node collapse**: When all 8 children are identical leaves, they collapse to a single leaf
- **Lazy allocation**: Empty regions are null pointers (no memory allocated)
- **Arbitrary coordinates**: Handles negative coordinates via signed integer division

The octree stores `VoxelData` records containing type and material:

```csharp
public readonly record struct VoxelData(VoxelType Type, Material? Material);
```

**Octant indexing**: Child index is computed from position relative to node center:

```csharp
int childX = (x - originX) >= halfSize ? 1 : 0;
int childY = (y - originY) >= halfSize ? 1 : 0;
int childZ = (z - originZ) >= halfSize ? 1 : 0;
int childIndex = childX | (childY << 1) | (childZ << 2);
// Results in indices 0-7 for the 8 octants
```

### Layer 2: Incremental Prism Builder

`IncrementalPrismBuilder` combines SVO storage with lazy per-block prism building:

```csharp
public class IncrementalPrismBuilder {
    private readonly SparseVoxelOctree<VoxelData> _svo;
    private readonly Dictionary<BlockPos, List<Prism>> _prismCache;
    private readonly HashSet<BlockPos> _dirtyBlocks;
    private long _version;
}
```

When a voxel is modified:
1. The SVO is updated immediately (O(log n))
2. The containing block is marked dirty
3. Prisms are rebuilt lazily when accessed

**Per-block rebuild**: When prisms are requested for a dirty block:
1. Extract 16x16x16 voxels from SVO for the block
2. Run greedy meshing algorithm
3. Store resulting prisms in cache
4. Remove block from dirty set

### Layer 3: VoxelGrid (Public API)

`VoxelGrid` is a thin wrapper providing the public interface:

```csharp
public class VoxelGrid {
    private readonly IncrementalPrismBuilder _builder;

    public void SetVoxel(VoxelPos pos, VoxelType type);
    public void SetVoxel(VoxelPos pos, Material material);  // Conductor with material
    public VoxelType GetVoxelType(VoxelPos pos);
    public IEnumerable<(BlockPos, Prism)> GetAllPrisms();
}
```

Conductor voxels default to Copper material when set without explicit material.

## Core Types

### VoxelType

```csharp
public enum VoxelType {
    Air = 0,               // Empty space (default)
    Conductor = 1,         // Zero-resistance conductor
    ResistiveConductor = 2, // Conductor with resistance
    Insulator = 3          // Blocks connectivity
}
```

### VoxelPos

World-space voxel coordinate. Each block contains 16x16x16 voxels:

```csharp
public readonly record struct VoxelPos(int X, int Y, int Z) {
    public BlockPos Block { get; }        // Containing block
    public (int X, int Y, int Z) Local { get; }  // Position within block (0-15)
}
```

### BlockPos

Block-space coordinate (16x16x16 chunks):

```csharp
public readonly record struct BlockPos(int X, int Y, int Z);
```

### Prism

Axis-aligned rectangular region of same-type voxels within a single block:

```csharp
public readonly record struct Prism {
    public byte LocalX, LocalY, LocalZ;  // Position in block (0-15)
    public byte SizeX, SizeY, SizeZ;     // Dimensions (1-16)
    public VoxelType Type;
    public Material? Material;
}
```

Prisms are bounded to single blocks (never span block boundaries).

### Material

Defines electrical properties of conductor materials:

```csharp
public sealed class Material {
    public string Name { get; }
    public double Resistivity { get; }  // Ohms per voxel

    public static Material Copper { get; }  // 0.001 ohm/voxel
    public static Material Lead { get; }    // 0.01 ohm/voxel
    public static Material Iron { get; }    // 0.005 ohm/voxel
    public static Material Gold { get; }    // 0.0015 ohm/voxel
}
```

## Greedy Meshing Algorithm

The prism builder uses greedy meshing to coalesce contiguous same-material voxels:

1. For each voxel type (Conductor, ResistiveConductor, Insulator):
2. Find unclaimed seed voxel
3. Grow in +X until hitting different type/material or boundary
4. Grow in +Y (maintaining X extent)
5. Grow in +Z (maintaining X and Y extent)
6. Mark all voxels in prism as claimed
7. Repeat until all voxels claimed

Each voxel type is processed separately to ensure prisms don't span type boundaries.

## Dirty Block Tracking

The system tracks modifications for incremental topology updates:

- `Version`: Counter incremented on every voxel change
- `DirtyBlocks`: Set of blocks needing prism rebuild
- `HasDirtyBlocks`: Quick check for pending rebuilds

```csharp
// Get old and new prisms for incremental update
(IReadOnlyList<Prism> old, IReadOnlyList<Prism> new) =
    grid.RebuildBlockIncremental(block);

// Get cached prisms WITHOUT triggering rebuild
IReadOnlyList<Prism> cached = grid.GetCachedPrisms(block);
```

## Performance Characteristics

| Operation | Complexity | Notes |
|-----------|------------|-------|
| SetVoxel | O(log n) | SVO update + mark dirty |
| GetVoxelType | O(log n) | SVO lookup |
| GetAllPrisms | O(dirty blocks) | Lazy rebuild |
| GetPrismsInBlock | O(1) or O(4096) | Cached or rebuild |

**Memory**: Prisms use ~14 bytes each vs ~50 bytes per voxel in dictionary storage.

**Uniform regions**: Large uniform areas compress to single octree nodes. A 16x16x16 block of identical voxels is one leaf node plus one prism.

## Usage Example

```csharp
var grid = new VoxelGrid();

// Set conductor voxels
grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
grid.SetVoxel(new VoxelPos(1, 0, 0), Material.Copper);  // Same as above

// Set resistive wire
grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.ResistiveConductor);

// Query
var type = grid.GetVoxelType(new VoxelPos(0, 0, 0));  // Conductor
var material = grid.GetMaterial(new VoxelPos(0, 0, 0));  // Copper

// Get prisms (triggers lazy rebuild of dirty blocks)
foreach (var (block, prism) in grid.GetAllPrisms()) {
    // Process prisms for topology building
}
```
