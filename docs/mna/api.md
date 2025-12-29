# MNA API Layer

The API layer (`Sparky.Mna.Api`) provides a high-level interface for circuit simulation, wrapping the low-level solver with strongly-typed IDs, automatic graph partitioning, and line optimization. It maintains two representations of the circuit: a logical graph for the user and physical circuits for the solver.

## Key Files

```
src/mna/api/
├── ISimulation.cs       # Public interface with all component operations
├── SimulationManager.cs # Implementation (~2000 lines)
├── Ids.cs               # Strongly-typed ID structs
├── Exceptions.cs        # Custom exception types
├── Energy/
│   └── EnergyCounter.cs # Accumulates energy per component
└── Limits/
    ├── ComponentRef.cs  # Type-erased component reference
    ├── LimitConfig.cs   # Threshold configuration
    ├── LimitEvent.cs    # Event fired on limit violation
    └── LimitKind.cs     # Limit type enumeration
```

## Architecture

### Logical vs Physical Separation

The `SimulationManager` maintains two parallel representations:

**Logical Graph** - The user's view of the circuit:
- `Dictionary<NodeId, LogicalNode>` - Nodes with connection lists
- `Dictionary<ResistorId, LogicalResistor>` - One dictionary per component type
- Components store their logical parameters and energy counters

**Physical Circuits** - The solver's view:
- `List<Circuit>` - Independent partitions for parallel solving
- `Dictionary<NodeId, Node>` - Logical-to-physical node mapping
- `Dictionary<ResistorId, Resistor>` - Physical component references for fast updates

This separation enables:
1. Optimizations that eliminate nodes (line optimization)
2. Graph partitioning for parallel solving
3. Fast-path updates when topology unchanged

### Rebuild Process

When topology changes (`_isDirty = true`), the next `Step()` triggers a rebuild:

1. **Save Transient State**: Preserve capacitor voltages, inductor currents, and diode operating points from physical to logical components
2. **Clear Physical Maps**: Remove all physical circuits and mappings
3. **Optimize**: Run line optimization to merge resistor chains
4. **Partition**: BFS from each unvisited node to find connected components
5. **Build Partitions**: Create a `Circuit` for each partition, restore transient state

## ID Types

Each component type has a distinct ID struct wrapping an int, preventing type confusion at compile time:

```csharp
public readonly record struct NodeId(int Value);
public readonly record struct ResistorId(int Value);
public readonly record struct VoltageSourceId(int Value);
public readonly record struct CurrentSourceId(int Value);
public readonly record struct CapacitorId(int Value);
public readonly record struct InductorId(int Value);
public readonly record struct DiodeId(int Value);
public readonly record struct TransformerId(int Value);
public readonly record struct SwitchId(int Value);
public readonly record struct VcvsId(int Value);
public readonly record struct VccsId(int Value);
public readonly record struct CcvsId(int Value);
public readonly record struct CccsId(int Value);
```

Ground is always `NodeId(0)` and is accessible via `ISimulation.Ground`.

## Components

Each component follows a consistent lifecycle:

| Operation | Behavior |
|-----------|----------|
| Add | Validate nodes exist, validate parameters, create logical component, connect to nodes, set dirty flag |
| Update | Validate exists, validate parameters, update logical value, try fast-path physical update, else set dirty |
| Remove | Validate exists, disconnect from nodes, remove from dictionaries, set dirty flag |
| Query | Validate exists, return value from logical or physical component |

### Supported Components

| Type | Parameters | Notes |
|------|------------|-------|
| Resistor | resistance, isVariable | Variable resistors skip line optimization |
| VoltageSource | voltage | Positive at nodePos |
| CurrentSource | current | Flows from nodeIn to nodeOut |
| Capacitor | capacitance | Transient state preserved across rebuilds |
| Inductor | inductance | Transient state preserved across rebuilds |
| Diode | (none) | Nonlinear, operating point preserved |
| Transformer | ratio (Ns/Np) | 4-terminal ideal transformer |
| Switch | initiallyClosed | Implemented as variable resistor |
| VCVS | gain | Voltage-controlled voltage source |
| VCCS | transconductance | Voltage-controlled current source |
| CCVS | transresistance | Current-controlled voltage source |
| CCCS | gain | Current-controlled current source |

