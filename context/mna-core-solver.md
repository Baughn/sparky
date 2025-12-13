# MNA Core Solver

Last updated: 2025-12-13

This document describes the low-level circuit solver in `MNA/Core/`.

## Overview

The core solver implements Modified Nodal Analysis (MNA) to solve electrical circuits. It builds a system of linear equations `Ax = z` where:
- `A` = system matrix (conductances and constraints)
- `x` = unknowns (node voltages + auxiliary currents)
- `z` = RHS vector (sources)

## Key Files

- `Circuit.cs` - Main solver class
- `Component.cs` - Abstract base for components
- `Node.cs` - Simple voltage holder
- Component implementations: `Resistor.cs`, `VoltageSource.cs`, `CurrentSource.cs`, `Capacitor.cs`, `Inductor.cs`, `Diode.cs`, `Transformer.cs`

## Circuit Class (`Circuit.cs`)

### Data Structures

```csharp
List<Node> Nodes              // Node 0 is always ground
List<Component> Components
CoordinateStorage<double> _matrixA   // Sparse matrix builder (accumulates duplicates)
CompressedColumnStorage<double> _compressedA  // CSparse format for solving
double[] _vectorZ, _vectorX   // RHS and solution vectors
```

### Solver Selection (lines 410-417)

Two solver paths based on matrix characteristics:

| Condition | Solver |
|-----------|--------|
| Size ≤ 96 nodes | Dense LU with partial pivoting |
| Density ≥ 0.18 | Dense LU |
| Otherwise | CSparse SparseLU |

The dense solver is faster for small/dense matrices due to lower overhead.

### Solve Loop (`Solve(double dt)`, line 111)

1. **Fast path** (lines 116-126): If circuit is static linear and nothing changed, reuse previous solution
2. **Newton-Raphson iteration** (lines 128-223):
   - Clear and re-stamp matrix/RHS each iteration
   - Pin ground row/col to 1 (line 157)
   - Apply gmin shunts (line 158) - tiny 1e-12 conductance to ground on every node
   - Solve linear system
   - Update operating points for nonlinear components
   - Check convergence via scaled infinity norms of step and residual
3. **Finalize** (lines 231-253): Update component states, extract currents from solution vector

### Convergence Criteria (lines 189-204)

Both must be satisfied:
- `stepNorm < tolerance * (1 + ||x||∞)` - solution change is small
- `residualNorm < tolerance * (1 + ||z||∞)` - equation residual is small

Default tolerance: `1e-6`, max iterations: `50`

### Matrix Conditioning

- **Ground anchoring** (`AnchorGround`, line 256): Sets A[0,0]=1, z[0]=0 to fix reference
- **Gmin shunts** (`ApplyGmin`, line 264): Adds 1e-12 conductance from each node to ground, prevents floating nodes from causing singularity

### Caching (lines 150-154, 308-312)

For static linear circuits (no diodes, no transient elements):
- Cached LU factorization reused across solves
- `_stampVersion` tracks topology changes
- `_requiresIteration` / `_requiresPerStepRestamp` flags control cache invalidation

## Component Interface (`Component.cs`)

```csharp
abstract class Component
{
    Node Node1, Node2;           // Connected nodes
    int MatrixIndex = -1;        // For auxiliary equations (voltage sources, transformers)

    virtual bool HasExtraEquation => false;      // Needs auxiliary current variable
    virtual bool RequiresIteration => false;      // Nonlinear (diode)
    virtual bool RequiresPerStepRestamp => false; // Time-dependent (capacitor, inductor)

    abstract void Stamp(CoordinateStorage<double> A, double[] z, double dt = 0);
    virtual void UpdateOperatingPoint(double[] x) { }  // Newton-Raphson update
    virtual void UpdateState(double[] x, double dt) { } // End-of-step state save
}
```

## Component Stamps

### Resistor (conductance G = 1/R)
```
A[n1,n1] += G    A[n1,n2] -= G
A[n2,n1] -= G    A[n2,n2] += G
```

### Voltage Source (auxiliary current I at index k)
```
A[n+,k] += 1     A[n-,k] -= 1
A[k,n+] += 1     A[k,n-] -= 1
z[k] = V
```
Current flows from n+ to n- internally.

### Current Source
```
z[n_in]  -= I
z[n_out] += I
```

### Capacitor (Backward Euler companion model)
```
G_eq = C / dt
I_eq = G_eq * V_prev
```
Stamps as resistor (G_eq) + current source (I_eq).

### Inductor (Backward Euler companion model)
```
G_eq = dt / L
I_eq = I_prev
```
Stamps as resistor (G_eq) + current source (-I_eq). Note sign flip.

### Diode (Newton-Raphson linearization)
```
I = Is * (exp(Vd/Vt) - 1)
G_eq = dI/dV = Is/Vt * exp(Vd/Vt)
I_eq = I(Vd) - G_eq * Vd
```
Stamps as resistor (G_eq) + current source (I_eq). Requires iteration.

### Transformer (ideal, ratio n = Ns/Np)
```
Primary constraint: V_p1 - V_p2 - (V_s1 - V_s2)/n = 0
Current relation: I_s = -I_p/n
```
Uses auxiliary row k for constraint equation.

## Dependencies

- **CSparse** (NuGet): Sparse matrix storage and LU factorization
- Uses `CoordinateStorage<double>` for matrix assembly (handles duplicate entries by summing)
- Uses `SparseLU` for factorization with natural column ordering

## Thread Safety

The `Circuit` class is **not thread-safe**. All calls must be from a single thread. The API layer (`SimulationManager`) handles parallel partition solving.
