using NUnit.Framework;
using Sparky.Game.Core;

namespace Sparky.Tests.Game;

[TestFixture]
public class BlockPosTests
{
    [Test]
    public void Zero_ReturnsOrigin()
    {
        var zero = BlockPos.Zero;
        Assert.That(zero.X, Is.EqualTo(0));
        Assert.That(zero.Y, Is.EqualTo(0));
        Assert.That(zero.Z, Is.EqualTo(0));
    }

    [Test]
    public void Neighbor_North_DecreasesZ()
    {
        var pos = new BlockPos(5, 10, 20);
        var neighbor = pos.Neighbor(BlockFacing.North);
        Assert.That(neighbor, Is.EqualTo(new BlockPos(5, 10, 19)));
    }

    [Test]
    public void Neighbor_South_IncreasesZ()
    {
        var pos = new BlockPos(5, 10, 20);
        var neighbor = pos.Neighbor(BlockFacing.South);
        Assert.That(neighbor, Is.EqualTo(new BlockPos(5, 10, 21)));
    }

    [Test]
    public void Neighbor_East_IncreasesX()
    {
        var pos = new BlockPos(5, 10, 20);
        var neighbor = pos.Neighbor(BlockFacing.East);
        Assert.That(neighbor, Is.EqualTo(new BlockPos(6, 10, 20)));
    }

    [Test]
    public void Neighbor_West_DecreasesX()
    {
        var pos = new BlockPos(5, 10, 20);
        var neighbor = pos.Neighbor(BlockFacing.West);
        Assert.That(neighbor, Is.EqualTo(new BlockPos(4, 10, 20)));
    }

    [Test]
    public void Neighbor_Up_IncreasesY()
    {
        var pos = new BlockPos(5, 10, 20);
        var neighbor = pos.Neighbor(BlockFacing.Up);
        Assert.That(neighbor, Is.EqualTo(new BlockPos(5, 11, 20)));
    }

    [Test]
    public void Neighbor_Down_DecreasesY()
    {
        var pos = new BlockPos(5, 10, 20);
        var neighbor = pos.Neighbor(BlockFacing.Down);
        Assert.That(neighbor, Is.EqualTo(new BlockPos(5, 9, 20)));
    }

    [Test]
    public void Neighbor_OppositeDirections_ReturnOriginal()
    {
        var pos = new BlockPos(5, 10, 20);
        foreach (var facing in BlockFacingExtensions.All)
        {
            var neighborThenBack = pos.Neighbor(facing).Neighbor(facing.Opposite());
            Assert.That(neighborThenBack, Is.EqualTo(pos),
                $"Going {facing} then {facing.Opposite()} should return to original");
        }
    }

    [Test]
    public void Offset_AddsToPosition()
    {
        var pos = new BlockPos(5, 10, 20);
        var offset = pos.Offset(1, 2, 3);
        Assert.That(offset, Is.EqualTo(new BlockPos(6, 12, 23)));
    }

    [Test]
    public void Offset_NegativeValues_SubtractsFromPosition()
    {
        var pos = new BlockPos(5, 10, 20);
        var offset = pos.Offset(-2, -3, -4);
        Assert.That(offset, Is.EqualTo(new BlockPos(3, 7, 16)));
    }

    [Test]
    public void ManhattanDistance_AdjacentBlocks_ReturnsOne()
    {
        var pos = new BlockPos(5, 10, 20);
        foreach (var facing in BlockFacingExtensions.All)
        {
            var neighbor = pos.Neighbor(facing);
            Assert.That(pos.ManhattanDistance(neighbor), Is.EqualTo(1));
        }
    }

    [Test]
    public void ManhattanDistance_SamePosition_ReturnsZero()
    {
        var pos = new BlockPos(5, 10, 20);
        Assert.That(pos.ManhattanDistance(pos), Is.EqualTo(0));
    }

    [Test]
    public void ManhattanDistance_DiagonalBlocks_ReturnsSumOfDeltas()
    {
        var pos1 = new BlockPos(0, 0, 0);
        var pos2 = new BlockPos(3, 4, 5);
        Assert.That(pos1.ManhattanDistance(pos2), Is.EqualTo(12)); // 3 + 4 + 5
    }

    [Test]
    public void ToString_FormatsCorrectly()
    {
        var pos = new BlockPos(5, 10, 20);
        Assert.That(pos.ToString(), Is.EqualTo("(5, 10, 20)"));
    }

    [Test]
    public void Equality_SameValues_AreEqual()
    {
        var pos1 = new BlockPos(5, 10, 20);
        var pos2 = new BlockPos(5, 10, 20);
        Assert.That(pos1, Is.EqualTo(pos2));
        Assert.That(pos1 == pos2, Is.True);
    }

    [Test]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var pos1 = new BlockPos(5, 10, 20);
        var pos2 = new BlockPos(5, 10, 21);
        Assert.That(pos1, Is.Not.EqualTo(pos2));
        Assert.That(pos1 != pos2, Is.True);
    }
}
