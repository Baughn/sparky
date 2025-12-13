using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.ComponentTypes;
using Sparky.MNA.Api;

namespace Sparky.Tests.Game;

[TestFixture]
public class TopologyBuilderTests
{
    private TopologyBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _builder = new TopologyBuilder();
    }

    #region FindConductorRegions Tests

    [Test]
    public void FindConductorRegions_EmptyGrid_ReturnsEmpty()
    {
        var grid = new VoxelGrid();

        var regions = _builder.FindConductorRegions(grid);

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void FindConductorRegions_SingleConductor_ReturnsOneRegion()
    {
        var grid = new VoxelGrid();
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);

        var regions = _builder.FindConductorRegions(grid);

        Assert.That(regions, Has.Count.EqualTo(1));
    }

    [Test]
    public void FindConductorRegions_AdjacentConductors_SameRegion()
    {
        var grid = new VoxelGrid();
        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(1, 0, 0);
        grid.SetVoxel(pos1, VoxelType.Conductor);
        grid.SetVoxel(pos2, VoxelType.Conductor);

        var regions = _builder.FindConductorRegions(grid);

        Assert.That(regions[pos1], Is.SameAs(regions[pos2]));
        Assert.That(regions[pos1].Voxels, Has.Count.EqualTo(2));
    }

    [Test]
    public void FindConductorRegions_DiagonalConductors_DifferentRegions()
    {
        var grid = new VoxelGrid();
        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(1, 1, 0);  // Diagonal - not adjacent
        grid.SetVoxel(pos1, VoxelType.Conductor);
        grid.SetVoxel(pos2, VoxelType.Conductor);

        var regions = _builder.FindConductorRegions(grid);

        Assert.That(regions[pos1], Is.Not.SameAs(regions[pos2]));
    }

    [Test]
    public void FindConductorRegions_SeparatedByInsulator_DifferentRegions()
    {
        var grid = new VoxelGrid();
        // Conductor - Insulator - Conductor
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Insulator);
        grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);

        var regions = _builder.FindConductorRegions(grid);

        Assert.That(regions[new VoxelPos(0, 0, 0)], Is.Not.SameAs(regions[new VoxelPos(2, 0, 0)]));
    }

    [Test]
    public void FindConductorRegions_LShapedConductor_OneRegion()
    {
        var grid = new VoxelGrid();
        // L-shaped conductor:
        // X
        // X X X
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(0, 1, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);

        var regions = _builder.FindConductorRegions(grid);

        // All 4 voxels should be in same region
        var region = regions[new VoxelPos(0, 0, 0)];
        Assert.That(regions[new VoxelPos(0, 1, 0)], Is.SameAs(region));
        Assert.That(regions[new VoxelPos(1, 0, 0)], Is.SameAs(region));
        Assert.That(regions[new VoxelPos(2, 0, 0)], Is.SameAs(region));
        Assert.That(region.Voxels, Has.Count.EqualTo(4));
    }

    [Test]
    public void FindConductorRegions_3DConnectivity_OneRegion()
    {
        var grid = new VoxelGrid();
        // Conductors connected in 3D space
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(0, 0, 1), VoxelType.Conductor);  // +Z
        grid.SetVoxel(new VoxelPos(0, 1, 1), VoxelType.Conductor);  // +Y from second

        var regions = _builder.FindConductorRegions(grid);

        var region = regions[new VoxelPos(0, 0, 0)];
        Assert.That(regions[new VoxelPos(0, 0, 1)], Is.SameAs(region));
        Assert.That(regions[new VoxelPos(0, 1, 1)], Is.SameAs(region));
    }

    [Test]
    public void FindConductorRegions_CrossBlockCable_OneRegion()
    {
        var grid = new VoxelGrid();

        // 4x4 cable spanning 3 blocks along Z (48 voxels long)
        // This tests cross-block prism connectivity
        for (int z = 0; z < 48; z++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    grid.SetVoxel(new VoxelPos(x, y, z), VoxelType.Conductor);
                }
            }
        }

        var regions = _builder.FindConductorRegions(grid);

        // All voxels should be in the same region despite spanning 3 blocks
        var firstRegion = regions[new VoxelPos(0, 0, 0)];
        Assert.That(regions[new VoxelPos(0, 0, 47)], Is.SameAs(firstRegion));
        Assert.That(regions[new VoxelPos(3, 3, 16)], Is.SameAs(firstRegion)); // In block 2
        Assert.That(regions[new VoxelPos(3, 3, 32)], Is.SameAs(firstRegion)); // In block 3

        // Should be exactly one region
        var uniqueRegions = regions.Values.Distinct().Count();
        Assert.That(uniqueRegions, Is.EqualTo(1));
    }

    [Test]
    public void FindConductorRegions_TwoPrismsSameBlock_Connected()
    {
        var grid = new VoxelGrid();

        // Two separate prisms that touch within the same block
        // Prism 1: x=0-3, y=0, z=0
        for (int x = 0; x < 4; x++)
            grid.SetVoxel(new VoxelPos(x, 0, 0), VoxelType.Conductor);

        // Prism 2: x=0, y=1-3, z=0 (touches prism 1 at x=0)
        for (int y = 1; y < 4; y++)
            grid.SetVoxel(new VoxelPos(0, y, 0), VoxelType.Conductor);

        var regions = _builder.FindConductorRegions(grid);

        // Should be one connected region
        var region1 = regions[new VoxelPos(3, 0, 0)];
        var region2 = regions[new VoxelPos(0, 3, 0)];
        Assert.That(region1, Is.SameAs(region2));
    }

    [Test]
    public void FindConductorRegions_DisjointPrisms_SeparateRegions()
    {
        var grid = new VoxelGrid();

        // Two prisms with a gap between them
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(5, 0, 0), VoxelType.Conductor); // Gap of 4 voxels

        var regions = _builder.FindConductorRegions(grid);

        // Should be two separate regions
        var region1 = regions[new VoxelPos(0, 0, 0)];
        var region2 = regions[new VoxelPos(5, 0, 0)];
        Assert.That(region1, Is.Not.SameAs(region2));
    }

    #endregion

    #region BuildTopology Integration Tests

    [Test]
    public void BuildTopology_SimpleResistorCircuit_CreatesCorrectNodes()
    {
        // Simple circuit: Ground - Wire - Resistor - Wire - Battery+
        //                                            |
        //                                         Battery-
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Two conductor regions (wires)
        var wire1Pos = new VoxelPos(0, 0, 0);
        var wire2Pos = new VoxelPos(10, 0, 0);
        grid.SetVoxel(wire1Pos, VoxelType.Conductor);
        grid.SetVoxel(wire2Pos, VoxelType.Conductor);

        // Components
        var ground = new GroundComponent(wire1Pos);
        var battery = new BatteryComponent(wire1Pos, wire2Pos, 5.0);
        var resistor = new ResistorComponent(wire1Pos, wire2Pos, 100.0);

        var components = new Component[] { ground, battery, resistor };

        var regions = _builder.BuildTopology(grid, components, sim);

        sim.Step(0);  // DC analysis

        // Wire1 should be at ground (0V)
        // Wire2 should be at 5V (battery voltage)
        // Current should be 5V / 100Ω = 0.05A
        var wire1Region = regions[wire1Pos];
        var wire2Region = regions[wire2Pos];

        Assert.That(sim.GetVoltage(wire1Region.NodeId), Is.EqualTo(0).Within(1e-9));
        Assert.That(sim.GetVoltage(wire2Region.NodeId), Is.EqualTo(5.0).Within(1e-9));
    }

    [Test]
    public void BuildTopology_TwoResistorsInSeries_CorrectVoltageDivider()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Three conductor regions
        var pos1 = new VoxelPos(0, 0, 0);  // Ground, battery negative
        var pos2 = new VoxelPos(5, 0, 0);  // Middle node (between resistors)
        var pos3 = new VoxelPos(10, 0, 0); // Battery positive

        grid.SetVoxel(pos1, VoxelType.Conductor);
        grid.SetVoxel(pos2, VoxelType.Conductor);
        grid.SetVoxel(pos3, VoxelType.Conductor);

        // Components: 10V battery with two 100Ω resistors in series
        var ground = new GroundComponent(pos1);
        var battery = new BatteryComponent(pos1, pos3, 10.0);
        var r1 = new ResistorComponent(pos1, pos2, 100.0);  // Ground to middle
        var r2 = new ResistorComponent(pos2, pos3, 100.0);  // Middle to battery+

        var components = new Component[] { ground, battery, r1, r2 };

        var regions = _builder.BuildTopology(grid, components, sim);

        sim.Step(0);  // DC analysis

        // Voltage divider: middle should be 5V
        var middleRegion = regions[pos2];
        Assert.That(sim.GetVoltage(middleRegion.NodeId), Is.EqualTo(5.0).Within(1e-9));
    }

    [Test]
    public void BuildTopology_ConnectedWireRegion_SharesNode()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Wire of 5 connected conductor voxels
        for (int x = 0; x < 5; x++)
        {
            grid.SetVoxel(new VoxelPos(x, 0, 0), VoxelType.Conductor);
        }

        var ground = new GroundComponent(new VoxelPos(0, 0, 0));

        var regions = _builder.BuildTopology(grid, [ground], sim);

        // All 5 voxels should share the same region
        var region = regions[new VoxelPos(0, 0, 0)];
        for (int x = 1; x < 5; x++)
        {
            Assert.That(regions[new VoxelPos(x, 0, 0)], Is.SameAs(region));
        }
    }

    [Test]
    public void BuildTopology_GroundComponent_SetsNodeToGround()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        var pos = new VoxelPos(0, 0, 0);
        grid.SetVoxel(pos, VoxelType.Conductor);

        var ground = new GroundComponent(pos);

        var regions = _builder.BuildTopology(grid, [ground], sim);

        // The region containing ground terminal should be sim.Ground
        Assert.That(regions[pos].NodeId, Is.EqualTo(sim.Ground));
    }

    [Test]
    public void BuildTopology_ComponentWithUnconnectedTerminal_CreatesIsolatedNode()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Only one conductor voxel, but resistor has two terminals
        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(10, 0, 0);  // No conductor here

        grid.SetVoxel(pos1, VoxelType.Conductor);
        // pos2 has no conductor

        var resistor = new ResistorComponent(pos1, pos2, 100.0);
        var ground = new GroundComponent(pos1);

        // Should not throw - isolated terminal gets its own node
        Assert.DoesNotThrow(() => _builder.BuildTopology(grid, [resistor, ground], sim));
    }

    [Test]
    public void BuildTopology_MultipleGroundComponents_AllShareGroundNode()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Two separate conductor regions, each with ground
        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(100, 0, 0);  // Far away - different region

        grid.SetVoxel(pos1, VoxelType.Conductor);
        grid.SetVoxel(pos2, VoxelType.Conductor);

        var ground1 = new GroundComponent(pos1);
        var ground2 = new GroundComponent(pos2);

        var regions = _builder.BuildTopology(grid, [ground1, ground2], sim);

        // Both regions should be at ground
        Assert.That(regions[pos1].NodeId, Is.EqualTo(sim.Ground));
        Assert.That(regions[pos2].NodeId, Is.EqualTo(sim.Ground));
    }

    #endregion

    #region Component Visual State Tests

    [Test]
    public void ResistorComponent_ComputeVisualState_ReturnsCorrectValues()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(10, 0, 0);

        grid.SetVoxel(pos1, VoxelType.Conductor);
        grid.SetVoxel(pos2, VoxelType.Conductor);

        var ground = new GroundComponent(pos1);
        var battery = new BatteryComponent(pos1, pos2, 10.0);
        var resistor = new ResistorComponent(pos1, pos2, 100.0);

        _builder.BuildTopology(grid, [ground, battery, resistor], sim);
        sim.Step(0);  // DC analysis

        var state = resistor.ComputeVisualState(sim);

        // 10V / 100Ω = 0.1A, Power = 1W
        // Voltage across resistor = 10V, normalized to 10V = 1.0
        Assert.That(state.VoltageNormalized, Is.EqualTo(1.0f).Within(0.01f));
        Assert.That(state.CurrentNormalized, Is.EqualTo(0.1f).Within(0.01f));
        Assert.That(state.PowerNormalized, Is.EqualTo(0.1f).Within(0.01f));
    }

    [Test]
    public void BatteryComponent_ComputeVisualState_ReturnsCorrectValues()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(10, 0, 0);

        grid.SetVoxel(pos1, VoxelType.Conductor);
        grid.SetVoxel(pos2, VoxelType.Conductor);

        var ground = new GroundComponent(pos1);
        var battery = new BatteryComponent(pos1, pos2, 5.0);
        var resistor = new ResistorComponent(pos1, pos2, 100.0);

        _builder.BuildTopology(grid, [ground, battery, resistor], sim);
        sim.Step(0);  // DC analysis

        var state = battery.ComputeVisualState(sim);

        // 5V battery, 5V / 100Ω = 0.05A
        Assert.That(state.VoltageNormalized, Is.EqualTo(0.5f).Within(0.01f));  // 5V / 10V ref
        Assert.That(state.CurrentNormalized, Is.EqualTo(0.05f).Within(0.01f));
    }

    #endregion

    #region Inter-Region Resistor Tests

    [Test]
    public void WirePrism_DoesNotMergeWithTerminals()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Create a chain: Conductor - ResistiveConductor - ResistiveConductor - Conductor
        // The two ResistiveConductor voxels will coalesce into one prism
        // Positions: (0,0,0) - (1,0,0) - (2,0,0) - (3,0,0)
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);          // Terminal A
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.ResistiveConductor); // Wire (coalesced)
        grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.ResistiveConductor); // Wire (coalesced)
        grid.SetVoxel(new VoxelPos(3, 0, 0), VoxelType.Conductor);          // Terminal B

        var regions = _builder.BuildTopology(grid, [], sim);

        // With Option A model: resistive prisms NEVER merge with other prisms
        // Region structure:
        // - Region TermA: just Terminal A (conductor)
        // - Region Wire: the coalesced wire prism (resistive)
        // - Region TermB: just Terminal B (conductor)

        var regionTermA = regions[new VoxelPos(0, 0, 0)];
        var regionWire1 = regions[new VoxelPos(1, 0, 0)];
        var regionWire2 = regions[new VoxelPos(2, 0, 0)];
        var regionTermB = regions[new VoxelPos(3, 0, 0)];

        // Wire voxels should be in same region (coalesced prism)
        Assert.That(regionWire1, Is.SameAs(regionWire2), "Wire voxels should be in same region (coalesced)");

        // Wire should NOT merge with terminals
        Assert.That(regionWire1, Is.Not.SameAs(regionTermA), "Wire should NOT merge with Terminal A");
        Assert.That(regionWire1, Is.Not.SameAs(regionTermB), "Wire should NOT merge with Terminal B");

        // Terminals should be separate from each other
        Assert.That(regionTermA, Is.Not.SameAs(regionTermB), "Terminals should be in different regions");

        // Wire region should be resistive, terminals should not
        Assert.That(regionWire1.IsResistive, Is.True, "Wire region should be resistive");
        Assert.That(regionTermA.IsResistive, Is.False, "Terminal A region should not be resistive");
        Assert.That(regionTermB.IsResistive, Is.False, "Terminal B region should not be resistive");

        // Wire region should have 2 adjacent resistors (one to each terminal)
        Assert.That(regionWire1.AdjacentResistors, Has.Count.EqualTo(2),
            $"Wire region should have 2 adjacent resistors, has {regionWire1.AdjacentResistors.Count}");
    }

    [Test]
    public void AdjacentConductors_MergeWhenNoResistivesBetween()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Test that adjacent Conductor voxels still merge when there's no resistive between them
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor); // Terminal A
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor); // Terminal B

        var regions = _builder.BuildTopology(grid, [], sim);

        var regionA = regions[new VoxelPos(0, 0, 0)];
        var regionB = regions[new VoxelPos(1, 0, 0)];

        // Adjacent conductors should merge
        Assert.That(regionA, Is.SameAs(regionB), "Adjacent Conductor voxels should merge");
        Assert.That(regionA.IsResistive, Is.False, "Pure conductor region should not be resistive");
    }

    [Test]
    public void CoalescedWirePrism_SingleRegionWithTwoResistors()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Test that adjacent wire voxels coalesce into one prism/region
        // but that region still connects to terminals via resistors
        // Layout: TermA - Wire1 - Wire2 - TermB
        // Wire1 and Wire2 coalesce into a single 2x1x1 prism
        grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);          // Terminal A
        grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.ResistiveConductor); // Wire (coalesced)
        grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.ResistiveConductor); // Wire (coalesced)
        grid.SetVoxel(new VoxelPos(3, 0, 0), VoxelType.Conductor);          // Terminal B

        var regions = _builder.BuildTopology(grid, [], sim);

        var regionTermA = regions[new VoxelPos(0, 0, 0)];
        var regionWire1 = regions[new VoxelPos(1, 0, 0)];
        var regionWire2 = regions[new VoxelPos(2, 0, 0)];
        var regionTermB = regions[new VoxelPos(3, 0, 0)];

        // Wire voxels should be in same region (coalesced prism)
        Assert.That(regionWire1, Is.SameAs(regionWire2), "Wire voxels should coalesce into same region");

        // Wire region should not merge with terminals
        Assert.That(regionTermA, Is.Not.SameAs(regionWire1), "Terminal A should not merge with Wire");
        Assert.That(regionWire1, Is.Not.SameAs(regionTermB), "Wire should not merge with Terminal B");
        Assert.That(regionTermA, Is.Not.SameAs(regionTermB), "Terminals should be in different regions");

        // Wire region should have 2 adjacent resistors (to TermA and TermB)
        Assert.That(regionWire1.AdjacentResistors, Has.Count.EqualTo(2),
            $"Wire region should have 2 adjacent resistors, has {regionWire1.AdjacentResistors.Count}");

        // Terminal regions should have 1 adjacent resistor each (to the wire)
        Assert.That(regionTermA.AdjacentResistors, Has.Count.EqualTo(1),
            $"Terminal A should have 1 adjacent resistor, has {regionTermA.AdjacentResistors.Count}");
        Assert.That(regionTermB.AdjacentResistors, Has.Count.EqualTo(1),
            $"Terminal B should have 1 adjacent resistor, has {regionTermB.AdjacentResistors.Count}");
    }

    #endregion

    #region Large Wire Fuzz Tests

    public enum BuildMethod { Randomized, FloodFill, InterpolatedSlices }

    [Test]
    [TestCase(BuildMethod.Randomized, false)]
    [TestCase(BuildMethod.Randomized, true)]
    [TestCase(BuildMethod.FloodFill, false)]
    [TestCase(BuildMethod.FloodFill, true)]
    [TestCase(BuildMethod.InterpolatedSlices, false)]
    [TestCase(BuildMethod.InterpolatedSlices, true)]
    public void LargeWire_ConsistentTopology_RegardlessOfBuildOrder(BuildMethod method, bool withTimesteps)
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();

        // Build wire using specified method
        BuildWire(grid, method, sim, withTimesteps);

        // Assert prism count (14 total: 1 TermA, 12 wire, 1 TermB)
        Assert.That(grid.PrismCount, Is.EqualTo(14),
            $"Expected 14 prisms, got {grid.PrismCount}");

        // Assert region count (resistive prisms don't merge)
        var regions = _builder.BuildTopology(grid, [], sim);
        var uniqueRegions = regions.Values.Distinct().Count();
        Assert.That(uniqueRegions, Is.EqualTo(14),
            $"Expected 14 unique regions, got {uniqueRegions}");

        // Assert MNA optimization (series resistors should be optimized)
        sim.EnableLineOptimization = true;
        sim.Step(0);
        var stats = sim.GetStats();
        Assert.That(stats.OptimizedNodeCount, Is.GreaterThanOrEqualTo(10),
            $"Line optimization should merge most wire nodes, only optimized {stats.OptimizedNodeCount}");
    }

    private static IEnumerable<VoxelPos> GetLargeWirePositions()
    {
        // Terminal A: single voxel at center of Z=0 face
        yield return new VoxelPos(1, 1, 0);

        // Wire: 3x3x190 from z=1 to z=190
        for (int z = 1; z <= 190; z++)
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    yield return new VoxelPos(x, y, z);

        // Terminal B: 3x3 at z=191
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                yield return new VoxelPos(x, y, 191);
    }

    private void BuildWire(VoxelGrid grid, BuildMethod method, ISimulation sim, bool withTimesteps)
    {
        var positions = GetLargeWirePositions().ToList();

        switch (method)
        {
            case BuildMethod.Randomized:
                var rng = new Random(42); // Fixed seed for reproducibility
                positions = positions.OrderBy(_ => rng.Next()).ToList();
                break;
            case BuildMethod.FloodFill:
                // Already in Z-first order
                break;
            case BuildMethod.InterpolatedSlices:
                // Group by z/2 for 3x3x2 blocks
                positions = positions.OrderBy(p => p.Z / 2).ThenBy(p => p.Z).ToList();
                break;
        }

        int count = 0;
        foreach (var pos in positions)
        {
            var isTerminal = pos.Z == 0 || pos.Z == 191;
            grid.SetVoxel(pos, isTerminal ? VoxelType.Conductor : VoxelType.ResistiveConductor);
            count++;

            if (withTimesteps && count % 100 == 0)
            {
                // Rebuild topology and step every 100 voxels (not every single one)
                _builder.BuildTopology(grid, [], sim);
                sim.Step(0.001);
            }
        }
    }

    #endregion
}
