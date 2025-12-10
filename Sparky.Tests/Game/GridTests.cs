using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.MNA.Api;

namespace Sparky.Tests.Game;

[TestFixture]
public class GridTests
{
    // Simple test cell implementation
    private class TestCell : Cell
    {
        public override CellType Type => CellType.Wire;
    }

    #region Placement Tests

    [Test]
    public void PlaceCell_ValidPosition_ReturnsValidId()
    {
        var grid = new Grid();
        var cell = new TestCell();

        var id = grid.PlaceCell(cell, CellPos.At2D(5, 5));

        Assert.That(id.IsValid, Is.True);
        Assert.That(grid.CellCount, Is.EqualTo(1));
    }

    [Test]
    public void PlaceCell_SetsPositionOnCell()
    {
        var grid = new Grid();
        var cell = new TestCell();
        var pos = CellPos.At2D(5, 10);

        grid.PlaceCell(cell, pos);

        Assert.That(cell.Position, Is.EqualTo(pos));
    }

    [Test]
    public void PlaceCell_SetsIdOnCell()
    {
        var grid = new Grid();
        var cell = new TestCell();

        var id = grid.PlaceCell(cell, CellPos.At2D(5, 5));

        Assert.That(cell.Id, Is.EqualTo(id));
    }

    [Test]
    public void PlaceCell_SetsRotationOnCell()
    {
        var grid = new Grid();
        var cell = new TestCell();

        grid.PlaceCell(cell, CellPos.At2D(5, 5), rotation: 90);

        Assert.That(cell.Rotation, Is.EqualTo(90));
    }

    [Test]
    public void PlaceCell_PositionOccupied_ThrowsArgumentException()
    {
        var grid = new Grid();
        var pos = CellPos.At2D(5, 5);

        grid.PlaceCell(new TestCell(), pos);

        Assert.Throws<ArgumentException>(() =>
            grid.PlaceCell(new TestCell(), pos));
    }

    [Test]
    public void PlaceCell_InvalidSubPosition_ThrowsArgumentException()
    {
        var grid = new Grid();
        var invalidPos = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(20, 0));

