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

### Grid World
- 2D tile-based world (16x16 minimum, expandable)
- Each tile can hold one component or wire segment
- Wires auto-connect to adjacent compatible tiles
- Components snap to grid with rotation support

### Component Palette
```
Basic:
  [─] Wire          - connects adjacent tiles
  [+] Battery       - voltage source (configurable V)
  [█] Resistor      - resistance (configurable Ω)
  [/] Switch        - toggleable open/closed
  [⏚] Ground        - reference point

Intermediate:
  [▷] Diode         - one-way current flow
  [◉] LED           - diode + light output
  [║] Capacitor     - energy storage
  [●] Lamp          - resistor + light/heat output

Advanced:
  [⌇] Inductor      - opposes current change
  [⊠] Transformer   - voltage conversion
  [◎] Motor         - electrical → rotational
  [⊛] Generator     - rotational → electrical
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

### Gap 2: Component Events / Callbacks

**Problem**: No way to know when component exceeds limits.

**Use case**: Fuse blowing, LED burning out, overcurrent detection.

**Proposed API**:
```csharp
event Action<ComponentEvent> OnComponentEvent;

public enum ComponentEventType
{
    OverCurrent,    // I > I_max
    OverVoltage,    // V > V_max
    OverPower,      // P > P_max
    ThermalLimit,   // T > T_max (requires thermal coupling)
    Breakdown       // Reverse voltage exceeded (diode)
}

// Per-component limits
void SetResistorLimits(ResistorId id, ComponentLimits limits);

public record ComponentLimits(
    double? MaxCurrent = null,
    double? MaxVoltage = null,
    double? MaxPower = null
);
```

### Gap 3: Controlled Sources

**Problem**: Can't model components where V or I depends on another circuit quantity.

**Use case**: Op-amps, transistors (simplified), motor back-EMF.

**Proposed API**:
```csharp
// Voltage-Controlled Voltage Source: Vout = gain * Vin
VcvsId AddVCVS(NodeId inP, NodeId inN, NodeId outP, NodeId outN, double gain);

// Voltage-Controlled Current Source: Iout = transconductance * Vin
VccsId AddVCCS(NodeId inP, NodeId inN, NodeId outP, NodeId outN, double gm);

// Current-Controlled Voltage Source: Vout = transresistance * Iin
CcvsId AddCCVS(NodeId inP, NodeId inN, NodeId outP, NodeId outN, double rm);

// Current-Controlled Current Source: Iout = gain * Iin
CccsId AddCCCS(NodeId inP, NodeId inN, NodeId outP, NodeId outN, double beta);
```

### ~~Gap 4: Variable/Nonlinear Resistors~~ ✓ IMPLEMENTED

**Status**: Implemented in `MNA/Api/`.

**API**:
```csharp
ResistorId AddResistor(NodeId a, NodeId b, double resistance, bool isVariable = false);
```

Variable resistors (`isVariable: true`) have `IsOptimizable => false`, so they are excluded from line optimization. This ensures `UpdateResistor` always uses the fast-path without triggering topology rebuild. Use for thermistors, photoresistors, potentiometers, and any resistance that changes during simulation.

### Gap 5: Time-Varying Sources

**Problem**: No built-in AC sources, PWM, or waveform generators.

**Use case**: AC circuits, switching power supplies, signal sources.

**Proposed API**:
```csharp
// AC voltage source: V(t) = amplitude * sin(2π * frequency * t + phase)
AcSourceId AddAcVoltageSource(NodeId pos, NodeId neg,
    double amplitude, double frequency, double phase = 0);

// PWM source: V = V_high when (t % period) < duty * period, else V_low
PwmSourceId AddPwmVoltageSource(NodeId pos, NodeId neg,
    double vHigh, double vLow, double frequency, double duty);

// Simulation needs internal time tracking:
double SimulationTime { get; }
void ResetTime();
```

### Gap 6: Energy Accounting

**Problem**: Can't easily track total energy delivered/consumed.

**Use case**: Battery capacity, energy efficiency calculations.

**Proposed API**:
```csharp
double GetVoltageSourceEnergy(VoltageSourceId id);  // Joules delivered
double GetResistorEnergy(ResistorId id);            // Joules dissipated
void ResetEnergyCounters();
```

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
