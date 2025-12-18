using System.Collections.Generic;
using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;
using Sparky.VSIntegration;
using Sparky.VSIntegration.CableLaying;

namespace Sparky.Tests.Mod;

[TestFixture]
public class WorldVoxelCacheBehaviorTests {
    [SetUp]
    public void SetUp() {
        BEBehaviorCircuit.ClearConductorRegistrations();
    }

    [TearDown]
    public void TearDown() {
        BEBehaviorCircuit.ClearConductorRegistrations();
    }

    [Test]
    public void ApplyCircuitCuboids_UsesConductorRegistry() {
        BEBehaviorCircuit.RegisterConductor(100, Material.Copper);

        var octree = new SparseVoxelOctree<CacheVoxelState>(CacheVoxelState.Empty);
        var cuboids = new List<uint>
        {
            BEBehaviorCircuit.ToUint(0, 0, 0, 1, 1, 1, 0),
            BEBehaviorCircuit.ToUint(1, 0, 0, 2, 1, 1, 1)
        };
        var blockIds = new[] { 100, 200 };

        WorldVoxelCache.ApplyCircuitCuboids(
            octree,
            0,
            0,
            0,
            cuboids,
            blockIds,
            BEBehaviorCircuit.IsConductor);

        Assert.That(octree.Get(new VoxelPos(0, 0, 0)), Is.EqualTo(CacheVoxelState.PreExistingConductor));
        Assert.That(octree.Get(new VoxelPos(1, 0, 0)), Is.EqualTo(CacheVoxelState.Insulation));
        Assert.That(octree.Get(new VoxelPos(2, 0, 0)), Is.EqualTo(CacheVoxelState.Empty));
    }
}
