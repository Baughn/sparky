namespace Sparky.Game.Core;

/// <summary>
/// The type of a voxel, determining its electrical connectivity behavior.
/// </summary>
public enum VoxelType
{
    /// <summary>
    /// Empty space. Does not participate in connectivity.
    /// This is the default for unset voxels (sparse storage).
    /// </summary>
    Air = 0,

    /// <summary>
    /// Conductive material. Connects to adjacent conductor voxels.
    /// In Phase 3, conductors will have Material properties (resistivity).
    /// </summary>
    Conductor = 1,

    /// <summary>
    /// Insulating material. Blocks connectivity.
    /// Used in component bodies to prevent internal shorts (e.g., diode insulator).
    /// </summary>
    Insulator = 2
}
