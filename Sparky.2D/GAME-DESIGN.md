# Circuit Spark: 2D Electricity Education Game

A grid-based puzzle/sandbox game that teaches electrical circuit fundamentals using the MNA solver.

## Educational Objectives

### Learning Progression

**Level 1: Fundamentals**
- Voltage as "electrical pressure" (water tank analogy)
- Current as "flow" (water flow analogy)
- Complete circuits: why circuits must be closed
- Ground as reference point

**Level 2: Ohm's Law**
- Resistance limits current flow
- V = IR relationship through experimentation
- Voltage dividers
- Series vs parallel resistance

**Level 3: Power & Energy**
- P = VI = I²R
- Power dissipation and heat
- Why wires have limits (introduces thermal domain)
- Energy storage concepts

**Level 4: Components**
- Diodes: one-way valves for electricity
- Capacitors: electrical "springs" / energy storage
- LEDs: diodes that produce light
- Switches: controlled breaks in circuit

**Level 5: Dynamics**
- Capacitor charging/discharging (RC circuits)
- Inductors: electrical "inertia"
- LC oscillation
- Transformers: trading voltage for current

**Level 6: Applications**
- Rectifiers (AC to DC)
- Voltage regulation
- Motors and generators (introduces kinetic domain)
- Power distribution

---

## Core Gameplay

### Voxel World

Each Vintage Story block contains a **16×16×16 voxel grid**. Circuit elements are placed at voxel resolution:

- **Conductor voxels** connect to all 6 adjacent conductor voxels (implicit connectivity)
- **4 parallel traces** require 8 voxels (4 conductor + 3 gap for isolation)
- **Wire crossings** are built as 3D structures - physical separation prevents shorts
- **Components** are multi-voxel structures (e.g., battery is ~14×14×8 voxels)

For the 2D tablet game, circuits are on a single Y-layer (horizontal slice), but the voxel model supports full 3D.

### Component Palette

All components are **multi-voxel structures** with terminal regions:

```
Basic:
  Wire           - conductor voxels (player places freely)
  Battery        - multi-voxel with + and - terminal faces
  Resistor       - multi-voxel with two terminal faces
  Switch         - toggleable connection between terminals
  Ground         - single terminal, forces node to 0V

Intermediate:
  Diode          - anode + insulator + cathode (3+ voxels)
  LED            - diode variant with light output
  Capacitor      - energy storage between terminals
  Lamp           - resistor variant with light/heat output

Advanced:
  Inductor       - opposes current change
  Transformer    - voltage conversion (coupled inductors)
  Motor          - electrical → rotational
  Generator      - rotational → electrical
```

**Component internals are abstracted** - we don't simulate individual voxels inside a battery. The MNA component connects between terminal regions.

### Cable Physics (Phase 3)

> **Note**: Material properties and prism coalescing are deferred to Phase 3.

Cable behavior emerges from voxel properties:
- **More metal = lower resistance** - a 4×4 cable has 1/16 the resistance of a 1×1 cable
- **Fuses** - use higher-resistance material (lead) at a specific point; it heats and evaporates first
- **Damage** - under sustained overload, conductor voxels evaporate randomly

Materials have resistivity (Ω per voxel-length):
```
Copper: low resistivity  → good for cables
Lead:   high resistivity → good for fuses
```

### Visual Feedback

**Current Flow Animation**
- Moving dots along wires showing electron flow direction
- Speed proportional to current magnitude
- Color indicates current level (blue→green→yellow→red)

**Voltage Coloring**
- Node color gradient from ground (black) to highest voltage (bright)
- Helps visualize potential difference

**Heat Visualization**
- Components glow red when dissipating significant power
- Smoke/failure when thermal limits exceeded

**Component States**
- LEDs: brightness proportional to forward current
- Lamps: color temperature based on power
- Switches: visual open/closed state
- Motors: rotation speed indicator
- Capacitors: charge level bar

### Interaction Modes

1. **Build Mode**: Place/remove/configure components
2. **Simulate Mode**: Circuit runs, can toggle switches
3. **Probe Mode**: Click to see V/I/P at any point
4. **Challenge Mode**: Meet objectives with constraints

---

## Game Modes

### Sandbox
- Unlimited components
- No objectives
- Full component palette
- Save/load circuits

### Challenges
Progressive puzzles with constraints:

