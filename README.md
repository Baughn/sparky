# Sparky Circuit Simulator

A real-time electrical circuit simulator using Modified Nodal Analysis (MNA). Designed for integration into Vintage Story as a mod, but the core solver is standalone.

## Component Library

| Component | Symbol | Parameters | Description |
|-----------|--------|------------|-------------|
| **Resistor** | R | resistance (Ω) | Ohm's law: V = IR |
| **Capacitor** | C | capacitance (F) | Stores charge, blocks DC: I = C(dV/dt) |
| **Inductor** | L | inductance (H) | Stores energy in magnetic field: V = L(dI/dt) |
| **Voltage Source** | V | voltage (V) | Maintains constant voltage between terminals |
| **Current Source** | I | current (A) | Maintains constant current through terminals |
| **Diode** | D | — | Nonlinear; conducts in one direction (~0.7V drop) |
| **Switch** | SW | state (open/closed) | High resistance when open, near-zero when closed |
| **Transformer** | XFMR | turns ratio | Couples two inductors: V₂/V₁ = N₂/N₁ |
| **VCVS** | E | voltage gain | Voltage-Controlled Voltage Source |
| **VCCS** | G | transconductance (S) | Voltage-Controlled Current Source |
| **CCVS** | H | transresistance (Ω) | Current-Controlled Voltage Source |
| **CCCS** | F | current gain | Current-Controlled Current Source |

## Solver Features

- **DC Analysis**: Solves steady-state circuits
- **Transient Analysis**: Time-stepping with Backward Euler integration
- **Nonlinear Solving**: Newton-Raphson iteration for diodes
- **Graph Partitioning**: Disconnected sub-circuits solve in parallel
- **Sparse/Dense Selection**: Automatic algorithm selection based on matrix structure
- **Component Limits**: Per-component thresholds with event callbacks (overcurrent, overvoltage, overpower)

## Test Helper: CircuitBuilder

Fluent API for building circuits with named nodes:

```csharp
// Before: 8+ lines of boilerplate
var nSrc = sim.CreateNode();
var nMid = sim.CreateNode();
sim.AddVoltageSource(nSrc, sim.Ground, 10.0);
sim.AddResistor(nSrc, nMid, 100.0);
sim.AddResistor(nMid, sim.Ground, 100.0);
sim.Step(0.001);
Assert.That(sim.GetVoltage(nMid), Is.EqualTo(5.0).Within(1e-6));

// After: 2 lines
var c = CircuitPatterns.VoltageDivider(10.0, 100.0, 100.0).Step();
Assert.That(c.V("mid"), Is.EqualTo(5.0).Within(Tolerances.Voltage));
```

### Pre-built Patterns

| Pattern | Creates | Node Names |
|---------|---------|------------|
| `VoltageDivider(V, R1, R2)` | V — R1 — mid — R2 — GND | src, mid |
| `RCCircuit(V, R, C)` | V — R — C — GND | src, cap |
| `RLCircuit(V, R, L)` | V — R — L — GND | src, ind |
| `SeriesRLC(V, R, L, C)` | V — R — L — C — GND | src, r_out, l_out |
| `ResistiveLoad(V, R)` | V — R — GND | src |
| `CurrentSourceWithLoad(I, R)` | I — R — GND | load |

### Custom Circuits

```csharp
var c = new CircuitBuilder()
    .VoltageSource(12.0, "vcc")
    .Resistor(1000.0, "vcc", "base")
    .Diode("base", "GND")           // cathode defaults to GND
    .Step();

var swId = c.Switch("vcc", "load", closed: true);
c.Resistor(100.0, "load", "GND").Step();

c.Sim.SetSwitchState(swId, false);  // Open the switch
c.Step();
```

## Quick Stats

| Metric | Value |
|--------|-------|
| Components | 12 types |
| Test coverage | 300 tests |
| Lines of solver code | ~2,500 |
| Lines of test code | ~4,000 |

## Architecture

```
┌─────────────────────────────────────────────────┐
│                   Game Layer                     │
│        (Vintage Story block entities)            │
└─────────────────────┬───────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────┐
│              API Layer (MNA/Api/)                │
│  SimulationManager, strongly-typed IDs,          │
│  graph partitioning, line optimization           │
└─────────────────────┬───────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────┐
│             Core Layer (MNA/Core/)               │
│  Circuit, Component, sparse/dense solvers,       │
│  Newton-Raphson, Backward Euler                  │
└─────────────────────────────────────────────────┘
```

## Getting Started

### Linux/macOS

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run benchmarks
./benchmark.sh run
```

### Windows 11

To build and run:

1. Install Vintage Story from the website.
2. Launch Vintage Story and confirm that it is logged in and starts properly.
3. Install the .net 8 SDK from Microsoft.
4. Run `start.ps1` to launch the game with the mod enabled.

#### Troubleshooting

**Note: If the build fails with the following error:**

```
Could not execute because the application was not found or a compatible .NET SDK is not installed.
Possible reasons for this include:
  * You intended to execute a .NET program:
      The application 'build' does not exist.
  * You intended to execute a .NET SDK command:
      It was not possible to find any installed .NET SDKs.
      Install a .NET SDK from:
        https://aka.ms/dotnet-download
```

but you know you installed the (64 bit) SDK, you will need to do the following:

1. Open the Start menu and search for "Environment Variables".
2. Select "Edit the system environment variables".
3. Select "Environment Variables..." from the System Properties dialog.
4. Find the `Path` variable underneath System Variables (not User Variables).
5. Move the 64 bit dotnet path (`C:\Program Files\dotnet\`) above the x86 path (`C:\Program Files (x86)\dotnet\`).
6. Close and reopen the command prompt that you are using (important!)
7. Run `dotnet --list-sdks` and verify that the 64 bit path is listed (see below for example)`

```
PS C:\Users\jared\Projects\sparky> dotnet --list-sdks
8.0.416 [C:\Program Files\dotnet\sdk]
PS C:\Users\jared\Projects\sparky> 
```
