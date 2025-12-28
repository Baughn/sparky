namespace Sparky.Handbook.Protocol;

/// <summary>
/// 2D grid coordinate for the circuit editor.
/// </summary>
/// <remarks>
/// Maps to VoxelPos on the Y=0 plane when converting to 3D voxel space.
/// </remarks>
public readonly record struct GridPos(int X, int Y) {
    /// <summary>
    /// Returns neighbors in the 4 cardinal directions.
    /// </summary>
    public IEnumerable<GridPos> Neighbors() {
        yield return new GridPos(X - 1, Y);
        yield return new GridPos(X + 1, Y);
        yield return new GridPos(X, Y - 1);
        yield return new GridPos(X, Y + 1);
    }

    public override string ToString() => $"({X}, {Y})";
}