```
Challenge 1-1: "First Light"
  Objective: Light the LED
  Given: Battery(5V), LED, wires
  Learn: Complete circuits

Challenge 2-3: "Voltage Divider"
  Objective: Create exactly 2.5V at probe point
  Given: Battery(10V), resistors, wires
  Constraint: Use exactly 2 resistors
  Learn: Voltage division

Challenge 3-2: "Power Budget"
  Objective: Light 3 LEDs without exceeding 1W total
  Given: Battery(9V), resistors, LEDs
  Constraint: Total power < 1W
  Learn: Power calculation, current limiting

Challenge 5-1: "Delay Circuit"
  Objective: LED turns on 2 seconds after switch closes
  Given: Battery, switch, capacitor, resistor, LED
  Learn: RC time constants
```

### Survival Mode
- Limited budget
- Must power increasingly demanding loads
- Components can fail from overload
- Introduces thermal management

---

## Technical Architecture

### Simulation Layer Stack

```
┌─────────────────────────────────────────────┐
│              Game Logic Layer               │
│  (Challenges, UI, Save/Load, Achievements)  │
├─────────────────────────────────────────────┤
│            Cell Manager Layer               │
│  (Grid→Simulation mapping, multi-domain     │
│   coordination, thermal coupling)           │
├─────────────────────────────────────────────┤
│           Domain Solvers Layer              │
│  ┌──────────┬──────────┬──────────────┐    │
│  │Electrical│ Thermal  │   Kinetic    │    │
│  │  (MNA)   │(Diffusion)│ (Rotational) │    │
│  └──────────┴──────────┴──────────────┘    │
├─────────────────────────────────────────────┤
│         Physics Abstraction Layer           │
│   (ISimulation, IDomain, ICoupledSolver)    │
└─────────────────────────────────────────────┘
```

### Cell Model (Game↔Simulation Bridge)

Each grid tile maps to a `Cell` that can participate in multiple domains:

```csharp
public interface IGameCell
{
    GridPos Position { get; }
    CellType Type { get; }
    int Rotation { get; }  // 0, 90, 180, 270 degrees

    // Domain participation
    IElectricalPorts? Electrical { get; }
    IThermalPorts? Thermal { get; }
    IKineticPorts? Kinetic { get; }

    // Cross-domain coupling
    void ApplyCoupling(float dt);

    // Visual state for rendering
    CellVisualState GetVisualState();
}

public interface IElectricalPorts
{
    // Which edges have electrical connections
    IEnumerable<(Direction dir, NodeId node)> GetPorts();
}

public interface IThermalPorts
{
    double Temperature { get; }
    double ThermalMass { get; }  // J/K
    double HeatInput { get; set; }  // W (from electrical dissipation)
    IEnumerable<(Direction dir, double conductance)> GetConductionPaths();
}
```

### Component→Cell Mapping Examples

**Wire**
```
Electrical: 2-4 ports depending on neighbors, single node
Thermal: Conducts heat along length, small dissipation from resistance
```

**Resistor**
```
Electrical: 2 ports (in/out), one node each end
Thermal: P=I²R fed into thermal mass, radiates to ambient
Coupling: T_high → R increases (positive tempco)
```

**LED**
```
Electrical: Diode between 2 ports
Thermal: Forward power → heat (minus light output ~10-20%)
Visual: Brightness = f(forward_current)
```

**Motor**
```
Electrical: Appears as back-EMF voltage source + resistance
Kinetic: Torque output port
Coupling: τ = k_t * I, V_bemf = k_e * ω
```

---

## MNA API Gaps & Proposed Extensions

### ~~Gap 1: Initial Conditions~~ ✓ IMPLEMENTED

**Status**: Implemented in `MNA/Api/`.

**API**:
```csharp
void SetCapacitorVoltage(CapacitorId id, double voltage);
void SetInductorCurrent(InductorId id, double current);
```

Sets the internal state of capacitors/inductors for initial conditions. Updates both logical and physical components, and state persists across topology rebuilds.

### ~~Gap 2: Component Events / Callbacks~~ ✓ IMPLEMENTED

**Status**: Implemented in `MNA/Api/`.

