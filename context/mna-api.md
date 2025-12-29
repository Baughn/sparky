# MNA High-Level API Design

## Overview
The MNA library is structured into two main layers:
1.  **Solver (`Sparky.Mna.Solver`, `src/mna/solver/`)**: The low-level solver. It deals with `Circuit`, `Node`, `Component`, and matrix solving. It is unaware of "game objects" or optimizations like resistor merging.
2.  **API (`Sparky.Mna.Api`, `src/mna/api/`)**: The high-level interface for the game engine. It manages the `LogicalCircuit`, performs optimizations (Line Optimization), and maps logical IDs to physical nodes/components.

## Layer Boundary

MNA is a pure circuit math library with no spatial concepts. The Voxel layer provides:
- `VoxelSimulation` - unified facade
- `MnaTopologyBuilder` - extracts circuits from voxels

Direct use of `ISimulation` for creating/stepping simulations is allowed.
Querying voltage/current should go through `VoxelSimulation` spatial methods.

## Solver Layer
The solver classes live in `src/mna/solver/`:
- `Circuit`
- `Node`
- `Component` and subclasses
- `Graph` (if used for internal solver graph)

## API Layer

### Strong Types for IDs
To ensure type safety, we will use lightweight structs for IDs.

```csharp
public readonly record struct NodeId(int Value);
public readonly record struct ResistorId(int Value);
public readonly record struct VoltageSourceId(int Value);
public readonly record struct CurrentSourceId(int Value);
public readonly record struct TransformerId(int Value);
// ... others
```

### `ISimulation` Interface
The primary interface for the game engine.

```csharp
namespace Sparky.Mna.Api
{
    public interface ISimulation
    {
        // Node Management
        NodeId CreateNode();

        // Component Management
        ResistorId AddResistor(NodeId nodeA, NodeId nodeB, double resistance);
        void UpdateResistor(ResistorId id, double resistance);
        void RemoveResistor(ResistorId id);

        VoltageSourceId AddVoltageSource(NodeId nodePos, NodeId nodeNeg, double voltage);
        void UpdateVoltageSource(VoltageSourceId id, double voltage);
        void RemoveVoltageSource(VoltageSourceId id);

        CurrentSourceId AddCurrentSource(NodeId nodeIn, NodeId nodeOut, double current);
        void UpdateCurrentSource(CurrentSourceId id, double current);
        void RemoveCurrentSource(CurrentSourceId id);
        
        // Transformer (example of multi-port component)
        TransformerId AddTransformer(NodeId p1, NodeId p2, NodeId s1, NodeId s2, double ratio);
        void RemoveTransformer(TransformerId id);

        // Simulation Control
        /// <summary>
        /// Advances the simulation by dt seconds.
        /// </summary>
        void Step(double dt);

        /// <summary>
        /// Clears the entire simulation.
        /// </summary>
        void Clear();

        // State Readout
        
        /// <summary>
        /// Gets the voltage at a logical node.
        /// If the node was optimized away, returns an interpolated value.
        /// </summary>
        double GetVoltage(NodeId nodeId);

        /// <summary>
        /// Gets the current flowing through a resistor.
        /// </summary>
        double GetCurrent(ResistorId id);

        /// <summary>
        /// Gets the current flowing through a voltage source.
        /// </summary>
        double GetCurrent(VoltageSourceId id);
        
        /// <summary>
        /// Gets the currents for a transformer.
        /// </summary>
        (double Primary, double Secondary) GetCurrents(TransformerId id);
        
        // Optimization Control
        bool EnableLineOptimization { get; set; }
    }
}
```

### `SimulationManager`
The concrete implementation of `ISimulation`.

#### Data Structures
- **`LogicalGraph`**: A graph representation of the user's circuit. Nodes are `NodeId`, edges are typed components.
- **`PhysicalCircuit`**: The `Sparky.Mna.Solver.Circuit` instance being solved.
- **`OptimizationMap`**: Stores mapping from Logical Nodes/Components to Physical ones.
    - `NodeId -> PhysicalNodeIndex` (direct mapping)
    - `NodeId -> InterpolationInfo` (for optimized nodes)
        - `InterpolationInfo`: `{ NodeA, NodeB, Ratio }`
- **`PartitionList`**: List of `PhysicalCircuit` instances (partitions).
- **`NodePartitionMap`**: `NodeId -> CircuitIndex`.

#### Graph Partitioning
To improve performance ($O(N^2)$ complexity), the circuit is split into independent sub-circuits.
1.  **Connectivity**: Two nodes are connected if a component exists between them.
2.  **Ground Exception**: Ground (Node 0) is global and does not bridge partitions. It exists in all partitions.
3.  **Algorithm**:
    -   Run BFS/DFS on the `LogicalGraph` (ignoring Ground).
    -   Each connected component forms a `Partition`.
    -   Each `Partition` becomes a separate `PhysicalCircuit`.
4.  **Management**:
    -   `SimulationManager` iterates over all partitions during `Step()`.
    -   `GetVoltage` looks up the partition index for the node.

#### Line Optimization Algorithm
1.  **Detection**: Identify "Line Nodes" in the `LogicalGraph`. A Line Node is a node with exactly degree 2, where both incident edges are Resistors.
2.  **Chaining**: Traverse connected Line Nodes to form a "Resistor Chain".
    - Chain: `NodeStart -- R1 -- Node2 -- R2 -- ... -- NodeEnd`