        Assert.Throws<ArgumentException>(() =>
            grid.PlaceCell(new TestCell(), invalidPos));
    }

    [Test]
    public void PlaceCell_MarksDirty()
    {
        var grid = new Grid();
        Assert.That(grid.IsDirty, Is.False);

        grid.PlaceCell(new TestCell(), CellPos.At2D(5, 5));

        Assert.That(grid.IsDirty, Is.True);
    }

    #endregion

    #region Removal Tests

    [Test]
    public void RemoveCell_ExistingPosition_ReturnsTrue()
    {
        var grid = new Grid();
        var pos = CellPos.At2D(5, 5);
        grid.PlaceCell(new TestCell(), pos);

        var removed = grid.RemoveCell(pos);

        Assert.That(removed, Is.True);
        Assert.That(grid.CellCount, Is.EqualTo(0));
    }

    [Test]
    public void RemoveCell_EmptyPosition_ReturnsFalse()
    {
        var grid = new Grid();

        var removed = grid.RemoveCell(CellPos.At2D(5, 5));

        Assert.That(removed, Is.False);
    }

    [Test]
    public void RemoveCell_MarksDirty()
    {
        var grid = new Grid();
        var pos = CellPos.At2D(5, 5);
        grid.PlaceCell(new TestCell(), pos);

        // Clear dirty flag (simulate rebuild)
        var sim = new SimulationManager();
        grid.BindSimulation(sim);
        grid.RebuildTopology();
        Assert.That(grid.IsDirty, Is.False);

        grid.RemoveCell(pos);

        Assert.That(grid.IsDirty, Is.True);
    }

    #endregion

    #region Retrieval Tests

    [Test]
    public void GetCell_ExistingPosition_ReturnsCell()
    {
        var grid = new Grid();
        var cell = new TestCell();
        var pos = CellPos.At2D(5, 5);
        grid.PlaceCell(cell, pos);

        var retrieved = grid.GetCell(pos);

        Assert.That(retrieved, Is.SameAs(cell));
    }

    [Test]
    public void GetCell_EmptyPosition_ReturnsNull()
    {
        var grid = new Grid();

        var retrieved = grid.GetCell(CellPos.At2D(5, 5));

        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public void HasCell_ExistingPosition_ReturnsTrue()
    {
        var grid = new Grid();
        var pos = CellPos.At2D(5, 5);
        grid.PlaceCell(new TestCell(), pos);

        Assert.That(grid.HasCell(pos), Is.True);
    }

    [Test]
    public void HasCell_EmptyPosition_ReturnsFalse()
    {
        var grid = new Grid();

        Assert.That(grid.HasCell(CellPos.At2D(5, 5)), Is.False);
    }

    [Test]
    public void GetAllCells_ReturnsAllPlacedCells()
    {
        var grid = new Grid();
        var cell1 = new TestCell();
        var cell2 = new TestCell();
        var cell3 = new TestCell();

        grid.PlaceCell(cell1, CellPos.At2D(0, 0));
        grid.PlaceCell(cell2, CellPos.At2D(1, 0));
        grid.PlaceCell(cell3, CellPos.At2D(2, 0));

        var allCells = grid.GetAllCells().ToList();

        Assert.That(allCells, Has.Count.EqualTo(3));
        Assert.That(allCells, Does.Contain(cell1));
        Assert.That(allCells, Does.Contain(cell2));
        Assert.That(allCells, Does.Contain(cell3));
    }

    [Test]
    public void GetCellById_ExistingId_ReturnsCell()
    {
        var grid = new Grid();
        var cell = new TestCell();
        var id = grid.PlaceCell(cell, CellPos.At2D(5, 5));

        var retrieved = grid.GetCellById(id);

        Assert.That(retrieved, Is.SameAs(cell));
    }

    [Test]
    public void GetCellById_NonExistingId_ReturnsNull()
    {
        var grid = new Grid();

        var retrieved = grid.GetCellById(new CellId(999));

        Assert.That(retrieved, Is.Null);
    }

    #endregion

    #region Simulation Binding Tests

    [Test]
    public void BindSimulation_SetsSimulation()
    {
        var grid = new Grid();
        var sim = new SimulationManager();

        grid.BindSimulation(sim);

        Assert.That(grid.Simulation, Is.SameAs(sim));
    }

    [Test]
    public void BindSimulation_MarksDirty()
    {
        var grid = new Grid();
        var sim = new SimulationManager();

        grid.BindSimulation(sim);

        Assert.That(grid.IsDirty, Is.True);
    }

    [Test]
    public void Simulation_NotBound_ThrowsInvalidOperationException()
    {
        var grid = new Grid();

        Assert.Throws<InvalidOperationException>(() => _ = grid.Simulation);
    }

    #endregion

    #region Sparse Storage Tests

    [Test]
    public void SparseStorage_OnlyAllocatesForPlacedCells()
    {
        var grid = new Grid();

        // Place cells at very distant positions
        grid.PlaceCell(new TestCell(), CellPos.At2D(0, 0));
        grid.PlaceCell(new TestCell(), CellPos.At2D(1000, 1000));

        // Only 2 cells should be stored
        Assert.That(grid.CellCount, Is.EqualTo(2));
    }

    [Test]
    public void SparseStorage_DifferentFaces_SameBlockPos_AreDifferent()
    {
        var grid = new Grid();
        var block = new BlockPos(5, 10, 15);

        grid.PlaceCell(new TestCell(), new CellPos(block, BlockFacing.Up, SubPos.Zero));
        grid.PlaceCell(new TestCell(), new CellPos(block, BlockFacing.Down, SubPos.Zero));
        grid.PlaceCell(new TestCell(), new CellPos(block, BlockFacing.North, SubPos.Zero));

        Assert.That(grid.CellCount, Is.EqualTo(3));
    }

    [Test]
    public void SparseStorage_SameFace_DifferentSubPos_AreDifferent()
    {
        var grid = new Grid();
        var pos1 = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(0, 0));
        var pos2 = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(0, 1));
        var pos3 = new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(1, 0));

        grid.PlaceCell(new TestCell(), pos1);
        grid.PlaceCell(new TestCell(), pos2);
        grid.PlaceCell(new TestCell(), pos3);

        Assert.That(grid.CellCount, Is.EqualTo(3));
    }

    #endregion

    #region Topology Event Tests

    [Test]
    public void RebuildTopology_RaisesTopologyChangedEvent()
    {
        var grid = new Grid();
        grid.BindSimulation(new SimulationManager());

        bool eventRaised = false;
        grid.TopologyChanged += () => eventRaised = true;

        grid.RebuildTopology();

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void RebuildTopology_ClearsDirtyFlag()
    {
        var grid = new Grid();
        grid.BindSimulation(new SimulationManager());
        grid.PlaceCell(new TestCell(), CellPos.At2D(0, 0));
        Assert.That(grid.IsDirty, Is.True);

        grid.RebuildTopology();

        Assert.That(grid.IsDirty, Is.False);
    }

    #endregion
}