**API**:
```csharp
// Register for limit events
IDisposable OnLimitEvent(LimitEventHandler handler);

// Set limits per component (available for all component types)
void SetResistorLimit(ResistorId id, LimitKind kind, LimitConfig config);
void ClearResistorLimit(ResistorId id, LimitKind kind);
LimitConfig? GetResistorLimit(ResistorId id, LimitKind kind);
// ... similar for VoltageSource, CurrentSource, Capacitor, Inductor, Diode, etc.

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

Limits are checked after each `Step()`. Events fire when thresholds are exceeded. Use `TriggerOnce` to fire only on first violation.

### ~~Gap 3: Controlled Sources~~ ✓ IMPLEMENTED

**Status**: Implemented in `MNA/Core/` and `MNA/Api/`.

**Use case**: Op-amps, transistors (simplified), motor back-EMF.

**API**:
```csharp
// Voltage-Controlled Voltage Source: Vout = gain * Vin
VcvsId AddVCVS(NodeId ctrlP, NodeId ctrlN, NodeId outP, NodeId outN, double gain);

// Voltage-Controlled Current Source: Iout = transconductance * Vin
VccsId AddVCCS(NodeId ctrlP, NodeId ctrlN, NodeId outP, NodeId outN, double gm);

// Current-Controlled Voltage Source: Vout = transresistance * Iin
// Input terminals are shorted (zero voltage) to sense current
CcvsId AddCCVS(NodeId ctrlP, NodeId ctrlN, NodeId outP, NodeId outN, double rm);

// Current-Controlled Current Source: Iout = gain * Iin
// Input terminals are shorted (zero voltage) to sense current
CccsId AddCCCS(NodeId ctrlP, NodeId ctrlN, NodeId outP, NodeId outN, double gain);
```

All four types support Update/Remove/Exists/Get operations. See `MNA.md` for MNA stamp details.

### ~~Gap 4: Variable/Nonlinear Resistors~~ ✓ IMPLEMENTED

**Status**: Implemented in `MNA/Api/`.

**API**:
```csharp
ResistorId AddResistor(NodeId a, NodeId b, double resistance, bool isVariable = false);
```

Variable resistors (`isVariable: true`) have `IsOptimizable => false`, so they are excluded from line optimization. This ensures `UpdateResistor` always uses the fast-path without triggering topology rebuild. Use for thermistors, photoresistors, potentiometers, and any resistance that changes during simulation.

### ~~Gap 5: Time-Varying Sources~~ ✓ IMPLEMENTED (via utilities)

**Status**: Implemented as utility library in `MNA/Utilities/`. Core API additions: `SimulationTime` property and `ResetTime()` method.

**Design decision**: Rather than building AC/PWM into the solver, time-varying sources are implemented as wrapper classes that call `UpdateVoltageSource()`/`UpdateCurrentSource()` each tick. This matches how motor/generator cells will work (computing back-EMF from kinetic state).

**API** (utilities):
```csharp
// AC voltage: V(t) = Offset + Amplitude × sin(2πft + Phase)
var ac = new AcVoltageSource(sim, nodePos, nodeNeg,
    amplitude: 120.0, frequency: 60.0, phase: 0, offset: 0);

// PWM voltage: V = VHigh during duty cycle, VLow otherwise
var pwm = new PwmVoltageSource(sim, nodePos, nodeNeg,
    vHigh: 5.0, vLow: 0.0, frequency: 1000.0, dutyCycle: 0.5);

// Also: AcCurrentSource, SourceUpdater (batch helper)

// Usage pattern:
for (int i = 0; i < steps; i++)
{
    ac.Update();   // Sets voltage based on sim.SimulationTime
    sim.Step(dt);  // Advances time
}
```

**API** (core):
```csharp
double SimulationTime { get; }  // Cumulative time from Step() calls
void ResetTime();               // Reset to zero without clearing circuit
```

### ~~Gap 6: Energy Accounting~~ ✓ IMPLEMENTED

**Status**: Implemented in `MNA/Api/`.

**API**:
```csharp
// Query cumulative energy (Joules)
double GetVoltageSourceEnergy(VoltageSourceId id);  // +ve = delivering power
double GetCurrentSourceEnergy(CurrentSourceId id);  // +ve = delivering power
double GetResistorEnergy(ResistorId id);            // Always positive (dissipated)
double GetDiodeEnergy(DiodeId id);                  // Always positive (dissipated)
double GetCapacitorEnergy(CapacitorId id);          // +ve = charging, -ve = discharging
double GetInductorEnergy(InductorId id);            // +ve = storing, -ve = releasing

