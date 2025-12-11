using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.CellTypes;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.Game;

[TestFixture]
public class GridBuilderTests
{
    #region Basic Construction

    [Test]
    public void Constructor_CreatesEmptyGrid()
    {
        var builder = new GridBuilder();

        Assert.That(builder.Grid.CellCount, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_DefaultsToUpFace()
    {
        var builder = new GridBuilder()
            .Wire(0, 0);

        var cell = builder.GetCell(0, 0)!;
        Assert.That(cell.Position.Face, Is.EqualTo(BlockFacing.Up));
    }

    [Test]
    public void Constructor_CustomFace_UsesSpecifiedFace()
    {
        var builder = new GridBuilder(BlockFacing.South)
            .Wire(0, 0);

        var cell = builder.GetCell(0, 0)!;
        Assert.That(cell.Position.Face, Is.EqualTo(BlockFacing.South));
    }

    #endregion

    #region Cell Placement

    [Test]
    public void Wire_PlacesCellAtPosition()
    {
        var builder = new GridBuilder()
            .Wire(3, 5);

        var cell = builder.GetCell(3, 5);
        Assert.That(cell, Is.Not.Null);
        Assert.That(cell, Is.TypeOf<WireCell>());
    }

    [Test]
    public void Battery_PlacesCellWithVoltage()
    {
        var builder = new GridBuilder()
            .Battery(0, 0, voltage: 12.0);

        var cell = builder.GetCell<BatteryCell>(0, 0);
        Assert.That(cell, Is.Not.Null);
        Assert.That(cell!.Voltage, Is.EqualTo(12.0));
    }

    [Test]
    public void Resistor_PlacesCellWithResistance()
    {
        var builder = new GridBuilder()
            .Resistor(1, 2, resistance: 220);

        var cell = builder.GetCell<ResistorCell>(1, 2);
        Assert.That(cell, Is.Not.Null);
        Assert.That(cell!.Resistance, Is.EqualTo(220));
    }

    [Test]
    public void Ground_PlacesCellWithRotation()
    {
        var builder = new GridBuilder()
            .Ground(5, 5, rotation: 180);

        var cell = builder.GetCell<GroundCell>(5, 5);
        Assert.That(cell, Is.Not.Null);
        Assert.That(cell!.Rotation, Is.EqualTo(180));
    }

    [Test]
    public void FluentChaining_PlacesMultipleCells()
    {
        var builder = new GridBuilder()
            .Battery(0, 0)
            .Wire(1, 0)
            .Resistor(2, 0)
            .Ground(3, 0);

        Assert.That(builder.Grid.CellCount, Is.EqualTo(4));
        Assert.That(builder.GetCell(0, 0), Is.TypeOf<BatteryCell>());
        Assert.That(builder.GetCell(1, 0), Is.TypeOf<WireCell>());
        Assert.That(builder.GetCell(2, 0), Is.TypeOf<ResistorCell>());
        Assert.That(builder.GetCell(3, 0), Is.TypeOf<GroundCell>());
    }

    #endregion

    #region Simulation Integration

    [Test]
    public void Tick_RunsSimulation()
    {
        var builder = new GridBuilder()
            .Battery(0, 0)
            .Tick();

        Assert.That(builder.Sim.SimulationTime, Is.GreaterThan(0));
    }

    [Test]
    public void TickN_RunsMultipleTicks()
    {
        var builder = new GridBuilder()
            .Battery(0, 0)
            .TickN(10, dt: 0.001);

        Assert.That(builder.Sim.SimulationTime, Is.EqualTo(0.01).Within(1e-9));
    }

    [Test]
    public void GetVisualState_ReturnsStateForCell()
    {
        var builder = new GridBuilder()
            .Battery(0, 0, voltage: 5.0)
            .Resistor(1, 0, resistance: 100)
            .Ground(2, 0, rotation: 270)
            .Tick();

        var batteryState = builder.GetVisualState(0, 0);
        Assert.That(batteryState, Is.Not.Null);
    }

    [Test]
    public void GetVisualState_InvalidPosition_Throws()
    {
        var builder = new GridBuilder()
            .Wire(0, 0)
            .Tick();

        Assert.Throws<InvalidOperationException>(() => builder.GetVisualState(99, 99));
    }

    #endregion

    #region Integration Tests (Using Builder)

    [Test]
    public void SimpleCircuit_ResistorInSeries()
    {
        // Compare to equivalent without builder (much more verbose):
        // var grid = new Grid();
        // var sim = new GameSimulation(grid);
        // var battery = new BatteryCell { Voltage = 10.0 };
        // grid.PlaceCell(battery, new CellPos(new BlockPos(0, 0, 0), BlockFacing.Up, SubPos.Zero));
        // ...etc

        var builder = new GridBuilder()
            .Battery(0, 0, voltage: 10.0)
            .Resistor(1, 0, resistance: 100)
            .Ground(2, 0, rotation: 270)
            .Tick();

        // Verify components were created
        var battery = builder.GetCell<BatteryCell>(0, 0)!;
        var resistor = builder.GetCell<ResistorCell>(1, 0)!;

        Assert.That(battery.VoltageSourceId, Is.Not.Null);
        Assert.That(resistor.ResistorId, Is.Not.Null);
    }

    [Test]
    public void SimplestCircuit_BatteryToGround()
    {
        // Absolute simplest: Battery with - connected to ground
        // Battery at Sub(1,0): Left(-)->Sub(0,0), Right(+)->Sub(2,0) (floating)
        // Ground at Sub(0,0), rotation 90: Top->Right->Sub(1,0)
        //
        // This should create a battery with - at ground, + floating
        // Expected: VS current ~0 (no load), + terminal at 10V

        var builder = new GridBuilder()
            .Ground(0, 0, rotation: 90)   // Port faces Right toward battery
            .Battery(1, 0, voltage: 10.0)  // Left->Ground, Right->nothing
            .Tick();

        var battery = builder.GetCell<BatteryCell>(1, 0)!;
        var vsCurrent = builder.Sim.Electrical.GetVoltageSourceCurrent(battery.VoltageSourceId!.Value);

        TestContext.WriteLine($"Battery VS current: {vsCurrent}");

        // With only one terminal grounded, current should be ~0 (floating positive)
        Assert.That(Math.Abs(vsCurrent), Is.LessThan(1e-9), "Floating battery should have no current");
    }

    [Test]
    public void VoltageDivider_DebugConnectivity()
    {
        // This test verifies that the circuit topology is properly connected.
        // Components are created and current flows through the resistor.

        var builder = new GridBuilder()
            .Ground(0, 0, rotation: 90)
            .Battery(1, 0, voltage: 10.0)
            .Resistor(2, 0, resistance: 100)
            .Wire(3, 0)
            .Ground(4, 0, rotation: 270)
            .Tick();

        var battery = builder.GetCell<BatteryCell>(1, 0)!;
        Assert.That(battery.VoltageSourceId, Is.Not.Null);

        // Expected: 10V / 100Ω = 0.1A through the circuit
        var vsCurrent = builder.Sim.Electrical.GetVoltageSourceCurrent(battery.VoltageSourceId!.Value);
        Assert.That(Math.Abs(vsCurrent), Is.GreaterThan(0.05), "Should have significant current");
    }

    [Test]
    public void VoltageDivider_MidpointVoltageIsCorrect()
    {
        // Battery(10V) - R(100) - Wire - R(100) - Ground
        // Expected: Wire node at 5V (midpoint of divider)
        //
        // This tests that the voltage divider formula works correctly:
        // V_mid = V_source * R2 / (R1 + R2) = 10V * 100Ω / 200Ω = 5V

        var builder = new GridBuilder()
            .Ground(0, 0, rotation: 90)  // Connect to battery -
            .Battery(1, 0, voltage: 10.0)
            .Resistor(2, 0, resistance: 100)
            .Wire(3, 0)
            .Resistor(4, 0, resistance: 100)
            .Ground(5, 0, rotation: 270)
            .Tick();

        var battery = builder.GetCell<BatteryCell>(1, 0)!;
        Assert.That(battery.VoltageSourceId, Is.Not.Null, "Battery should have voltage source");

        // Get the wire's visual state - voltage should be at midpoint
        var wireState = builder.GetVisualState(3, 0);

        // With a 10V source and equal resistors, midpoint is 5V
        // VoltageNormalized is voltage / 10 (the divider max)
        // The wire should show approximately 5V (0.5 normalized)
        Assert.That(wireState.VoltageNormalized, Is.EqualTo(0.5f).Within(0.05f));
    }

    #endregion
}
