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
}
