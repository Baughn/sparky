namespace Sparky.Game.Core;

/// <summary>
/// Full cell position: which block, which face of the block, and where on that face.
/// <para>
/// This is the complete coordinate for a circuit component in 3D space:
/// <list type="bullet">
/// <item><description><see cref="Block"/>: The VS block coordinates (X, Y, Z)</description></item>
/// <item><description><see cref="Face"/>: Which of the 6 faces (North, East, South, West, Up, Down)</description></item>
/// <item><description><see cref="Sub"/>: Position within the 16x16 pixel grid on that face</description></item>
/// </list>
/// </para>
/// </summary>
public readonly record struct CellPos(BlockPos Block, BlockFacing Face, SubPos Sub)
{
    /// <summary>
    /// Creates a CellPos for 2D tablet game mode.
    /// Defaults to Y=0, Face=Up, Sub=(0,0).
    /// The (x, z) coordinates become block X and Z.
    /// </summary>
    public static CellPos At2D(int x, int z) =>
        new(new BlockPos(x, 0, z), BlockFacing.Up, SubPos.Zero);

    /// <summary>
    /// Creates a CellPos at the center of the given block face.
    /// </summary>
    public static CellPos AtFaceCenter(BlockPos block, BlockFacing face) =>
        new(block, face, SubPos.Center);

    /// <summary>
    /// Returns true if the sub-position is valid (within 0-15 range).
    /// </summary>
    public bool IsValid => Sub.IsValid;

    /// <summary>
    /// Returns the position moved in the given direction within the face.
    /// May cross block boundaries if at the edge of a face.
    /// </summary>
    /// <remarks>
    /// If the new sub-position would be outside [0, 15], this method
    /// moves to the adjacent block and wraps the sub-position.
    /// </remarks>
    public CellPos Neighbor(FaceDirection dir)
    {
        var newSub = Sub.Neighbor(dir);

        if (newSub.IsValid)
            return new CellPos(Block, Face, newSub);

        // Cross block boundary — need to move to adjacent block
        // This is complex because it depends on which face we're on
        // and which direction we're moving. For now, just clamp.
        // TODO: Implement proper cross-block neighbor calculation.
        return new CellPos(Block, Face, newSub.Clamp());
    }

    /// <summary>
    /// Returns the adjacent face on the neighboring block.
    /// This is the face that would be touching this face if both existed.
    /// </summary>
    public CellPos AdjacentBlockFace()
    {
        var neighborBlock = Block.Neighbor(Face);
        var oppositeFace = Face.Opposite();
        return new CellPos(neighborBlock, oppositeFace, Sub);
    }

    public override string ToString() => $"{Block} {Face} {Sub}";
}
