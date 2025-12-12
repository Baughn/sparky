namespace Sparky.TwoD.Protocol;

/// <summary>
/// Types of cells that can be placed on the 2D grid.
/// </summary>
public enum CellType
{
    /// <summary>Empty cell (air).</summary>
    Empty,

    /// <summary>Conductor wire that connects adjacent cells.</summary>
    Wire,

    /// <summary>Voltage source with positive and negative terminals.</summary>
    Battery,

    /// <summary>Resistance element.</summary>
    Resistor,

    /// <summary>Ground reference (0V).</summary>
    Ground
}

/// <summary>
/// Visual state for rendering a cell based on simulation results.
/// </summary>
public readonly record struct CellVisualState(
    /// <summary>Voltage at this cell, normalized to [0, 1] for coloring.</summary>
    float VoltageNormalized,

    /// <summary>Current through this cell, normalized to [0, 1] for coloring.</summary>
    float CurrentNormalized,

    /// <summary>Power dissipation, normalized to [0, 1] for heat visualization.</summary>
    float PowerNormalized
)
{
    public static readonly CellVisualState Default = new(0, 0, 0);
}
