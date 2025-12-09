# Multi-Domain Simulation Architecture

This document extends the Cell.md concept to support multiple coupled physical domains: electrical, thermal, kinetic (mechanical), and fluid (steam/hydraulic).

## Overview

Real-world systems involve multiple interacting physical domains:
- **Electrical → Thermal**: Joule heating (I²R losses)
- **Thermal → Electrical**: Temperature-dependent resistance
- **Electrical ↔ Kinetic**: Motors and generators
- **Thermal → Fluid**: Boiling, steam generation
- **Fluid → Kinetic**: Turbines, pistons
- **Kinetic → Electrical**: Generators

The architecture provides:
1. Independent solvers per domain (can run in parallel)
2. Well-defined coupling interfaces
3. Flexible iteration strategies for tight coupling
4. Unified cell abstraction for game integration

---

## Domain Solver Interfaces

### Base Interface

```csharp
public enum SimDomain
{
    Electrical,
    Thermal,
    Kinetic,
    Fluid
}

public interface IDomainSolver
{
    SimDomain Domain { get; }

    /// <summary>Advances simulation by dt seconds.</summary>
    void Step(double dt);

    /// <summary>Clears all state.</summary>
    void Clear();

    /// <summary>Returns solver statistics.</summary>
    DomainStats GetStats();
}

public record struct DomainStats(
    int NodeCount,
    int ComponentCount,
    int Iterations,        // For iterative solvers
    double LastStepTime    // Milliseconds
);
```

### Electrical Domain

The existing MNA solver (`ISimulation`) serves as the electrical domain solver:

```csharp
// Already implemented in Sparky.MNA.Api.ISimulation
// Key methods:
//   NodeId CreateNode()
//   ResistorId AddResistor(NodeId a, NodeId b, double R)
//   double GetVoltage(NodeId id)
//   double GetResistorPower(ResistorId id)
//   void Step(double dt)
```

### Thermal Domain

Heat transfer through conduction, convection, and radiation:

```csharp
namespace Sparky.Thermal;

public readonly record struct ThermalNodeId(int Value);
public readonly record struct ConductionPathId(int Value);

public interface IThermalSimulation : IDomainSolver
{
    // Node Management
    /// <summary>
    /// Creates a thermal node with given thermal mass and initial temperature.
    /// </summary>
    /// <param name="thermalMass">Heat capacity in J/K</param>
    /// <param name="initialTemp">Initial temperature in Kelvin</param>
    ThermalNodeId CreateNode(double thermalMass, double initialTemp = 293.15);

    void RemoveNode(ThermalNodeId id);
    bool NodeExists(ThermalNodeId id);

    // Conduction (node-to-node heat transfer)
    /// <summary>
    /// Adds a conduction path between two nodes.
    /// Heat flow: Q = conductance * (T_a - T_b)
    /// </summary>
    /// <param name="conductance">Thermal conductance in W/K</param>
    ConductionPathId AddConductionPath(ThermalNodeId a, ThermalNodeId b,
        double conductance);

    void UpdateConductionPath(ConductionPathId id, double conductance);
    void RemoveConductionPath(ConductionPathId id);

    // Heat Sources
    /// <summary>
    /// Sets external heat input to a node (from electrical dissipation,
    /// combustion, solar, etc.)
    /// </summary>
    /// <param name="power">Heat input in Watts</param>
    void SetHeatInput(ThermalNodeId id, double power);

    // Ambient Coupling (convection + radiation to environment)
    /// <summary>
    /// Sets coupling to ambient temperature.
    /// Heat loss: Q = coefficient * (T_node - T_ambient)
    /// </summary>
    /// <param name="coefficient">Combined h*A in W/K</param>
    void SetAmbientCoupling(ThermalNodeId id, double coefficient);

    /// <summary>Global ambient temperature (default 293.15 K = 20°C)</summary>
    double AmbientTemperature { get; set; }

    // Readout
    double GetTemperature(ThermalNodeId id);
    double GetHeatFlow(ConductionPathId id);  // Positive = a→b

    // Phase change support (optional, for boiling/melting)
    void SetPhaseChangePoint(ThermalNodeId id, double temperature,
        double latentHeat);
}
```

**Implementation Notes:**

