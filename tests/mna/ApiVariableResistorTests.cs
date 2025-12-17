using NUnit.Framework;
using Sparky.MNA.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA;

[TestFixture]
public class ApiVariableResistorTests {
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp() {
        _sim = new SimulationManager();
        _sim.EnableLineOptimization = true;
    }

    #region Variable Resistor Skips Optimization

    [Test]
    public void VariableResistor_InChain_SkipsLineOptimization() {
        // 10V -- R1(10) -- N1 -- R2_var(10) -- N2 -- R3(10) -- GND
        // With R2 as variable, N1 and N2 should NOT be optimized
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 10.0); // Regular
        _sim.AddResistor(n1, n2, 10.0, isVariable: true); // Variable
        _sim.AddResistor(n2, _sim.Ground, 10.0); // Regular

        _sim.Step(0.001);

        // Variable resistor breaks the chain - nodes should not be optimized
        Assert.That(_sim.IsNodeOptimized(n1), Is.False);
        Assert.That(_sim.IsNodeOptimized(n2), Is.False);

        // Circuit should still work correctly
        // Total R = 30, I = 10/30 = 1/3 A
        // V(n1) = 10 - 10*(1/3) = 20/3 ≈ 6.67
        // V(n2) = 10/3 ≈ 3.33
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(20.0 / 3.0).Within(Tolerances.Voltage));
        Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0 / 3.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void VariableResistor_UpdateUsesFastPath() {
        // Variable resistor should update without triggering rebuild
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var rVar = _sim.AddResistor(nPos, n1, 100.0, isVariable: true);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(5.0).Within(Tolerances.Voltage));

        // Update variable resistor - should use fast path (no rebuild)
        _sim.UpdateResistor(rVar, 300.0);

        _sim.Step(0.001);
        // New voltage: 10 * 100/(300+100) = 2.5V
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(2.5).Within(Tolerances.Voltage));
    }

    [Test]
    public void RegularResistor_InChain_StillOptimizes() {
        // 10V -- R1(10) -- N1 -- R2(10) -- N2 -- R3(10) -- GND
        // All regular resistors - N1 should be optimized
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 10.0);
        _sim.AddResistor(n1, n2, 10.0);
        _sim.AddResistor(n2, _sim.Ground, 10.0);

        _sim.Step(0.001);

        // Intermediate nodes should be optimized (merged chain)
        Assert.That(_sim.IsNodeOptimized(n1), Is.True);
        Assert.That(_sim.IsNodeOptimized(n2), Is.True);

        // Values should still be correct via interpolation
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(20.0 / 3.0).Within(Tolerances.Voltage));
        Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0 / 3.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void MixedChain_VariableResistorBreaksChain() {
        // 10V -- R1 -- N1 -- R2 -- N2 -- R3_var -- N3 -- R4 -- N4 -- R5 -- GND
        // R3 is variable, so:
        // - N1, N2 could be optimized (chain R1-R2)
        // - N3, N4 could be optimized (chain R4-R5)
        // But R3_var breaks the full chain
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();
        var n3 = _sim.CreateNode();
        var n4 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 10.0); // R1
        _sim.AddResistor(n1, n2, 10.0); // R2
        _sim.AddResistor(n2, n3, 10.0, isVariable: true); // R3_var
        _sim.AddResistor(n3, n4, 10.0); // R4
        _sim.AddResistor(n4, _sim.Ground, 10.0); // R5

        _sim.Step(0.001);

        // N2 and N3 are endpoints of the variable resistor - should not be optimized
        Assert.That(_sim.IsNodeOptimized(n2), Is.False);
        Assert.That(_sim.IsNodeOptimized(n3), Is.False);

        // Total R = 50, V should drop evenly
        // Each resistor drops 2V
        Assert.That(_sim.GetVoltage(n2), Is.EqualTo(6.0).Within(Tolerances.Voltage));
        Assert.That(_sim.GetVoltage(n3), Is.EqualTo(4.0).Within(Tolerances.Voltage));
    }

    #endregion

    #region Variable Resistor Default Behavior

    [Test]
    public void AddResistor_DefaultIsNotVariable() {
        // Default should be non-variable (optimizable)
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 10.0); // Default
        _sim.AddResistor(n1, n2, 10.0); // Default
        _sim.AddResistor(n2, _sim.Ground, 10.0); // Default

        _sim.Step(0.001);

        // Should optimize
        Assert.That(_sim.IsNodeOptimized(n1), Is.True);
    }

    [Test]
    public void VariableResistor_ExplicitFalse_StillOptimizes() {
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 10.0, isVariable: false);
        _sim.AddResistor(n1, n2, 10.0, isVariable: false);
        _sim.AddResistor(n2, _sim.Ground, 10.0, isVariable: false);

        _sim.Step(0.001);

        Assert.That(_sim.IsNodeOptimized(n1), Is.True);
    }

    #endregion

    #region Variable Resistor Circuit Behavior

    [Test]
    public void VariableResistor_SimulatesCorrectly() {
        // Simple voltage divider with variable resistor
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var rVar = _sim.AddResistor(nPos, n1, 100.0, isVariable: true);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(5.0).Within(Tolerances.Voltage));

        // Simulate potentiometer sweep
        _sim.UpdateResistor(rVar, 50.0);
        _sim.Step(0.001);
        // V = 10 * 100/(50+100) = 6.67
        Assert.That(
            _sim.GetVoltage(n1),
            Is.EqualTo(10.0 * 100.0 / 150.0).Within(Tolerances.Voltage)
        );

        _sim.UpdateResistor(rVar, 200.0);
        _sim.Step(0.001);
        // V = 10 * 100/(200+100) = 3.33
        Assert.That(
            _sim.GetVoltage(n1),
            Is.EqualTo(10.0 * 100.0 / 300.0).Within(Tolerances.Voltage)
        );
    }

    #endregion
}
