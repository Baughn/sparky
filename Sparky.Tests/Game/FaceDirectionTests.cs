using NUnit.Framework;
using Sparky.Game.Core;

namespace Sparky.Tests.Game;

[TestFixture]
public class FaceDirectionTests
{
    [Test]
    public void Opposite_ReturnsCorrectOpposite()
    {
        Assert.That(FaceDirection.Top.Opposite(), Is.EqualTo(FaceDirection.Bottom));
        Assert.That(FaceDirection.Bottom.Opposite(), Is.EqualTo(FaceDirection.Top));
        Assert.That(FaceDirection.Left.Opposite(), Is.EqualTo(FaceDirection.Right));
        Assert.That(FaceDirection.Right.Opposite(), Is.EqualTo(FaceDirection.Left));
    }

    [Test]
    public void Opposite_DoubleOpposite_ReturnsOriginal()
    {
        foreach (var dir in FaceDirectionExtensions.All)
        {
            Assert.That(dir.Opposite().Opposite(), Is.EqualTo(dir));
        }
    }

    [Test]
    public void Rotate_0Degrees_ReturnsSame()
    {
        foreach (var dir in FaceDirectionExtensions.All)
        {
            Assert.That(dir.Rotate(0), Is.EqualTo(dir));
        }
    }

    [Test]
    public void Rotate_90Degrees_RotatesClockwise()
    {
        Assert.That(FaceDirection.Top.Rotate(90), Is.EqualTo(FaceDirection.Right));
        Assert.That(FaceDirection.Right.Rotate(90), Is.EqualTo(FaceDirection.Bottom));
        Assert.That(FaceDirection.Bottom.Rotate(90), Is.EqualTo(FaceDirection.Left));
        Assert.That(FaceDirection.Left.Rotate(90), Is.EqualTo(FaceDirection.Top));
    }

    [Test]
    public void Rotate_180Degrees_ReturnsOpposite()
    {
        foreach (var dir in FaceDirectionExtensions.All)
        {
            Assert.That(dir.Rotate(180), Is.EqualTo(dir.Opposite()));
        }
    }

    [Test]
    public void Rotate_270Degrees_RotatesCounterClockwise()
    {
        Assert.That(FaceDirection.Top.Rotate(270), Is.EqualTo(FaceDirection.Left));
        Assert.That(FaceDirection.Left.Rotate(270), Is.EqualTo(FaceDirection.Bottom));
        Assert.That(FaceDirection.Bottom.Rotate(270), Is.EqualTo(FaceDirection.Right));
        Assert.That(FaceDirection.Right.Rotate(270), Is.EqualTo(FaceDirection.Top));
    }

    [Test]
    public void Rotate_360Degrees_ReturnsSame()
    {
        foreach (var dir in FaceDirectionExtensions.All)
        {
            Assert.That(dir.Rotate(360), Is.EqualTo(dir));
        }
    }

    [Test]
    public void Rotate_Negative90Degrees_EqualsPositive270()
    {
        foreach (var dir in FaceDirectionExtensions.All)
        {
            Assert.That(dir.Rotate(-90), Is.EqualTo(dir.Rotate(270)));
        }
    }

    [Test]
    public void Rotate_NonMultipleOf90_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FaceDirection.Top.Rotate(45));
        Assert.Throws<ArgumentException>(() => FaceDirection.Top.Rotate(100));
    }

    [Test]
    public void Offset_ReturnsCorrectDeltas()
    {
        Assert.That(FaceDirection.Top.Offset(), Is.EqualTo((0, 1)));
        Assert.That(FaceDirection.Right.Offset(), Is.EqualTo((1, 0)));
        Assert.That(FaceDirection.Bottom.Offset(), Is.EqualTo((0, -1)));
        Assert.That(FaceDirection.Left.Offset(), Is.EqualTo((-1, 0)));
    }

    [Test]
    public void All_ContainsFourDirections()
    {
        Assert.That(FaceDirectionExtensions.All, Has.Length.EqualTo(4));
    }
}