3.  **Merging**:
    - Calculate `TotalResistance = sum(R_i)`.
    - Create a single physical Resistor `R_total` between `NodeStart` and `NodeEnd`.
    - Map `NodeStart` and `NodeEnd` to physical nodes.
    - Mark intermediate nodes (`Node2`, etc.) as "Virtual".
4.  **Interpolation Setup**:
    - For each virtual node `n_i` in the chain, calculate its cumulative resistance from `NodeStart`: `R_cum`.
    - `Ratio = R_cum / TotalResistance`.
    - Store `n_i -> { StartNode, EndNode, Ratio }`.

#### Incremental Updates
- **Value Change**:
    - If a component is part of an optimized chain, update the `TotalResistance` of the physical resistor and update interpolation ratios.
    - If not optimized, update physical component directly.
- **Topology Change (Add/Remove)**:
    - Mark the graph as "Dirty".
    - On next `Step()`, if Dirty:
        - Re-analyze the graph (or locally repair).
        - Rebuild `PhysicalCircuit` (or partial update if possible, but full rebuild is safer for MVP).
        - Re-run optimization.

### Readout Logic
- `GetVoltage(NodeId nodeId)`:
    - If `nodeId` is in `PhysicalCircuit`, return `PhysicalCircuit.Nodes[map[nodeId]].Voltage`.
    - If `nodeId` is Virtual (optimized away):
        - Retrieve `StartNode`, `EndNode`, `Ratio`.
        - `V_start = GetVoltage(StartNode)`
        - `V_end = GetVoltage(EndNode)`
        - Return `V_start + (V_end - V_start) * Ratio`.
- `GetCurrent(ResistorId id)`:
    - If optimized, calculate `(V_start - V_end) / R_total`.
    - If physical, retrieve from physical component.

## Switch Component

The Switch is implemented at the API layer (not core) using an internal resistor:

```csharp
SwitchId AddSwitch(NodeId a, NodeId b, bool initiallyClosed = false);
void SetSwitchState(SwitchId id, bool closed);
void ToggleSwitch(SwitchId id);
void RemoveSwitch(SwitchId id);
bool SwitchExists(SwitchId id);
bool GetSwitchState(SwitchId id);
double GetSwitchCurrent(SwitchId id);
```

**Implementation Details:**
- Closed: R = 1e-9 Ω (nearly short)
- Open: R = 1e9 Ω (nearly open)
- State changes use the resistor fast-path (no topology rebuild)
- Internal resistor is marked as variable (`IsOptimizable = false`)

## Energy Tracking

Energy is accumulated per-component during `Step()`:

```csharp
// Query cumulative energy (Joules)
double GetVoltageSourceEnergy(VoltageSourceId id);  // +ve = delivering power
double GetCurrentSourceEnergy(CurrentSourceId id);  // +ve = delivering power
double GetResistorEnergy(ResistorId id);            // Always positive (dissipated)
double GetDiodeEnergy(DiodeId id);                  // Always positive (dissipated)
double GetCapacitorEnergy(CapacitorId id);          // +ve = charging, -ve = discharging
double GetInductorEnergy(InductorId id);            // +ve = storing, -ve = releasing

// Reset counters
void ResetEnergyCounters();                   // Reset all to zero
void ResetEnergyCounter(ResistorId id);       // Reset specific component
// ... similar for each component type
```

**Line Optimization Handling:**
Energy for merged resistor chains is distributed by resistance ratio:
```
individual_energy = chain_energy * (R_individual / R_total)
```

## Limit Events

Components can have limits that trigger events when exceeded:

```csharp
// Set limits (available for all component types)
void SetResistorLimit(ResistorId id, LimitKind kind, LimitConfig config);
void ClearResistorLimit(ResistorId id, LimitKind kind);
LimitConfig? GetResistorLimit(ResistorId id, LimitKind kind);

// Subscribe to events
IDisposable OnLimitEvent(LimitEventHandler handler);

public enum LimitKind { Current, Voltage, Power }

public record LimitConfig(double Threshold, bool TriggerOnce = false);

public record struct LimitEvent(
    ComponentRef Component,
    LimitKind Kind,
    double Value,
    double Threshold,
    double SimulationTime
);
```

Limits are checked after each `Step()`. Use `TriggerOnce = true` to fire only on first violation.

## Time Tracking

Simulation time is tracked automatically:

```csharp
double SimulationTime { get; }  // Cumulative time from Step() calls
void ResetTime();               // Reset to zero without clearing circuit
```

Time advances by `dt` after each `Step(dt)` call. `LimitEvent` includes `SimulationTime` for debugging.

## Default Parameter Values

| Component | Parameter | Default |
|-----------|-----------|---------|
| Diode | Is (saturation current) | 1e-14 A |
| Diode | Vt (thermal voltage) | 26mV (room temp) |
| Solver | Convergence tolerance | 1e-6 |
| Solver | Max Newton iterations | 50 |
| Solver | Gmin shunt | 1e-12 S |
| Switch | Closed resistance | 1e-9 Ω |
| Switch | Open resistance | 1e9 Ω |

## Directory Structure
```
src/mna/
├── Sparky.Mna.csproj
├── api/
│   ├── ISimulation.cs
│   ├── SimulationManager.cs
│   ├── Ids.cs
│   ├── Exceptions.cs
│   └── Utilities/
│       ├── AcVoltageSource.cs
│       ├── PwmVoltageSource.cs
│       └── ...
├── solver/
│   ├── Circuit.cs
│   ├── Component.cs
│   ├── Node.cs
│   └── ...
├── topology/
│   └── TopologyBuilder.cs
└── utilities/
    └── ...
```
