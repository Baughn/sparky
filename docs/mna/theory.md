# MNA Solver Theory

The MNA (Modified Nodal Analysis) solver converts circuits into systems of linear equations and solves them using direct matrix methods. It supports DC analysis, transient simulation with reactive components (capacitors and inductors), and nonlinear components (diodes) via Newton-Raphson iteration.

## Key Files

```
src/mna/solver/
├── Circuit.cs        # Main solver: matrix assembly, solve loop, LU factorization
├── Component.cs      # Abstract base class for all circuit elements
├── Node.cs           # Circuit node with ID and voltage
├── Resistor.cs       # Ohmic resistance
├── VoltageSource.cs  # Ideal voltage source (adds auxiliary equation)
├── CurrentSource.cs  # Ideal current source
├── Capacitor.cs      # Transient: Backward Euler companion model
├── Inductor.cs       # Transient: Backward Euler companion model
├── Diode.cs          # Nonlinear: Shockley equation with Newton-Raphson
├── Transformer.cs    # Ideal transformer (coupled inductors)
├── VCVS.cs           # Voltage-controlled voltage source
├── VCCS.cs           # Voltage-controlled current source
├── CCCS.cs           # Current-controlled current source
├── CCVS.cs           # Current-controlled voltage source
```

## Matrix Formulation

MNA formulates circuit equations as a linear system:

```
A x = z
```

Where:
- **A** is the system matrix containing conductances and constraint coefficients
- **x** is the solution vector containing node voltages and branch currents
- **z** is the right-hand side vector containing source values

The matrix dimension is `n + m` where `n` is the number of nodes and `m` is the number of auxiliary equations (one per voltage source, inductor, transformer, etc.).

### Ground Node

Node 0 is always ground. The solver anchors ground by setting `A[0,0] = 1` and `z[0] = 0`, which forces `V_0 = 0`.

### Conditioning

A tiny shunt conductance `gmin = 1e-12` is added to every non-ground node's diagonal entry to prevent singular matrices from floating nodes.

## Component Stamps

Each component contributes entries to the matrix A and/or vector z. These contributions are called "stamps."

### Resistor

A resistor with conductance `G = 1/R` between nodes n1 and n2:

```
A[n1, n1] += G
A[n2, n2] += G
A[n1, n2] -= G
A[n2, n1] -= G
```

### Voltage Source

A voltage source requires an auxiliary variable for its branch current. If the source voltage is V and the auxiliary index is k:

```
A[n1, k] += 1
A[n2, k] -= 1
A[k, n1] += 1
A[k, n2] -= 1
z[k] = V
```

The auxiliary equation enforces `V_n1 - V_n2 = V`, and the solution `x[k]` gives the current through the source.

### Current Source

A current source I flowing from n1 to n2 only affects the RHS:

```
z[n1] -= I
z[n2] += I
```

### Transformer

An ideal transformer with turns ratio `n = N_secondary / N_primary` connecting primary nodes (n1, n2) to secondary nodes (n3, n4) uses one auxiliary equation at index k:

```
Constraint: V_primary = (1/n) * V_secondary
A[k, n1] += 1
A[k, n2] -= 1
A[k, n3] -= 1/n
A[k, n4] += 1/n

KCL (primary current):
A[n1, k] += 1
A[n2, k] -= 1

KCL (secondary current = -primary/n):
A[n3, k] -= 1/n
A[n4, k] += 1/n
```

### Controlled Sources

**VCVS** (Voltage-Controlled Voltage Source): One auxiliary equation for output current. Gain is dimensionless.

```
Constraint: V_out = Gain * V_ctrl
A[k, outP] += 1
A[k, outN] -= 1
A[k, ctrlP] -= Gain
A[k, ctrlN] += Gain
A[outP, k] += 1
A[outN, k] -= 1
z[k] = 0
```

**VCCS** (Voltage-Controlled Current Source): No auxiliary equation; uses cross-conductance terms. Transconductance `gm` has units A/V.

```
A[outP, ctrlP] -= gm
A[outP, ctrlN] += gm
A[outN, ctrlP] += gm
A[outN, ctrlN] -= gm
```

**CCCS** (Current-Controlled Current Source): One auxiliary equation to sense input current (zero-voltage short circuit at input). Gain is dimensionless.

```
Current sensing (V_ctrl = 0):
A[k, ctrlP] += 1
A[k, ctrlN] -= 1
A[ctrlP, k] += 1
A[ctrlN, k] -= 1

Output current = Gain * x[k]:
A[outP, k] -= Gain
A[outN, k] += Gain
z[k] = 0
```

