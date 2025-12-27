namespace Sparky.Voxel;

/// <summary>
/// The type of a voxel, determining its electrical connectivity behavior.
/// </summary>
public enum VoxelType {
    /// <summary>
    /// Empty space. Does not participate in connectivity.
    /// This is the default for unset voxels (sparse storage).
    /// </summary>
    Air = 0,

    /// <summary>
    /// Pure conductive material (zero resistance). Connects to adjacent conductor voxels.
    /// Used for component terminals where wires connect.
    /// </summary>
    Conductor = 1,

    /// <summary>
    /// Resistive conductive material. Each prism gets its own MNA node.
    /// Adjacent resistive prisms are connected via auto-generated resistors.
    /// Merges with adjacent pure Conductor (for wire-to-terminal connections).
    /// </summary>
    ResistiveConductor = 2,

    /// <summary>
    /// Insulating material. Blocks connectivity.
    /// Used in component bodies to prevent internal shorts (e.g., diode insulator).
    /// </summary>
    Insulator = 3
}
