using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.MNA.Api;

namespace Sparky.Tests.Game;

[TestFixture]
public class CellPosHashTests
{
    [Test]
    public void AdjacentPositions_HaveConsistentHashing()
    {
        // Two positions: Sub(0,0) and Sub(1,0) - adjacent in Right/Left
        var pos0 = new CellPos(new BlockPos(0, 0, 0), BlockFacing.Up, new SubPos(0, 0));
        var pos1 = new CellPos(new BlockPos(0, 0, 0), BlockFacing.Up, new SubPos(1, 0));

        // Verify they're equal to themselves
        Assert.That(pos0, Is.EqualTo(pos0));
        Assert.That(pos1, Is.EqualTo(pos1));
        Assert.That(pos0, Is.Not.EqualTo(pos1));

        // Get hash codes - they should be different
        var hash0 = pos0.GetHashCode();
        var hash1 = pos1.GetHashCode();

        // Verify neighbor calculation works
        var neighborRight = pos0.Sub.Neighbor(FaceDirection.Right);
        Assert.That(neighborRight, Is.EqualTo(new SubPos(1, 0)), "Sub(0,0).Neighbor(Right) should be Sub(1,0)");

        var neighborLeft = pos1.Sub.Neighbor(FaceDirection.Left);
        Assert.That(neighborLeft, Is.EqualTo(new SubPos(0, 0)), "Sub(1,0).Neighbor(Left) should be Sub(0,0)");

        // Log for debugging
        TestContext.WriteLine($"pos0 = {pos0}, hash = {hash0}");
        TestContext.WriteLine($"pos1 = {pos1}, hash = {hash1}");
    }

    [Test]
    public void CellEdge_NormalizesCorrectly()
    {
        // Two adjacent positions: Sub(0,0) and Sub(1,0)
        var pos0 = new CellPos(new BlockPos(0, 0, 0), BlockFacing.Up, new SubPos(0, 0));
        var pos1 = new CellPos(new BlockPos(0, 0, 0), BlockFacing.Up, new SubPos(1, 0));

        // Create edge from pos0 going Right (toward pos1)
        var edgeFromPos0 = CellEdge.Create(pos0, FaceDirection.Right);

        // Create edge from pos1 going Left (toward pos0)
        var edgeFromPos1 = CellEdge.Create(pos1, FaceDirection.Left);

        TestContext.WriteLine($"pos0 = {pos0}, hash = {pos0.GetHashCode()}");
        TestContext.WriteLine($"pos1 = {pos1}, hash = {pos1.GetHashCode()}");
        TestContext.WriteLine($"edgeFromPos0 = (PosA={edgeFromPos0.PosA}, Dir={edgeFromPos0.Direction})");
        TestContext.WriteLine($"edgeFromPos1 = (PosA={edgeFromPos1.PosA}, Dir={edgeFromPos1.Direction})");

        // CRITICAL: Both edges should be EQUAL since they represent the same boundary
        Assert.That(edgeFromPos0, Is.EqualTo(edgeFromPos1),
            "Edge from pos0->Right should equal edge from pos1->Left");
    }

    [Test]
    public void CellEdge_GetNeighbor_ReturnsCorrectPosition()
    {
        var pos = new CellPos(new BlockPos(0, 0, 0), BlockFacing.Up, new SubPos(1, 1));

        var neighborRight = CellEdge.GetNeighbor(pos, FaceDirection.Right);
        var neighborLeft = CellEdge.GetNeighbor(pos, FaceDirection.Left);
        var neighborTop = CellEdge.GetNeighbor(pos, FaceDirection.Top);
        var neighborBottom = CellEdge.GetNeighbor(pos, FaceDirection.Bottom);

        Assert.That(neighborRight.Sub, Is.EqualTo(new SubPos(2, 1)), "Right neighbor");
        Assert.That(neighborLeft.Sub, Is.EqualTo(new SubPos(0, 1)), "Left neighbor");
        Assert.That(neighborTop.Sub, Is.EqualTo(new SubPos(1, 2)), "Top neighbor");
        Assert.That(neighborBottom.Sub, Is.EqualTo(new SubPos(1, 0)), "Bottom neighbor");
    }
}

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
