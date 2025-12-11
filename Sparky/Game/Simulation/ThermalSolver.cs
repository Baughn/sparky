using System.Collections.Generic;

namespace Sparky.Game.Simulation;

/// <summary>
/// Strongly-typed identifier for a thermal node.
/// </summary>
public readonly record struct ThermalNodeId(int Value)
{
    public bool IsValid => Value > 0;
}

/// <summary>
/// Strongly-typed identifier for a thermal conduction path.
/// </summary>
public readonly record struct ConductionPathId(int Value)
{
    public bool IsValid => Value > 0;
}

/// <summary>
/// Interface for thermal (heat diffusion) simulation.
/// </summary>
public interface IThermalSolver : IDomainSolver
{
    /// <summary>
    /// Creates a thermal node with the given thermal mass and initial temperature.
    /// </summary>
    /// <param name="thermalMass">Thermal mass in J/K.</param>
    /// <param name="initialTemp">Initial temperature in Kelvin (default: 293.15K = 20°C).</param>
    /// <returns>ID of the created node.</returns>
    ThermalNodeId CreateNode(double thermalMass, double initialTemp = 293.15);

    /// <summary>
    /// Removes a thermal node.
    /// </summary>
    void RemoveNode(ThermalNodeId id);

    /// <summary>
    /// Returns true if the thermal node exists.
    /// </summary>
    bool NodeExists(ThermalNodeId id);

    /// <summary>
    /// Adds a conduction path between two nodes.
    /// </summary>
    /// <param name="a">First node.</param>
    /// <param name="b">Second node.</param>
    /// <param name="conductance">Thermal conductance in W/K.</param>
    /// <returns>ID of the created path.</returns>
    ConductionPathId AddConductionPath(ThermalNodeId a, ThermalNodeId b, double conductance);

    /// <summary>
    /// Removes a conduction path.
    /// </summary>
    void RemoveConductionPath(ConductionPathId id);

    /// <summary>
    /// Sets the heat input to a node (from electrical dissipation, etc.).
    /// </summary>
    /// <param name="id">Node ID.</param>
    /// <param name="power">Heat input in Watts.</param>
    void SetHeatInput(ThermalNodeId id, double power);

    /// <summary>
    /// Sets the coupling coefficient to ambient for a node.
    /// </summary>
    /// <param name="id">Node ID.</param>
    /// <param name="coefficient">Coupling coefficient in W/K.</param>
    void SetAmbientCoupling(ThermalNodeId id, double coefficient);

    /// <summary>
    /// Gets or sets the ambient temperature in Kelvin.
    /// </summary>
    double AmbientTemperature { get; set; }

    /// <summary>
    /// Gets the current temperature of a node in Kelvin.
    /// </summary>
    double GetTemperature(ThermalNodeId id);
}

/// <summary>
/// Stub implementation of IThermalSolver that does nothing.
/// All nodes remain at ambient temperature.
/// </summary>
public class ThermalSolverStub : IThermalSolver
{
    private int _nextNodeId = 1;
    private readonly HashSet<int> _nodes = new();

    public SimDomain Domain => SimDomain.Thermal;

    public double AmbientTemperature { get; set; } = 293.15; // 20°C

    public ThermalNodeId CreateNode(double thermalMass, double initialTemp = 293.15)
    {
        var id = new ThermalNodeId(_nextNodeId++);
        _nodes.Add(id.Value);
        return id;
    }

    public void RemoveNode(ThermalNodeId id) => _nodes.Remove(id.Value);

    public bool NodeExists(ThermalNodeId id) => _nodes.Contains(id.Value);

    public ConductionPathId AddConductionPath(ThermalNodeId a, ThermalNodeId b, double conductance)
        => new ConductionPathId(0); // Stub: doesn't track paths

    public void RemoveConductionPath(ConductionPathId id) { }

    public void SetHeatInput(ThermalNodeId id, double power) { }

    public void SetAmbientCoupling(ThermalNodeId id, double coefficient) { }

    public double GetTemperature(ThermalNodeId id) => AmbientTemperature; // Always ambient

    public void Step(double dt) { } // No-op

    public void Clear()
    {
        _nodes.Clear();
        _nextNodeId = 1;
    }

    public DomainStats GetStats() => new(
        NodeCount: _nodes.Count,
        ComponentCount: 0,
        Iterations: 0,
        LastStepTimeMs: 0
    );
}
