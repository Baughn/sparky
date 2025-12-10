using NUnit.Framework;
using Sparky.MNA.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA;

/// <summary>
/// Tests for component measurement methods (current, power, voltage).
/// These tests verify that GetResistorCurrent, GetResistorPower, GetCapacitorCurrent,
/// and similar measurement APIs return correct values.
/// </summary>
[TestFixture]
public class ComponentMeasurementTests
{
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp()
    {
        _sim = new SimulationManager();
    }

    #region Resistor Current Tests

    [Test]
    public void GetResistorCurrent_SimpleDivider_ReturnsCorrectCurrent()
    {
        // 10V -- R1(100) -- n1 -- R2(100) -- GND
        // Total resistance = 200 Ohm, Current = 10V / 200 = 0.05A
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(nPos, n1, 100.0);
        var r2 = _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Current through both resistors should be 0.05A
        Assert.That(_sim.GetResistorCurrent(r1), Is.EqualTo(0.05).Within(Tolerances.Loose));
        Assert.That(_sim.GetResistorCurrent(r2), Is.EqualTo(0.05).Within(Tolerances.Loose));
    }

    [Test]
    public void GetResistorCurrent_ParallelResistors_SplitsCurrent()
    {
        // 10V -- n1 -- R1(100) -- GND
        //            \-- R2(100) -- GND
        // Each resistor has 10V/100 = 0.1A
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(n1, _sim.Ground, 100.0);
        var r2 = _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetResistorCurrent(r1), Is.EqualTo(0.1).Within(Tolerances.Loose));
        Assert.That(_sim.GetResistorCurrent(r2), Is.EqualTo(0.1).Within(Tolerances.Loose));
    }

    [Test]
    public void GetResistorCurrent_CurrentPolarity_MatchesNodeOrder()
    {
        // Current flows from higher to lower potential
        // 10V at nPos, 0V at ground => positive current from nPos to ground
        var nPos = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var rId = _sim.AddResistor(nPos, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Current from nodeA (nPos) to nodeB (ground) should be positive
        double current = _sim.GetResistorCurrent(rId);
        Assert.That(current, Is.GreaterThan(0));
        Assert.That(current, Is.EqualTo(0.1).Within(Tolerances.Loose));
    }

    [Test]
    public void GetResistorCurrent_ReversePolarity_NegativeCurrent()
    {
        // If we define resistor from ground to nPos, current should be negative
        var nPos = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        // Resistor from ground to nPos (opposite direction)
        var rId = _sim.AddResistor(_sim.Ground, nPos, 100.0);

        _sim.Step(0.001);

        // Current flows from nPos to ground physically, but resistor is defined GND->nPos
        // So measured current is negative (from nodeA to nodeB)
        double current = _sim.GetResistorCurrent(rId);
        Assert.That(current, Is.LessThan(0));
        Assert.That(current, Is.EqualTo(-0.1).Within(Tolerances.Loose));
    }

    [Test]
    public void GetResistorCurrent_NoExcitation_ZeroCurrent()
    {
        // Resistor with no voltage source
        var n1 = _sim.CreateNode();

        var rId = _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetResistorCurrent(rId), Is.EqualTo(0.0).Within(Tolerances.Voltage));
    }

    #endregion

    #region Resistor Power Tests

    [Test]
    public void GetResistorPower_SimpleDivider_ReturnsCorrectPower()
    {
        // 10V -- R1(100) -- n1 -- R2(100) -- GND
        // Current = 0.05A, Power in R1 = I^2 * R = 0.0025 * 100 = 0.25W
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(nPos, n1, 100.0);
        var r2 = _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // P = I^2 * R = (0.05)^2 * 100 = 0.25W for each resistor
        Assert.That(_sim.GetResistorPower(r1), Is.EqualTo(0.25).Within(Tolerances.Loose));
        Assert.That(_sim.GetResistorPower(r2), Is.EqualTo(0.25).Within(Tolerances.Loose));
    }

    [Test]
    public void GetResistorPower_HighCurrent_HighPower()
    {
        // 10V -- R(10) -- GND
        // I = 1A, P = 1^2 * 10 = 10W
        var nPos = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var rId = _sim.AddResistor(nPos, _sim.Ground, 10.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetResistorPower(rId), Is.EqualTo(10.0).Within(Tolerances.Loose));
    }

    [Test]
    public void GetResistorPower_AlwaysPositive_RegardlessOfPolarity()
    {
        // Power dissipation is always positive regardless of current direction
        var nPos = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var rId = _sim.AddResistor(_sim.Ground, nPos, 100.0); // Reversed polarity

        _sim.Step(0.001);

        // Power should be positive: P = I^2 * R = 0.1^2 * 100 = 1W
        Assert.That(_sim.GetResistorPower(rId), Is.GreaterThan(0));
        Assert.That(_sim.GetResistorPower(rId), Is.EqualTo(1.0).Within(Tolerances.Loose));
    }

    [Test]
    public void GetResistorPower_TotalPower_EqualsSourcePower()
    {
        // Power conservation: total power dissipated = power from source
        // 10V -- R1(100) -- R2(100) -- GND
        // Total P = V^2 / R_total = 100 / 200 = 0.5W
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(nPos, n1, 100.0);
        var r2 = _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        double totalPower = _sim.GetResistorPower(r1) + _sim.GetResistorPower(r2);
        double sourcePower = 10.0 * 10.0 / 200.0; // V^2 / R_total = 0.5W

        Assert.That(totalPower, Is.EqualTo(sourcePower).Within(Tolerances.Loose));
    }

    #endregion

    #region Capacitor Current Tests

    [Test]
    public void GetCapacitorCurrent_Charging_PositiveCurrent()
    {
        // RC charging circuit: current should be positive initially
        // 10V -- R(1000) -- n1 -- C(1uF) -- GND
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 1000.0);
        var cId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        // Initial step: capacitor is uncharged, maximum current flows
        _sim.Step(1e-6);

        double current = _sim.GetCapacitorCurrent(cId);
        Assert.That(current, Is.GreaterThan(0));
    }

    [Test]
    public void GetCapacitorCurrent_SteadyState_ZeroCurrent()
    {
        // After many time constants, capacitor should have near-zero current
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 1000.0);
        var cId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        // tau = R * C = 1000 * 1e-6 = 1ms
        // After 5*tau (5ms), capacitor is essentially fully charged
        for (int i = 0; i < 50; i++)
        {
            _sim.Step(1e-4); // 0.1ms per step, 50 steps = 5ms
        }

        double current = _sim.GetCapacitorCurrent(cId);
        Assert.That(current, Is.EqualTo(0.0).Within(Tolerances.Loose));
    }

    [Test]
    public void GetCapacitorCurrent_CurrentSourceCharging_ConstantCurrent()
    {
        // I -- C -- GND: constant current charges capacitor
        var n1 = _sim.CreateNode();

        _sim.AddCurrentSource(_sim.Ground, n1, 0.001); // 1mA into capacitor
        var cId = _sim.AddCapacitor(n1, _sim.Ground, 1e-3); // 1mF

        _sim.Step(0.001);

        // Capacitor current should equal source current (ideally)
        double current = _sim.GetCapacitorCurrent(cId);
        Assert.That(Math.Abs(current), Is.EqualTo(0.001).Within(Tolerances.Loose));
    }

    [Test]
    public void GetCapacitorCurrent_DCOnly_ZeroCurrent()
    {
        // With dt=0 (DC analysis), capacitor is open circuit
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        var cId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        _sim.Step(0); // DC analysis

        // In DC steady state, capacitor current is 0
        Assert.That(_sim.GetCapacitorCurrent(cId), Is.EqualTo(0.0).Within(Tolerances.Voltage));
    }

    #endregion

    #region Voltage Source Current Tests

    [Test]
    public void GetVoltageSourceCurrent_LoadedSource_ReturnsCorrectCurrent()
    {
        // 10V -- R(100) -- GND
        // Current from source = 0.1A
        var nPos = _sim.CreateNode();

        var vsId = _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Voltage source supplies current to the load
        double current = _sim.GetVoltageSourceCurrent(vsId);
        Assert.That(Math.Abs(current), Is.EqualTo(0.1).Within(Tolerances.Loose));
    }

    [Test]
    public void GetVoltageSourceCurrent_MultipleLoads_SumsCurrent()
    {
        // 10V with two 100 Ohm resistors in parallel
        // Total current = 10/100 + 10/100 = 0.2A
        var nPos = _sim.CreateNode();

        var vsId = _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, _sim.Ground, 100.0);
        _sim.AddResistor(nPos, _sim.Ground, 100.0);

        _sim.Step(0.001);

        double current = _sim.GetVoltageSourceCurrent(vsId);
        Assert.That(Math.Abs(current), Is.EqualTo(0.2).Within(Tolerances.Loose));
    }

    #endregion

    #region Invalid Component ID Tests

    [Test]
    public void GetResistorCurrent_InvalidId_ThrowsInvalidComponentException()
    {
        var invalidId = new ResistorId(999);

        Assert.Throws<InvalidComponentException>(() => _sim.GetResistorCurrent(invalidId));
    }

    [Test]
    public void GetResistorPower_InvalidId_ThrowsInvalidComponentException()
    {
        var invalidId = new ResistorId(999);

        Assert.Throws<InvalidComponentException>(() => _sim.GetResistorPower(invalidId));
    }

    [Test]
    public void GetCapacitorCurrent_InvalidId_ThrowsInvalidComponentException()
    {
        var invalidId = new CapacitorId(999);

        Assert.Throws<InvalidComponentException>(() => _sim.GetCapacitorCurrent(invalidId));
    }

    #endregion
}
