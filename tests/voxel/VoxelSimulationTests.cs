using NUnit.Framework;
using Sparky.Voxel;

namespace Sparky.Tests.Voxel;

[TestFixture]
public class VoxelSimulationTests {
    [Test]
    public void Step_WithEmptyGrid_DoesNotThrow() {
        var sim = new VoxelSimulation();
        Assert.DoesNotThrow(() => sim.Step(0.001));
    }

    [Test]
    public void Grid_ReturnsVoxelGrid() {
        var sim = new VoxelSimulation();
        Assert.That(sim.Grid, Is.Not.Null);
        Assert.That(sim.Grid, Is.InstanceOf<VoxelGrid>());
    }

    [Test]
    public void ElectricalEnabled_DefaultsToTrue() {
        var sim = new VoxelSimulation();
        Assert.That(sim.ElectricalEnabled, Is.True);
    }

    [Test]
    public void GetVoltageAt_WithNoCircuit_ReturnsZero() {
        var sim = new VoxelSimulation();
        var voltage = sim.GetVoltageAt(new VoxelPos(0, 0, 0));
        Assert.That(voltage, Is.EqualTo(0.0));
    }

    [Test]
    public void GetVoltageAt_WithSimpleCircuit_ReturnsCorrectVoltage() {
        var sim = new VoxelSimulation();

        // Place two separate conductor regions (not touching each other)
        // Region 1: ground terminal at origin
        sim.Grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);

        // Region 2: positive terminal at (2,0,0) - gap at (1,0,0) to keep them separate
        sim.Grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);

        // Add ground at origin
        sim.AddGround(new VoxelPos(0, 0, 0));

        // Add voltage source: positive at (2,0,0), negative at (0,0,0), 5V
        // This bridges the two separate conductor regions
        sim.AddVoltageSource(new VoxelPos(2, 0, 0), new VoxelPos(0, 0, 0), 5.0);

        sim.RebuildTopology();
        sim.Step(0.001);

        // Ground region should be at 0V, positive region should be at 5V
        Assert.That(sim.GetVoltageAt(new VoxelPos(0, 0, 0)), Is.EqualTo(0.0).Within(1e-6));
        Assert.That(sim.GetVoltageAt(new VoxelPos(2, 0, 0)), Is.EqualTo(5.0).Within(1e-6));
    }

    [Test]
    public void GetCurrentThrough_WithResistiveWire_ReturnsNonZeroCurrent() {
        var sim = new VoxelSimulation();

        // Ground at origin
        sim.Grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
        sim.AddGround(new VoxelPos(0, 0, 0));

        // Resistive wire at (1,0,0)
        sim.Grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.ResistiveConductor);

        // Conductor at far end
        sim.Grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);

        // 5V source at (2,0,0)
        sim.AddVoltageSource(new VoxelPos(2, 0, 0), new VoxelPos(0, 0, 0), 5.0);

        sim.Step(0.001);

        // Current through resistive voxel should be non-zero
        var current = sim.GetCurrentThrough(new VoxelPos(1, 0, 0));
        Assert.That(current, Is.Not.EqualTo(0.0));
    }
}
