using NUnit.Framework;
using Sparky.Game.Core;

namespace Sparky.Tests.Game;

[TestFixture]
public class PrismTests {
    [Test]
    public void Prism_Contains_WorksCorrectly() {
        var prism = new Prism(2, 3, 4, 5, 6, 7, VoxelType.Conductor);

        // Inside
        Assert.That(prism.Contains(2, 3, 4), Is.True);
        Assert.That(prism.Contains(6, 8, 10), Is.True);  // Last valid point
        Assert.That(prism.Contains(4, 5, 6), Is.True);

        // Outside - before start
        Assert.That(prism.Contains(1, 3, 4), Is.False);
        Assert.That(prism.Contains(2, 2, 4), Is.False);
        Assert.That(prism.Contains(2, 3, 3), Is.False);

        // Outside - at/after end
        Assert.That(prism.Contains(7, 3, 4), Is.False);  // x >= 2+5
        Assert.That(prism.Contains(2, 9, 4), Is.False);  // y >= 3+6
        Assert.That(prism.Contains(2, 3, 11), Is.False); // z >= 4+7
    }

    [Test]
    public void Prism_Volume_CalculatesCorrectly() {
        var prism = new Prism(0, 0, 0, 4, 4, 16, VoxelType.Conductor);

        Assert.That(prism.Volume, Is.EqualTo(256));
    }

    [Test]
    public void Prism_SingleVoxel_HasVolumeOne() {
        var prism = Prism.SingleVoxel(5, 5, 5, VoxelType.Conductor, Material.Copper);

        Assert.That(prism.Volume, Is.EqualTo(1));
        Assert.That(prism.SizeX, Is.EqualTo(1));
        Assert.That(prism.SizeY, Is.EqualTo(1));
        Assert.That(prism.SizeZ, Is.EqualTo(1));
    }
}

[TestFixture]
public class BlockVoxelDataCoalescingTests {
    [Test]
    public void SingleVoxel_CreatesSinglePrism() {
        var block = new BlockVoxelData();
        var voxels = new (VoxelType, Material?)[4096];
        voxels[0] = (VoxelType.Conductor, Material.Copper);

        block.RebuildFromVoxels(voxels);

        Assert.That(block.PrismCount, Is.EqualTo(1));
        Assert.That(block.Prisms[0].Volume, Is.EqualTo(1));
    }

    [Test]
    public void StraightLineX_CoalescesToSinglePrism() {
        var block = new BlockVoxelData();
        var voxels = new (VoxelType, Material?)[4096];

        // 10 voxels in a row along X
        for (int x = 0; x < 10; x++) {
            voxels[x] = (VoxelType.Conductor, Material.Copper);
        }

        block.RebuildFromVoxels(voxels);

        Assert.That(block.PrismCount, Is.EqualTo(1));
        Assert.That(block.Prisms[0].SizeX, Is.EqualTo(10));
        Assert.That(block.Prisms[0].SizeY, Is.EqualTo(1));
        Assert.That(block.Prisms[0].SizeZ, Is.EqualTo(1));
    }

