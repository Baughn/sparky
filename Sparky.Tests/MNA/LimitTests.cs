using NUnit.Framework;
using Sparky.MNA.Api;
using Sparky.MNA.Api.Limits;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA;

[TestFixture]
public class LimitTests
{
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp()
    {
        _sim = new SimulationManager();
    }

    #region Basic Limit Triggering

    [Test]
    public void Limit_BasicOverCurrent_FiresEventWhenExceeded()
    {
        // 12V source, 100 ohm resistor = 0.12A current
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Set a 100mA limit (will be exceeded)
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Kind, Is.EqualTo(LimitKind.OverCurrent));
        Assert.That(events[0].IsExceeded, Is.True);
        Assert.That(events[0].ActualValue, Is.EqualTo(0.12).Within(Tolerances.Current));
        Assert.That(events[0].Threshold, Is.EqualTo(0.1));
        Assert.That(events[0].Component.ComponentType, Is.EqualTo("Resistor"));
    }

    [Test]
    public void Limit_NotExceeded_NoEventFires()
    {
        // 5V source, 100 ohm resistor = 0.05A current
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 5.0);

        // Set a 100mA limit (will NOT be exceeded)
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Limit_OverPower_FiresEventWhenExceeded()
    {
        // 12V source, 100 ohm resistor = 0.12A current, P = 1.44W
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Set a 1W power limit (will be exceeded)
        _sim.SetResistorLimit(r, LimitKind.OverPower, new LimitConfig { Threshold = 1.0 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Kind, Is.EqualTo(LimitKind.OverPower));
        Assert.That(events[0].ActualValue, Is.EqualTo(1.44).Within(Tolerances.Loose));
    }

    #endregion

    #region Edge-Triggered Behavior

    [Test]
    public void Limit_EdgeTriggered_OnlyFiresOnTransition()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        // First step: exceeds, should fire
        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1));

        // Second step: still exceeded, should NOT fire (edge-triggered)
        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1)); // Still just 1

        // Third step: still exceeded, should NOT fire
        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1)); // Still just 1
    }

    [Test]
    public void Limit_FireEveryStep_FiresRepeatedly()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(
            r,
            LimitKind.OverCurrent,
            new LimitConfig { Threshold = 0.1, FireEveryStep = true }
        );

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);
        _sim.Step(0.001);
        _sim.Step(0.001);

        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(events.All(e => e.IsExceeded), Is.True);
    }

    #endregion

    #region Hysteresis

    [Test]
    public void Limit_Hysteresis_DoesNotClearImmediately()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0, isVariable: true);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Threshold 0.1A with 0.02A hysteresis
        // Clears when current < 0.08A
        _sim.SetResistorLimit(
            r,
            LimitKind.OverCurrent,
            new LimitConfig { Threshold = 0.1, Hysteresis = 0.02 }
        );

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        // Initial: 12V / 100R = 0.12A (exceeds)
        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].IsExceeded, Is.True);

        // Reduce to 0.09A by changing resistance to ~133 ohms (12V/133 = 0.09A)
        // Still above clear threshold (0.08A), so NO clear event
        _sim.UpdateResistor(r, 133.0);
        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1)); // No new event

        // Reduce to 0.07A by changing resistance to ~171 ohms (12V/171 = 0.07A)
        // Now below clear threshold (0.08A), so SHOULD clear
        _sim.UpdateResistor(r, 171.0);
        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[1].IsExceeded, Is.False); // Clear event
    }

    #endregion

    #region Component Removal Cleanup

    [Test]
    public void Limit_RemovedWithComponent_NoOrphanedLimits()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        // Remove the resistor
        _sim.RemoveResistor(r);

        // Verify limit is gone (GetResistorLimit should throw since resistor doesn't exist)
        Assert.Throws<InvalidComponentException>(() =>
            _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 })
        );
    }

    [Test]
    public void Limit_ClearedOnSimulationClear()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1));

        // Clear simulation
        _sim.Clear();

        // Create new circuit
        var node2 = _sim.CreateNode();
        var r2 = _sim.AddResistor(node2, _sim.Ground, 100.0);
        var v2 = _sim.AddVoltageSource(node2, _sim.Ground, 12.0);

        // Step without setting limits - should not fire old limit
        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1)); // No new events
    }

    #endregion

    #region Multiple Handlers

    [Test]
    public void Limit_MultipleHandlers_AllReceiveEvents()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events1 = new List<LimitEvent>();
        var events2 = new List<LimitEvent>();
        var events3 = new List<LimitEvent>();

        using var sub1 = _sim.OnLimitEvent(evt => events1.Add(evt));
        using var sub2 = _sim.OnLimitEvent(evt => events2.Add(evt));
        using var sub3 = _sim.OnLimitEvent(evt => events3.Add(evt));

        _sim.Step(0.001);

        Assert.That(events1, Has.Count.EqualTo(1));
        Assert.That(events2, Has.Count.EqualTo(1));
        Assert.That(events3, Has.Count.EqualTo(1));
    }

    [Test]
    public void Limit_HandlerUnsubscribed_NoLongerReceivesEvents()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0, isVariable: true);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);
        Assert.That(events, Has.Count.EqualTo(1));

        // Unsubscribe
        sub.Dispose();

        // Lower below threshold to clear, then raise again
        _sim.UpdateResistor(r, 200.0);
        _sim.Step(0.001);
        _sim.UpdateResistor(r, 100.0);
        _sim.Step(0.001);

        // Should still only have 1 event (no new events after unsubscribe)
        Assert.That(events, Has.Count.EqualTo(1));
    }

    #endregion

    #region Handler Exception Isolation

    [Test]
    public void Limit_HandlerException_DoesNotBreakSimulation()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();

        // Handler that throws
        using var sub1 = _sim.OnLimitEvent(evt => throw new Exception("Test exception"));

        // Handler that works
        using var sub2 = _sim.OnLimitEvent(evt => events.Add(evt));

        // Should not throw, and second handler should still receive event
        Assert.DoesNotThrow(() => _sim.Step(0.001));
        Assert.That(events, Has.Count.EqualTo(1));
    }

    #endregion

    #region Signed Value / Directional Behavior

    [Test]
    public void Limit_SignedCurrentPositive_ExceedsPositiveThreshold()
    {
        // Current flows from high to low potential (node -> ground)
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Positive threshold 0.1A, positive current 0.12A -> exceeds
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].ActualValue, Is.GreaterThan(0));
    }

    [Test]
    public void Limit_SignedCurrentNegative_DoesNotExceedPositiveThreshold()
    {
        // Current flows opposite direction (ground -> node)
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(_sim.Ground, node, 100.0); // Swapped nodes
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Positive threshold 0.1A, negative current -0.12A -> does NOT exceed
        // (because -0.12 is NOT > 0.1)
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        Assert.That(events, Is.Empty, "Negative current should not exceed positive threshold");
    }

    [Test]
    public void Limit_NegativeThreshold_DetectsNegativeCurrent()
    {
        // Test negative threshold to detect reverse current
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(_sim.Ground, node, 100.0); // Node order gives negative current
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Negative threshold -0.1A
        // Current is -0.12A, which is NOT > -0.1 (it's more negative)
        // To detect "more negative than X", we'd need a different comparison
        // But the current design uses value > threshold, so -0.12 is NOT > -0.1
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = -0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        // With signed comparison, -0.12 > -0.1 is FALSE, so no event
        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Limit_NegativeThreshold_TriggeredByLessNegativeCurrent()
    {
        // Current of -0.05A is greater than threshold of -0.1A
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(_sim.Ground, node, 200.0); // 12V/200R = 0.06A, but negative direction
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Actually wait - let's think about this more carefully
        // With resistor from Ground to node, current = (V_ground - V_node) / R = (0 - 12) / 200 = -0.06A
        // Threshold -0.1A
        // -0.06 > -0.1? YES (less negative is greater)
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = -0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        // -0.06 > -0.1 is TRUE
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].ActualValue, Is.EqualTo(-0.06).Within(Tolerances.Current));
    }

    #endregion

    #region Voltage Source Limits

    [Test]
    public void Limit_VoltageSourceCurrent_FiresEvent()
    {
        // 12V source with 100 ohm load delivers 0.12A
        // Note: Voltage source current sign convention may be negative
        // (current flowing OUT of positive terminal)
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        // Use a negative threshold since current may be reported as negative
        // This demonstrates signed comparison: -0.12 > -0.15 is TRUE
        _sim.SetVoltageSourceLimit(v, LimitKind.OverCurrent, new LimitConfig { Threshold = -0.15 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Component.ComponentType, Is.EqualTo("VoltageSource"));
        // Current should be around -0.12A (negative sign indicates direction)
        Assert.That(Math.Abs(events[0].ActualValue), Is.EqualTo(0.12).Within(Tolerances.Loose));
    }

    #endregion

    #region Simulation Time Tracking

    [Test]
    public void SimulationTime_AccumulatesCorrectly()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        Assert.That(_sim.SimulationTime, Is.EqualTo(0.0));

        _sim.Step(0.001);
        Assert.That(_sim.SimulationTime, Is.EqualTo(0.001).Within(1e-9));

        _sim.Step(0.002);
        Assert.That(_sim.SimulationTime, Is.EqualTo(0.003).Within(1e-9));

        _sim.Step(0.005);
        Assert.That(_sim.SimulationTime, Is.EqualTo(0.008).Within(1e-9));
    }

    [Test]
    public void SimulationTime_ResetsOnClear()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.Step(0.001);
        _sim.Step(0.001);

        Assert.That(_sim.SimulationTime, Is.EqualTo(0.002).Within(1e-9));

        _sim.Clear();

        Assert.That(_sim.SimulationTime, Is.EqualTo(0.0));
    }

    [Test]
    public void LimitEvent_IncludesSimulationTime()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        // Run a few steps first to accumulate time
        _sim.Step(0.001);

        // Remove and re-add limit so it fires again after some time has passed
        _sim.ClearResistorLimit(r, LimitKind.OverCurrent);
        _sim.Step(0.001);
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        var events = new List<LimitEvent>();
        using var sub = _sim.OnLimitEvent(evt => events.Add(evt));

        _sim.Step(0.001);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].SimulationTime, Is.EqualTo(0.003).Within(1e-9));
    }

    #endregion

    #region Limit Get/Set/Clear

    [Test]
    public void Limit_GetReturnsSetConfig()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);

        var config = new LimitConfig
        {
            Threshold = 0.5,
            Hysteresis = 0.1,
            FireEveryStep = true,
        };
        _sim.SetResistorLimit(r, LimitKind.OverCurrent, config);

        var retrieved = _sim.GetResistorLimit(r, LimitKind.OverCurrent);

        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Value.Threshold, Is.EqualTo(0.5));
        Assert.That(retrieved.Value.Hysteresis, Is.EqualTo(0.1));
        Assert.That(retrieved.Value.FireEveryStep, Is.True);
    }

    [Test]
    public void Limit_GetReturnsNullIfNotSet()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);

        var retrieved = _sim.GetResistorLimit(r, LimitKind.OverCurrent);

        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public void Limit_ClearRemovesLimit()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.5 });
        Assert.That(_sim.GetResistorLimit(r, LimitKind.OverCurrent), Is.Not.Null);

        _sim.ClearResistorLimit(r, LimitKind.OverCurrent);
        Assert.That(_sim.GetResistorLimit(r, LimitKind.OverCurrent), Is.Null);
    }

    [Test]
    public void Limit_SetOnNonExistentComponent_Throws()
    {
        Assert.Throws<InvalidComponentException>(() =>
            _sim.SetResistorLimit(
                new ResistorId(999),
                LimitKind.OverCurrent,
                new LimitConfig { Threshold = 0.1 }
            )
        );
    }

    #endregion

    #region No Handlers Optimization

    [Test]
    public void Limit_NoHandlers_DoesNotCrash()
    {
        var node = _sim.CreateNode();
        var r = _sim.AddResistor(node, _sim.Ground, 100.0);
        var v = _sim.AddVoltageSource(node, _sim.Ground, 12.0);

        _sim.SetResistorLimit(r, LimitKind.OverCurrent, new LimitConfig { Threshold = 0.1 });

        // No handlers registered - should not throw
        Assert.DoesNotThrow(() => _sim.Step(0.001));
    }

    #endregion
}
