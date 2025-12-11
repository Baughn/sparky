using System;

namespace Sparky.Game.Core;

/// <summary>
/// Render-friendly state for a cell after simulation.
/// All values normalized/computed for direct use by renderer.
/// </summary>
/// <remarks>
/// This struct is computed after each simulation tick and contains
/// everything a renderer needs to display the cell without accessing
/// the simulation directly.
/// </remarks>
public readonly record struct CellVisualState(
    /// <summary>
    /// Voltage level normalized to 0-1 range for color mapping.
    /// Based on the cell's primary node voltage relative to the circuit's voltage range.
    /// </summary>
    float VoltageNormalized,
    /// <summary>
    /// Current magnitude (absolute value, in amperes) for particle animation speed.
    /// </summary>
    float CurrentMagnitude,
    /// <summary>
    /// Primary current flow direction within the face (null if no significant flow).
    /// Used to orient current flow particles/animations.
    /// </summary>
    FaceDirection? CurrentFlowDirection,
    /// <summary>
    /// Power dissipation in watts (for heat visualization effects).
    /// Always positive for dissipative elements.
    /// </summary>
    float PowerDissipation,
    /// <summary>
    /// Component-specific state: charge level (0-1) for capacitors,
    /// or other component-specific normalized value.
    /// </summary>
    float ChargeLevel,
    /// <summary>
    /// Whether the component is "active" in a visual sense:
    /// LED lit, switch closed, motor spinning, etc.
    /// </summary>
    bool IsActive
)
{
    /// <summary>
    /// Default state: all zeros, not active.
    /// </summary>
    public static CellVisualState Default => new(0, 0, null, 0, 0, false);

    /// <summary>
    /// Creates a visual state for a simple conducting element (wire, junction).
    /// </summary>
    public static CellVisualState ForConductor(
        float voltageNormalized,
        float current,
        FaceDirection? flowDir
    ) => new(voltageNormalized, Math.Abs(current), flowDir, 0, 0, Math.Abs(current) > 1e-9f);

    /// <summary>
    /// Creates a visual state for a resistive element.
    /// </summary>
    public static CellVisualState ForResistor(
        float voltageNormalized,
        float current,
        FaceDirection? flowDir,
        float power
    ) => new(voltageNormalized, Math.Abs(current), flowDir, power, 0, Math.Abs(current) > 1e-9f);
}
