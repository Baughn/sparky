namespace Sparky.Game.Core;

/// <summary>
/// Defines electrical properties of a conductor material.
/// </summary>
/// <remarks>
/// Materials use game-scaled resistivity (Ω/voxel) for intuitive calculations:
/// - Copper: 1mΩ/voxel → 100 voxel wire = 0.1Ω
/// - Lead: 10mΩ/voxel → Same wire = 1Ω (useful for fuses)
/// </remarks>
public sealed class Material
{
    /// <summary>
    /// The display name of the material.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Resistivity in ohms per voxel (Ω/voxel).
    /// </summary>
    /// <remarks>
    /// Game-scaled for easy mental math. Actual resistance of a conductor
    /// depends on geometry: R = Resistivity × Length / CrossSection.
    /// </remarks>
    public double Resistivity { get; }

    private Material(string name, double resistivity)
    {
        Name = name;
        Resistivity = resistivity;
    }

    /// <summary>
    /// Copper - the baseline conductor. 1mΩ/voxel.
    /// </summary>
    public static Material Copper { get; } = new("Copper", 0.001);

    /// <summary>
    /// Lead - 10x copper resistivity. Good for fuses that heat up faster.
    /// </summary>
    public static Material Lead { get; } = new("Lead", 0.01);

    /// <summary>
    /// Iron - 5x copper resistivity.
    /// </summary>
    public static Material Iron { get; } = new("Iron", 0.005);

    /// <summary>
    /// Gold - 1.5x copper resistivity. Slightly worse but doesn't oxidize.
    /// </summary>
    public static Material Gold { get; } = new("Gold", 0.0015);

    public override string ToString() => Name;
}
