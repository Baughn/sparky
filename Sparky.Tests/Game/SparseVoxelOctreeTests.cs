using NUnit.Framework;
using Sparky.Game.Core;
using System.Linq;

namespace Sparky.Tests.Game;

[TestFixture]
public class SparseVoxelOctreeTests
{
    private SparseVoxelOctree _svo = null!;

    [SetUp]
    public void SetUp()
    {
        _svo = new SparseVoxelOctree();
    }

    #region Basic Operations

    [Test]
    public void NewOctree_IsEmpty()
    {
        Assert.That(_svo.VoxelCount, Is.EqualTo(0));
    }

    [Test]
    public void Set_SingleVoxel_CanBeRetrieved()
    {
        var pos = new VoxelPos(5, 5, 5);
        _svo.Set(pos, VoxelType.Conductor, Material.Copper);

        var (type, material) = _svo.Get(pos);

        Assert.That(type, Is.EqualTo(VoxelType.Conductor));
        Assert.That(material, Is.SameAs(Material.Copper));
        Assert.That(_svo.VoxelCount, Is.EqualTo(1));
    }

    [Test]
    public void Get_EmptyPosition_ReturnsAir()
    {
        var (type, material) = _svo.Get(new VoxelPos(99, 99, 99));

        Assert.That(type, Is.EqualTo(VoxelType.Air));
        Assert.That(material, Is.Null);
    }

    [Test]
    public void Set_Air_RemovesVoxel()
    {
        var pos = new VoxelPos(5, 5, 5);
        _svo.Set(pos, VoxelType.Conductor, Material.Copper);

        _svo.Set(pos, VoxelType.Air, null);

        Assert.That(_svo.VoxelCount, Is.EqualTo(0));
        Assert.That(_svo.Get(pos).Type, Is.EqualTo(VoxelType.Air));
    }

    [Test]
    public void Set_OverwriteExisting_UpdatesValue()
    {
        var pos = new VoxelPos(5, 5, 5);
        _svo.Set(pos, VoxelType.Conductor, Material.Copper);

        _svo.Set(pos, VoxelType.Insulator, null);

        Assert.That(_svo.VoxelCount, Is.EqualTo(1));
        var (type, material) = _svo.Get(pos);
        Assert.That(type, Is.EqualTo(VoxelType.Insulator));
        Assert.That(material, Is.Null);
    }

    #endregion

    #region Negative Coordinates

    [Test]
    public void Set_NegativeCoordinates_Works()
    {
        var pos = new VoxelPos(-10, -20, -30);
        _svo.Set(pos, VoxelType.Conductor, Material.Lead);

        var (type, material) = _svo.Get(pos);

        Assert.That(type, Is.EqualTo(VoxelType.Conductor));
        Assert.That(material, Is.SameAs(Material.Lead));
    }

    [Test]
    public void Set_MixedCoordinates_AllAccessible()
    {
        var positions = new[]
        {
            new VoxelPos(-100, -100, -100),
            new VoxelPos(0, 0, 0),
            new VoxelPos(100, 100, 100),
            new VoxelPos(-50, 50, -50)
        };

        foreach (var pos in positions)
        {
            _svo.Set(pos, VoxelType.Conductor, Material.Copper);
        }

        foreach (var pos in positions)
        {
            Assert.That(_svo.Get(pos).Type, Is.EqualTo(VoxelType.Conductor),
                $"Position {pos} should be Conductor");
        }

        Assert.That(_svo.VoxelCount, Is.EqualTo(4));
    }

    #endregion

    #region Uniform Node Collapse

    [Test]
    public void Set_UniformCube_CollapsesToSingleLeaf()
    {
        // Fill a 2x2x2 cube with the same voxel type
        for (int z = 0; z < 2; z++)
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    _svo.Set(new VoxelPos(x, y, z), VoxelType.Conductor, Material.Copper);

        // All should be accessible
        Assert.That(_svo.VoxelCount, Is.EqualTo(8));

        // Check leaf count - should be collapsed to 1 leaf
        var leaves = _svo.GetLeafNodes().ToList();
        Assert.That(leaves, Has.Count.EqualTo(1),
            $"Expected 1 collapsed leaf, got {leaves.Count}");
        Assert.That(leaves[0].Size, Is.EqualTo(2));
    }