// Reset
void ResetEnergyCounters();              // Reset all to zero
void ResetEnergyCounter(ResistorId id);  // Reset specific component
// ... similar for each component type
```

Energy is accumulated per-component during `Step()`. Line-optimized resistor chains distribute energy by resistance ratio.

### ~~Gap 7: Switch Component~~ ✓ IMPLEMENTED

**Status**: Implemented in `MNA/Api/`.

**API**:
```csharp
SwitchId AddSwitch(NodeId a, NodeId b, bool initiallyClosed = false);
void SetSwitchState(SwitchId id, bool closed);
void ToggleSwitch(SwitchId id);
void RemoveSwitch(SwitchId id);
bool SwitchExists(SwitchId id);
bool GetSwitchState(SwitchId id);
double GetSwitchCurrent(SwitchId id);
```

**Implementation**: Switch wraps an internal resistor (1e-9 Ω closed, 1e9 Ω open). State changes use the resistor fast-path update when possible, avoiding topology rebuild. Future optimization: switches left open can be removed from topology to enable graph partitioning.

---

## Multi-Domain Architecture

### Domain Abstraction

```csharp
public enum SimDomain
{
    Electrical,
    Thermal,
    Kinetic,
    Fluid  // For steam/hydraulics
}

public interface IDomainSolver
{
    SimDomain Domain { get; }
    void Step(double dt);
    void Clear();
}
```

### Thermal Domain Solver

Heat diffusion between adjacent cells:

```csharp
public interface IThermalSolver : IDomainSolver
{
    ThermalNodeId CreateNode(double thermalMass, double initialTemp);
    void RemoveNode(ThermalNodeId id);

    // Conduction between nodes
    ThermalLinkId AddConductionPath(ThermalNodeId a, ThermalNodeId b,
        double thermalConductance);  // W/K

    // Heat sources (from electrical dissipation, combustion, etc.)
    void SetHeatInput(ThermalNodeId id, double power);  // Watts

    // Convection/radiation to ambient
    void SetAmbientCoupling(ThermalNodeId id, double coefficient);  // W/K

    // Readout
    double GetTemperature(ThermalNodeId id);
}
```

**Implementation**: Simple explicit Euler for heat equation:
```
dT/dt = (Q_in - Q_out) / C_thermal
Q_conduction = k * (T_neighbor - T) for each neighbor
Q_ambient = h * (T_ambient - T)
```

### Kinetic Domain Solver

Rotational mechanics for motors/generators:

```csharp
public interface IKineticSolver : IDomainSolver
{
    ShaftId CreateShaft(double momentOfInertia);
    void RemoveShaft(ShaftId id);

    // Mechanical coupling (gears, belts)
    GearLinkId AddGearLink(ShaftId a, ShaftId b, double ratio);

    // Torque input (from motor, hand crank, etc.)
    void SetTorqueInput(ShaftId id, double torque);  // N·m

    // Load (friction, useful work)
    void SetLoadTorque(ShaftId id, double torque);

    // Readout
    double GetAngularVelocity(ShaftId id);  // rad/s
    double GetAngle(ShaftId id);            // radians (for position display)
}
```

**Implementation**: Simple rotational dynamics:
```
dω/dt = (τ_in - τ_load - τ_friction) / J
dθ/dt = ω
```

### Cross-Domain Coupling

Coupling happens at the cell level, after all domain solvers run:

```csharp
public interface ICoupledCell
{
    // Called after electrical solve, before thermal solve
    void ElectricalToThermal(ISimulation elec, IThermalSolver thermal)
    {
        // Example: Resistor
        double power = elec.GetResistorPower(_resistorId);
        thermal.SetHeatInput(_thermalNodeId, power);
    }

    // Called after thermal solve, before next electrical solve
    void ThermalToElectrical(IThermalSolver thermal, ISimulation elec)
    {
        // Example: Thermistor
        double temp = thermal.GetTemperature(_thermalNodeId);
        double resistance = _r0 * Math.Exp(_beta * (1/temp - 1/_t0));
        elec.UpdateResistor(_resistorId, resistance);
    }

    // Called between electrical and kinetic
    void ElectricalToKinetic(ISimulation elec, IKineticSolver kinetic)
    {
        // Example: Motor
        double current = elec.GetVoltageSourceCurrent(_backEmfId);
        double torque = _torqueConstant * current;
        kinetic.SetTorqueInput(_shaftId, torque);
    }

