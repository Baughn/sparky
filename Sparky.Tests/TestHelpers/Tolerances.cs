namespace Sparky.Tests.TestHelpers;

/// <summary>
/// Centralized tolerance constants for test assertions.
/// Use these instead of hardcoded tolerances for consistency and easy tuning.
/// </summary>
public static class Tolerances
{
    /// <summary>
    /// Default tolerance for voltage comparisons (1 μV).
    /// Use for steady-state DC voltage assertions.
    /// </summary>
    public const double Voltage = 1e-6;

    /// <summary>
    /// Default tolerance for current comparisons (1 nA).
    /// Use for current measurements through components.
    /// </summary>
    public const double Current = 1e-9;

    /// <summary>
    /// Default tolerance for power comparisons (1 μW).
    /// Use for power dissipation and conservation checks.
    /// </summary>
    public const double Power = 1e-6;

    /// <summary>
    /// Tolerance for component parameter comparisons (1e-12).
    /// Use for capacitance, inductance, and other small values.
    /// </summary>
    public const double Parameter = 1e-12;

    /// <summary>
    /// Loose tolerance for transient and nonlinear simulations (1e-3).
    /// Use when numerical errors accumulate over multiple timesteps.
    /// </summary>
    public const double Loose = 1e-3;

    /// <summary>
    /// Very loose tolerance (1e-2 = 1%).
    /// Use for complex circuits where exact values are less important.
    /// </summary>
    public const double VeryLoose = 1e-2;

    /// <summary>
    /// Tolerance for resistance comparisons (1e-6).
    /// </summary>
    public const double Resistance = 1e-6;

    /// <summary>
    /// Tolerance for capacitance comparisons (1e-12 = 1pF).
    /// </summary>
    public const double Capacitance = 1e-12;

    /// <summary>
    /// Tolerance for inductance comparisons (1e-9 = 1nH).
    /// </summary>
    public const double Inductance = 1e-9;
}
