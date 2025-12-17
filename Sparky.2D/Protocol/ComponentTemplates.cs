namespace Sparky.TwoD.Protocol;

/// <summary>
/// Defines the cell layout for each component type.
/// Shared between client (ghost preview) and server (placement).
/// </summary>
public static class ComponentTemplates {
    /// <summary>
    /// Gets the cells that make up a component at a given origin and rotation.
    /// </summary>
    /// <param name="tool">The component type to place.</param>
    /// <param name="rotation">Rotation (0-3 representing 0°, 90°, 180°, 270°).</param>
    /// <returns>List of (offset, cell type) pairs relative to origin.</returns>
    public static IReadOnlyList<(GridPos Offset, CellType Type)> GetCells(CellType tool, int rotation) {
        return tool switch {
            CellType.Wire => [(new GridPos(0, 0), CellType.Wire)],
            CellType.Ground => [(new GridPos(0, 0), CellType.Ground)],
            CellType.Switch => [
                (new GridPos(0, 0), CellType.Switch),
                (GetOffset(rotation, 1), CellType.SwitchBody),
                (GetOffset(rotation, 2), CellType.SwitchTerminalB),
            ],
            CellType.Battery => [
                (new GridPos(0, 0), CellType.Battery),
                (GetOffset(rotation, 1), CellType.BatteryBody),
                (GetOffset(rotation, 2), CellType.BatteryPositive),
            ],
            CellType.Resistor => [
                (new GridPos(0, 0), CellType.Resistor),
                (GetOffset(rotation, 1), CellType.ResistorBody),
                (GetOffset(rotation, 2), CellType.ResistorTerminalB),
            ],
            // Non-placeable cell types (body cells, etc.) have no template
            _ => [],
        };
    }

    /// <summary>
    /// Gets the grid offset for a given rotation and distance.
    /// </summary>
    private static GridPos GetOffset(int rotation, int distance) => (rotation % 4) switch {
        0 => new GridPos(distance, 0),   // +X (right)
        1 => new GridPos(0, distance),   // +Y (down)
        2 => new GridPos(-distance, 0),  // -X (left)
        3 => new GridPos(0, -distance),  // -Y (up)
        _ => new GridPos(0, 0),
    };
}
