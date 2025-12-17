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

### VCVS (Voltage-Controlled Voltage Source)
*   **Gain**: $k$ (dimensionless)
*   **Equation**: $V_{out} = k \cdot V_{in}$
*   **Matrix Stamp** (Auxiliary Row $k$ for output current):
    *   $A[k, outP] += 1$, $A[k, outN] -= 1$ (Output voltage)
    *   $A[k, ctrlP] -= k$, $A[k, ctrlN] += k$ (Control voltage scaled)
    *   $A[outP, k] += 1$, $A[outN, k] -= 1$ (Output current KCL)
    *   $z[k] = 0$

### VCCS (Voltage-Controlled Current Source)
*   **Transconductance**: $g_m$ (A/V)
*   **Equation**: $I_{out} = g_m \cdot V_{in}$
*   **Matrix Stamp** (No auxiliary row, cross-conductance):
    *   $A[outP, ctrlP] -= g_m$, $A[outP, ctrlN] += g_m$
    *   $A[outN, ctrlP] += g_m$, $A[outN, ctrlN] -= g_m$

### CCCS (Current-Controlled Current Source)
*   **Gain**: $\beta$ (dimensionless)
*   **Equation**: $I_{out} = \beta \cdot I_{in}$ (input is short-circuited)
*   **Matrix Stamp** (Auxiliary Row $k$ for sensed current):
    *   $A[k, ctrlP] += 1$, $A[k, ctrlN] -= 1$ (Zero-voltage constraint)
    *   $A[ctrlP, k] += 1$, $A[ctrlN, k] -= 1$ (Input current KCL)
    *   $A[outP, k] -= \beta$, $A[outN, k] += \beta$ (Output current)
    *   $z[k] = 0$

### CCVS (Current-Controlled Voltage Source)
*   **Transresistance**: $r_m$ (V/A)
*   **Equation**: $V_{out} = r_m \cdot I_{in}$ (input is short-circuited)
*   **Matrix Stamp** (Two Auxiliary Rows $k_1$, $k_2$):
    *   Row $k_1$ (current sensing):
        *   $A[k_1, ctrlP] += 1$, $A[k_1, ctrlN] -= 1$
        *   $A[ctrlP, k_1] += 1$, $A[ctrlN, k_1] -= 1$
        *   $z[k_1] = 0$
    *   Row $k_2$ (output voltage constraint):
        *   $A[k_2, outP] += 1$, $A[k_2, outN] -= 1$
        *   $A[k_2, k_1] -= r_m$
        *   $A[outP, k_2] += 1$, $A[outN, k_2] -= 1$
        *   $z[k_2] = 0$

## Architecture

### Classes
*   **`Circuit`**: Manages Nodes, Components, and the Solve loop.
    *   `BuildSystem()`: Allocates matrix indices and builds the initial sparse `CoordinateStorage` (duplicate stamps accumulate).
    *   `Solve(dt)`: Performs Newton-Raphson loop and time stepping.
    *   Dense vs sparse: small or dense systems (<= 96 unknowns or density >= 0.18) take a dense LU path; otherwise convert to CSC and use CSparse `SparseLU`.
    *   Caching: static linear circuits reuse the compressed matrix and LU across solves; cache is cleared when any component requests per-step restamp or iteration.
    *   Conditioning helpers: pin ground (row/col 0 = 1) and add tiny `gmin` shunts on every node.
*   **`Node`**: Represents a circuit node (holds Voltage).
*   **`Component`**: Abstract base class.
    *   `Stamp(A, z, dt)`: Adds contribution to matrix.
    *   `UpdateOperatingPoint(x)`: Updates internal state for Newton-Raphson (e.g., Diode $V_d$).
    *   `UpdateState(x, dt)`: Updates history for transient analysis (e.g., Capacitor voltage).

### Solve Loop
1.  **Build System**: Assign matrix indices for Voltage Sources/Inductors.
2.  **Newton-Raphson Loop**:
    a.  Clear $A$ and $z$, pin ground, apply `gmin`.
    b.  **Stamp**: All components write to $A$ and $z$ (per-step restamp for sources, transient elements).
    c.  **Solve**: $x = A^{-1}z$ (dense or sparse path as above).
    d.  **Update Operating Point**: Non-linear components update guesses.
    e.  Check convergence (scaled infinity norms of step and residual).
3.  **Update State**: Components update history (Capacitor voltage, Inductor current) for next time step.
