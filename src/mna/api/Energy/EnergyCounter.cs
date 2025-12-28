namespace Sparky.Mna.Api.Energy;

/// <summary>
/// Accumulates energy (Joules) transferred through a component over time.
/// Used by logical components to track cumulative energy across topology rebuilds.
/// </summary>
public struct EnergyCounter {
    /// <summary>
    /// Total energy accumulated (Joules).
    /// </summary>
    public double Joules { get; private set; }

    /// <summary>
    /// Add energy from a single time step.
    /// </summary>
    /// <param name="energyDelta">Energy transferred in this step (Joules)</param>
    public void Accumulate(double energyDelta) {
        Joules += energyDelta;
    }

    /// <summary>
    /// Reset the counter to zero.
    /// </summary>
    public void Reset() {
        Joules = 0;
    }
}
