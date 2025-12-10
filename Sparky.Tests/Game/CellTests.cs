using NUnit.Framework;
using Sparky.Game.Core;

namespace Sparky.Tests.Game;

[TestFixture]
public class CellTests
{
    #region CellId Tests

    [Test]
    public void CellId_Zero_IsNotValid()
    {
        var id = new CellId(0);
        Assert.That(id.IsValid, Is.False);
    }

    [Test]
    public void CellId_Positive_IsValid()
    {
        var id = new CellId(1);
        Assert.That(id.IsValid, Is.True);
    }

    [Test]
    public void CellId_ToString_IncludesValue()
    {
        var id = new CellId(42);
        Assert.That(id.ToString(), Does.Contain("42"));
    }

    #endregion

    #region CellVisualState Tests

    [Test]
    public void CellVisualState_Default_IsZeroAndInactive()
    {
        var state = CellVisualState.Default;

        Assert.That(state.VoltageNormalized, Is.EqualTo(0));
        Assert.That(state.CurrentMagnitude, Is.EqualTo(0));
        Assert.That(state.CurrentFlowDirection, Is.Null);
        Assert.That(state.PowerDissipation, Is.EqualTo(0));
        Assert.That(state.ChargeLevel, Is.EqualTo(0));
        Assert.That(state.IsActive, Is.False);
    }

    [Test]
    public void CellVisualState_ForConductor_SetsCorrectFields()
    {
        var state = CellVisualState.ForConductor(0.5f, 0.1f, FaceDirection.Right);

        Assert.That(state.VoltageNormalized, Is.EqualTo(0.5f));
        Assert.That(state.CurrentMagnitude, Is.EqualTo(0.1f));
        Assert.That(state.CurrentFlowDirection, Is.EqualTo(FaceDirection.Right));
        Assert.That(state.IsActive, Is.True);
    }

    [Test]
    public void CellVisualState_ForConductor_NegativeCurrent_UsesAbsoluteValue()
    {
        var state = CellVisualState.ForConductor(0.5f, -0.1f, FaceDirection.Left);
        Assert.That(state.CurrentMagnitude, Is.EqualTo(0.1f));
    }

    [Test]
    public void CellVisualState_ForConductor_ZeroCurrent_IsNotActive()
    {
        var state = CellVisualState.ForConductor(0.5f, 0, null);
        Assert.That(state.IsActive, Is.False);
    }

    [Test]
    public void CellVisualState_ForResistor_IncludesPower()
    {
        var state = CellVisualState.ForResistor(0.5f, 0.1f, FaceDirection.Right, 1.0f);

        Assert.That(state.PowerDissipation, Is.EqualTo(1.0f));
        Assert.That(state.IsActive, Is.True);
    }

    #endregion

    #region Cell Rotation Tests

    // We need a concrete Cell implementation for testing
    private class TestCell : Cell
    {
        public override CellType Type => CellType.Wire;
    }

    [Test]
    public void Cell_LocalToWorld_NoRotation_ReturnsSame()
    {
        var cell = new TestCell { Rotation = 0 };

        Assert.That(cell.LocalToWorld(FaceDirection.Top), Is.EqualTo(FaceDirection.Top));
        Assert.That(cell.LocalToWorld(FaceDirection.Right), Is.EqualTo(FaceDirection.Right));
    }

    [Test]
    public void Cell_LocalToWorld_90DegreeRotation_RotatesClockwise()
    {
        var cell = new TestCell { Rotation = 90 };

        Assert.That(cell.LocalToWorld(FaceDirection.Top), Is.EqualTo(FaceDirection.Right));
        Assert.That(cell.LocalToWorld(FaceDirection.Right), Is.EqualTo(FaceDirection.Bottom));
        Assert.That(cell.LocalToWorld(FaceDirection.Bottom), Is.EqualTo(FaceDirection.Left));
        Assert.That(cell.LocalToWorld(FaceDirection.Left), Is.EqualTo(FaceDirection.Top));
    }

    [Test]
    public void Cell_LocalToWorld_180DegreeRotation_FlipsDirections()
    {
        var cell = new TestCell { Rotation = 180 };

        Assert.That(cell.LocalToWorld(FaceDirection.Top), Is.EqualTo(FaceDirection.Bottom));
        Assert.That(cell.LocalToWorld(FaceDirection.Right), Is.EqualTo(FaceDirection.Left));
    }

    [Test]
    public void Cell_LocalToWorld_270DegreeRotation_RotatesCounterClockwise()
    {
        var cell = new TestCell { Rotation = 270 };

        Assert.That(cell.LocalToWorld(FaceDirection.Top), Is.EqualTo(FaceDirection.Left));
        Assert.That(cell.LocalToWorld(FaceDirection.Right), Is.EqualTo(FaceDirection.Top));
    }

    [Test]
    public void Cell_WorldToLocal_InversesLocalToWorld()
    {
        var cell = new TestCell { Rotation = 90 };

        foreach (var dir in FaceDirectionExtensions.All)
        {
            var world = cell.LocalToWorld(dir);
            var backToLocal = cell.WorldToLocal(world);
            Assert.That(backToLocal, Is.EqualTo(dir),
                $"WorldToLocal should inverse LocalToWorld for {dir}");
        }
    }

    [Test]
    public void Cell_AsElectrical_NotImplementing_ReturnsNull()
    {
        var cell = new TestCell();
        Assert.That(cell.AsElectrical(), Is.Null);
    }

    #endregion
}
