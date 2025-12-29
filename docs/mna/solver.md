# MNA Core Solver

The core solver implements Modified Nodal Analysis (MNA) to solve electrical circuits. It constructs and solves a linear system `Ax = z` where `A` is the system matrix containing conductances and constraints, `x` contains unknown node voltages and branch currents, and `z` is the right-hand side vector of sources. The solver supports DC, transient, and nonlinear analysis through Newton-Raphson iteration.

## Key Files

```
src/mna/solver/
├── Circuit.cs        # Main solver: matrix assembly, Newton-Raphson iteration, dense/sparse selection
├── Component.cs      # Abstract base class defining the stamping interface
├── Node.cs           # Simple node with ID and voltage
├── Resistor.cs       # Passive conductance element
├── VoltageSource.cs  # Ideal voltage source (requires auxiliary equation)
├── CurrentSource.cs  # Ideal current source
├── Capacitor.cs      # Backward Euler companion model for transient analysis
├── Inductor.cs       # Backward Euler companion model for transient analysis
├── Diode.cs          # Nonlinear element with Newton-Raphson linearization
├── Transformer.cs    # Ideal transformer with turns ratio
├── VCCS.cs           # Voltage-controlled current source
├── VCVS.cs           # Voltage-controlled voltage source
├── CCCS.cs           # Current-controlled current source
├── CCVS.cs           # Current-controlled voltage source
```

## Architecture

### Data Flow

1. **Build phase** (`BuildSystem`): Assigns matrix indices to components with auxiliary equations, creates sparse matrix storage, and performs initial stamping.

2. **Solve phase** (`Solve`): Iteratively stamps all components, solves the linear system, updates operating points for nonlinear components, and checks convergence.

3. **Finalize phase**: Updates component states for the next time step and extracts branch currents from the solution vector.

### Matrix Structure

The system matrix has dimensions `(n + k) x (n + k)` where:
- `n` = number of nodes (including ground at index 0)
- `k` = total auxiliary equations from components (voltage sources, transformers, controlled sources)

The upper-left `n x n` block contains conductances. Additional rows/columns handle voltage constraints and branch current variables.

## Circuit Class

### Key Data Structures

```csharp
List<Node> Nodes                    // Node 0 is always ground
List<Component> Components          // All circuit elements
CoordinateStorage<double> _matrixA  // Sparse matrix builder (sums duplicate entries)
double[] _vectorZ                   // RHS vector (sources)
double[] _vectorX                   // Solution vector (voltages + currents)
```

### Solver Selection

The solver chooses between dense and sparse algorithms based on matrix characteristics:

| Condition | Solver | Reason |
|-----------|--------|--------|
| Size <= 96 | Dense LU | Lower overhead dominates |
| Density >= 0.18 | Dense LU | Better cache utilization |
| Otherwise | CSparse SparseLU | Scales for large sparse systems |

Density is computed as `NonZerosCount / (n * n)`. The dense solver uses in-place LU decomposition with partial pivoting.

### Newton-Raphson Iteration

For circuits containing nonlinear components (diodes), the solver iterates:

1. Clear and re-stamp the matrix and RHS vector
2. Anchor ground node and apply Gmin shunts
3. Solve the linear system
4. Update operating points for nonlinear components
5. Check convergence criteria

Convergence requires both conditions to be satisfied:
- Step norm: `||x_new - x_prev||_inf < tolerance * (1 + ||x||_inf)`
- Residual norm: `||Ax - z||_inf < tolerance * (1 + ||z||_inf)`

Default tolerance is `1e-6` with maximum 50 iterations.

### Matrix Conditioning

**Ground Anchoring**: Sets `A[0,0] = 1` and `z[0] = 0` to fix the voltage reference and ensure a non-singular matrix.

**Gmin Shunts**: Adds a tiny conductance (`1e-12 S`) from each non-ground node to ground. This prevents floating nodes from causing singularity without significantly affecting results.

### Caching

For static linear circuits (no diodes, no transient elements), the solver caches:
- LU factorization for reuse across solves
- `_stampVersion` tracks topology changes
- Fast path skips re-solving when nothing has changed

## Component Interface

All circuit elements inherit from the abstract `Component` class:

```csharp
abstract class Component {
    Node Node1, Node2;                      // Terminal nodes
    int MatrixIndex = -1;                   // Index for auxiliary equations

    virtual bool HasExtraEquation => false;          // Needs auxiliary row/column
    virtual int ExtraEquationCount => ...;           // Number of aux equations (usually 0 or 1)
    virtual bool IsNonLinear => false;               // Participates in Newton-Raphson
    virtual bool RequiresIteration => IsNonLinear;   // Requires iterative solving
    virtual bool RequiresPerStepRestamp => false;    // Must re-stamp every solve

    abstract void Stamp(CoordinateStorage<double> A, double[] z, double dt = 0);
    virtual void UpdateOperatingPoint(double[] x) { }  // Newton-Raphson update
    virtual void UpdateState(double[] x, double dt) { } // End-of-step state save
    virtual void AccumulateEnergy(double[] x, double dt) { } // Energy tracking
}
```

