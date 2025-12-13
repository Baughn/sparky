using NUnit.Framework;
using Sparky.Game.Core;
using System.Linq;

namespace Sparky.Tests.Game;

[TestFixture]
public class IncrementalPrismBuilderTests
{
    private IncrementalPrismBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _builder = new IncrementalPrismBuilder();
    }

    #region Basic Operations

    [Test]
    public void NewBuilder_IsEmpty()
    {
        Assert.That(_builder.VoxelCount, Is.EqualTo(0));
        Assert.That(_builder.PrismCount, Is.EqualTo(0));
    }

    [Test]
    public void SetVoxel_SingleVoxel_CanBeRetrieved()
    {
        var pos = new VoxelPos(5, 5, 5);
        _builder.SetVoxel(pos, VoxelType.Conductor, Material.Copper);

        var (type, material) = _builder.GetVoxel(pos);

        Assert.That(type, Is.EqualTo(VoxelType.Conductor));
        Assert.That(material, Is.SameAs(Material.Copper));
        Assert.That(_builder.VoxelCount, Is.EqualTo(1));
    }

    [Test]
    public void SetVoxel_Air_RemovesVoxel()
    {
        var pos = new VoxelPos(5, 5, 5);
        _builder.SetVoxel(pos, VoxelType.Conductor, Material.Copper);

        _builder.SetVoxel(pos, VoxelType.Air, null);

        Assert.That(_builder.VoxelCount, Is.EqualTo(0));
        Assert.That(_builder.GetVoxelType(pos), Is.EqualTo(VoxelType.Air));
    }

    [Test]
    public void GetVoxel_EmptyPosition_ReturnsAir()
    {
        var (type, material) = _builder.GetVoxel(new VoxelPos(99, 99, 99));

        Assert.That(type, Is.EqualTo(VoxelType.Air));
        Assert.That(material, Is.Null);
    }

    #endregion

    #region Prism Building

    [Test]
    public void GetAllPrisms_SingleVoxel_ReturnsSinglePrism()
    {
        _builder.SetVoxel(new VoxelPos(5, 5, 5), VoxelType.Conductor, Material.Copper);

        var prisms = _builder.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(1));
        Assert.That(prisms[0].Prism.SizeX, Is.EqualTo(1));
        Assert.That(prisms[0].Prism.SizeY, Is.EqualTo(1));
        Assert.That(prisms[0].Prism.SizeZ, Is.EqualTo(1));
    }

    [Test]
    public void GetAllPrisms_AdjacentVoxels_CoalescesIntoPrism()
    {
        // Create a 2x1x1 line
        _builder.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        _builder.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor, Material.Copper);

        var prisms = _builder.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(1));
        Assert.That(prisms[0].Prism.SizeX, Is.EqualTo(2));
    }

    [Test]
    public void GetAllPrisms_DifferentMaterials_SeparatePrisms()
    {
        _builder.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        _builder.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor, Material.Lead);

        var prisms = _builder.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetAllPrisms_DifferentTypes_SeparatePrisms()
    {
        _builder.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        _builder.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Insulator, null);

        var prisms = _builder.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetAllPrisms_LargeCube_CoalescesEfficiently()
    {
        // Fill a 4x4x4 cube
        for (int z = 0; z < 4; z++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    _builder.SetVoxel(new VoxelPos(x, y, z), VoxelType.Conductor, Material.Copper);

        var prisms = _builder.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(1));
        Assert.That(prisms[0].Prism.SizeX, Is.EqualTo(4));
        Assert.That(prisms[0].Prism.SizeY, Is.EqualTo(4));
        Assert.That(prisms[0].Prism.SizeZ, Is.EqualTo(4));
    }

    #endregion

    #region Cross-Block Prisms

    [Test]
    public void GetAllPrisms_CrossBlockVoxels_ReturnsSeparatePrisms()
    {
        // Voxels in different blocks
        _builder.SetVoxel(new VoxelPos(15, 0, 0), VoxelType.Conductor, Material.Copper);  // Block (0,0,0)
        _builder.SetVoxel(new VoxelPos(16, 0, 0), VoxelType.Conductor, Material.Copper);  // Block (1,0,0)

        var prisms = _builder.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(2));
        Assert.That(prisms.Select(p => p.Block).Distinct().Count(), Is.EqualTo(2));
    }

    #endregion

    #region Caching and Invalidation

    [Test]
    public void GetAllPrisms_CalledTwice_ReturnsSameResults()
    {
        _builder.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);

        var prisms1 = _builder.GetAllPrisms().ToList();
        var prisms2 = _builder.GetAllPrisms().ToList();

        Assert.That(prisms1, Has.Count.EqualTo(prisms2.Count));
    }

    [Test]
    public void SetVoxel_AfterGetAllPrisms_InvalidatesCache()
    {
        _builder.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        var prisms1 = _builder.GetAllPrisms().ToList();

        _builder.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor, Material.Copper);
        var prisms2 = _builder.GetAllPrisms().ToList();

        // After adding adjacent voxel, should coalesce
        Assert.That(prisms2, Has.Count.EqualTo(1));
        Assert.That(prisms2[0].Prism.SizeX, Is.EqualTo(2));
    }

    [Test]
    public void SetVoxel_DifferentBlock_OnlyInvalidatesAffectedBlock()
    {
        // Set voxels in two different blocks
        _builder.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);   // Block (0,0,0)
        _builder.SetVoxel(new VoxelPos(20, 0, 0), VoxelType.Conductor, Material.Copper);  // Block (1,0,0)

        // Get prisms to cache them
        var prisms1 = _builder.GetAllPrisms().ToList();
        Assert.That(prisms1, Has.Count.EqualTo(2));

        // Modify only block (0,0,0)
        _builder.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor, Material.Copper);

        var prisms2 = _builder.GetAllPrisms().ToList();
        Assert.That(prisms2, Has.Count.EqualTo(2)); // Still 2 prisms (one larger, one unchanged)
    }

    #endregion

    #region Batch Operations

    [Test]
    public void SetVoxels_Batch_SetsAllVoxels()
    {
        var voxels = new[]
        {
            (new VoxelPos(0, 0, 0), VoxelType.Conductor, (Material?)Material.Copper),
            (new VoxelPos(1, 0, 0), VoxelType.Conductor, (Material?)Material.Copper),
            (new VoxelPos(2, 0, 0), VoxelType.Conductor, (Material?)Material.Copper)
        };

        _builder.SetVoxels(voxels);

        Assert.That(_builder.VoxelCount, Is.EqualTo(3));
        var prisms = _builder.GetAllPrisms().ToList();
        Assert.That(prisms, Has.Count.EqualTo(1));
        Assert.That(prisms[0].Prism.SizeX, Is.EqualTo(3));
    }

    #endregion

    #region Large Wire Test

    [Test]
    public void LargeWire_MatchesExpectedVoxelCount()
    {
        // Same as benchmark: 3x3x192 wire
        for (int z = 0; z < 192; z++)
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    _builder.SetVoxel(new VoxelPos(x, y, z), VoxelType.ResistiveConductor, Material.Copper);

        Assert.That(_builder.VoxelCount, Is.EqualTo(3 * 3 * 192));
    }

    [Test]
    public void LargeWire_HasReasonablePrismCount()
    {
        // 3x3x192 wire spans multiple blocks
        for (int z = 0; z < 192; z++)
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    _builder.SetVoxel(new VoxelPos(x, y, z), VoxelType.ResistiveConductor, Material.Copper);

        var prisms = _builder.GetAllPrisms().ToList();

        // Should have ~12 prisms (one per block, 192/16 = 12 blocks in Z)
        Assert.That(prisms.Count, Is.LessThanOrEqualTo(20),
            "Should coalesce into a reasonable number of prisms");
    }

    #endregion

    #region Clear

    [Test]
    public void Clear_RemovesAllVoxelsAndPrisms()
    {
        _builder.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        _builder.SetVoxel(new VoxelPos(20, 0, 0), VoxelType.Conductor, Material.Copper);

        _builder.Clear();

        Assert.That(_builder.VoxelCount, Is.EqualTo(0));
        Assert.That(_builder.PrismCount, Is.EqualTo(0));
        Assert.That(_builder.GetAllPrisms().ToList(), Is.Empty);
    }

    #endregion

    #region Negative Coordinates

    [Test]
    public void SetVoxel_NegativeCoordinates_Works()
    {
        var pos = new VoxelPos(-10, -20, -30);
        _builder.SetVoxel(pos, VoxelType.Conductor, Material.Copper);

        Assert.That(_builder.GetVoxelType(pos), Is.EqualTo(VoxelType.Conductor));
        Assert.That(_builder.GetMaterial(pos), Is.SameAs(Material.Copper));
    }

    [Test]
    public void GetAllPrisms_NegativeCoordinates_ReturnsCorrectBlockPos()
    {
        _builder.SetVoxel(new VoxelPos(-10, -10, -10), VoxelType.Conductor, Material.Copper);

        var prisms = _builder.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(1));
        Assert.That(prisms[0].Block.X, Is.EqualTo(-1));
        Assert.That(prisms[0].Block.Y, Is.EqualTo(-1));
        Assert.That(prisms[0].Block.Z, Is.EqualTo(-1));
    }

    #endregion
}
