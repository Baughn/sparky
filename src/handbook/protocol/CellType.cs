namespace Sparky.Handbook.Protocol;

/// <summary>
/// Types of cells that can be placed on the 2D grid.
/// </summary>
public enum CellType {
    /// <summary>Empty cell (air).</summary>
    Empty,

    /// <summary>Conductor wire that connects adjacent cells.</summary>
    Wire,

    /// <summary>Voltage source negative terminal (origin cell).</summary>
    Battery,

    /// <summary>Battery body (insulator between terminals).</summary>
    BatteryBody,

    /// <summary>Battery positive terminal (far end).</summary>
    BatteryPositive,

    /// <summary>Resistor terminal A (origin cell).</summary>
    Resistor,

    /// <summary>Resistor body (insulator between terminals).</summary>
    ResistorBody,

    /// <summary>Resistor terminal B (far end).</summary>
    ResistorTerminalB,

    /// <summary>Ground reference (0V).</summary>
    Ground,

    /// <summary>Toggleable switch terminal A (origin cell).</summary>
    Switch,

    /// <summary>Switch body (visual only, between terminals).</summary>
    SwitchBody,

    /// <summary>Switch terminal B (far end).</summary>
    SwitchTerminalB
}

/// <summary>
/// Visual state for rendering a cell based on simulation results.
/// </summary>
public readonly record struct CellVisualState(
    /// <summary>Voltage at this cell, normalized to [-1, 1] for coloring.</summary>
    float VoltageNormalized,

    /// <summary>Current through this cell, normalized to [0, 1] for coloring.</summary>
    float CurrentNormalized,

    /// <summary>Power dissipation, normalized to [0, 1] for heat visualization.</summary>
    float PowerNormalized,

    /// <summary>For switches: true if closed, false if open.</summary>
    bool SwitchClosed = false
) {
    public static readonly CellVisualState Default = new(0, 0, 0, false);
}