    [Test]
    public void Set_MixedTypes_DoesNotCollapse()
    {
        // Fill a 2x2x2 cube with different types
        _svo.Set(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        _svo.Set(new VoxelPos(1, 0, 0), VoxelType.Insulator, null);

        var leaves = _svo.GetLeafNodes().ToList();
        Assert.That(leaves, Has.Count.EqualTo(2));
    }

    [Test]
    public void Remove_LastDifferentVoxel_RecollapsesPossible()
    {
        // Fill 2x2x2 uniformly, then change one, then change it back
        for (int z = 0; z < 2; z++)
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    _svo.Set(new VoxelPos(x, y, z), VoxelType.Conductor, Material.Copper);

        // Change one
        _svo.Set(new VoxelPos(0, 0, 0), VoxelType.Insulator, null);

        // Change it back
        _svo.Set(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);

        // Should be collapsed again
        var leaves = _svo.GetLeafNodes().ToList();
        Assert.That(leaves, Has.Count.EqualTo(1));
    }

    #endregion

    #region GetAllVoxels

    [Test]
    public void GetAllVoxels_ReturnsAllNonAir()
    {
        _svo.Set(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        _svo.Set(new VoxelPos(10, 10, 10), VoxelType.Insulator, null);
        _svo.Set(new VoxelPos(-5, -5, -5), VoxelType.ResistiveConductor, Material.Lead);

        var voxels = _svo.GetAllVoxels().ToList();

        Assert.That(voxels, Has.Count.EqualTo(3));
    }

    [Test]
    public void GetAllVoxels_UniformCube_ReturnsAllVoxels()
    {
        // Fill a 4x4x4 cube
        for (int z = 0; z < 4; z++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    _svo.Set(new VoxelPos(x, y, z), VoxelType.Conductor, Material.Copper);

        var voxels = _svo.GetAllVoxels().ToList();

        Assert.That(voxels, Has.Count.EqualTo(64));
    }

    #endregion

    #region SetBatch

    [Test]
    public void SetBatch_MultipleVoxels_AllSet()
    {
        var batch = new[]
        {
            (new VoxelPos(0, 0, 0), VoxelType.Conductor, (Material?)Material.Copper),
            (new VoxelPos(1, 0, 0), VoxelType.Conductor, (Material?)Material.Copper),
            (new VoxelPos(2, 0, 0), VoxelType.Insulator, (Material?)null)
        };

        _svo.SetBatch(batch);

        Assert.That(_svo.VoxelCount, Is.EqualTo(3));
        Assert.That(_svo.Get(new VoxelPos(0, 0, 0)).Type, Is.EqualTo(VoxelType.Conductor));
        Assert.That(_svo.Get(new VoxelPos(2, 0, 0)).Type, Is.EqualTo(VoxelType.Insulator));
    }

    #endregion

    #region Large Scale

    [Test]
    public void LargeWire_Performance()
    {
        // Create a 3x3x192 wire (same as benchmark)
        for (int z = 0; z < 192; z++)
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    _svo.Set(new VoxelPos(x, y, z), VoxelType.ResistiveConductor, Material.Copper);

        Assert.That(_svo.VoxelCount, Is.EqualTo(3 * 3 * 192));

        // Verify a few random accesses
        Assert.That(_svo.Get(new VoxelPos(1, 1, 100)).Type, Is.EqualTo(VoxelType.ResistiveConductor));
        Assert.That(_svo.Get(new VoxelPos(1, 1, 0)).Type, Is.EqualTo(VoxelType.ResistiveConductor));
        Assert.That(_svo.Get(new VoxelPos(1, 1, 191)).Type, Is.EqualTo(VoxelType.ResistiveConductor));
    }

    [Test]
    public void Clear_RemovesAllVoxels()
    {
        _svo.Set(new VoxelPos(0, 0, 0), VoxelType.Conductor, Material.Copper);
        _svo.Set(new VoxelPos(10, 10, 10), VoxelType.Insulator, null);

        _svo.Clear();

        Assert.That(_svo.VoxelCount, Is.EqualTo(0));
        Assert.That(_svo.Get(new VoxelPos(0, 0, 0)).Type, Is.EqualTo(VoxelType.Air));
    }

    #endregion

    #region GetLeafNodes

    [Test]
    public void GetLeafNodes_SingleVoxel_ReturnsLeafWithSize1()
    {
        _svo.Set(new VoxelPos(5, 5, 5), VoxelType.Conductor, Material.Copper);

        var leaves = _svo.GetLeafNodes().ToList();

        Assert.That(leaves, Has.Count.EqualTo(1));
        Assert.That(leaves[0].Size, Is.EqualTo(1));
        Assert.That(leaves[0].Origin, Is.EqualTo(new VoxelPos(5, 5, 5)));
    }

    [Test]
    public void GetLeafNodes_4x4x4Cube_ReturnsLeafWithSize4()
    {
        // Fill aligned 4x4x4 cube
        for (int z = 0; z < 4; z++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    _svo.Set(new VoxelPos(x, y, z), VoxelType.Conductor, Material.Copper);

        var leaves = _svo.GetLeafNodes().ToList();

        Assert.That(leaves, Has.Count.EqualTo(1));
        Assert.That(leaves[0].Size, Is.EqualTo(4));
    }

    #endregion
}