### Switch Implementation

Switches are implemented at the API layer using an internal variable resistor:

| State | Resistance |
|-------|-----------|
| Closed | 1e-9 ohm |
| Open | 1e9 ohm |

State changes use the resistor fast-path (no topology rebuild). The internal resistor is marked as `isVariable = true`, excluding it from line optimization.

## Optimizations

### Graph Partitioning

Independent sub-circuits are solved in parallel to reduce overall solve time:

1. BFS from each non-ground, unvisited node
2. Ground (`NodeId(0)`) is included in every partition it touches but does not bridge them
3. Each connected component becomes one partition
4. `Step()` uses `Parallel.ForEach` for multiple partitions (sequential for single partition to avoid overhead)

### Line Optimization

Series resistor chains are merged to reduce matrix size:

**Line Node Detection**: A node qualifies as a "line node" if:
- It has exactly 2 connections
- Both connections are resistors
- Both resistors are optimizable (not variable)

**Chain Building**:
1. Start from any resistor
2. Extend forward through line nodes
3. Extend backward through line nodes
4. Result: chain of resistors and intermediate nodes

**Merging**:
1. Calculate `totalR = sum(R_i)`
2. Create single merged resistor between chain endpoints
3. Store interpolation info for intermediate nodes: `ratio = cumulative_R / total_R`

**Voltage Interpolation**: For optimized-away nodes:
```csharp
double vA = GetVoltage(startNode);
double vB = GetVoltage(endNode);
return vA + (vB - vA) * ratio;
```

Control via `EnableLineOptimization` property (default: `true`).

### Fast-Path Updates

Component value updates can skip rebuild when:

| Component | Fast-Path Condition |
|-----------|---------------------|
| Resistor | Not part of optimized chain |
| VoltageSource | Always (restamps every iteration) |
| CurrentSource | Always (restamps every iteration) |
| Capacitor | Physical component exists |
| Inductor | Physical component exists |
| Others | Physical component exists |

## Simulation Control

### Step Execution

```csharp
void Step(double dt);
```

1. Validates dt is non-negative and finite
2. Throws if called during bulk update
3. Rebuilds if dirty flag is set
4. Solves partitions (parallel if multiple)
5. Merges energy deltas from physical to logical components
6. Updates simulation time
7. Checks limits and fires events

### Bulk Updates

```csharp
using (sim.BeginBulkUpdate()) {
    // Many Add/Remove/Update calls
    // No rebuilds happen here
}
// Single rebuild on next Step()
```

Nested scopes are supported; only the outermost dispose sets the dirty flag. `Step()` cannot be called during a bulk update.

## State Readout

### Voltage

```csharp
double GetVoltage(NodeId nodeId);
```

- Ground (NodeId 0) always returns 0.0
- Physical nodes return directly from solver
- Optimized nodes return interpolated value
- Unknown nodes throw `InvalidNodeException`

### Current

Each component provides current query methods:
- `GetResistorCurrent(id)`: Computed from voltage difference and resistance
- `GetVoltageSourceCurrent(id)`: From solver (voltage sources have explicit current)
- `GetCapacitorCurrent(id)`, `GetInductorCurrent(id)`: From physical component
- `GetTransformerCurrents(id)`: Returns `(Primary, Secondary)` tuple
- `GetSwitchCurrent(id)`: Delegates to internal resistor

## Energy Tracking

Energy is accumulated per-component across topology rebuilds:

```csharp
double GetResistorEnergy(ResistorId id);       // Always positive (dissipated)
double GetVoltageSourceEnergy(VoltageSourceId id);  // +ve = delivering
double GetCapacitorEnergy(CapacitorId id);     // +ve = charging
double GetInductorEnergy(InductorId id);       // +ve = storing
double GetDiodeEnergy(DiodeId id);             // Always positive

void ResetEnergyCounters();              // Reset all
void ResetEnergyCounter(ResistorId id);  // Reset specific
```

For line-optimized resistor chains, energy is distributed by resistance ratio:
```
individual_energy = chain_energy * (R_individual / R_total)
```

## Limit Events

Components can have limits that trigger events when exceeded:

```csharp
void SetResistorLimit(ResistorId id, LimitKind kind, LimitConfig config);
void ClearResistorLimit(ResistorId id, LimitKind kind);
IDisposable OnLimitEvent(LimitEventHandler handler);
```

### LimitKind

| Kind | Description |
|------|-------------|
| OverCurrent | Current exceeds threshold (signed) |
| OverVoltage | Voltage exceeds threshold (signed) |
| OverPower | Power exceeds threshold |
| OverTemperature | Reserved for thermal domain |
| OverHeatRate | Reserved for thermal domain |
| OverSpeed | Reserved for kinetic domain |
| OverTorque | Reserved for kinetic domain |

### LimitConfig

```csharp
public readonly record struct LimitConfig {
    public required double Threshold { get; init; }
    public double Hysteresis { get; init; }      // Default 0
    public bool FireEveryStep { get; init; }     // Default false
}
```

- `Threshold`: Value that triggers the limit
- `Hysteresis`: Event clears when value drops below `Threshold - Hysteresis`
- `FireEveryStep`: If true, callback fires every step while exceeded; if false, only on rising edge

### LimitEvent

```csharp
public readonly record struct LimitEvent {
    public required ComponentRef Component { get; init; }
    public required LimitKind Kind { get; init; }
    public required double Threshold { get; init; }
    public required double ActualValue { get; init; }
    public required bool IsExceeded { get; init; }
    public double SimulationTime { get; init; }
}
```

## Time Tracking

```csharp
double SimulationTime { get; }  // Cumulative time from Step() calls
void ResetTime();               // Reset to zero without clearing circuit
```

Time advances by `dt` after each `Step(dt)` call.

## Diagnostics

```csharp
int PartitionCount { get; }           // Number of independent circuits
bool IsNodeOptimized(NodeId id);      // True if node was merged away
SimulationStats GetStats();           // Aggregated statistics
```

### SimulationStats

```csharp
public readonly record struct SimulationStats(
    int TotalIterations,    // Sum of Newton iterations across partitions
    int PartitionCount,
    int PhysicalNodeCount,  // Nodes in solver
    int OptimizedNodeCount  // Nodes eliminated by line optimization
);
```

## Exception Types

| Exception | When Thrown |
|-----------|-------------|
| `InvalidNodeException` | Reference to non-existent node |
| `InvalidComponentException` | Reference to non-existent component |
| `NodeInUseException` | Removing node with connected components |
| `InvalidParameterException` | Invalid value (R <= 0, C <= 0, etc.) |
| `SimulationException` | Base class for all simulation errors |

## Thread Safety

From `ISimulation.cs`:

> All methods must be called from a single thread, except `Step()` which may be called from a worker thread after all modifications are complete.

The internal `Parallel.ForEach` in `Step()` is safe because partitions are independent and do not share state.

## Usage Example

```csharp
var sim = new SimulationManager();

// Create nodes
var n1 = sim.CreateNode();
var n2 = sim.CreateNode();
var n3 = sim.CreateNode();

// Build a voltage divider
var vs = sim.AddVoltageSource(n1, sim.Ground, 10.0);
var r1 = sim.AddResistor(n1, n2, 1000.0);
var r2 = sim.AddResistor(n2, sim.Ground, 1000.0);

// Add a capacitor for transient behavior
var cap = sim.AddCapacitor(n2, n3, 1e-6);
var r3 = sim.AddResistor(n3, sim.Ground, 1000.0);

// Set up limit monitoring
sim.SetResistorLimit(r1, LimitKind.OverPower, new LimitConfig { Threshold = 0.1 });
sim.OnLimitEvent(evt => Console.WriteLine($"Limit {evt.Kind} on {evt.Component}"));

// Run simulation
for (int i = 0; i < 100; i++) {
    sim.Step(1e-3);
}

// Query results
Console.WriteLine($"Voltage at n2: {sim.GetVoltage(n2):F3}V");
Console.WriteLine($"Current through R1: {sim.GetResistorCurrent(r1)*1000:F3}mA");
Console.WriteLine($"Energy dissipated in R1: {sim.GetResistorEnergy(r1)*1e6:F3}uJ");
Console.WriteLine($"Stats: {sim.GetStats()}");
```