    [Test]
    public void SolidBlock_CoalescesToSinglePrism() {
        var block = new BlockVoxelData();
        var voxels = new (VoxelType, Material?)[4096];

        // 4x4x4 solid block
        for (int z = 0; z < 4; z++) {
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < 4; x++) {
                    voxels[x + y * 16 + z * 256] = (VoxelType.Conductor, Material.Copper);
                }
            }
        }

        block.RebuildFromVoxels(voxels);

        Assert.That(block.PrismCount, Is.EqualTo(1));
        Assert.That(block.Prisms[0].Volume, Is.EqualTo(64));
    }

    [Test]
    public void DifferentMaterials_CreateSeparatePrisms() {
        var block = new BlockVoxelData();
        var voxels = new (VoxelType, Material?)[4096];

        // Copper voxels at x=0-4
        for (int x = 0; x < 5; x++) {
            voxels[x] = (VoxelType.Conductor, Material.Copper);
        }

        // Lead voxels at x=5-9
        for (int x = 5; x < 10; x++) {
            voxels[x] = (VoxelType.Conductor, Material.Lead);
        }

        block.RebuildFromVoxels(voxels);

        Assert.That(block.PrismCount, Is.EqualTo(2));
    }

    [Test]
    public void SideTap_SplitsPrism() {
        var block = new BlockVoxelData();
        var voxels = new (VoxelType, Material?)[4096];

        // Horizontal wire along X (y=0, z=0)
        for (int x = 0; x < 10; x++) {
            voxels[x] = (VoxelType.Conductor, Material.Copper);
        }

        // Side tap at x=5, y=1
        voxels[5 + 1 * 16] = (VoxelType.Conductor, Material.Copper);

        block.RebuildFromVoxels(voxels);

        // Should split: [0-4], [5], [6-9], [tap]
        // Or some variation depending on growth order
        Assert.That(block.PrismCount, Is.GreaterThan(1));

        // Verify the tap voxel is correctly identified
        var tapType = block.GetVoxelType(5, 1, 0);
        Assert.That(tapType, Is.EqualTo(VoxelType.Conductor));
    }

    [Test]
    public void TJunction_CreatesSeparatePrisms() {
        var block = new BlockVoxelData();
        var voxels = new (VoxelType, Material?)[4096];

        // Horizontal wire along X
        for (int x = 0; x < 10; x++) {
            voxels[x + 5 * 16] = (VoxelType.Conductor, Material.Copper);  // y=5, z=0
        }

        // Vertical wire along Y at x=5
        for (int y = 0; y < 10; y++) {
            voxels[5 + y * 16] = (VoxelType.Conductor, Material.Copper);  // x=5, z=0
        }

        block.RebuildFromVoxels(voxels);

        // T-junction should create multiple prisms to handle resistance correctly
        Assert.That(block.PrismCount, Is.GreaterThan(1));

        // Total voxels should be preserved
        int totalVolume = 0;
        foreach (var prism in block.Prisms) {
            totalVolume += prism.Volume;
        }
        // 10 + 10 - 1 (overlap at center) = 19 unique voxels
        Assert.That(totalVolume, Is.EqualTo(19));
    }

    [Test]
    public void Insulator_SeparateFromConductor() {
        var block = new BlockVoxelData();
        var voxels = new (VoxelType, Material?)[4096];

        // Conductor at (0,0,0)
        voxels[0] = (VoxelType.Conductor, Material.Copper);

        // Insulator at (1,0,0)
        voxels[1] = (VoxelType.Insulator, null);

        block.RebuildFromVoxels(voxels);

        Assert.That(block.PrismCount, Is.EqualTo(2));
        Assert.That(block.GetVoxelType(0, 0, 0), Is.EqualTo(VoxelType.Conductor));
        Assert.That(block.GetVoxelType(1, 0, 0), Is.EqualTo(VoxelType.Insulator));
    }

    [Test]
    public void ExpandToVoxels_RoundTrips() {
        var block = new BlockVoxelData();
        var original = new (VoxelType, Material?)[4096];

        // Create a complex pattern
        for (int x = 0; x < 4; x++) {
            for (int y = 0; y < 4; y++) {
                original[x + y * 16] = (VoxelType.Conductor, Material.Copper);
            }
        }
        original[100] = (VoxelType.Insulator, null);
        original[200] = (VoxelType.Conductor, Material.Lead);

        block.RebuildFromVoxels(original);
        var expanded = block.ExpandToVoxels();

        // Verify all voxels match
        for (int i = 0; i < 4096; i++) {
            Assert.That(expanded[i].Item1, Is.EqualTo(original[i].Item1),
                $"Type mismatch at index {i}");
            Assert.That(expanded[i].Item2, Is.EqualTo(original[i].Item2),
                $"Material mismatch at index {i}");
        }
    }
}

[TestFixture]
public class VoxelGridPrismTests {
    [Test]
    public void VoxelGrid_StraightCable_MinimalPrisms() {
        var grid = new VoxelGrid();

        // 4x4x16 cable in one block
        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < 4; x++) {
                    grid.SetVoxel(new VoxelPos(x, y, z), VoxelType.Conductor);
                }
            }
        }

        Assert.That(grid.VoxelCount, Is.EqualTo(256));
        Assert.That(grid.BlockCount, Is.EqualTo(1));

        // Should coalesce to 1 prism (solid 4x4x16 block)
        Assert.That(grid.PrismCount, Is.EqualTo(1));
    }

    [Test]
    public void VoxelGrid_CrossBlockCable_MultiplePrisms() {
        var grid = new VoxelGrid();

        // 4x4 cable spanning 3 blocks along Z (48 voxels long)
        for (int z = 0; z < 48; z++) {
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < 4; x++) {
                    grid.SetVoxel(new VoxelPos(x, y, z), VoxelType.Conductor);
                }
            }
        }

        Assert.That(grid.VoxelCount, Is.EqualTo(4 * 4 * 48));
        Assert.That(grid.BlockCount, Is.EqualTo(3));

        // Each block should have 1 prism (cable clips at block boundaries)
        Assert.That(grid.PrismCount, Is.EqualTo(3));
    }

    [Test]
    public void VoxelGrid_GetAllPrisms_ReturnsAllPrisms() {
        var grid = new VoxelGrid();

        // Create voxels in 2 blocks
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(20, 0, 0), VoxelType.Conductor);  // Different block

        var prisms = grid.GetAllPrisms().ToList();

        Assert.That(prisms, Has.Count.EqualTo(2));
    }
}
