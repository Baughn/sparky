namespace Sparky.Game.Simulation;

/// <summary>
/// Simulation domain type.
/// </summary>
public enum SimDomain
{
    /// <summary>Electrical circuit simulation (MNA solver).</summary>
    Electrical,

    /// <summary>Heat diffusion simulation.</summary>
    Thermal,

    /// <summary>Rotational mechanics simulation.</summary>
    Kinetic,
}

/// <summary>
/// Statistics about a domain solver's last step.
/// </summary>
public readonly record struct DomainStats(
    /// <summary>Number of nodes in the domain.</summary>
    int NodeCount,
    /// <summary>Number of components/links in the domain.</summary>
    int ComponentCount,
    /// <summary>Number of iterations (for iterative solvers).</summary>
    int Iterations,
    /// <summary>Wall-clock time of last step in milliseconds.</summary>
    double LastStepTimeMs
);

/// <summary>
/// Base interface for all domain solvers.
/// </summary>
public interface IDomainSolver
{
    /// <summary>
    /// Which domain this solver handles.
    /// </summary>
    SimDomain Domain { get; }

    /// <summary>
    /// Advances the simulation by one timestep.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    void Step(double dt);

    /// <summary>
    /// Clears all state from the solver.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets statistics about the solver's current state.
    /// </summary>
    DomainStats GetStats();
}
