namespace Sparky.Game.Core;

/// <summary>
/// Strongly-typed identifier for a grid cell.
/// </summary>
public readonly record struct CellId(int Value)
{
    /// <summary>
    /// Returns true if this is a valid (non-zero) ID.
    /// Zero is reserved for "no cell" / invalid.
    /// </summary>
    public bool IsValid => Value > 0;

    public override string ToString() => $"Cell({Value})";
}
