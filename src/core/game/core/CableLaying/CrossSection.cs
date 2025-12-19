namespace Sparky.Game.Core.CableLaying;

/// <summary>
/// Defines a cable cross-section size.
/// </summary>
/// <param name="Width">The width of the cross-section (smaller or equal dimension).</param>
/// <param name="Height">The height of the cross-section (larger or equal dimension).</param>
public readonly record struct CrossSection(int Width, int Height) {
    /// <summary>Gets the cross-sectional area (Width × Height).</summary>
    public int Area => Width * Height;

    /// <summary>Gets the minimum turning distance for this cross-section.</summary>
    public int MinTurnDistance => Math.Max(Width, Height) + 1;

    /// <summary>
    /// Returns true if the cross-section is square (Width == Height).
    /// Square cross-sections don't need orientation tracking.
    /// </summary>
    public bool IsSquare => Width == Height;

    // Pre-defined cable sizes
    public static readonly CrossSection Size1x1 = new(1, 1);
    public static readonly CrossSection Size1x2 = new(1, 2);
    public static readonly CrossSection Size2x2 = new(2, 2);
    public static readonly CrossSection Size2x3 = new(2, 3);
    public static readonly CrossSection Size3x5 = new(3, 5);

    /// <summary>All available cable cross-section sizes.</summary>
    public static readonly CrossSection[] AllSizes =
    [
        Size1x1, Size1x2, Size2x2, Size2x3, Size3x5
    ];

    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// Orientation of a non-square cross-section relative to the travel direction.
/// </summary>
/// <remarks>
/// For a cable traveling in direction D:
/// - The cross-section lies in the plane perpendicular to D
/// - This enum determines which axis the Width vs Height aligns to
///
/// For example, a 2x3 cable traveling in +X:
/// - Flat: Width(2) along Y, Height(3) along Z → cable is 2 tall, 3 wide
/// - Upright: Width(2) along Z, Height(3) along Y → cable is 3 tall, 2 wide
/// </remarks>
public enum CrossSectionOrientation {
    /// <summary>
    /// Width along the "first" perpendicular axis, Height along the "second".
    /// For +X/-X travel: Width=Y, Height=Z
    /// For +Y/-Y travel: Width=X, Height=Z
    /// For +Z/-Z travel: Width=X, Height=Y
    /// </summary>
    Flat,

    /// <summary>
    /// Height along the "first" perpendicular axis, Width along the "second".
    /// For +X/-X travel: Height=Y, Width=Z
    /// For +Y/-Y travel: Height=X, Width=Z
    /// For +Z/-Z travel: Height=X, Width=Y
    /// </summary>
    Upright
}

/// <summary>
/// Extension methods for cross-section orientation calculations.
/// </summary>
public static class CrossSectionExtensions {
    /// <summary>
    /// Gets the two axes perpendicular to the given travel direction.
    /// Returns (first axis, second axis) where axes are 0=X, 1=Y, 2=Z.
    /// </summary>
    public static (int First, int Second) GetPerpendicularAxes(this VoxelDirection direction) {
        return direction switch {
            VoxelDirection.XPos or VoxelDirection.XNeg => (1, 2), // Y, Z
            VoxelDirection.YPos or VoxelDirection.YNeg => (0, 2), // X, Z
            VoxelDirection.ZPos or VoxelDirection.ZNeg => (0, 1), // X, Y
            _ => (0, 1)
        };
    }

    /// <summary>
    /// Gets the dimensions along each axis for a cross-section with given orientation.
    /// </summary>
    /// <param name="crossSection">The cross-section size.</param>
    /// <param name="direction">The travel direction.</param>
    /// <param name="orientation">The orientation (Flat or Upright).</param>
    /// <returns>Array of 3 ints: size along X, Y, Z axes. Travel axis is always 1.</returns>
    public static int[] GetAxisSizes(
        this CrossSection crossSection,
        VoxelDirection direction,
        CrossSectionOrientation orientation) {
        var sizes = new int[3];
        var (first, second) = direction.GetPerpendicularAxes();
        int travelAxis = direction.Axis();

        sizes[travelAxis] = 1; // Always 1 voxel thick in travel direction

        if (orientation == CrossSectionOrientation.Flat) {
            sizes[first] = crossSection.Width;
            sizes[second] = crossSection.Height;
        } else {
            sizes[first] = crossSection.Height;
            sizes[second] = crossSection.Width;
        }

        return sizes;
    }

    /// <summary>
    /// Gets the orientation that makes the cable lie flat on the surface.
    /// Width (smaller dimension) aligns with the upright direction (perpendicular to surface).
    /// Height (larger dimension) lies along the surface.
    /// </summary>
    /// <param name="travelDir">The direction the cable is traveling.</param>
    /// <param name="uprightDir">The face direction (perpendicular to surface).</param>
    /// <returns>The orientation that places the cable flat on the surface.</returns>
    public static CrossSectionOrientation GetOrientationForUpright(
        this VoxelDirection travelDir,
        VoxelDirection uprightDir) {
        var (first, second) = travelDir.GetPerpendicularAxes();
        int uprightAxis = uprightDir.Axis();

        // Width should align with upright direction (cable lies flat on surface)
        // Flat: Width on first axis; Upright: Width on second axis
        return uprightAxis == first
            ? CrossSectionOrientation.Flat
            : CrossSectionOrientation.Upright;
    }

    /// <summary>
    /// Enumerates all voxel positions in a cross-section at the given anchor position.
    /// The anchor is the minimum corner of the cross-section.
    /// </summary>
    public static IEnumerable<VoxelPos> GetVoxelPositions(
        this CrossSection crossSection,
        VoxelPos anchor,
        VoxelDirection direction,
        CrossSectionOrientation orientation) {
        var sizes = crossSection.GetAxisSizes(direction, orientation);

        for (int dz = 0; dz < sizes[2]; dz++) {
            for (int dy = 0; dy < sizes[1]; dy++) {
                for (int dx = 0; dx < sizes[0]; dx++) {
                    yield return anchor.Offset(dx, dy, dz);
                }
            }
        }
    }
}