The thermal solver uses explicit Euler integration (conditionally stable):

```
For each node:
    Q_net = Q_input
          + Σ(k_i * (T_neighbor_i - T))   // conduction from neighbors
          + h * (T_ambient - T)            // ambient coupling

    dT/dt = Q_net / C_thermal
    T_new = T + dT/dt * dt
```

Stability condition: `dt < C_min / (2 * k_max * n_neighbors)`

For game use, this is typically fine with 50ms ticks. For stiff systems, implement implicit Euler or use sub-stepping.

### Kinetic Domain

Rotational and linear mechanical systems:

```csharp
namespace Sparky.Kinetic;

public readonly record struct ShaftId(int Value);
public readonly record struct GearLinkId(int Value);
public readonly record struct LinearMassId(int Value);

public interface IKineticSimulation : IDomainSolver
{
    // Rotational elements
    /// <summary>
    /// Creates a rotating shaft with given moment of inertia.
    /// </summary>
    /// <param name="momentOfInertia">In kg·m²</param>
    ShaftId CreateShaft(double momentOfInertia);

    void RemoveShaft(ShaftId id);

    /// <summary>
    /// Couples two shafts with a gear ratio.
    /// ω_b = ratio * ω_a, τ_a = ratio * τ_b
    /// </summary>
    GearLinkId AddGearLink(ShaftId a, ShaftId b, double ratio);

    void UpdateGearLink(GearLinkId id, double ratio);
    void RemoveGearLink(GearLinkId id);

    // Torque inputs
    /// <summary>Sets driving torque on shaft (from motor, hand crank, etc.)</summary>
    void SetDriveTorque(ShaftId id, double torque);

    /// <summary>Sets load torque (friction, useful work extraction)</summary>
    void SetLoadTorque(ShaftId id, double torque);

    /// <summary>Sets viscous friction coefficient (τ_friction = b * ω)</summary>
    void SetFrictionCoefficient(ShaftId id, double coefficient);

    // Readout
    double GetAngularVelocity(ShaftId id);  // rad/s
    double GetAngle(ShaftId id);            // radians
    double GetNetTorque(ShaftId id);        // N·m

    // Linear motion (optional, for pistons/sliders)
    LinearMassId CreateLinearMass(double mass);
    void SetForce(LinearMassId id, double force);
    double GetVelocity(LinearMassId id);
    double GetPosition(LinearMassId id);
}
```

**Implementation Notes:**

Simple rotational dynamics:
```
τ_net = τ_drive - τ_load - b * ω
dω/dt = τ_net / J
ω_new = ω + dω/dt * dt
θ_new = θ + ω * dt
```

Gear constraints can be handled by:
1. Reducing to single equivalent inertia per connected system
2. Or solving a small linear system for coupled shafts

### Fluid Domain

Compressible flow for steam/pneumatics, incompressible for hydraulics:

