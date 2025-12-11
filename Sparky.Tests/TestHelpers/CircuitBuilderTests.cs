using NUnit.Framework;
using Sparky.MNA.Api;

namespace Sparky.Tests.TestHelpers;

/// <summary>
/// Tests for the CircuitBuilder fluent API and CircuitPatterns library.
/// </summary>
[TestFixture]
public class CircuitBuilderTests
{
    #region Named Node Tests

    [Test]
    public void Node_SameName_ReturnsSameNodeId()
    {
        var builder = new CircuitBuilder();

        var n1 = builder.Node("test");
        var n2 = builder.Node("test");

        Assert.That(n1, Is.EqualTo(n2));
    }

    [Test]
    public void Node_DifferentNames_ReturnsDifferentNodeIds()
    {
        var builder = new CircuitBuilder();

        var n1 = builder.Node("a");
        var n2 = builder.Node("b");

        Assert.That(n1, Is.Not.EqualTo(n2));
    }

    [Test]
    public void Ground_ReturnsSimulationGround()
    {
        var builder = new CircuitBuilder();

        Assert.That(builder.Ground, Is.EqualTo(builder.Sim.Ground));
    }

    [Test]
    [TestCase("GND")]
    [TestCase("gnd")]
    [TestCase("Gnd")]
    [TestCase("gND")]
    public void Node_GND_CaseInsensitive(string gndName)
    {
        var builder = new CircuitBuilder();

        Assert.That(builder.Node(gndName), Is.EqualTo(builder.Sim.Ground));
    }

    #endregion

    #region Fluent Chaining Tests

