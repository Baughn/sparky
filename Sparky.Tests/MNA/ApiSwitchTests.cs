using NUnit.Framework;
using Sparky.MNA.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA;

[TestFixture]
public class ApiSwitchTests
{
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp()
    {
        _sim = new SimulationManager();
    }

    #region Lifecycle Tests

    [Test]
    public void Switch_AddRemove_WorksCorrectly()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var swId = _sim.AddSwitch(n1, n2);
        Assert.That(_sim.SwitchExists(swId), Is.True);

        _sim.RemoveSwitch(swId);
        Assert.That(_sim.SwitchExists(swId), Is.False);
    }

    [Test]
    public void Switch_InitiallyOpen_DefaultState()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var swId = _sim.AddSwitch(n1, n2);
        Assert.That(_sim.GetSwitchState(swId), Is.False);
    }

    [Test]
    public void Switch_InitiallyClosed_WhenSpecified()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var swId = _sim.AddSwitch(n1, n2, initiallyClosed: true);
        Assert.That(_sim.GetSwitchState(swId), Is.True);
    }

    [Test]
    public void Switch_RemoveNonExistent_ThrowsInvalidComponentException()
    {
        Assert.Throws<InvalidComponentException>(() => _sim.RemoveSwitch(new SwitchId(999)));
    }

    #endregion

    #region State Transition Tests

    [Test]
    public void Switch_SetState_ChangesState()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var swId = _sim.AddSwitch(n1, n2);
        Assert.That(_sim.GetSwitchState(swId), Is.False);

        _sim.SetSwitchState(swId, true);
        Assert.That(_sim.GetSwitchState(swId), Is.True);

        _sim.SetSwitchState(swId, false);
        Assert.That(_sim.GetSwitchState(swId), Is.False);
    }

    [Test]
    public void Switch_Toggle_InvertsState()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var swId = _sim.AddSwitch(n1, n2);
        Assert.That(_sim.GetSwitchState(swId), Is.False);

        _sim.ToggleSwitch(swId);
        Assert.That(_sim.GetSwitchState(swId), Is.True);

        _sim.ToggleSwitch(swId);
        Assert.That(_sim.GetSwitchState(swId), Is.False);
    }

    [Test]
    public void Switch_SetStateSameValue_NoOp()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var swId = _sim.AddSwitch(n1, n2, initiallyClosed: true);

        // Setting to same value should work without error
        _sim.SetSwitchState(swId, true);
        Assert.That(_sim.GetSwitchState(swId), Is.True);
    }

    #endregion

    #region Circuit Behavior Tests

    [Test]
    public void Switch_Open_BlocksCurrent()
    {
        // 10V -- SW (open) -- R(100) -- GND
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var swId = _sim.AddSwitch(nPos, n1, initiallyClosed: false);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // With open switch (1e9 ohms) in series with 100 ohms:
        // Current ~ 10V / 1e9 ohms ~ 1e-8 A (negligible)
        var current = Math.Abs(_sim.GetSwitchCurrent(swId));
        Assert.That(current, Is.LessThan(1e-6));

        // Voltage at n1 should be near 0 (voltage divider with 1e9 vs 100)
        Assert.That(_sim.GetVoltage(n1), Is.LessThan(1e-3));
    }

    [Test]
    public void Switch_Closed_AllowsCurrent()
    {
        // 10V -- SW (closed) -- R(100) -- GND
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var swId = _sim.AddSwitch(nPos, n1, initiallyClosed: true);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // With closed switch (1e-9 ohms) in series with 100 ohms:
        // Current ~ 10V / 100 ohms = 0.1 A
        var current = _sim.GetSwitchCurrent(swId);
        Assert.That(current, Is.EqualTo(0.1).Within(Tolerances.Voltage));

        // Voltage at n1 should be near 10V
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(Tolerances.Loose));
    }

    [Test]
    public void Switch_Toggle_ChangesCircuitBehavior()
    {
        // 10V -- SW -- R(100) -- GND
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var swId = _sim.AddSwitch(nPos, n1, initiallyClosed: false);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        // Open state
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.LessThan(1e-3));

        // Close switch
        _sim.SetSwitchState(swId, true);
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(Tolerances.Loose));

        // Open switch again
        _sim.SetSwitchState(swId, false);
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.LessThan(1e-3));
    }

    [Test]
    public void Switch_InVoltageDivider_AffectsVoltageDistribution()
    {
        // 10V -- R(100) -- N1 -- SW -- N2 -- R(100) -- GND
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 100.0);
        var swId = _sim.AddSwitch(n1, n2, initiallyClosed: true);
        _sim.AddResistor(n2, _sim.Ground, 100.0);

        // Closed: N1 and N2 should both be ~5V
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(5.0).Within(0.1));
        Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(0.1));

        // Open: N1 should be ~10V (no current path), N2 should be ~0V
        _sim.SetSwitchState(swId, false);
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(0.1));
        Assert.That(_sim.GetVoltage(n2), Is.LessThan(0.1));
    }

    #endregion

    #region Partitioning Tests

    [Test]
    public void Switch_Open_DoesNotAffectPartitioning()
    {
        // Switch still exists as high-resistance resistor, so nodes remain connected
        // 10V -- R1 -- N1 -- SW(open) -- N2 -- R2 -- GND
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        _sim.AddResistor(n1, n2, 100.0);
        _sim.AddSwitch(n1, n2, initiallyClosed: false);

        _sim.Step(0.001);

        // Should be single partition (switch connects them even when open)
        Assert.That(_sim.PartitionCount, Is.EqualTo(1));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void Switch_MultipleInCircuit_WorkIndependently()
    {
        // 10V -- SW1 -- N1 -- SW2 -- N2 -- R(100) -- GND
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var sw1 = _sim.AddSwitch(nPos, n1, initiallyClosed: true);
        var sw2 = _sim.AddSwitch(n1, n2, initiallyClosed: true);
        _sim.AddResistor(n2, _sim.Ground, 100.0);

        // Both closed: N2 = 10V
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0).Within(0.1));

        // SW2 open: N2 = 0V, N1 = 10V
        _sim.SetSwitchState(sw2, false);
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(0.1));
        Assert.That(_sim.GetVoltage(n2), Is.LessThan(0.1));

        // SW1 also open: N1 floats between two high-R switches
        // Voltage divider: 1e9 vs (1e9 + 100) ≈ 5V (midpoint)
        _sim.SetSwitchState(sw1, false);
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(5.0).Within(0.1));
    }

    [Test]
    public void Switch_Clear_RemovesAllSwitches()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var swId = _sim.AddSwitch(n1, n2);
        Assert.That(_sim.SwitchExists(swId), Is.True);

        _sim.Clear();

        Assert.That(_sim.SwitchExists(swId), Is.False);
    }

    [Test]
    public void Switch_BulkUpdate_DefersSolve()
    {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        var swId = _sim.AddSwitch(n1, n2, initiallyClosed: true);
        _sim.AddResistor(n2, _sim.Ground, 100.0);

        using (_sim.BeginBulkUpdate())
        {
            _sim.ToggleSwitch(swId);
            _sim.ToggleSwitch(swId);
            _sim.ToggleSwitch(swId);
            // Now open after 3 toggles
        }

        _sim.Step(0.001);
        Assert.That(_sim.GetSwitchState(swId), Is.False);
        Assert.That(_sim.GetVoltage(n2), Is.LessThan(0.1));
    }

    #endregion

    #region Exception Tests

    [Test]
    public void Switch_InvalidNode_ThrowsInvalidNodeException()
    {
        var n1 = _sim.CreateNode();
        var invalidNode = new NodeId(999);

        Assert.Throws<InvalidNodeException>(() => _sim.AddSwitch(n1, invalidNode));
    }

    [Test]
    public void Switch_GetStateNonExistent_ThrowsInvalidComponentException()
    {
        Assert.Throws<InvalidComponentException>(() => _sim.GetSwitchState(new SwitchId(999)));
    }

    [Test]
    public void Switch_SetStateNonExistent_ThrowsInvalidComponentException()
    {
        Assert.Throws<InvalidComponentException>(() =>
            _sim.SetSwitchState(new SwitchId(999), true)
        );
    }

    [Test]
    public void Switch_ToggleNonExistent_ThrowsInvalidComponentException()
    {
        Assert.Throws<InvalidComponentException>(() => _sim.ToggleSwitch(new SwitchId(999)));
    }

    [Test]
    public void Switch_GetCurrentNonExistent_ThrowsInvalidComponentException()
    {
        Assert.Throws<InvalidComponentException>(() => _sim.GetSwitchCurrent(new SwitchId(999)));
    }

    #endregion
}