```csharp
namespace Sparky.Fluid;

public readonly record struct PressureNodeId(int Value);
public readonly record struct FlowPathId(int Value);

public interface IFluidSimulation : IDomainSolver
{
    // Pressure vessels / volumes
    /// <summary>
    /// Creates a pressure node (tank, chamber, pipe segment).
    /// </summary>
    /// <param name="volume">Volume in m³</param>
    /// <param name="initialPressure">Initial pressure in Pa</param>
    /// <param name="initialTemp">Initial temperature in K</param>
    PressureNodeId CreateNode(double volume, double initialPressure,
        double initialTemp = 373.15);

    void RemoveNode(PressureNodeId id);

    // Flow paths (pipes, orifices, valves)
    /// <summary>
    /// Adds a flow path between nodes.
    /// Mass flow ≈ Cv * √(ΔP) for gas, or Cv * ΔP for liquid
    /// </summary>
    /// <param name="flowCoefficient">Cv in appropriate units</param>
    FlowPathId AddFlowPath(PressureNodeId a, PressureNodeId b,
        double flowCoefficient);

    void UpdateFlowPath(FlowPathId id, double flowCoefficient);
    void RemoveFlowPath(FlowPathId id);

    // Thermal interaction
    /// <summary>Sets heat input for phase change (boiling)</summary>
    void SetHeatInput(PressureNodeId id, double power);

    /// <summary>Links to thermal domain for temperature coupling</summary>
    void SetThermalCoupling(PressureNodeId id, double wallConductance);

    // Mechanical work extraction
    /// <summary>
    /// Sets mechanical power extraction (turbine, piston).
    /// Reduces pressure proportionally.
    /// </summary>
    void SetMechanicalPowerOut(PressureNodeId id, double power);

    // Mass sources/sinks
    void SetMassFlowIn(PressureNodeId id, double massFlow);  // kg/s

    // Readout
    double GetPressure(PressureNodeId id);      // Pa
    double GetTemperature(PressureNodeId id);   // K
    double GetMass(PressureNodeId id);          // kg
    double GetMassFlow(FlowPathId id);          // kg/s (positive = a→b)
    FluidPhase GetPhase(PressureNodeId id);     // Liquid, Gas, TwoPhase

    // Fluid properties (can swap for different working fluids)
    IFluidProperties FluidProperties { get; set; }
}

public enum FluidPhase { Liquid, TwoPhase, Gas }

public interface IFluidProperties
{
    string Name { get; }  // "Water", "Air", etc.
    double GetDensity(double pressure, double temperature);
    double GetSpecificHeat(double pressure, double temperature);
    double GetSaturationPressure(double temperature);
    double GetSaturationTemperature(double pressure);
    double GetLatentHeat(double pressure);
}
```

**Implementation Notes:**

For steam simulation, use ideal gas law with phase change:
```
For gas: P * V = n * R * T
For liquid: ρ ≈ constant
For two-phase: Track quality (vapor fraction)

Mass flow through orifice:
  ṁ = Cv * √(ρ * ΔP)  (subsonic)
  ṁ = Cv * P_up * √(ρ_up / T_up) * f(P_down/P_up)  (choked)

Energy balance for boiling:
  Q_in = ṁ_vaporized * h_fg + m * c_p * dT/dt
```

This gets complex quickly. For games, simplified models often suffice.

---

## Coupling Architecture

### Coupling Interface

```csharp
public interface IDomainCoupling
{
    /// <summary>Source domain that provides data.</summary>
    SimDomain SourceDomain { get; }

    /// <summary>Target domain that receives data.</summary>
    SimDomain TargetDomain { get; }

    /// <summary>
    /// Applies coupling from source to target.
    /// Called after source domain solves, before target domain solves.
    /// </summary>
    void Apply(IDomainSolver source, IDomainSolver target);
}
```

### Standard Couplings

**Electrical → Thermal (Joule Heating)**
```csharp
public class JouleHeatingCoupling : IDomainCoupling
{
    public SimDomain SourceDomain => SimDomain.Electrical;
    public SimDomain TargetDomain => SimDomain.Thermal;

    private readonly List<(ResistorId resistor, ThermalNodeId thermal)> _links = new();

    public void AddLink(ResistorId resistor, ThermalNodeId thermalNode)
        => _links.Add((resistor, thermalNode));

    public void Apply(IDomainSolver source, IDomainSolver target)
    {
        var elec = (ISimulation)source;
        var thermal = (IThermalSimulation)target;

        foreach (var (r, t) in _links)
        {
            double power = elec.GetResistorPower(r);
            thermal.SetHeatInput(t, power);
        }
    }
}
```

**Thermal → Electrical (Temperature-Dependent Resistance)**
```csharp
public class ThermistorCoupling : IDomainCoupling
{
    public SimDomain SourceDomain => SimDomain.Thermal;
    public SimDomain TargetDomain => SimDomain.Electrical;

    private readonly List<ThermistorLink> _links = new();

    public record ThermistorLink(
        ThermalNodeId ThermalNode,
        ResistorId Resistor,
        double R0,           // Resistance at T0
        double T0,           // Reference temperature (K)
        double Beta          // Material constant
    );

    public void AddLink(ThermistorLink link) => _links.Add(link);

    public void Apply(IDomainSolver source, IDomainSolver target)
    {
        var thermal = (IThermalSimulation)source;
        var elec = (ISimulation)target;

        foreach (var link in _links)
        {
            double T = thermal.GetTemperature(link.ThermalNode);
            // Steinhart-Hart simplified: R = R0 * exp(β * (1/T - 1/T0))
            double R = link.R0 * Math.Exp(link.Beta * (1/T - 1/link.T0));
            elec.UpdateResistor(link.Resistor, R);
        }
    }
}
```

