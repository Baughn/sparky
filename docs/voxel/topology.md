# Voxel-to-Circuit Topology Extraction

The topology layer bridges voxel geometry and MNA circuit simulation. It analyzes conductor prisms from the VoxelGrid, identifies connected regions using union-find, and creates corresponding MNA nodes and resistors. Components (batteries, resistors, etc.) are then connected to these regions via their terminal voxels.

## Key Files

```
src/voxel/MnaTopology/
├── TopologyBuilder.cs           # Main topology extraction with incremental updates
├── Component.cs                 # Abstract base for multi-voxel components
├── TerminalRegion.cs            # Named conductor region for component terminals
└── ComponentTypes/
    ├── BatteryComponent.cs      # Voltage source between terminals
    ├── ResistorComponent.cs     # Resistor between terminals
    ├── SwitchComponent.cs       # Switchable resistance
    └── GroundComponent.cs       # Ground reference marker
```

## Architecture

```
VoxelGrid (prisms)
       │
       ▼
┌─────────────────────────────────────────────────────┐
│              TopologyBuilder.BuildTopology()        │
│                                                     │
│  1. Find conductor prisms (Conductor, Resistive)   │
│  2. Union-find: group adjacent non-resistive       │
│  3. Create MNA node per ConductorRegion            │
│  4. Create resistors between adjacent regions      │
│  5. Map component terminals to region nodes        │
│  6. Create MNA components (voltage sources, etc.)  │
└─────────────────────────────────────────────────────┘
       │
       ▼
ISimulation (nodes, resistors, voltage sources)
```

## Conductor Regions

A `ConductorRegion` represents a connected group of conductor prisms that share the same electrical potential (same MNA node).

```csharp
public class ConductorRegion {
    public NodeId NodeId { get; set; }
    public HashSet<VoxelPos> Voxels { get; }
    public List<(BlockPos Block, Prism Prism)> Prisms { get; }
    public bool IsResistive { get; internal set; }
    public List<ResistorId> AdjacentResistors { get; }
}
```

### Region Types

| Voxel Type | Union Behavior | Node Assignment |
|------------|----------------|-----------------|
| `Conductor` | Merge with adjacent conductors | Single shared node |
| `ResistiveConductor` | Never merge (each is own region) | Own node, resistors to neighbors |

This distinction enables modeling wires (conductors merge into equipotential regions) vs resistive elements (each gets its own node with resistors providing voltage drop).

## Union-Find Algorithm

The topology builder uses union-find to group connected conductor prisms into regions:

```
1. Collect all conductor prisms with their block positions
2. Initialize parent array: parent[i] = i for each prism
3. For each pair of prisms:
   a. Same block: check if prisms touch (share a face)
   b. Adjacent blocks: check if prisms connect across boundary
   c. Union if: both non-resistive AND adjacent
4. Build regions from union-find roots
5. Map each voxel in region to the ConductorRegion
```

### Prism Adjacency

Two prisms touch if they:
- Overlap in 2 dimensions AND are adjacent in the third
- For cross-block: one prism at block boundary, other at opposite boundary

```csharp
// Within-block: ranges [a1,a2) and [b1,b2) overlap?
private static bool RangesOverlap(int a1, int a2, int b1, int b2) {
    return a1 < b2 && b1 < a2;
}
```

## Inter-Region Resistors

When two regions are adjacent (prisms touch) and at least one is resistive, a resistor is created between them:

```
Resistance = Resistivity / ContactArea
```

Where:
- `Resistivity` = average of both regions' material resistivity
- `ContactArea` = number of voxel faces in contact

The spatial hash enables O(n) complexity for finding adjacent prisms instead of O(n^2).

## Component Integration

Components are multi-voxel structures with terminal regions. The topology builder:

1. Finds which `ConductorRegion` each terminal voxel belongs to
2. Passes terminal-to-node mapping to component
3. Component creates its MNA elements (voltage source, resistor, etc.)

