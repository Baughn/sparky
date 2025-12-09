# Simulation API Layer

Last updated: 2025-12-09

This document describes the high-level API in `MNA/Api/` that wraps the core solver.

## Overview

The API layer (`SimulationManager`) provides:
- Strongly-typed component IDs
- Automatic graph partitioning for parallel solving
- Line optimization (series resistor merging)
- Logical-to-physical node mapping
- Voltage interpolation for optimized-away nodes

## Key Files

- `ISimulation.cs` - Public interface
- `SimulationManager.cs` - Implementation (~1200 lines)
- `Ids.cs` - Strongly-typed ID structs
- `Exceptions.cs` - Custom exception types

## ID Types (`Ids.cs`)

Each component type has a distinct ID struct wrapping an int:
```csharp
public readonly record struct NodeId(int Value);
public readonly record struct ResistorId(int Value);
public readonly record struct VoltageSourceId(int Value);
// ... etc
```

Ground is always `NodeId(0)`.

## SimulationManager Architecture

### Logical vs Physical

The manager maintains two representations:

**Logical Graph** - User's view:
```csharp
Dictionary<NodeId, LogicalNode> _logicalNodes
Dictionary<ResistorId, LogicalResistor> _resistors
// ... one dictionary per component type
```

**Physical Circuits** - Solver's view:
```csharp
List<Circuit> _partitions                    // Independent sub-circuits
Dictionary<NodeId, Node> _physicalNodes      // Logical -> physical node
Dictionary<ResistorId, Resistor> _physicalResistors  // etc
```

### Rebuild Process (`Rebuild()`, line 848)

Triggered when `_isDirty` is true (topology changed). Steps:

1. **Optimize** (line 863): Run line optimization to merge resistor chains
2. **Partition** (lines 866-906): BFS from each unvisited node to find connected components
3. **BuildPartition** (line 905): Create a `Circuit` for each partition, map nodes and components

### Line Optimization (`Optimize()`, line 952)

Merges series resistor chains to reduce matrix size.

**Line Node Detection** (line 1092):
```csharp
bool IsLineNode(LogicalNode node)
{
    return node.Connections.Count == 2
        && node.Connections.All(c => c is LogicalResistor);
}
```

A node with exactly 2 connections, both resistors, is a "line node" and can be eliminated.

**Chain Building** (lines 999-1042):
- Start from any resistor
- Extend forward through line nodes
- Extend backward through line nodes
- Result: chain of resistors and nodes

**Merging** (lines 1043-1078):
- Calculate `totalR = sum of resistances`
- Create single merged resistor between chain endpoints
- For intermediate nodes, store interpolation info:
  ```csharp
  record InterpolationInfo(NodeId NodeA, NodeId NodeB, double Ratio)
  ```
  where `Ratio = cumulative_R / total_R`

**Tracking**:
- `_optimizedResistors` HashSet tracks which original resistors were merged
- `_interpolationMap` maps optimized-away nodes to their interpolation data

### Voltage Readout (`GetVoltage`, line 786)

```csharp
double GetVoltage(NodeId nodeId)
{
    if (nodeId.Value == 0) return 0.0;  // Ground

    // Physical node - direct lookup
    if (_physicalNodes.TryGetValue(nodeId, out var node))
        return node.Voltage;

    // Optimized node - interpolate
    if (_interpolationMap.TryGetValue(nodeId, out var info))
    {
        double vA = GetVoltage(info.NodeA);
        double vB = GetVoltage(info.NodeB);
        return vA + (vB - vA) * info.Ratio;
    }

    // Node exists but not in any partition (disconnected)
    return 0.0;
}
```

### Fast Path Updates

Component value updates can skip rebuild when:
- Resistor: Not part of an optimized chain (`!_optimizedResistors.Contains(id)`)
- Voltage/Current source: Always fast (they restamp every iteration anyway)
- Others: Fast if physical component exists

Example (line 256):
```csharp
void UpdateResistor(ResistorId id, double resistance)
{
    r.Resistance = resistance;

    // Fast path: not optimized, physical exists
    if (!_optimizedResistors.Contains(id) && _physicalResistors.TryGetValue(id, out var phys))
    {
        phys.Resistance = resistance;
        return;  // No rebuild needed
    }

    _isDirty = true;  // Will rebuild on next Step()
}
```

### Bulk Update (`BeginBulkUpdate`, line 761)

Defers rebuild until scope disposed:
```csharp
using (sim.BeginBulkUpdate())
{
    // Many Add/Remove/Update calls
    // No rebuilds happen here
}
// Single rebuild happens when scope exits (on next Step)
```

Nested scopes supported; only outermost dispose triggers rebuild eligibility.

### Parallel Solving (`Step`, line 703)

```csharp
void Step(double dt)
{
    if (_bulkUpdateDepth > 0)
        throw new InvalidOperationException("Cannot Step during bulk update");

    if (_isDirty) Rebuild();

    if (_partitions.Count <= 1)
    {
        foreach (var circuit in _partitions)
            circuit.Solve(dt);
    }
    else
    {
        Parallel.ForEach(_partitions, circuit => circuit.Solve(dt));
    }
}
```

Single partition avoids `Parallel.ForEach` overhead.

### Graph Partitioning (lines 866-906)

BFS from each non-ground, unvisited node:
- Ground (`NodeId(0)`) is included in every partition it touches but doesn't bridge them
- Each connected component becomes one partition
- Components are assigned to partitions based on whether all their nodes are in that partition

## Exception Types (`Exceptions.cs`)

| Exception | When |
|-----------|------|
| `InvalidNodeException` | Reference to non-existent node |
| `InvalidComponentException` | Reference to non-existent component |
| `NodeInUseException` | Removing node with connected components |
| `InvalidParameterException` | Invalid value (R≤0, C≤0, etc.) |

## Thread Safety

From `ISimulation.cs`:
> All methods must be called from a single thread, except `Step()` which may be called from a worker thread after all modifications are complete.

The internal `Parallel.ForEach` in `Step()` is safe because partitions are independent.

## Diagnostics

```csharp
int PartitionCount { get; }           // Number of independent circuits
bool IsNodeOptimized(NodeId id);      // True if node was merged away
SimulationStats GetStats();           // Aggregated stats

record SimulationStats(
    int TotalIterations,    // Sum of Newton iterations across partitions
    int PartitionCount,
    int PhysicalNodeCount,  // Nodes in solver
    int OptimizedNodeCount  // Nodes eliminated by line optimization
);
```

## Component Lifecycle

All components follow the same pattern:
1. **Add**: Validate nodes exist, validate parameters, create logical component, connect to nodes, set `_isDirty`
2. **Update**: Validate exists, validate parameters, update logical value, try fast-path physical update, else set `_isDirty`
3. **Remove**: Validate exists, disconnect from nodes, remove from dictionaries, set `_isDirty`
4. **Query**: Validate exists, return value from logical or physical component

The `Connect`/`Disconnect` helpers (lines 830-846) maintain the `LogicalNode.Connections` lists used for partitioning and optimization.
