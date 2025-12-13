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

    #region Material Tests

    [Test]
    public void SetVoxel_WithMaterial_StoresMaterial()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(0, 0, 0);

        grid.SetVoxel(pos, Material.Lead);

        Assert.That(grid.GetMaterial(pos), Is.SameAs(Material.Lead));
        Assert.That(grid.GetVoxelType(pos), Is.EqualTo(VoxelType.Conductor));
    }

    [Test]
    public void SetVoxel_ConductorWithoutMaterial_DefaultsToCopper()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(0, 0, 0);

        grid.SetVoxel(pos, VoxelType.Conductor);

        Assert.That(grid.GetMaterial(pos), Is.SameAs(Material.Copper));
    }

    [Test]
    public void GetMaterial_ReturnsCorrectMaterial()
    {
        var grid = new VoxelGrid();
        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(1, 0, 0);
        var pos3 = new VoxelPos(2, 0, 0);

        grid.SetVoxel(pos1, Material.Copper);
        grid.SetVoxel(pos2, Material.Lead);
        grid.SetVoxel(pos3, Material.Iron);

        Assert.That(grid.GetMaterial(pos1), Is.SameAs(Material.Copper));
        Assert.That(grid.GetMaterial(pos2), Is.SameAs(Material.Lead));
        Assert.That(grid.GetMaterial(pos3), Is.SameAs(Material.Iron));
    }

    [Test]
    public void GetMaterial_AirVoxel_ReturnsNull()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(99, 99, 99);  // Never set

        Assert.That(grid.GetMaterial(pos), Is.Null);
    }

    [Test]
    public void GetMaterial_InsulatorVoxel_ReturnsNull()
    {
        var grid = new VoxelGrid();
        var pos = new VoxelPos(0, 0, 0);
        grid.SetVoxel(pos, VoxelType.Insulator);

        Assert.That(grid.GetMaterial(pos), Is.Null);
    }

    [Test]
    public void Material_PredefinedValues_HaveCorrectResistivity()
    {
        // Verify the predefined materials have expected resistivity ratios
        Assert.That(Material.Copper.Resistivity, Is.EqualTo(0.001));
        Assert.That(Material.Lead.Resistivity, Is.EqualTo(0.01));   // 10x copper
        Assert.That(Material.Iron.Resistivity, Is.EqualTo(0.005)); // 5x copper
        Assert.That(Material.Gold.Resistivity, Is.EqualTo(0.0015)); // 1.5x copper
    }

    #endregion

    #region ResistiveConductor Prism Tests

    [Test]
    public void ResistiveConductor_AdjacentVoxels_AreCoalesced()
    {
        // ResistiveConductor voxels SHOULD be coalesced for storage efficiency
        // (topology handles them specially - each prism gets its own node)
        var grid = new VoxelGrid();

        // Place 3 adjacent ResistiveConductor voxels
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.ResistiveConductor);
        grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.ResistiveConductor);
        grid.SetVoxel(new VoxelPos(3, 0, 0), VoxelType.ResistiveConductor);

        var prisms = grid.GetAllPrisms().ToList();

        // Should be coalesced into 1 prism (3x1x1) for storage efficiency
        Assert.That(prisms, Has.Count.EqualTo(1),
            $"Expected 1 coalesced prism for 3 ResistiveConductor voxels, got {prisms.Count}");

        var (_, prism) = prisms[0];
        Assert.That(prism.SizeX, Is.EqualTo(3), "Prism should be 3x1x1");
        Assert.That(prism.SizeY, Is.EqualTo(1));
        Assert.That(prism.SizeZ, Is.EqualTo(1));
    }

    [Test]
    public void Conductor_AdjacentVoxels_AreCoalesced()
    {
        // Regular Conductor voxels SHOULD be coalesced for storage efficiency
        var grid = new VoxelGrid();

        // Place 3 adjacent Conductor voxels
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(3, 0, 0), VoxelType.Conductor);

        var prisms = grid.GetAllPrisms().ToList();

        // Should be coalesced into 1 prism (3x1x1)
        Assert.That(prisms, Has.Count.EqualTo(1),
            $"Expected 1 coalesced prism for 3 Conductor voxels, got {prisms.Count}");

        var (_, prism) = prisms[0];
        Assert.That(prism.SizeX, Is.EqualTo(3), "Prism should be 3x1x1");
        Assert.That(prism.SizeY, Is.EqualTo(1));
        Assert.That(prism.SizeZ, Is.EqualTo(1));
    }

    #endregion
}
