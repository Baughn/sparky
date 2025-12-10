using NUnit.Framework;
using Sparky.Game.Core;

namespace Sparky.Tests.Game;

[TestFixture]
public class CellPosTests
{
    [Test]
    public void At2D_CreatesCorrectPosition()
    {
        var pos = CellPos.At2D(5, 10);

        Assert.That(pos.Block.X, Is.EqualTo(5));
        Assert.That(pos.Block.Y, Is.EqualTo(0));
        Assert.That(pos.Block.Z, Is.EqualTo(10));
        Assert.That(pos.Face, Is.EqualTo(BlockFacing.Up));
        Assert.That(pos.Sub, Is.EqualTo(SubPos.Zero));
    }

    [Test]
    public void AtFaceCenter_CreatesCorrectPosition()
    {
        var block = new BlockPos(5, 10, 20);
        var pos = CellPos.AtFaceCenter(block, BlockFacing.North);

        Assert.That(pos.Block, Is.EqualTo(block));
        Assert.That(pos.Face, Is.EqualTo(BlockFacing.North));
        Assert.That(pos.Sub, Is.EqualTo(SubPos.Center));
    }

    [Test]
    public void IsValid_ValidSubPos_ReturnsTrue()
    {
        var pos = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(8, 8));
        Assert.That(pos.IsValid, Is.True);
    }

    [Test]
    public void IsValid_InvalidSubPos_ReturnsFalse()
    {
        var pos = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(20, 8));
        Assert.That(pos.IsValid, Is.False);
    }

    [Test]
    public void AdjacentBlockFace_ReturnsCorrectNeighbor()
    {
        var pos = new CellPos(new BlockPos(5, 10, 20), BlockFacing.East, new SubPos(8, 8));
        var adjacent = pos.AdjacentBlockFace();

        Assert.That(adjacent.Block, Is.EqualTo(new BlockPos(6, 10, 20)));
        Assert.That(adjacent.Face, Is.EqualTo(BlockFacing.West));
        Assert.That(adjacent.Sub, Is.EqualTo(new SubPos(8, 8)));
    }

    [Test]
    public void AdjacentBlockFace_AllFacings_ReturnsCorrectNeighbors()
    {
        var block = new BlockPos(5, 10, 20);
        var sub = new SubPos(8, 8);

        foreach (var facing in BlockFacingExtensions.All)
        {
            var pos = new CellPos(block, facing, sub);
            var adjacent = pos.AdjacentBlockFace();

            Assert.That(adjacent.Block, Is.EqualTo(block.Neighbor(facing)),
                $"Adjacent block for {facing} should be neighbor in that direction");
            Assert.That(adjacent.Face, Is.EqualTo(facing.Opposite()),
                $"Adjacent face for {facing} should be opposite");
            Assert.That(adjacent.Sub, Is.EqualTo(sub),
                "Sub position should be preserved");
        }
    }

    [Test]
    public void Neighbor_WithinBounds_MovesWithinFace()
    {
        var pos = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(8, 8));

        var top = pos.Neighbor(FaceDirection.Top);
        Assert.That(top.Sub, Is.EqualTo(new SubPos(8, 9)));
        Assert.That(top.Block, Is.EqualTo(pos.Block));

        var right = pos.Neighbor(FaceDirection.Right);
        Assert.That(right.Sub, Is.EqualTo(new SubPos(9, 8)));

        var bottom = pos.Neighbor(FaceDirection.Bottom);
        Assert.That(bottom.Sub, Is.EqualTo(new SubPos(8, 7)));

        var left = pos.Neighbor(FaceDirection.Left);
        Assert.That(left.Sub, Is.EqualTo(new SubPos(7, 8)));
    }

    [Test]
    public void Neighbor_AtEdge_ClampsToEdge()
    {
        // At the edge of the sub-grid
        var pos = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(15, 15));

        // Moving right/top would go out of bounds — currently clamps
        var right = pos.Neighbor(FaceDirection.Right);
        Assert.That(right.Sub.U, Is.EqualTo(15)); // Clamped

        var top = pos.Neighbor(FaceDirection.Top);
        Assert.That(top.Sub.V, Is.EqualTo(15)); // Clamped
    }

    [Test]
    public void ToString_FormatsCorrectly()
    {
        var pos = new CellPos(new BlockPos(5, 10, 20), BlockFacing.North, new SubPos(8, 12));
        Assert.That(pos.ToString(), Does.Contain("5"));
        Assert.That(pos.ToString(), Does.Contain("North"));
    }

    [Test]
    public void Equality_SameValues_AreEqual()
    {
        var pos1 = new CellPos(new BlockPos(5, 10, 20), BlockFacing.Up, new SubPos(8, 8));
        var pos2 = new CellPos(new BlockPos(5, 10, 20), BlockFacing.Up, new SubPos(8, 8));
        Assert.That(pos1, Is.EqualTo(pos2));
    }

    [Test]
    public void Equality_DifferentBlock_AreNotEqual()
    {
        var pos1 = new CellPos(new BlockPos(5, 10, 20), BlockFacing.Up, new SubPos(8, 8));
        var pos2 = new CellPos(new BlockPos(5, 10, 21), BlockFacing.Up, new SubPos(8, 8));
        Assert.That(pos1, Is.Not.EqualTo(pos2));
    }

    [Test]
    public void Equality_DifferentFace_AreNotEqual()
    {
        var pos1 = new CellPos(new BlockPos(5, 10, 20), BlockFacing.Up, new SubPos(8, 8));
        var pos2 = new CellPos(new BlockPos(5, 10, 20), BlockFacing.Down, new SubPos(8, 8));
        Assert.That(pos1, Is.Not.EqualTo(pos2));
    }

    [Test]
    public void Equality_DifferentSub_AreNotEqual()
    {
        var pos1 = new CellPos(new BlockPos(5, 10, 20), BlockFacing.Up, new SubPos(8, 8));
        var pos2 = new CellPos(new BlockPos(5, 10, 20), BlockFacing.Up, new SubPos(8, 9));
        Assert.That(pos1, Is.Not.EqualTo(pos2));
    }
}
