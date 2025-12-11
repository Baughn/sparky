using System;

namespace Sparky.Game.Core;

/// <summary>
/// Sub-block position within a face (0-15 each, pixel resolution).
/// <para>
/// Each VS block face contains a 16x16 grid of "pixels" where circuit
/// components can be placed. This allows ribbon cables to be implemented
/// as literal multi-pixel traces.
/// </para>
/// </summary>
public readonly record struct SubPos(int U, int V)
{
    /// <summary>The size of the sub-grid (16 pixels per axis).</summary>
    public const int Size = 16;

    /// <summary>The origin (0, 0).</summary>
    public static SubPos Zero => new(0, 0);

    /// <summary>The center of the sub-grid (7, 7) — note: not exactly centered in 16x16.</summary>
    public static SubPos Center => new(7, 7);

    /// <summary>
    /// Returns true if U and V are both in valid range [0, 15].
    /// </summary>
    public bool IsValid => U >= 0 && U < Size && V >= 0 && V < Size;

    /// <summary>
    /// Returns the neighboring sub-position in the given direction.
    /// May return an invalid position if at the edge.
    /// </summary>
    public SubPos Neighbor(FaceDirection dir)
    {
        var (du, dv) = dir.Offset();
        return new SubPos(U + du, V + dv);
    }

    /// <summary>
    /// Clamps U and V to valid range [0, 15].
    /// </summary>
    public SubPos Clamp() => new(Math.Clamp(U, 0, Size - 1), Math.Clamp(V, 0, Size - 1));

    public override string ToString() => $"[{U}, {V}]";
}
