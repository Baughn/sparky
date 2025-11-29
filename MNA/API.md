# MNA High-Level API Design

## Overview
The MNA library will be structured into two main layers:
1.  **Core (`Sparky.MNA.Core`)**: The low-level solver (existing code). It deals with `Circuit`, `Node`, `Component`, and matrix solving. It is unaware of "game objects" or optimizations like resistor merging.
2.  **API (`Sparky.MNA.Api`)**: The high-level interface for the game engine. It manages the `LogicalCircuit`, performs optimizations (Line Optimization), and maps logical IDs to physical nodes/components.

## Core Refactoring
The existing classes in `Sparky.MNA` will be moved to `Sparky.MNA.Core`.
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
namespace Sparky.MNA.Api
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
- **`PhysicalCircuit`**: The `Sparky.MNA.Core.Circuit` instance being solved.
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

## Directory Structure
```
MNA/
├── API.md
├── Api/
│   ├── ISimulation.cs
│   ├── SimulationManager.cs
│   └── ...
├── Core/
│   ├── Circuit.cs
│   ├── Component.cs
│   ├── Node.cs
│   └── ...
└── Tests/ (or separate project)
```
