namespace Sparky.Game.Core;

/// <summary>
/// Types of cells that can be placed on the grid.
/// </summary>
public enum CellType
{
    // Phase 1: Basic components
    /// <summary>Connects adjacent compatible ports into a single node.</summary>
    Wire,

    /// <summary>Voltage source with configurable voltage.</summary>
    Battery,

    /// <summary>Resistor with configurable resistance.</summary>
    Resistor,

    /// <summary>Forces connected port to ground (0V reference).</summary>
    Ground,

    // Phase 2+: Additional components (to be implemented later)
    // Switch,
    // Diode,
    // LED,
    // Capacitor,
    // Inductor,
    // Motor,
    // Generator,
}
