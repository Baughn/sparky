using NUnit.Framework;
using Sparky.Mna.Api;

namespace Sparky.Tests.MNA;

/// <summary>
/// Tests for edge cases in parameter validation: NaN, Infinity, and other edge values.
/// These tests ensure the API properly rejects or handles invalid floating-point values.
/// </summary>
[TestFixture]
public class ParameterValidationEdgeCaseTests {
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp() {
        _sim = new SimulationManager();
    }

    #region Resistor NaN/Infinity Tests

    [Test]
    public void AddResistor_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddResistor(n1, n2, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("resistance"));
    }

    [Test]
    public void AddResistor_WithPositiveInfinity_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddResistor(n1, n2, double.PositiveInfinity)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("resistance"));
    }

    [Test]
    public void AddResistor_WithNegativeInfinity_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddResistor(n1, n2, double.NegativeInfinity)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("resistance"));
    }

    [Test]
    public void UpdateResistor_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();
        var rId = _sim.AddResistor(n1, n2, 100.0);

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.UpdateResistor(rId, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("resistance"));
    }

    #endregion

    #region Capacitor NaN/Infinity Tests

    [Test]
    public void AddCapacitor_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddCapacitor(n1, n2, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("capacitance"));
    }

    [Test]
    public void AddCapacitor_WithPositiveInfinity_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddCapacitor(n1, n2, double.PositiveInfinity)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("capacitance"));
    }

    [Test]
    public void AddCapacitor_WithZero_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() => _sim.AddCapacitor(n1, n2, 0.0));

        Assert.That(ex!.ParameterName, Is.EqualTo("capacitance"));
    }

    #endregion

    #region Inductor NaN/Infinity Tests

    [Test]
    public void AddInductor_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddInductor(n1, n2, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("inductance"));
    }

    [Test]
    public void AddInductor_WithPositiveInfinity_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddInductor(n1, n2, double.PositiveInfinity)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("inductance"));
    }

    [Test]
    public void AddInductor_WithZero_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() => _sim.AddInductor(n1, n2, 0.0));

        Assert.That(ex!.ParameterName, Is.EqualTo("inductance"));
    }

    #endregion

    #region Voltage Source NaN/Infinity Tests

    [Test]
    public void AddVoltageSource_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddVoltageSource(n1, _sim.Ground, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("voltage"));
    }

    [Test]
    public void AddVoltageSource_WithPositiveInfinity_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddVoltageSource(n1, _sim.Ground, double.PositiveInfinity)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("voltage"));
    }

    [Test]
    public void UpdateVoltageSource_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var vsId = _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.UpdateVoltageSource(vsId, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("voltage"));
    }

    #endregion

    #region Current Source NaN/Infinity Tests

    [Test]
    public void AddCurrentSource_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddCurrentSource(_sim.Ground, n1, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("current"));
    }

    [Test]
    public void AddCurrentSource_WithPositiveInfinity_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddCurrentSource(_sim.Ground, n1, double.PositiveInfinity)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("current"));
    }

    #endregion

    #region Transformer NaN/Infinity Tests

    [Test]
    public void AddTransformer_WithNaN_ThrowsInvalidParameterException() {
        var p1 = _sim.CreateNode();
        var p2 = _sim.CreateNode();
        var s1 = _sim.CreateNode();
        var s2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddTransformer(p1, p2, s1, s2, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("ratio"));
    }

    [Test]
    public void AddTransformer_WithPositiveInfinity_ThrowsInvalidParameterException() {
        var p1 = _sim.CreateNode();
        var p2 = _sim.CreateNode();
        var s1 = _sim.CreateNode();
        var s2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddTransformer(p1, p2, s1, s2, double.PositiveInfinity)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("ratio"));
    }

    #endregion

    #region Controlled Source NaN/Infinity Tests

    [Test]
    public void AddVCVS_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddVCVS(n1, _sim.Ground, n2, _sim.Ground, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("gain"));
    }

    [Test]
    public void AddVCCS_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddVCCS(n1, _sim.Ground, n2, _sim.Ground, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("transconductance"));
    }

    [Test]
    public void AddCCVS_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddCCVS(n1, _sim.Ground, n2, _sim.Ground, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("transresistance"));
    }

    [Test]
    public void AddCCCS_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.AddCCCS(n1, _sim.Ground, n2, _sim.Ground, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("gain"));
    }

    #endregion

    #region Step Time NaN/Infinity Tests

    [Test]
    public void Step_WithNaN_ThrowsArgumentException() {
        var n1 = _sim.CreateNode();
        _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        Assert.Throws<ArgumentException>(() => _sim.Step(double.NaN));
    }

    [Test]
    public void Step_WithNegativeTime_ThrowsArgumentException() {
        var n1 = _sim.CreateNode();
        _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        Assert.Throws<ArgumentException>(() => _sim.Step(-0.001));
    }

    [Test]
    public void Step_WithPositiveInfinity_ThrowsArgumentException() {
        var n1 = _sim.CreateNode();
        _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        Assert.Throws<ArgumentException>(() => _sim.Step(double.PositiveInfinity));
    }

    #endregion

    #region Initial Condition Edge Cases

    [Test]
    public void SetCapacitorVoltage_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var cId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.SetCapacitorVoltage(cId, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("voltage"));
    }

    [Test]
    public void SetInductorCurrent_WithNaN_ThrowsInvalidParameterException() {
        var n1 = _sim.CreateNode();
        var lId = _sim.AddInductor(n1, _sim.Ground, 1e-3);

        var ex = Assert.Throws<InvalidParameterException>(() =>
            _sim.SetInductorCurrent(lId, double.NaN)
        );

        Assert.That(ex!.ParameterName, Is.EqualTo("current"));
    }

    #endregion
}