**Electrical ↔ Kinetic (DC Motor/Generator)**
```csharp
public class DcMotorCoupling
{
    // Motor model: V = I*R + k_e*ω, τ = k_t*I
    // For ideal motor: k_e = k_t = k

    public record MotorLink(
        VoltageSourceId BackEmfSource,  // Models back-EMF as controlled source
        ResistorId WindingResistance,
        ShaftId Shaft,
        double MotorConstant           // k in V/(rad/s) = N·m/A
    );

    private readonly List<MotorLink> _links = new();

    public void AddLink(MotorLink link) => _links.Add(link);

    // Electrical → Kinetic: Compute torque from current
    public void ElectricalToKinetic(ISimulation elec, IKineticSimulation kinetic)
    {
        foreach (var link in _links)
        {
            // Current through motor = current through back-EMF source
            double current = elec.GetVoltageSourceCurrent(link.BackEmfSource);
            double torque = link.MotorConstant * current;
            kinetic.SetDriveTorque(link.Shaft, torque);
        }
    }

    // Kinetic → Electrical: Update back-EMF from speed
    public void KineticToElectrical(IKineticSimulation kinetic, ISimulation elec)
    {
        foreach (var link in _links)
        {
            double omega = kinetic.GetAngularVelocity(link.Shaft);
            double backEmf = link.MotorConstant * omega;
            elec.UpdateVoltageSource(link.BackEmfSource, backEmf);
        }
    }
}
```

**Thermal → Fluid (Boiler)**
```csharp
public class BoilerCoupling : IDomainCoupling
{
    public SimDomain SourceDomain => SimDomain.Thermal;
    public SimDomain TargetDomain => SimDomain.Fluid;

    private readonly List<(ThermalNodeId thermal, PressureNodeId fluid)> _links = new();

    public void AddLink(ThermalNodeId thermal, PressureNodeId fluid)
        => _links.Add((thermal, fluid));

    public void Apply(IDomainSolver source, IDomainSolver target)
    {
        var thermal = (IThermalSimulation)source;
        var fluid = (IFluidSimulation)target;

        foreach (var (t, f) in _links)
        {
            // Transfer heat from thermal node to fluid
            double T_wall = thermal.GetTemperature(t);
            double T_fluid = fluid.GetTemperature(f);
            // Simplified: assume all excess heat goes to phase change
            // Real implementation needs wall conductance model
            if (T_wall > T_fluid)
            {
                double Q = 1000 * (T_wall - T_fluid);  // Simplified
                fluid.SetHeatInput(f, Q);
            }
        }
    }
}
```

---

## Multi-Domain Simulation Manager

### Orchestration

