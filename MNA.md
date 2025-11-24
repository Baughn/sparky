# MNA Solver Design Reference

This document serves as a persistent knowledge base for the Modified Nodal Analysis (MNA) solver implemented in Sparky.

## Core Concepts

### Modified Nodal Analysis (MNA)
The solver uses MNA to linearize the circuit into a system of linear equations:
$$Ax = z$$

*   **$A$ (System Matrix)**: Contains conductances ($G = 1/R$) and connection constraints. Size $N \times N$.
*   **$x$ (Unknowns Vector)**: Contains Node Voltages ($V$) and auxiliary currents ($I$) for voltage sources/inductors.
*   **$z$ (RHS Vector)**: Contains known quantities like Current Sources ($I$) and Voltage Source values ($V$).

### Time Stepping (Transient Analysis)
We use **Backward Euler** integration for its stability in game environments (L-stable).
*   **Time Step**: $dt$
*   **Capacitors/Inductors**: Discretized into "Companion Models" consisting of a resistor (conductance) and a current source.

### Non-Linear Solving
We use **Newton-Raphson** iteration to solve for non-linear components (Diodes).
*   Linearize component at operating point $V_{op}$.
*   Solve linear system.
*   Update $V_{op}$.
*   Repeat until convergence or max iterations.

## Component Stamps

### Resistor
*   **Resistance**: $R$
*   **Conductance**: $G = 1/R$
*   **Matrix Stamp**:
    *   $A[n1, n1] += G$
    *   $A[n2, n2] += G$
    *   $A[n1, n2] -= G$
    *   $A[n2, n1] -= G$

### Voltage Source
Adds an auxiliary variable $I_{branch}$ (current through source) at index $k$.
*   **Voltage**: $V$
*   **Matrix Stamp**:
    *   $A[n1, k] += 1$
    *   $A[n2, k] -= 1$
    *   $A[k, n1] += 1$
    *   $A[k, n2] -= 1$
    *   $z[k] = V$

### Current Source
*   **Current**: $I$ (flows $n1 \to n2$)
*   **RHS Stamp**:
    *   $z[n1] -= I$
    *   $z[n2] += I$

### Capacitor (Transient)
Modeled as conductance $G_{eq}$ in parallel with current source $I_{eq}$.
*   **Backward Euler**:
    *   $G_{eq} = C / dt$
    *   $I_{eq} = G_{eq} \cdot V_{prev}$
*   **Matrix Stamp**: Same as Resistor ($G_{eq}$).
*   **RHS Stamp**: Same as Current Source ($I_{eq}$).

### Inductor (Transient)
Modeled as conductance $G_{eq}$ in parallel with current source $I_{eq}$.
*   **Backward Euler**:
    *   $G_{eq} = dt / L$
    *   $I_{eq} = I_{prev}$
*   **Matrix Stamp**: Same as Resistor ($G_{eq}$).
*   **RHS Stamp**: Same as Current Source ($-I_{eq}$). *Note sign flip: Source opposes change.*

### Diode (Non-Linear)
Shockley Diode Equation linearized at $V_d$.
*   $I = I_s(e^{V_d/V_t} - 1)$
*   $G_{eq} = \frac{dI}{dV} = \frac{I_s}{V_t} e^{V_d/V_t}$
*   $I_{eq} = I(V_d) - G_{eq}V_d$
*   **Matrix Stamp**: Same as Resistor ($G_{eq}$).
*   **RHS Stamp**: Same as Current Source ($I_{eq}$).

### Transformer (Ideal)
*   **Ratio**: $n = N_s / N_p$
*   **Equations**:
    *   $V_p - \frac{1}{n} V_s = 0$
    *   $I_s = -\frac{1}{n} I_p$
*   **Matrix Stamp** (Auxiliary Row $k$):
    *   $A[k, n1] += 1$, $A[k, n2] -= 1$
    *   $A[k, n3] -= 1/n$, $A[k, n4] += 1/n$
    *   $A[n1, k] += 1$, $A[n2, k] -= 1$ (Primary Current)
    *   $A[n3, k] -= 1/n$, $A[n4, k] += 1/n$ (Secondary Current)

## Architecture

### Classes
*   **`Circuit`**: Manages Nodes, Components, and the Solve loop.
    *   `BuildSystem()`: Allocates matrix indices.
    *   `Solve(dt)`: Performs Newton-Raphson loop and time stepping.
*   **`Node`**: Represents a circuit node (holds Voltage).
*   **`Component`**: Abstract base class.
    *   `Stamp(A, z, dt)`: Adds contribution to matrix.
    *   `UpdateOperatingPoint(x)`: Updates internal state for Newton-Raphson (e.g., Diode $V_d$).
    *   `UpdateState(x, dt)`: Updates history for transient analysis (e.g., Capacitor voltage).

### Solve Loop
1.  **Build System**: Assign matrix indices for Voltage Sources/Inductors.
2.  **Newton-Raphson Loop**:
    a.  Clear $A$ and $z$.
    b.  **Stamp**: All components write to $A$ and $z$.
    c.  **Solve**: $x = A^{-1}z$.
    d.  **Update Operating Point**: Non-linear components update guesses.
    e.  Check convergence.
3.  **Update State**: Components update history (Capacitor voltage, Inductor current) for next time step.
