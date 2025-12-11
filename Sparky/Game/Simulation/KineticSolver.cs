using System.Collections.Generic;

namespace Sparky.Game.Simulation;

/// <summary>
/// Strongly-typed identifier for a rotating shaft.
/// </summary>
public readonly record struct ShaftId(int Value)
{
    public bool IsValid => Value > 0;
}

/// <summary>
/// Strongly-typed identifier for a gear/belt link between shafts.
/// </summary>
public readonly record struct GearLinkId(int Value)
{
    public bool IsValid => Value > 0;
}

/// <summary>
/// Interface for kinetic (rotational mechanics) simulation.
/// </summary>
public interface IKineticSolver : IDomainSolver
{
    /// <summary>
    /// Creates a rotating shaft with the given moment of inertia.
    /// </summary>
    /// <param name="momentOfInertia">Moment of inertia in kg·m².</param>
    /// <returns>ID of the created shaft.</returns>
    ShaftId CreateShaft(double momentOfInertia);

    /// <summary>
    /// Removes a shaft.
    /// </summary>
    void RemoveShaft(ShaftId id);

    /// <summary>
    /// Returns true if the shaft exists.
    /// </summary>
    bool ShaftExists(ShaftId id);

    /// <summary>
    /// Adds a gear/belt link between two shafts.
    /// </summary>
    /// <param name="a">First shaft.</param>
    /// <param name="b">Second shaft.</param>
    /// <param name="ratio">Gear ratio (ωb/ωa).</param>
    /// <returns>ID of the created link.</returns>
    GearLinkId AddGearLink(ShaftId a, ShaftId b, double ratio);

    /// <summary>
    /// Removes a gear link.
    /// </summary>
    void RemoveGearLink(GearLinkId id);

    /// <summary>
    /// Sets the drive torque on a shaft (from motor, hand crank, etc.).
    /// </summary>
    /// <param name="id">Shaft ID.</param>
    /// <param name="torque">Torque in N·m.</param>
    void SetDriveTorque(ShaftId id, double torque);

    /// <summary>
    /// Sets the load torque on a shaft (friction, useful work, etc.).
    /// </summary>
    /// <param name="id">Shaft ID.</param>
    /// <param name="torque">Load torque in N·m (positive opposes rotation).</param>
    void SetLoadTorque(ShaftId id, double torque);

    /// <summary>
    /// Sets the friction coefficient for a shaft.
    /// </summary>
    /// <param name="id">Shaft ID.</param>
    /// <param name="coefficient">Friction coefficient.</param>
    void SetFrictionCoefficient(ShaftId id, double coefficient);

    /// <summary>
    /// Gets the angular velocity of a shaft in rad/s.
    /// </summary>
    double GetAngularVelocity(ShaftId id);

    /// <summary>
    /// Gets the angular position of a shaft in radians.
    /// </summary>
    double GetAngle(ShaftId id);
}

/// <summary>
/// Stub implementation of IKineticSolver that does nothing.
/// All shafts remain stationary.
/// </summary>
public class KineticSolverStub : IKineticSolver
{
    private int _nextShaftId = 1;
    private readonly HashSet<int> _shafts = new();

    public SimDomain Domain => SimDomain.Kinetic;

    public ShaftId CreateShaft(double momentOfInertia)
    {
        var id = new ShaftId(_nextShaftId++);
        _shafts.Add(id.Value);
        return id;
    }

    public void RemoveShaft(ShaftId id) => _shafts.Remove(id.Value);

    public bool ShaftExists(ShaftId id) => _shafts.Contains(id.Value);

    public GearLinkId AddGearLink(ShaftId a, ShaftId b, double ratio) => new GearLinkId(0); // Stub: doesn't track links

    public void RemoveGearLink(GearLinkId id) { }

    public void SetDriveTorque(ShaftId id, double torque) { }

    public void SetLoadTorque(ShaftId id, double torque) { }

    public void SetFrictionCoefficient(ShaftId id, double coefficient) { }

    public double GetAngularVelocity(ShaftId id) => 0; // Stationary

    public double GetAngle(ShaftId id) => 0;

    public void Step(double dt) { } // No-op

    public void Clear()
    {
        _shafts.Clear();
        _nextShaftId = 1;
    }

    public DomainStats GetStats() =>
        new(NodeCount: _shafts.Count, ComponentCount: 0, Iterations: 0, LastStepTimeMs: 0);
}
