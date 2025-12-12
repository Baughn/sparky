using NUnit.Framework;
using Sparky.Game.Core;

namespace Sparky.Tests.Game;

[TestFixture]
public class VoxelPosTests
{
    [Test]
    public void Zero_ReturnsOrigin()
    {
        var pos = VoxelPos.Zero;

        Assert.That(pos.X, Is.EqualTo(0));
        Assert.That(pos.Y, Is.EqualTo(0));
        Assert.That(pos.Z, Is.EqualTo(0));
    }

    [Test]
    public void Block_PositiveCoordinates_ReturnsCorrectBlock()
    {
        // Voxel (17, 32, 8) is in block (1, 2, 0)
        var pos = new VoxelPos(17, 32, 8);

        Assert.That(pos.Block.X, Is.EqualTo(1));
        Assert.That(pos.Block.Y, Is.EqualTo(2));
        Assert.That(pos.Block.Z, Is.EqualTo(0));
    }

    [Test]
    public void Block_NegativeCoordinates_ReturnsCorrectBlock()
    {
        // Voxel (-1, -16, -17) is in block (-1, -1, -2)
        var pos = new VoxelPos(-1, -16, -17);

        Assert.That(pos.Block.X, Is.EqualTo(-1));
        Assert.That(pos.Block.Y, Is.EqualTo(-1));
        Assert.That(pos.Block.Z, Is.EqualTo(-2));
    }

    [Test]
    public void Local_ReturnsLocalOffset()
    {
        var pos = new VoxelPos(17, 32, 8);
        var local = pos.Local;

        Assert.That(local.X, Is.EqualTo(1));  // 17 % 16 = 1
        Assert.That(local.Y, Is.EqualTo(0));  // 32 % 16 = 0
        Assert.That(local.Z, Is.EqualTo(8));  // 8 % 16 = 8
    }

    [Test]
    public void FromBlockLocal_CreatesCorrectVoxelPos()
    {
        var block = new BlockPos(2, 3, 4);
        var pos = VoxelPos.FromBlockLocal(block, 5, 6, 7);

        Assert.That(pos.X, Is.EqualTo(2 * 16 + 5));
        Assert.That(pos.Y, Is.EqualTo(3 * 16 + 6));
        Assert.That(pos.Z, Is.EqualTo(4 * 16 + 7));
    }

    [Test]
    public void Neighbor_ReturnsAdjacentPosition()
    {
        var pos = new VoxelPos(10, 10, 10);

        Assert.That(pos.Neighbor(VoxelDirection.XPos), Is.EqualTo(new VoxelPos(11, 10, 10)));
        Assert.That(pos.Neighbor(VoxelDirection.XNeg), Is.EqualTo(new VoxelPos(9, 10, 10)));
        Assert.That(pos.Neighbor(VoxelDirection.YPos), Is.EqualTo(new VoxelPos(10, 11, 10)));
        Assert.That(pos.Neighbor(VoxelDirection.YNeg), Is.EqualTo(new VoxelPos(10, 9, 10)));
        Assert.That(pos.Neighbor(VoxelDirection.ZPos), Is.EqualTo(new VoxelPos(10, 10, 11)));
        Assert.That(pos.Neighbor(VoxelDirection.ZNeg), Is.EqualTo(new VoxelPos(10, 10, 9)));
    }
}

[TestFixture]
public class VoxelGridTests
{
    [Test]
    public void NewGrid_IsEmpty()
    {
        var grid = new VoxelGrid();

        Assert.That(grid.VoxelCount, Is.EqualTo(0));
    }

    [Test]
    public void SetVoxel_Conductor_IncreasesCount()
    {
        var grid = new VoxelGrid();

        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);

        Assert.That(grid.VoxelCount, Is.EqualTo(1));
    }

    [Test]
    public void SetVoxel_Air_RemovesVoxel()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(0, 0, 0);
        grid.SetVoxel(pos, VoxelType.Conductor);

        grid.SetVoxel(pos, VoxelType.Air);

        Assert.That(grid.VoxelCount, Is.EqualTo(0));
    }

    [Test]
    public void GetVoxelType_EmptyPosition_ReturnsAir()
    {
        var grid = new VoxelGrid();

        var type = grid.GetVoxelType(new VoxelPos(99, 99, 99));

        Assert.That(type, Is.EqualTo(VoxelType.Air));
    }

    [Test]
    public void GetVoxelType_SetPosition_ReturnsCorrectType()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(5, 5, 5);
        grid.SetVoxel(pos, VoxelType.Insulator);

        var type = grid.GetVoxelType(pos);

        Assert.That(type, Is.EqualTo(VoxelType.Insulator));
    }

    [Test]
    public void IsConductor_ConductorVoxel_ReturnsTrue()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(0, 0, 0);
        grid.SetVoxel(pos, VoxelType.Conductor);

        Assert.That(grid.IsConductor(pos), Is.True);
    }

    [Test]
    public void IsConductor_InsulatorVoxel_ReturnsFalse()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(0, 0, 0);
        grid.SetVoxel(pos, VoxelType.Insulator);

        Assert.That(grid.IsConductor(pos), Is.False);
    }

    [Test]
    public void GetAdjacentConductors_ReturnsOnlyConductorNeighbors()
    {
        var grid = new VoxelGrid();
        var center = new VoxelPos(5, 5, 5);

        // Set up: center surrounded by mix of conductors, insulators, and air
        grid.SetVoxel(center.Neighbor(VoxelDirection.XPos), VoxelType.Conductor);
        grid.SetVoxel(center.Neighbor(VoxelDirection.XNeg), VoxelType.Insulator);
        grid.SetVoxel(center.Neighbor(VoxelDirection.YPos), VoxelType.Conductor);
        // YNeg, ZPos, ZNeg are Air

        var adjacent = grid.GetAdjacentConductors(center).ToList();

        Assert.That(adjacent, Has.Count.EqualTo(2));
        Assert.That(adjacent, Does.Contain(center.Neighbor(VoxelDirection.XPos)));
        Assert.That(adjacent, Does.Contain(center.Neighbor(VoxelDirection.YPos)));
    }

    [Test]
    public void GetAllConductors_ReturnsOnlyConductors()
    {
        var grid = new VoxelGrid();
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Insulator);
        grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);

        var conductors = grid.GetAllConductors().ToList();

        Assert.That(conductors, Has.Count.EqualTo(2));
    }
}
