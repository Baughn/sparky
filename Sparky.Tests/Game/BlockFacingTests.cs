using NUnit.Framework;
using Sparky.Game.Core;

namespace Sparky.Tests.Game;

[TestFixture]
public class BlockFacingTests {
    [Test]
    public void Opposite_ReturnsCorrectOpposite() {
        Assert.That(BlockFacing.North.Opposite(), Is.EqualTo(BlockFacing.South));
        Assert.That(BlockFacing.South.Opposite(), Is.EqualTo(BlockFacing.North));
        Assert.That(BlockFacing.East.Opposite(), Is.EqualTo(BlockFacing.West));
        Assert.That(BlockFacing.West.Opposite(), Is.EqualTo(BlockFacing.East));
        Assert.That(BlockFacing.Up.Opposite(), Is.EqualTo(BlockFacing.Down));
        Assert.That(BlockFacing.Down.Opposite(), Is.EqualTo(BlockFacing.Up));
    }

    [Test]
    public void Opposite_DoubleOpposite_ReturnsOriginal() {
        foreach (var facing in BlockFacingExtensions.All) {
            Assert.That(facing.Opposite().Opposite(), Is.EqualTo(facing));
        }
    }

    [Test]
    public void IsHorizontal_ReturnsTrueForNESW() {
        Assert.That(BlockFacing.North.IsHorizontal(), Is.True);
        Assert.That(BlockFacing.East.IsHorizontal(), Is.True);
        Assert.That(BlockFacing.South.IsHorizontal(), Is.True);
        Assert.That(BlockFacing.West.IsHorizontal(), Is.True);
        Assert.That(BlockFacing.Up.IsHorizontal(), Is.False);
        Assert.That(BlockFacing.Down.IsHorizontal(), Is.False);
    }

    [Test]
    public void IsVertical_ReturnsTrueForUpDown() {
        Assert.That(BlockFacing.Up.IsVertical(), Is.True);
        Assert.That(BlockFacing.Down.IsVertical(), Is.True);
        Assert.That(BlockFacing.North.IsVertical(), Is.False);
        Assert.That(BlockFacing.East.IsVertical(), Is.False);
    }

    [Test]
    public void Normal_ReturnsCorrectVectors() {
        Assert.That(BlockFacing.North.Normal(), Is.EqualTo((0, 0, -1)));
        Assert.That(BlockFacing.South.Normal(), Is.EqualTo((0, 0, 1)));
        Assert.That(BlockFacing.East.Normal(), Is.EqualTo((1, 0, 0)));
        Assert.That(BlockFacing.West.Normal(), Is.EqualTo((-1, 0, 0)));
        Assert.That(BlockFacing.Up.Normal(), Is.EqualTo((0, 1, 0)));
        Assert.That(BlockFacing.Down.Normal(), Is.EqualTo((0, -1, 0)));
    }

    [Test]
    public void All_ContainsSixFacings() {
        Assert.That(BlockFacingExtensions.All, Has.Length.EqualTo(6));
    }

    [Test]
    public void Horizontal_ContainsFourFacings() {
        Assert.That(BlockFacingExtensions.Horizontal, Has.Length.EqualTo(4));
        Assert.That(BlockFacingExtensions.Horizontal, Does.Not.Contain(BlockFacing.Up));
        Assert.That(BlockFacingExtensions.Horizontal, Does.Not.Contain(BlockFacing.Down));
    }
}