### Node Class

Simple container holding a node ID and its computed voltage:

```csharp
class Node {
    int Id;           // Index in the solution vector
    double Voltage;   // Computed voltage (populated by solver)
}
```

## Component Stamps

### Resistor

A resistor with conductance `G = 1/R` stamps a symmetric 2x2 block:

```
A[n1,n1] += G    A[n1,n2] -= G
A[n2,n1] -= G    A[n2,n2] += G
```

Ground (node 0) rows/columns are skipped since that row is anchored.

### Voltage Source

Voltage sources require an auxiliary equation (row `k`) to solve for current. The voltage constraint `V_n1 - V_n2 = V` is enforced:

```
A[n1, k] += 1    A[n-, k] -= 1
A[k, n1] += 1    A[k, n2] -= 1
z[k] = V
```

The solution `x[k]` gives the current flowing from `n1` to `n2`. Sets `RequiresPerStepRestamp = true` to support time-varying voltages.

### Current Source

Current sources modify only the RHS vector. For current `I` flowing from `n1` to `n2`:

```
z[n1] -= I
z[n2] += I
```

Sets `RequiresPerStepRestamp = true` to support time-varying currents.

### Capacitor (Backward Euler)

In transient analysis, a capacitor becomes an equivalent conductance plus current source:

```
G_eq = C / dt
I_eq = G_eq * V_prev
```

Stamps as a resistor `G_eq` plus current source `I_eq`. For DC analysis (`dt <= 0`), the capacitor is an open circuit (no stamp).

State variable `VoltageAcross` preserves voltage between solves. The current `I = C * dV/dt` is computed in `UpdateState`.

### Inductor (Backward Euler)

In transient analysis, an inductor becomes:

```
G_eq = dt / L
I_eq = I_prev
```

Stamps as a resistor `G_eq` plus current source `-I_eq` (note sign). For DC analysis (`dt = 0`), modeled as a tiny resistor (`1e-9 ohms`) to approximate a short circuit without causing singularity.

State variable `CurrentThrough` preserves current between solves.

### Diode (Newton-Raphson Linearization)

The Shockley diode equation is linearized around the operating voltage `V_d`:

```
I = Is * (exp(Vd / (n*Vt)) - 1)
G_eq = dI/dV = (Is / (n*Vt)) * exp(Vd / (n*Vt))
I_eq = I - G_eq * Vd
```

Constants: `Is = 1e-12 A`, `Vt = 0.026 V`, `n = 1.0`.

Stamps as resistor `G_eq` plus current source `I_eq`. The operating voltage is updated via `UpdateOperatingPoint` after each Newton-Raphson iteration. Voltage limiting clamps `V_d` to `[-5.0, 0.9]` to prevent exponential overflow.

### Transformer

An ideal transformer with turns ratio `n = Ns/Np` enforces:

```
V_primary = V_secondary / n
I_secondary = -I_primary / n
```

Uses one auxiliary equation (row `k`) for the voltage constraint:

```
(V1 - V2) - (1/n) * (V3 - V4) = 0
```

Current contributions couple the primary and secondary windings via the auxiliary variable.

### Controlled Sources

**VCCS (Voltage-Controlled Current Source)**: Output current proportional to input voltage. Uses transconductance `gm`. Stamps cross-coupling terms in the matrix without auxiliary equations.

**VCVS (Voltage-Controlled Voltage Source)**: Output voltage proportional to input voltage. Uses one auxiliary equation for output current tracking.

**CCCS (Current-Controlled Current Source)**: Output current proportional to input current. Uses one auxiliary equation to sense input current through a zero-voltage constraint.

**CCVS (Current-Controlled Voltage Source)**: Output voltage proportional to input current. Uses two auxiliary equations: one for current sensing, one for output voltage constraint.

## Energy Tracking

All components implement `AccumulateEnergy(x, dt)` to compute energy transferred during a time step:

- **Resistor/Diode**: `E = P * dt = V^2 * G * dt` (always positive, dissipated as heat)
- **Voltage/Current Source**: `E = V * I * dt` (positive = delivering power)
- **Capacitor/Inductor**: `E = V * I * dt` (positive = charging/storing)

The `EnergyDelta` property holds the computed value after each solve.

## Dependencies

- **CSparse** (NuGet package): Provides sparse matrix storage (`CoordinateStorage`, `CompressedColumnStorage`) and LU factorization (`SparseLU`).
- Coordinate storage automatically sums duplicate entries, simplifying stamping logic.
- Natural column ordering is used for sparse factorization.

## Thread Safety

The `Circuit` class is **not thread-safe**. All operations must occur on a single thread. The higher-level API (`SimulationManager`) handles parallel solving of independent circuit partitions.
