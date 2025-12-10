using NUnit.Framework;
using Sparky.MNA.Api;

namespace Sparky.Tests.MNA;

[TestFixture]
public class ApiInitialConditionsTests
{
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp()
    {
        _sim = new SimulationManager();
    }

    #region Capacitor Initial Conditions

    [Test]
    public void SetCapacitorVoltage_BeforeFirstStep_SetsInitialState()
    {
        // Pre-charged capacitor in RC circuit
        // 10V -- R(1000) -- N1 -- C(1e-6) -- GND
        // Set capacitor to 5V initially
        var nSrc = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, n1, 1000.0);
        var capId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        // Set initial voltage before any step
        _sim.SetCapacitorVoltage(capId, 5.0);

        // First step should start from 5V, not 0V
        _sim.Step(1e-6);

        // Voltage should be close to 5V (slightly higher due to charging from 10V source)
        var v = _sim.GetCapacitorVoltage(capId);
        Assert.That(v, Is.GreaterThan(4.9).And.LessThan(6.0));
    }

    [Test]
    public void SetCapacitorVoltage_BetweenSteps_UpdatesState()
    {
        var nSrc = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, n1, 1000.0);
        var capId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        // Run a few steps
        for (int i = 0; i < 10; i++)
            _sim.Step(1e-5);

        // Force capacitor to specific voltage
        _sim.SetCapacitorVoltage(capId, 7.5);

        _sim.Step(1e-6);

        // Should be near 7.5V
        var v = _sim.GetCapacitorVoltage(capId);
        Assert.That(v, Is.EqualTo(7.5).Within(0.5));
    }

    [Test]
    public void SetCapacitorVoltage_InvalidId_ThrowsException()
    {
        Assert.Throws<InvalidComponentException>(() =>
            _sim.SetCapacitorVoltage(new CapacitorId(999), 5.0));
    }

    [Test]
    public void SetCapacitorVoltage_PreChargedDischarge_StartsFromSetVoltage()
    {
        // Capacitor connected only to resistor (no source) - should discharge
        // N1 -- R(1000) -- N2 -- C(1e-6) -- GND
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddResistor(n1, n2, 1000.0);
        var capId = _sim.AddCapacitor(n2, _sim.Ground, 1e-6);

        // Pre-charge to 10V
        _sim.SetCapacitorVoltage(capId, 10.0);

        // Need a path to ground for n1 - add high resistance
        _sim.AddResistor(n1, _sim.Ground, 1e9);

        _sim.Step(1e-6);

        // Should start discharging from 10V
        var v = _sim.GetCapacitorVoltage(capId);
        Assert.That(v, Is.LessThan(10.0).And.GreaterThan(9.0));
    }

    #endregion

    #region Inductor Initial Conditions

    [Test]
    public void SetInductorCurrent_BeforeFirstStep_SetsInitialState()
    {
        // Inductor with initial current in RL circuit
        // 10V -- R(100) -- N1 -- L(0.01) -- GND
        var nSrc = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, n1, 100.0);
        var indId = _sim.AddInductor(n1, _sim.Ground, 0.01);

        // Set initial current (steady state would be 10V/100Ω = 0.1A)
        // Set to 0.05A (half of steady state)
        _sim.SetInductorCurrent(indId, 0.05);

        _sim.Step(1e-5);

        // Current should be between initial 0.05A and steady-state 0.1A
        // (inductor current rises toward steady state)
        var vN1 = _sim.GetVoltage(n1);
        // At 0.05A, voltage drop across R is 5V, so n1 = 5V
        // After step, current increases, so voltage drop increases, n1 decreases
        Assert.That(vN1, Is.LessThan(5.5).And.GreaterThan(0.0));
    }

    [Test]
    public void SetInductorCurrent_BetweenSteps_UpdatesState()
    {
        var nSrc = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, n1, 100.0);
        var indId = _sim.AddInductor(n1, _sim.Ground, 0.01);

        // Run until near steady state
        for (int i = 0; i < 100; i++)
            _sim.Step(1e-4);

        // Force inductor current to different value
        _sim.SetInductorCurrent(indId, 0.02);

        _sim.Step(1e-5);

        // Circuit should continue from new state
        // Current was forced low, so voltage at n1 should be higher than steady state
        var vN1 = _sim.GetVoltage(n1);
        Assert.That(vN1, Is.GreaterThan(1.0));
    }

    [Test]
    public void SetInductorCurrent_InvalidId_ThrowsException()
    {
        Assert.Throws<InvalidComponentException>(() =>
            _sim.SetInductorCurrent(new InductorId(999), 0.1));
    }

    #endregion

    #region State Preservation

    [Test]
    public void SetCapacitorVoltage_PreservesAcrossTopologyChange()
    {
        var nSrc = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, n1, 1000.0);
        var capId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        _sim.SetCapacitorVoltage(capId, 8.0);
        _sim.Step(1e-6);

        var vBefore = _sim.GetCapacitorVoltage(capId);

        // Topology change - add unrelated component
        var n2 = _sim.CreateNode();
        _sim.AddResistor(n2, _sim.Ground, 10000.0);

        _sim.Step(1e-6);

        var vAfter = _sim.GetCapacitorVoltage(capId);

        // Voltage should be preserved (within simulation tolerance)
        Assert.That(vAfter, Is.EqualTo(vBefore).Within(0.5));
    }

    #endregion
}