    void KineticToElectrical(IKineticSolver kinetic, ISimulation elec)
    {
        // Example: Motor back-EMF / Generator
        double omega = kinetic.GetAngularVelocity(_shaftId);
        double backEmf = _emfConstant * omega;
        elec.UpdateVoltageSource(_backEmfId, backEmf);
    }
}
```

### Simulation Tick Order

```
1. Pre-tick: Read UI state (switch toggles, dial changes)
2. Electrical solve: sim.Step(dt)
3. Coupling: Electrical → Thermal (P = I²R)
4. Thermal solve: thermal.Step(dt)
5. Coupling: Thermal → Electrical (temperature-dependent R)
6. Coupling: Electrical → Kinetic (motor torque)
7. Kinetic solve: kinetic.Step(dt)
8. Coupling: Kinetic → Electrical (back-EMF)
9. Post-tick: Update visuals, check events
```

For tightly coupled systems (motor-generator), may need iteration:
```
repeat until converged or max_iters:
    solve_electrical()
    update_motor_torque()
    solve_kinetic()
    update_back_emf()
```

---

## Fluid/Steam Domain (Future)

For boilers, steam engines, hydraulics:

```csharp
public interface IFluidSolver : IDomainSolver
{
    // Pressure nodes (tanks, pipes, chambers)
    PressureNodeId CreatePressureNode(double volume, double initialPressure);

    // Flow paths (pipes, valves, orifices)
    FlowPathId AddFlowPath(PressureNodeId a, PressureNodeId b,
        double flowCoefficient);

    // Phase change (boiling/condensing)
    void SetHeatInput(PressureNodeId id, double power);

    // Mechanical output (piston, turbine)
    void SetMechanicalWork(PressureNodeId id, double power);

    // Readout
    double GetPressure(PressureNodeId id);
    double GetTemperature(PressureNodeId id);
    double GetMassFlow(FlowPathId id);
}
```

**Coupling examples:**
- Boiler: Thermal heat input → steam pressure
- Steam turbine: Pressure drop → shaft torque
- Steam engine: Pressure × volume change → work output
- Generator: Shaft rotation → electrical power

---

## File Structure

```
Sparky/
├── Game/
│   ├── Core/
│   │   ├── Grid.cs              # 2D tile grid
│   │   ├── Cell.cs              # Base cell class
│   │   ├── CellTypes/           # Component implementations
│   │   │   ├── WireCell.cs
│   │   │   ├── ResistorCell.cs
│   │   │   ├── BatteryCell.cs
│   │   │   ├── SwitchCell.cs
│   │   │   ├── DiodeCell.cs
│   │   │   ├── CapacitorCell.cs
│   │   │   ├── MotorCell.cs
│   │   │   └── ...
│   │   └── CellVisualState.cs   # Rendering data
│   ├── Simulation/
│   │   ├── GameSimulation.cs    # Orchestrates all domains
│   │   ├── ThermalSolver.cs     # Heat diffusion
│   │   ├── KineticSolver.cs     # Rotational mechanics
│   │   └── Coupling/
│   │       ├── ElectricalThermal.cs
│   │       └── ElectricalKinetic.cs
│   ├── Challenges/
│   │   ├── ChallengeDefinition.cs
│   │   ├── ChallengeLoader.cs
│   │   └── Levels/              # Challenge data files
│   └── UI/
│       ├── GridRenderer.cs
│       ├── ComponentPalette.cs
│       ├── ProbeDisplay.cs
│       └── ChallengeUI.cs
├── MNA/                          # Existing
│   ├── Api/
│   └── Core/
└── Sparky.Tests/
    ├── Game/
    │   ├── GridTests.cs
    │   ├── CellTests.cs
    │   └── ThermalSolverTests.cs
    └── MNA/                      # Existing