```csharp
public class MultiDomainSimulation
{
    private readonly Dictionary<SimDomain, IDomainSolver> _solvers = new();
    private readonly List<IDomainCoupling> _couplings = new();
    private readonly List<Action> _bidirectionalCouplings = new();

    public void RegisterSolver(IDomainSolver solver)
        => _solvers[solver.Domain] = solver;

    public void AddCoupling(IDomainCoupling coupling)
        => _couplings.Add(coupling);

    public void AddBidirectionalCoupling(Action couplingAction)
        => _bidirectionalCouplings.Add(couplingAction);

    public T GetSolver<T>(SimDomain domain) where T : IDomainSolver
        => (T)_solvers[domain];

    /// <summary>
    /// Advances all domains by dt with coupling.
    /// </summary>
    public void Step(double dt, int maxCouplingIterations = 3)
    {
        // For loosely coupled systems: sequential solve with coupling
        //
        // Solve order (respects primary data flow):
        // 1. Electrical (independent, or uses previous kinetic state)
        // 2. Apply E→T coupling (Joule heating)
        // 3. Thermal
        // 4. Apply T→E coupling (thermistors)
        // 5. Apply E↔K coupling (motors/generators)
        // 6. Kinetic
        // 7. Apply K→F coupling (turbines)
        // 8. Fluid

        for (int iter = 0; iter < maxCouplingIterations; iter++)
        {
            // Solve electrical
            if (_solvers.TryGetValue(SimDomain.Electrical, out var elec))
                elec.Step(dt);

            // Apply electrical → thermal coupling
            ApplyCouplings(SimDomain.Electrical, SimDomain.Thermal);

            // Solve thermal
            if (_solvers.TryGetValue(SimDomain.Thermal, out var thermal))
                thermal.Step(dt);

            // Apply thermal → electrical coupling
            ApplyCouplings(SimDomain.Thermal, SimDomain.Electrical);

            // Apply electrical ↔ kinetic coupling
            foreach (var bc in _bidirectionalCouplings)
                bc();

            // Solve kinetic
            if (_solvers.TryGetValue(SimDomain.Kinetic, out var kinetic))
                kinetic.Step(dt);

            // Apply kinetic → fluid coupling (turbines)
            ApplyCouplings(SimDomain.Kinetic, SimDomain.Fluid);

            // Apply thermal → fluid coupling (boilers)
            ApplyCouplings(SimDomain.Thermal, SimDomain.Fluid);

            // Solve fluid
            if (_solvers.TryGetValue(SimDomain.Fluid, out var fluid))
                fluid.Step(dt);

            // Check convergence for tightly coupled systems
            // (For now, just run fixed iterations)
        }
    }

    private void ApplyCouplings(SimDomain source, SimDomain target)
    {
        if (!_solvers.TryGetValue(source, out var srcSolver)) return;
        if (!_solvers.TryGetValue(target, out var tgtSolver)) return;

        foreach (var coupling in _couplings)
        {
            if (coupling.SourceDomain == source && coupling.TargetDomain == target)
                coupling.Apply(srcSolver, tgtSolver);
        }
    }

    public void Clear()
    {
        foreach (var solver in _solvers.Values)
            solver.Clear();
        _couplings.Clear();
        _bidirectionalCouplings.Clear();
    }
}
```

### Usage Example

```csharp
// Setup
var multiSim = new MultiDomainSimulation();

var electrical = new SimulationManager();
var thermal = new ThermalSimulation();
var kinetic = new KineticSimulation();

multiSim.RegisterSolver(electrical);
multiSim.RegisterSolver(thermal);
multiSim.RegisterSolver(kinetic);

// Create components
var ground = electrical.Ground;
var node1 = electrical.CreateNode();
var batteryPos = electrical.CreateNode();
var resistor = electrical.AddResistor(batteryPos, node1, 10.0);
var battery = electrical.AddVoltageSource(batteryPos, ground, 12.0);

var thermalNode = thermal.CreateNode(thermalMass: 1.0, initialTemp: 293.15);
thermal.SetAmbientCoupling(thermalNode, coefficient: 0.1);

// Setup coupling
var jouleHeating = new JouleHeatingCoupling();
jouleHeating.AddLink(resistor, thermalNode);
multiSim.AddCoupling(jouleHeating);

// Simulate
for (int i = 0; i < 1000; i++)
{
    multiSim.Step(0.05);  // 50ms tick

    if (i % 20 == 0)  // Every second
    {
        double power = electrical.GetResistorPower(resistor);
        double temp = thermal.GetTemperature(thermalNode);
        Console.WriteLine($"t={i*0.05:F1}s: P={power:F2}W, T={temp-273.15:F1}°C");
    }
}
```

---

## Cell Integration (Game Layer)

### Cell Interface (Extended from Cell.md)

```csharp
public interface IMultiDomainCell
{
    // Identity
    CellId Id { get; }
    CellType Type { get; }

    // Domain participation (null if not participating)
    IElectricalCell? Electrical { get; }
    IThermalCell? Thermal { get; }
    IKineticCell? Kinetic { get; }
    IFluidCell? Fluid { get; }

    // Lifecycle
    void OnCreate(MultiDomainSimulation sim);
    void OnDestroy(MultiDomainSimulation sim);

    // Called each tick to set up couplings
    void SetupCouplings(MultiDomainSimulation sim);
}

public interface IElectricalCell
{
    IEnumerable<NodeId> GetNodes();
    IEnumerable<object> GetComponents();  // Resistors, sources, etc.
}

public interface IThermalCell
{
    ThermalNodeId ThermalNode { get; }
    double ThermalMass { get; }
    double AmbientCoupling { get; }
}

public interface IKineticCell
{
    ShaftId? Shaft { get; }
    double MomentOfInertia { get; }
}
```