**CCVS** (Current-Controlled Voltage Source): Two auxiliary equations (k1 for current sensing, k2 for output voltage). Transresistance `rm` has units V/A.

```
Current sensing (row k1):
A[k1, ctrlP] += 1
A[k1, ctrlN] -= 1
A[ctrlP, k1] += 1
A[ctrlN, k1] -= 1

Output voltage (row k2): V_out = rm * x[k1]
A[k2, outP] += 1
A[k2, outN] -= 1
A[k2, k1] -= rm
A[outP, k2] += 1
A[outN, k2] -= 1
z[k1] = z[k2] = 0
```

## Time Stepping (Transient Analysis)

The solver uses Backward Euler integration for capacitors and inductors. This method is L-stable, meaning it handles stiff circuits well and never exhibits numerical oscillation.

### Capacitor Companion Model

For a capacitor C with time step dt, the Backward Euler discretization yields:

```
I_n = C * (V_n - V_{n-1}) / dt
```

This is modeled as a conductance in parallel with a current source:

```
G_eq = C / dt
I_eq = G_eq * V_{n-1}
```

The capacitor stamps like a resistor (G_eq) plus a current source (I_eq):

```
Matrix: same as resistor with G_eq
RHS: z[n1] += I_eq, z[n2] -= I_eq
```

After solving, `UpdateState()` saves the new voltage for the next time step.

### Inductor Companion Model

For an inductor L with time step dt:

```
V_n = L * (I_n - I_{n-1}) / dt
```

Rearranged for Backward Euler:

```
G_eq = dt / L
I_n = G_eq * V_n + I_{n-1}
```

The inductor stamps as a conductance G_eq with a current source representing previous current:

```
Matrix: same as resistor with G_eq
RHS: z[n1] -= I_eq, z[n2] += I_eq  (note opposite sign from capacitor)
```

For DC analysis (dt = 0), inductors are modeled as very small resistances (1e-9 ohms) to approximate a short circuit without causing singularity.

## Nonlinear Solving (Newton-Raphson)

Nonlinear components like diodes cannot be directly stamped because their current depends nonlinearly on voltage. The solver uses Newton-Raphson iteration to linearize around an operating point.

### Diode Model

The Shockley diode equation:

```
I = I_s * (exp(V_d / (n * V_t)) - 1)
```

Where:
- `I_s = 1e-12 A` (saturation current)
- `V_t = 0.026 V` (thermal voltage at room temperature)
- `n = 1.0` (emission coefficient)

At each iteration, the diode is linearized around its current operating voltage V_d:

```
G_eq = dI/dV = (I_s / (n * V_t)) * exp(V_d / (n * V_t))
I_eq = I(V_d) - G_eq * V_d
```

The diode stamps as a conductance G_eq with a current source I_eq. Voltage limiting (clamped to -5V to 0.9V) prevents exponential overflow and aids convergence.

### Iteration Loop

The Newton-Raphson solve loop:

1. Clear matrix A and vector z
2. Apply ground anchor and gmin conditioning
3. All components stamp into A and z
4. Solve the linear system: x = A^(-1) z
5. Update operating points for nonlinear components (`UpdateOperatingPoint()`)
6. Check convergence: both step norm and residual norm must be below tolerance
7. Repeat until converged or max iterations (default 50) exceeded

Convergence uses scaled infinity norms:

```
stepTol = tolerance * (1 + ||x||_inf)
residualTol = tolerance * (1 + ||z||_inf)
```

Default tolerance is 1e-6.

## Solver Selection

The solver automatically chooses between dense and sparse linear algebra:

- **Dense LU** (in-place with partial pivoting): Used when matrix size <= 96 or density >= 18%
- **Sparse LU** (CSparse library): Used for larger sparse matrices

For static linear circuits (no nonlinear components, no transient elements), the LU factorization is cached and reused across solve calls.

## Solve Loop Summary

1. **BuildSystem()**: Assigns matrix indices for components with auxiliary equations
2. **Newton-Raphson iteration** (or single pass for linear circuits):
   - Clear and rebuild A and z
   - Stamp all components
   - Solve linear system
   - Update operating points
   - Check convergence
3. **UpdateState()**: Save component history (capacitor voltage, inductor current) for next time step
4. **AccumulateEnergy()**: Compute power and energy for each component