```csharp
public abstract class Component {
    public abstract IReadOnlyList<TerminalRegion> Terminals { get; }

    public abstract void CreateMnaComponents(
        ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes);

    public abstract void RemoveMnaComponents(ISimulation sim);
}
```

### Terminal Regions

A `TerminalRegion` is a named set of conductor voxels where a component connects to external wiring:

```csharp
public class TerminalRegion {
    public string Name { get; }  // e.g., "positive", "negative"
    public IReadOnlySet<VoxelPos> Voxels { get; }
}
```

### Example: Battery

```csharp
public class BatteryComponent : Component {
    private VoltageSourceId? _voltageSourceId;

    public override void CreateMnaComponents(
        ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes) {
        var negNode = terminalNodes["negative"];
        var posNode = terminalNodes["positive"];
        _voltageSourceId = sim.AddVoltageSource(posNode, negNode, Voltage);
    }
}
```

## Incremental Updates

The topology builder maintains persistent state to enable incremental updates when only a few voxels change.

### Version Tracking

```csharp
private long _lastBuiltVersion;

// In BuildTopology():
if (voxels.Version == _lastBuiltVersion) {
    // Skip rebuild - only update components if needed
    return _cachedRegions;
}
```

### Merge Detection

Before incremental update, check if new prisms would merge multiple existing regions:

```
For each new non-resistive prism:
    Check boundary faces for adjacent non-resistive regions
    If touches >1 distinct region: FULL REBUILD (merge case)
```

Resistive prisms never cause merges since they don't union.

### Incremental Update Flow

```
1. Expand dirty blocks to include neighbors (cross-block connectivity)
2. Remove MNA components that reference affected nodes
3. Remove affected regions from indexes and simulation
4. Rebuild prisms for dirty blocks
5. Run union-find on affected prisms
6. Create new regions with MNA nodes
7. Create inter-region resistors
8. Recreate components with new node mappings
```

### Extension Fast Path

When adding voxels to a large existing region:

```
If region extends beyond dirty area AND only adding (no removals):
    Use ExtendExistingRegion - O(dirty blocks) not O(all blocks)
```

## Data Structures

### Persistent State

```csharp
private Dictionary<VoxelPos, ConductorRegion>? _cachedRegions;
private readonly Dictionary<BlockPos, HashSet<ConductorRegion>> _blockToRegions;
private readonly SpatialHash<(ConductorRegion, BlockPos, Prism)> _prismIndex;
private readonly Dictionary<(ConductorRegion, ConductorRegion), ResistorId> _regionPairResistors;
```

### Spatial Hash

The `SpatialHash<T>` provides O(1) proximity queries for finding adjacent prisms:

```csharp
_prismIndex.Add((region, block, prism), min, max);  // AABB bounds
_prismIndex.QueryDistinct(expandedMin, expandedMax); // Nearby prisms
```

## Ground Handling

Ground components mark their terminal region as the simulation ground node:

```csharp
foreach (var component in componentList) {
    if (component.Type == ComponentType.Ground) {
        foreach (var terminal in component.Terminals) {
            foreach (var voxel in terminal.Voxels) {
                if (regions.TryGetValue(voxel, out var region)) {
                    groundRegions.Add(region);
                }
            }
        }
    }
}

// During node assignment:
if (groundRegions.Contains(region)) {
    region.NodeId = sim.Ground;
} else {
    region.NodeId = sim.CreateNode();
}
```

## Performance Characteristics

| Operation | Complexity | Notes |
|-----------|------------|-------|
| Full rebuild | O(P log P) | P = prism count, union-find with path compression |
| Incremental (no merge) | O(D) | D = dirty block prisms |
| Extension fast path | O(D) | Adding to existing region |
| Adjacent prism query | O(1) | Spatial hash lookup |
| Contact area calculation | O(1) | Range overlap math |

## Integration Points

- **Input**: `VoxelGrid.GetAllPrisms()` provides conductor/resistive prisms with block positions
- **Output**: Populates `ISimulation` with nodes, resistors, and component elements
- **Queried by**: `VoxelSimulation.GetVoltageAt(pos)` uses region mapping for spatial queries