```

---

## Client-Server Architecture

The game will eventually run on a tablet inside Vintage Story, serving as both an educational game and player handbook. This requires a client-server architecture to handle potentially high latency (up to 300ms round-trip).

### Design Principles

- **Dumb client**: The client knows nothing about circuits. It renders cells with visual states and forwards input events. All simulation and game logic stays server-side.
- **Smart primitives**: To minimize apparent latency, the client handles text input, numeric fields, and interactive widgets locally (X11-style). The server sends high-level commands, not pixel data.
- **No compatibility concerns**: For standalone, both run in-process. For VS, mod version mismatch prevents connection anyway.

### Vintage Story Rendering

VS provides two hooks for custom GUI rendering:

1. **`GuiElementCustomDraw`**: Cairo-based drawing to an `ImageSurface`, uploaded as a texture. Has `Redraw()` for dynamic updates.
2. **`GuiElementCustomRender`**: Direct per-frame callback with `deltaTime` for animations.

The client will use `GuiElementCustomDraw` for the grid/components (redraw on state change) and `GuiElementCustomRender` for animations (current flow particles).

### Protocol

No wire format needed for standalone (direct method calls). For VS integration, serialize over VS's network channel.

```csharp
// Server → Client: Render commands
abstract record RenderCommand;
record SetCell(GridPos Pos, CellType Type, int Rotation, CellVisualState State) : RenderCommand;
record ClearCell(GridPos Pos) : RenderCommand;
record SetGridSize(int Width, int Height) : RenderCommand;
record SetSimulationState(bool Running, double Time) : RenderCommand;
record ShowProbe(GridPos Pos, double Voltage, double Current, double Power) : RenderCommand;
record SetCurrentFlow(GridPos From, GridPos To, double Magnitude) : RenderCommand;

// Client → Server: Input events
abstract record InputEvent;
record PlaceComponent(GridPos Pos, CellType Type, int Rotation) : InputEvent;
record RemoveComponent(GridPos Pos) : InputEvent;
record ToggleSwitch(GridPos Pos) : InputEvent;
record SetComponentValue(GridPos Pos, string Param, double Value) : InputEvent;
record ProbeRequest(GridPos Pos) : InputEvent;
record SetSimSpeed(double Multiplier) : InputEvent;
record TogglePause() : InputEvent;
```

### Latency Handling

- **Optimistic updates**: Client immediately shows placed/removed components, server confirms or corrects.
- **Animation interpolation**: Current flow animations run client-side based on server-provided magnitude; don't wait for per-frame updates.
- **Input batching**: Rapid edits (dragging wire) batch into single server round-trip.

### Implementation Layers

```
┌─────────────────────────────────────────────────────────────┐
│                    IGameClient (interface)                  │
│  Methods: HandleCommand(RenderCommand), GetPendingInput()   │
├─────────────────────────────────────────────────────────────┤
│  StandaloneClient          │  VintageStoryClient            │
│  (renders to window)       │  (renders to GuiElement)       │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                    IGameServer (interface)                  │
│  Methods: HandleInput(InputEvent), Tick(dt), GetCommands()  │
├─────────────────────────────────────────────────────────────┤
│                      GameServer                             │
│  (grid state, simulation orchestration, challenge logic)    │
└─────────────────────────────────────────────────────────────┘
```

For standalone: `StandaloneClient` and `GameServer` communicate via direct method calls.
For VS: `VintageStoryClient` serializes to network channel; server runs on VS server.

## Implementation Phases

### Phase 1: Core Game Loop
1. Implement Grid and basic Cell types (Wire, Battery, Resistor, Ground)
2. Grid↔MNA mapping (node creation, component wiring)
3. Basic rendering with voltage coloring
4. Build mode: place/remove components

### Phase 2: Interactivity
1. Add Switch component (API extension needed)
2. Add Diode and LED cells
3. Current flow animation
4. Probe mode for inspecting values

### Phase 3: Dynamics
1. Add Capacitor with charge visualization
2. Add Inductor
3. Time controls (pause, speed, step)
4. RC/LC circuit challenges

### Phase 4: Thermal Domain
1. Implement ThermalSolver
2. Add thermal coupling to Resistor/LED
3. Heat visualization (glow effects)
4. Power limit challenges

### Phase 5: Kinetic Domain
1. Implement KineticSolver
2. Add Motor and Generator cells
3. Electromechanical coupling
4. Motor control challenges

### Phase 6: Polish
1. Full challenge system
2. Save/load circuits
3. Component failure modes
4. Sound effects
5. Tutorial flow

---

## Success Metrics

**Educational Effectiveness:**
- Players can predict circuit behavior before simulating
- Players can debug why a circuit doesn't work
- Players understand V/I/R/P relationships intuitively

**Engagement:**
- Average session length > 15 minutes
- Challenge completion rate > 60%
- Sandbox mode usage indicates creative exploration

**Technical:**
- 60 FPS with 100+ components
- Accurate simulation (matches SPICE within 1%)
- Stable under rapid editing
