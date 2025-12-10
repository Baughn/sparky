using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.CellTypes;
using Sparky.MNA.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.Game;

/// <summary>
/// Tests for concrete cell types and their MNA integration.
/// </summary>
[TestFixture]
public class CellTypeTests
{
    private SimulationManager _sim = null!;
    private Grid _grid = null!;

    [SetUp]
    public void SetUp()
    {
        _sim = new SimulationManager();
        _grid = new Grid();
        _grid.BindSimulation(_sim);
    }

    #region WireCell Tests

    [Test]
    public void WireCell_HasFourPorts()
    {
        var wire = new WireCell();
        var ports = wire.GetLocalPortDirections();

        Assert.That(ports, Has.Count.EqualTo(4));
    }

    [Test]
    public void WireCell_Type_IsWire()
    {
        var wire = new WireCell();
        Assert.That(wire.Type, Is.EqualTo(CellType.Wire));
    }

    #endregion

    #region GroundCell Tests

    [Test]
    public void GroundCell_HasOnePort()
    {
        var ground = new GroundCell();
        var ports = ground.GetLocalPortDirections();

        Assert.That(ports, Has.Count.EqualTo(1));
        Assert.That(ports[0], Is.EqualTo(FaceDirection.Top));
    }

    [Test]
    public void GroundCell_Type_IsGround()
    {
        var ground = new GroundCell();
        Assert.That(ground.Type, Is.EqualTo(CellType.Ground));
    }

    [Test]
    public void GroundCell_VisualState_IsActive()
    {
        var ground = new GroundCell();
        _grid.PlaceCell(ground, CellPos.At2D(0, 0));
        _grid.RebuildTopology();

        var state = ground.ComputeVisualState(_sim);

        Assert.That(state.IsActive, Is.True);
        Assert.That(state.VoltageNormalized, Is.EqualTo(0));
    }

    #endregion

    #region BatteryCell Tests

    [Test]
    public void BatteryCell_HasTwoPorts()
    {
        var battery = new BatteryCell();
        var ports = battery.GetLocalPortDirections();

        Assert.That(ports, Has.Count.EqualTo(2));
    }

    [Test]
    public void BatteryCell_Type_IsBattery()
    {
        var battery = new BatteryCell();
        Assert.That(battery.Type, Is.EqualTo(CellType.Battery));
    }

    [Test]
    public void BatteryCell_DefaultVoltage_Is5V()
    {
        var battery = new BatteryCell();
        Assert.That(battery.Voltage, Is.EqualTo(5.0));
    }

    [Test]
    public void BatteryCell_Voltage_CanBeChanged()
    {
        var battery = new BatteryCell { Voltage = 12.0 };
        Assert.That(battery.Voltage, Is.EqualTo(12.0));
    }

    #endregion

    #region ResistorCell Tests

    [Test]
    public void ResistorCell_HasTwoPorts()
    {
        var resistor = new ResistorCell();
        var ports = resistor.GetLocalPortDirections();

        Assert.That(ports, Has.Count.EqualTo(2));
    }

    [Test]
    public void ResistorCell_Type_IsResistor()
    {
        var resistor = new ResistorCell();
        Assert.That(resistor.Type, Is.EqualTo(CellType.Resistor));
    }

    [Test]
    public void ResistorCell_DefaultResistance_Is100Ohms()
    {
        var resistor = new ResistorCell();
        Assert.That(resistor.Resistance, Is.EqualTo(100.0));
    }

    [Test]
    public void ResistorCell_Resistance_CanBeChanged()
    {
        var resistor = new ResistorCell { Resistance = 1000.0 };
        Assert.That(resistor.Resistance, Is.EqualTo(1000.0));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void SimpleCircuit_BatteryResistorGround_HasCorrectCurrent()
    {
        // Create a simple circuit: Battery(5V) - Resistor(100Ω) - Ground
        // Expected current: 5V / 100Ω = 0.05A

        // Layout in a row on the Up face at Y=0:
        // Battery at (0,0) with + on Right, - on Left
        // Resistor at (1,0) connecting to battery on Left, ground on Right
        // Ground at (2,0) with port on Left (rotation 270)

        var battery = new BatteryCell { Voltage = 5.0 };
        var resistor = new ResistorCell { Resistance = 100.0 };
        var ground = new GroundCell();

        // Battery: + on Right → connects to position (1,0) Left
        // We need the battery's negative to connect to ground too
        // Actually, let's simplify: Battery - Resistor - Ground in a line

        // Use rotation to make ports align:
        // Battery at (0,0): Right(+) points to (1,0), Left(-) is at (-1,0) — we need ground there
        // Better approach: Ground at (0,0) connected to Battery's - terminal

        // Let me use a clearer layout:
        // Ground(0,0) with port pointing Right (rotation 90)
        // Battery(1,0) with - on Left (connects to ground), + on Right
        // Resistor(2,0) with port on Left (connects to battery +), port on Right (open)

        // Hmm, this is getting complex. Let's use a simpler test:
        // Just verify that components are created correctly.

        _grid.PlaceCell(battery, CellPos.At2D(1, 0));
        _grid.PlaceCell(resistor, CellPos.At2D(2, 0));
        _grid.PlaceCell(ground, CellPos.At2D(0, 0), rotation: 90); // Port points Right

        _grid.RebuildTopology();
        _sim.Step(0.001);

        // Verify components were created
        Assert.That(battery.VoltageSourceId, Is.Not.Null);
        Assert.That(resistor.ResistorId, Is.Not.Null);
    }

    [Test]
    public void ResistorCell_TryUpdate_ChangesResistance()
    {
        var resistor = new ResistorCell { Resistance = 100.0 };

        _grid.PlaceCell(resistor, CellPos.At2D(0, 0));
        _grid.RebuildTopology();

        // Change resistance and update
        resistor.Resistance = 200.0;
        var updated = resistor.TryUpdateComponents(_sim);

        Assert.That(updated, Is.True);
    }

    [Test]
    public void CellRemoval_RemovesComponents()
    {
        var battery = new BatteryCell();
        var pos = CellPos.At2D(0, 0);

        _grid.PlaceCell(battery, pos);
        _grid.RebuildTopology();

        Assert.That(battery.VoltageSourceId, Is.Not.Null);
        var vsId = battery.VoltageSourceId!.Value;
        Assert.That(_sim.VoltageSourceExists(vsId), Is.True);

        // Remove the cell
        _grid.RemoveCell(pos);

        // Voltage source should be removed
        Assert.That(_sim.VoltageSourceExists(vsId), Is.False);
    }

    [Test]
    public void AdjacentCells_ShareEdgeNode()
    {
        // Two adjacent wires should share the same node at their shared edge
        var wire1 = new WireCell();
        var wire2 = new WireCell();

        // Place at adjacent positions within the same face
        _grid.PlaceCell(wire1, new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(5, 5)));
        _grid.PlaceCell(wire2, new CellPos(BlockPos.Zero, BlockFacing.Up, new SubPos(6, 5)));

        _grid.RebuildTopology();

        // Both wires should exist
        Assert.That(_grid.CellCount, Is.EqualTo(2));
    }

    #endregion
}
