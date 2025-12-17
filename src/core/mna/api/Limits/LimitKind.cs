namespace Sparky.MNA.Api.Limits;

/// <summary>
/// Types of limits that can be set on simulation components.
/// Organized by domain for future extensibility.
/// </summary>
public enum LimitKind {
    // Electrical domain
    /// <summary>Current exceeds threshold (signed comparison).</summary>
    OverCurrent,

    /// <summary>Voltage exceeds threshold (signed comparison).</summary>
    OverVoltage,

    /// <summary>Power exceeds threshold.</summary>
    OverPower,

    // Thermal domain (future)
    /// <summary>Temperature exceeds threshold.</summary>
    OverTemperature,

    /// <summary>Heat rate (dT/dt) exceeds threshold.</summary>
    OverHeatRate,

    // Kinetic domain (future)
    /// <summary>Angular velocity exceeds threshold.</summary>
    OverSpeed,

    /// <summary>Torque exceeds threshold.</summary>
    OverTorque,
}
