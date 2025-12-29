# Layering Fix Design

## Problem

The project has an inverted dependency structure:

**Current (incorrect):**
```
    Handbook    Mod
        ↓       ↓
        MNA ←── ┘
         ↓
       Voxel (no dependencies)
```

MNA (circuit math) depends on Voxel (spatial storage), but it should be the reverse. MNA should be pure math with no spatial concepts.

Additionally, Handbook and Mod query MNA directly using node IDs, which is a layer violation - they should ask spatial questions and let Voxel translate.

## Solution

### Corrected Layer Structure

```
    Handbook    Mod
        ↓       ↓
       Voxel
         ↓
        MNA (pure math, no spatial concepts)
```

**Project reference changes:**
- Remove: `Sparky.Mna.csproj` → `Sparky.Voxel.csproj`
- Add: `Sparky.Voxel.csproj` → `Sparky.Mna.csproj`
- Handbook/Mod continue referencing both (Voxel for spatial, MNA for simulation lifecycle)

### VoxelSimulation

New unified facade in `src/voxel/VoxelSimulation.cs`:

```csharp
public class VoxelSimulation {
    // Spatial state
    public VoxelGrid Grid { get; }

    // Simulation control
    public void Step(double dt);
    public bool ElectricalEnabled { get; set; } = true;
    // Future: ThermalEnabled, KineticEnabled

    // Spatial queries (the key abstraction)
    public double GetVoltageAt(VoxelPos pos);
    public double GetCurrentThrough(VoxelPos pos);

    // Topology dirty tracking
    public void MarkDirty();  // Called when voxels change
}
```

**Responsibilities:**
- Owns `VoxelGrid` (spatial state)
- Owns `ISimulation` (MNA solver)
- Owns `MnaTopologyBuilder` (voxels → circuit extraction)
- Tracks dirty state, rebuilds topology when needed

**What it hides:**
- MNA node IDs - callers never see them
- Topology regions - internal mapping from voxel → node
- Solver details - dense vs sparse, Newton-Raphson iterations

### MnaTopology Move

**From:** `src/mna/topology/` (namespace `Sparky.Mna.Topology`)
**To:** `src/voxel/MnaTopology/` (namespace `Sparky.Voxel.MnaTopology`)

**Files moving:**
```
src/mna/topology/
├── TopologyBuilder.cs
├── TerminalRegion.cs
├── Component.cs
├── ComponentTypes/
│   ├── BatteryComponent.cs
│   ├── GroundComponent.cs
│   ├── ResistorComponent.cs
│   └── SwitchComponent.cs
└── CableLaying/
    ├── CablePathfinder.cs
    ├── CableValidator.cs
    ├── CrossSection.cs
    ├── IWorldVoxelCache.cs
    ├── PathResult.cs
    └── SnapPositionFinder.cs
```

**Namespace changes:**
- `Sparky.Mna.Topology` → `Sparky.Voxel.MnaTopology`
- `Sparky.Mna.Topology.ComponentTypes` → `Sparky.Voxel.MnaTopology.ComponentTypes`
- `Sparky.Mna.Topology.CableLaying` → `Sparky.Voxel.MnaTopology.CableLaying`

The "Mna" prefix clarifies this is electrical topology, distinguishing from future kinetic/thermal topologies.

### Consumer Updates

**Handbook (`GameServer.cs`):**

Current pattern (layer violation):
```csharp
private readonly VoxelGrid _voxelGrid = new();
private readonly SimulationManager _simulation = new();
private readonly TopologyBuilder _topologyBuilder = new();

// Queries MNA directly with node IDs
var voltage = _simulation.GetVoltage(region.NodeId);
```

New pattern:
```csharp
private readonly VoxelSimulation _simulation = new();

// Spatial query - no node IDs visible
var voltage = _simulation.GetVoltageAt(voxelPos);
```

**Mod (`CircuitNetworkManager.cs`):**

Current pattern:
```csharp
public class NetworkState {
    public VoxelGrid Voxels { get; } = new();
    public TopologyBuilder Topology { get; } = new();
    public ISimulation Simulation { get; init; } = null!;
}
```

New pattern:
```csharp
public class NetworkState {
    public VoxelSimulation Simulation { get; } = new();
    // VoxelGrid and topology are internal to VoxelSimulation
}
```

## Implementation Steps

1. **Create `VoxelSimulation` class** in `src/voxel/`
   - Scaffold the API (Grid, Step, GetVoltageAt, etc.)
   - Initially wraps existing components

2. **Move topology to Voxel**
   - Move `src/mna/topology/` → `src/voxel/MnaTopology/`
   - Update namespaces
   - Update project references (flip Voxel ↔ MNA dependency)

3. **Integrate topology into VoxelSimulation**
   - VoxelSimulation owns MnaTopologyBuilder
   - Handles dirty tracking and rebuild internally

4. **Update Handbook**
   - Replace VoxelGrid + SimulationManager + TopologyBuilder with VoxelSimulation
   - Change voltage/current queries to spatial API

5. **Update Mod**
   - Replace NetworkState internals with VoxelSimulation
   - Spatial queries where needed

6. **Update context docs**
   - Revise `context/voxel-storage.md` to include VoxelSimulation
   - Update `context/mna-api.md` to reflect new boundaries

## Future Considerations

- **Additional solvers**: Kinetic and thermal solvers will follow the same pattern - owned by VoxelSimulation with their own topology builders (`KineticTopology`, `ThermalTopology`)
- **Cross-domain coupling**: Deferred. When needed, likely through shared voxel properties (e.g., voxels have temperature, components read it)
- **Per-domain toggles**: `ElectricalEnabled`, `ThermalEnabled`, etc. allow independent stepping for testing/debugging
