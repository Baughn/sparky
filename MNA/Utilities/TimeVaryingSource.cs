using Sparky.MNA.Api;

namespace Sparky.MNA.Utilities;

/// <summary>
/// Base class for sources that vary with simulation time.
/// <para>
/// <b>Usage pattern:</b> Call <see cref="Update"/> before each <c>sim.Step()</c>.
/// The source value is computed from <c>sim.SimulationTime</c>, which reflects
/// the time <i>after</i> all previous steps. This means the source is set to
/// the value it should have at the start of the upcoming timestep.
/// </para>
/// <example>
/// <code>
/// // Typical game loop:
/// for (int i = 0; i &lt; steps; i++)
/// {
///     source.Update();  // Sets value for time = SimulationTime
///     sim.Step(dt);     // Advances SimulationTime by dt
/// }
/// </code>
/// </example>
/// </summary>
public abstract class TimeVaryingSource
{
    protected readonly ISimulation Sim;

    protected TimeVaryingSource(ISimulation sim)
    {
        Sim = sim;
    }

    /// <summary>
    /// Computes the source value at the given time.
    /// </summary>
    /// <param name="time">Simulation time in seconds.</param>
    /// <returns>The source value (voltage or current) at that time.</returns>
    public abstract double GetValue(double time);

    /// <summary>
    /// Updates the underlying source to match the current simulation time.
    /// </summary>
    /// <remarks>
    /// Call this <b>before</b> each <c>sim.Step()</c>. The value is computed
    /// using <c>sim.SimulationTime</c>, so the source reflects the correct
    /// value at the start of the upcoming timestep.
    /// </remarks>
    public abstract void Update();

    /// <summary>
    /// Removes the underlying source from the simulation.
    /// </summary>
    public abstract void Remove();

    /// <summary>
    /// Returns true if the underlying source still exists in the simulation.
    /// </summary>
    public abstract bool Exists { get; }
}