### Example: Resistor Cell

```csharp
public class ResistorCell : IMultiDomainCell, IElectricalCell, IThermalCell
{
    public CellId Id { get; }
    public CellType Type => CellType.Resistor;

    // Configuration
    public double Resistance { get; set; } = 100.0;  // Ohms
    public double ThermalMass { get; } = 0.1;        // J/K (small)
    public double AmbientCoupling { get; } = 0.01;   // W/K

    // Simulation state
    private NodeId _nodeA, _nodeB;
    private ResistorId _resistorId;
    private ThermalNodeId _thermalNodeId;

    public IElectricalCell Electrical => this;
    public IThermalCell Thermal => this;
    public IKineticCell? Kinetic => null;
    public IFluidCell? Fluid => null;

    // IElectricalCell
    public IEnumerable<NodeId> GetNodes() => new[] { _nodeA, _nodeB };
    public IEnumerable<object> GetComponents() => new object[] { _resistorId };
    public ThermalNodeId ThermalNode => _thermalNodeId;

    public void OnCreate(MultiDomainSimulation sim)
    {
        var elec = sim.GetSolver<ISimulation>(SimDomain.Electrical);
        var thermal = sim.GetSolver<IThermalSimulation>(SimDomain.Thermal);

        _nodeA = elec.CreateNode();
        _nodeB = elec.CreateNode();
        _resistorId = elec.AddResistor(_nodeA, _nodeB, Resistance);

        _thermalNodeId = thermal.CreateNode(ThermalMass, 293.15);
        thermal.SetAmbientCoupling(_thermalNodeId, AmbientCoupling);
    }

    public void OnDestroy(MultiDomainSimulation sim)
    {
        var elec = sim.GetSolver<ISimulation>(SimDomain.Electrical);
        var thermal = sim.GetSolver<IThermalSimulation>(SimDomain.Thermal);

        elec.RemoveResistor(_resistorId);
        elec.RemoveNode(_nodeB);
        elec.RemoveNode(_nodeA);

        thermal.RemoveNode(_thermalNodeId);
    }

    public void SetupCouplings(MultiDomainSimulation sim)
    {
        // Joule heating: P = I²R → heat input
        var joule = sim.GetCoupling<JouleHeatingCoupling>()
            ?? sim.AddCoupling(new JouleHeatingCoupling());
        joule.AddLink(_resistorId, _thermalNodeId);
    }
}
```

### Example: DC Motor Cell

```csharp
public class MotorCell : IMultiDomainCell, IElectricalCell, IThermalCell, IKineticCell
{
    public CellId Id { get; }
    public CellType Type => CellType.Motor;

    // Configuration
    public double WindingResistance { get; set; } = 1.0;    // Ohms
    public double MotorConstant { get; set; } = 0.1;        // V/(rad/s) = N·m/A
    public double MomentOfInertia { get; set; } = 0.001;    // kg·m²
    public double ThermalMass { get; } = 10.0;              // J/K (larger)
    public double AmbientCoupling { get; } = 0.5;           // W/K

    // Simulation state
    private NodeId _nodePos, _nodeNeg;
    private ResistorId _windingR;
    private VoltageSourceId _backEmf;
    private ThermalNodeId _thermalNodeId;
    private ShaftId _shaftId;

    public IElectricalCell Electrical => this;
    public IThermalCell Thermal => this;
    public IKineticCell Kinetic => this;
    public IFluidCell? Fluid => null;

    // Interface implementations
    public IEnumerable<NodeId> GetNodes() => new[] { _nodePos, _nodeNeg };
    public IEnumerable<object> GetComponents() => new object[] { _windingR, _backEmf };
    public ThermalNodeId ThermalNode => _thermalNodeId;
    public ShaftId? Shaft => _shaftId;

    public void OnCreate(MultiDomainSimulation sim)
    {
        var elec = sim.GetSolver<ISimulation>(SimDomain.Electrical);
        var thermal = sim.GetSolver<IThermalSimulation>(SimDomain.Thermal);
        var kinetic = sim.GetSolver<IKineticSimulation>(SimDomain.Kinetic);

        // Electrical: Winding resistance in series with back-EMF source
        _nodePos = elec.CreateNode();
        var nodeInt = elec.CreateNode();  // Internal node
        _nodeNeg = elec.CreateNode();

        _windingR = elec.AddResistor(_nodePos, nodeInt, WindingResistance);
        _backEmf = elec.AddVoltageSource(nodeInt, _nodeNeg, 0.0);  // Initially 0

        // Thermal
        _thermalNodeId = thermal.CreateNode(ThermalMass, 293.15);
        thermal.SetAmbientCoupling(_thermalNodeId, AmbientCoupling);

        // Kinetic
        _shaftId = kinetic.CreateShaft(MomentOfInertia);
        kinetic.SetFrictionCoefficient(_shaftId, 0.001);  // Small friction
    }

    public void SetupCouplings(MultiDomainSimulation sim)
    {
        // Winding I²R loss → heat
        var joule = sim.GetOrAddCoupling<JouleHeatingCoupling>();
        joule.AddLink(_windingR, _thermalNodeId);

        // Motor electromechanical coupling
        var motor = sim.GetOrAddCoupling<DcMotorCoupling>();
        motor.AddLink(new DcMotorCoupling.MotorLink(
            BackEmfSource: _backEmf,
            WindingResistance: _windingR,
            Shaft: _shaftId,
            MotorConstant: MotorConstant
        ));
    }
}
```