    [Test]
    public void FluentChaining_ReturnsBuilderForContinuation()
    {
        var builder = new CircuitBuilder();

        var result = builder
            .VoltageSource(10.0, "src")
            .Resistor(100.0, "src", "mid")
            .Resistor(100.0, "mid", "GND");

        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void Step_ReturnsBuilderForContinuation()
    {
        var builder = new CircuitBuilder().VoltageSource(10.0, "src").Resistor(100.0, "src", "GND");

        var result = builder.Step();

        Assert.That(result, Is.SameAs(builder));
    }

    [Test]
    public void StepN_ReturnsBuilderForContinuation()
    {
        var builder = new CircuitBuilder().VoltageSource(10.0, "src").Resistor(100.0, "src", "GND");

        var result = builder.StepN(5);

        Assert.That(result, Is.SameAs(builder));
    }

    #endregion

    #region Voltage Divider Pattern Tests

    [Test]
    public void VoltageDivider_EqualResistors_HalfVoltage()
    {
        var c = CircuitPatterns.VoltageDivider(10.0, 100.0, 100.0).Step();

        Assert.That(c.V("mid"), Is.EqualTo(5.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void VoltageDivider_UnequalResistors_ProportionalVoltage()
    {
        // 10V with R1=100, R2=300 → Vmid = 10 * 300/(100+300) = 7.5V
        var c = CircuitPatterns.VoltageDivider(10.0, 100.0, 300.0).Step();

        Assert.That(c.V("mid"), Is.EqualTo(7.5).Within(Tolerances.Voltage));
    }

    [Test]
    public void VoltageDivider_SourceNodeHasFullVoltage()
    {
        var c = CircuitPatterns.VoltageDivider(12.0, 100.0, 100.0).Step();

        Assert.That(c.V("src"), Is.EqualTo(12.0).Within(Tolerances.Voltage));
    }

    #endregion

    #region RC Circuit Pattern Tests

    [Test]
    public void RCCircuit_InitiallyUncharged()
    {
        var c = CircuitPatterns.RCCircuit(10.0, 1000.0, 1e-6);

        // Before stepping, capacitor should be at 0V
        Assert.That(c.V("cap"), Is.EqualTo(0.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void RCCircuit_ChargesOverTime()
    {
        // τ = RC = 1000 * 1e-6 = 1ms
        // After 5τ (5ms), should be ~99.3% charged
        // Note: Backward Euler integration undershoots slightly
        var c = CircuitPatterns.RCCircuit(10.0, 1000.0, 1e-6).StepN(50, dt: 0.0001); // 5ms total

        Assert.That(c.V("cap"), Is.EqualTo(10.0).Within(0.1));
    }

    [Test]
    public void RCCircuit_PartialCharge()
    {
        // After 1τ, should be ~63.2% charged (1 - e^-1)
        // τ = 1000 * 1e-6 = 1ms
        var c = CircuitPatterns.RCCircuit(10.0, 1000.0, 1e-6).StepN(10, dt: 0.0001); // 1ms total = 1τ

        // Expected: 10 * 0.632 = 6.32V
        Assert.That(c.V("cap"), Is.EqualTo(6.32).Within(0.2));
    }

    #endregion

    #region RL Circuit Pattern Tests

    [Test]
    public void RLCircuit_SteadyState_FullVoltageAcrossInductor()
    {
        // In steady state, inductor acts as short circuit
        // All voltage drops across resistor
        var c = CircuitPatterns.RLCircuit(10.0, 100.0, 1e-3).StepN(100, dt: 0.001); // 100ms >> 5τ

        // Voltage at "ind" node (between R and L) should be near 0
        // because inductor is nearly shorted
        Assert.That(c.V("ind"), Is.LessThan(Tolerances.Loose));
    }

    #endregion

    #region Series RLC Pattern Tests

    [Test]
    public void SeriesRLC_HasCorrectNodeNames()
    {
        var c = CircuitPatterns.SeriesRLC(10.0, 100.0, 1e-3, 1e-6);

        // Verify we can access expected nodes
        Assert.DoesNotThrow(() => c.V("src"));
        Assert.DoesNotThrow(() => c.V("r_out"));
        Assert.DoesNotThrow(() => c.V("l_out"));
    }

    [Test]
    public void SeriesRLC_SteadyState_CapacitorCharges()
    {
        var c = CircuitPatterns.SeriesRLC(10.0, 100.0, 1e-3, 1e-6).StepN(200, dt: 0.001); // Long enough for transients to settle

        // In DC steady state, capacitor blocks current
        // Capacitor should charge to source voltage
        Assert.That(c.V("l_out"), Is.EqualTo(10.0).Within(Tolerances.Loose));
    }

    #endregion

    #region Resistive Load Pattern Tests

    [Test]
    public void ResistiveLoad_OhmsLaw()
    {
        var c = CircuitPatterns.ResistiveLoad(10.0, 100.0).Step();

        Assert.That(c.V("src"), Is.EqualTo(10.0).Within(Tolerances.Voltage));
    }

    #endregion

    #region Current Source Pattern Tests

    [Test]
    public void CurrentSourceWithLoad_OhmsLaw()
    {
        // I = 0.1A, R = 100Ω → V = 10V
        var c = CircuitPatterns.CurrentSourceWithLoad(0.1, 100.0).Step();

        Assert.That(c.V("load"), Is.EqualTo(10.0).Within(Tolerances.Voltage));
    }

    #endregion

    #region Component Method Tests

    [Test]
    public void Diode_MethodExists_AndWorks()
    {
        var c = new CircuitBuilder()
            .VoltageSource(5.0, "src")
            .Resistor(1000.0, "src", "anode")
            .Diode("anode", "GND")
            .Step();

        // Forward biased diode should have ~0.6-0.7V drop
        Assert.That(c.V("anode"), Is.GreaterThan(0.5).And.LessThan(0.8));
    }

    [Test]
    public void CurrentSource_MethodExists_AndWorks()
    {
        var c = new CircuitBuilder()
            .CurrentSource(0.01, "GND", "n1")
            .Resistor(100.0, "n1", "GND")
            .Step();

        // V = I × R = 0.01 × 100 = 1V
        Assert.That(c.V("n1"), Is.EqualTo(1.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void Capacitor_MethodExists_AndWorks()
    {
        var c = new CircuitBuilder()
            .VoltageSource(10.0, "src")
            .Resistor(100.0, "src", "cap")
            .Capacitor(1e-6, "cap")
            .StepN(100, dt: 0.001);

        Assert.That(c.V("cap"), Is.EqualTo(10.0).Within(Tolerances.Loose));
    }

    [Test]
    public void Inductor_MethodExists_AndWorks()
    {
        var c = new CircuitBuilder()
            .VoltageSource(10.0, "src")
            .Resistor(100.0, "src", "ind")
            .Inductor(1e-3, "ind")
            .StepN(100, dt: 0.001);

        // Inductor shorts to ground in steady state
        Assert.That(c.V("ind"), Is.LessThan(Tolerances.Loose));
    }

    [Test]
    public void Switch_MethodExists_AndReturnsSwitchId()
    {
        var c = new CircuitBuilder().VoltageSource(10.0, "src");

        var swId = c.Switch("src", "load", closed: true);
        c.Resistor(100.0, "load", "GND").Step();

        Assert.That(c.V("load"), Is.EqualTo(10.0).Within(0.1));

        // Can control switch via returned ID
        c.Sim.SetSwitchState(swId, false);
        c.Step();
        Assert.That(c.V("load"), Is.LessThan(0.1));
    }

    [Test]
    public void Diode_DefaultCathode_IsGround()
    {
        // Diode with only anode specified should connect cathode to ground
        var c = new CircuitBuilder()
            .VoltageSource(5.0, "src")
            .Resistor(1000.0, "src", "anode")
            .Diode("anode") // cathode defaults to GND
            .Step();

        // Forward biased diode should have ~0.6-0.7V drop
        Assert.That(c.V("anode"), Is.GreaterThan(0.5).And.LessThan(0.8));
    }

    #endregion

    #region Sim Access Tests

    [Test]
    public void Sim_ProvidesAccessToUnderlyingSimulation()
    {
        var builder = new CircuitBuilder().VoltageSource(10.0, "src").Resistor(100.0, "src", "GND");

        // Can access SimulationManager for advanced operations
        Assert.That(builder.Sim, Is.Not.Null);
        Assert.That(builder.Sim, Is.TypeOf<SimulationManager>());
    }

    [Test]
    public void Sim_AllowsDirectNodeAccess()
    {
        var builder = new CircuitBuilder()
            .VoltageSource(10.0, "src")
            .Resistor(100.0, "src", "GND")
            .Step();

        // Can use named nodes with direct sim access
        var srcNode = builder.Node("src");
        Assert.That(builder.Sim.GetVoltage(srcNode), Is.EqualTo(10.0).Within(Tolerances.Voltage));
    }

    #endregion
}
