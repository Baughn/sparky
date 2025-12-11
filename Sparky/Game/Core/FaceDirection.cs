using System;

namespace Sparky.Game.Core;

/// <summary>
/// Directions within a face (for edges and ports).
/// These are relative to the face's local coordinate system, not world coordinates.
/// <para>
/// When looking at a face:
/// <list type="bullet">
/// <item><description>Top: toward the "top" of the face</description></item>
/// <item><description>Right: toward the "right" of the face</description></item>
/// <item><description>Bottom: toward the "bottom" of the face</description></item>
/// <item><description>Left: toward the "left" of the face</description></item>
/// </list>
/// </para>
/// </summary>
public enum FaceDirection
{
    Top = 0,
    Right = 1,
    Bottom = 2,
    Left = 3,
}

/// <summary>
/// Extension methods for <see cref="FaceDirection"/>.
/// </summary>
public static class FaceDirectionExtensions
{
    /// <summary>
    /// Returns the opposite direction (Top -> Bottom, Left -> Right, etc.).
    /// </summary>
    public static FaceDirection Opposite(this FaceDirection dir) =>
        (FaceDirection)(((int)dir + 2) % 4);

    /// <summary>
    /// Rotates the direction by the given degrees (must be 0, 90, 180, or 270).
    /// Positive rotation is clockwise when looking at the face.
    /// </summary>
    /// <exception cref="ArgumentException">Rotation is not a multiple of 90.</exception>
    public static FaceDirection Rotate(this FaceDirection dir, int degrees)
    {
        if (degrees % 90 != 0)
            throw new ArgumentException(
                "Rotation must be a multiple of 90 degrees.",
                nameof(degrees)
            );

        // Normalize to 0-3 steps (each step is 90 degrees clockwise)
        int steps = ((degrees / 90) % 4 + 4) % 4;
        return (FaceDirection)(((int)dir + steps) % 4);
    }

    /// <summary>
    /// Returns the (du, dv) offset within a face for moving in this direction.
    /// </summary>
    public static (int du, int dv) Offset(this FaceDirection dir) =>
        dir switch
        {
            FaceDirection.Top => (0, 1),
            FaceDirection.Right => (1, 0),
            FaceDirection.Bottom => (0, -1),
            FaceDirection.Left => (-1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(dir)),
        };

    /// <summary>
    /// All four directions in order: Top, Right, Bottom, Left.
    /// </summary>
    public static readonly FaceDirection[] All =
    {
        FaceDirection.Top,
        FaceDirection.Right,
        FaceDirection.Bottom,
        FaceDirection.Left,
    };
}