---

## Performance Considerations

### Parallel Solving

Independent domains can solve in parallel:

```csharp
public void StepParallel(double dt)
{
    // Phase 1: Apply pre-couplings
    ApplyAllCouplings();

    // Phase 2: Solve all domains in parallel
    Parallel.ForEach(_solvers.Values, solver => solver.Step(dt));

    // Phase 3: Apply post-couplings (may need another iteration)
}
```

### Partitioning

Each domain solver can independently partition disconnected components:
- Electrical: Already implements graph partitioning
- Thermal: Spatially disconnected regions solve independently
- Kinetic: Mechanically uncoupled shafts solve independently

### Solver Selection

- **Electrical**: MNA with dense/sparse selection based on size
- **Thermal**: Explicit Euler is sufficient for typical game scenarios; implicit for stiff problems
- **Kinetic**: Usually small systems, direct integration
- **Fluid**: Most complex; may need implicit solver for stability

### Tick Budgeting

```csharp
public class TickBudget
{
    public TimeSpan MaxTickTime { get; set; } = TimeSpan.FromMilliseconds(10);

    public void Step(MultiDomainSimulation sim, double dt)
    {
        var sw = Stopwatch.StartNew();

        // Always do electrical (critical for gameplay)
        sim.StepDomain(SimDomain.Electrical, dt);

        // Thermal less critical, can skip
        if (sw.Elapsed < MaxTickTime * 0.5)
            sim.StepDomain(SimDomain.Thermal, dt);

        // Kinetic medium priority
        if (sw.Elapsed < MaxTickTime * 0.7)
            sim.StepDomain(SimDomain.Kinetic, dt);

        // Fluid lowest priority (complex, often not needed)
        if (sw.Elapsed < MaxTickTime * 0.9)
            sim.StepDomain(SimDomain.Fluid, dt);
    }
}
```

---

## Implementation Priority

### Phase 1: Thermal Coupling
1. Implement `ThermalSimulation` with basic heat diffusion
2. Add `JouleHeatingCoupling` for resistor heating
3. Test with simple resistor heating scenario

### Phase 2: Kinetic Domain
1. Implement `KineticSimulation` with shaft dynamics
2. Add `DcMotorCoupling` for motor/generator
3. Test with motor driving load

### Phase 3: Thermal-Electrical Feedback
1. Add `ThermistorCoupling`
2. Implement temperature-dependent behavior
3. Test thermal runaway scenario (positive feedback)

### Phase 4: Fluid Domain
1. Implement simplified `FluidSimulation`
2. Add boiler coupling
3. Test steam generation from heat input

### Phase 5: Integration
1. Unified cell abstraction
2. Game integration (grid, rendering)
3. Example scenarios (power plant, motor control)
