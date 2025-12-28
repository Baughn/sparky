using NUnit.Framework;
using Sparky.Mna.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA;

/// <summary>
/// Tests for energy accounting (Gap 6 from GAME-DESIGN.md).
/// Validates cumulative energy tracking for sources and loads.
/// </summary>
[TestFixture]
public class EnergyTests {
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp() {
        _sim = new SimulationManager();
    }

    #region Basic Accumulation Tests

    [Test]
    public void ResistorEnergy_DCCircuit_EqualsExpectedDissipation() {
        // 10V -- R(100) -- GND
        // P = V²/R = 100/100 = 1W
        // E = P × t = 1W × 0.001s = 0.001J
        var nPos = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r = _sim.AddResistor(nPos, _sim.Ground, 100.0);

        _sim.Step(0.001); // 1ms

        double expectedEnergy = 1.0 * 0.001; // 1W × 1ms = 0.001J
        Assert.That(
            _sim.GetResistorEnergy(r),
            Is.EqualTo(expectedEnergy).Within(Tolerances.Energy)
        );
    }

    [Test]
    public void VoltageSourceEnergy_DCCircuit_EqualsExpectedDelivery() {
        // 10V -- R(100) -- GND
        // I = V/R = 0.1A
        // P = V × I = 10 × 0.1 = 1W
        // E = P × t = 1W × 0.001s = 0.001J
        var nPos = _sim.CreateNode();

        var vs = _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, _sim.Ground, 100.0);

        _sim.Step(0.001);

        double expectedEnergy = 1.0 * 0.001; // 1W × 1ms = 0.001J
        Assert.That(
            _sim.GetVoltageSourceEnergy(vs),
            Is.EqualTo(expectedEnergy).Within(Tolerances.Energy)
        );
    }

    [Test]
    public void MultiStepAccumulation_EnergySumsCorrectly() {
        // Run 10 steps of 0.001s each
        // Total energy should be 10 × (1W × 0.001s) = 0.01J
        var nPos = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r = _sim.AddResistor(nPos, _sim.Ground, 100.0);

        for (int i = 0; i < 10; i++) {
            _sim.Step(0.001);
        }

        double expectedEnergy = 1.0 * 0.01; // 1W × 10ms = 0.01J
        Assert.That(
            _sim.GetResistorEnergy(r),
            Is.EqualTo(expectedEnergy).Within(Tolerances.Energy * 10)
        );
    }

    #endregion

    #region Reset Tests

    [Test]
    public void ResetEnergyCounters_ClearsAllEnergy() {
        var nPos = _sim.CreateNode();

        var vs = _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r = _sim.AddResistor(nPos, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Energy should be non-zero
        Assert.That(_sim.GetResistorEnergy(r), Is.GreaterThan(0));
        Assert.That(_sim.GetVoltageSourceEnergy(vs), Is.GreaterThan(0));

        _sim.ResetEnergyCounters();

        // Energy should be zero after reset
        Assert.That(_sim.GetResistorEnergy(r), Is.EqualTo(0.0));
        Assert.That(_sim.GetVoltageSourceEnergy(vs), Is.EqualTo(0.0));
    }

    [Test]
    public void ResetEnergyCounter_ClearsSpecificComponent() {
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(nPos, n1, 100.0);
        var r2 = _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        double r1Energy = _sim.GetResistorEnergy(r1);
        double r2Energy = _sim.GetResistorEnergy(r2);

        // Reset only r1
        _sim.ResetEnergyCounter(r1);

        Assert.That(_sim.GetResistorEnergy(r1), Is.EqualTo(0.0));
        Assert.That(_sim.GetResistorEnergy(r2), Is.EqualTo(r2Energy).Within(Tolerances.Energy));
    }

    #endregion

    #region Energy Conservation Tests

    [Test]
    public void EnergyConservation_SourceEnergyEqualsLoadEnergy() {
        // Energy delivered by source should equal energy dissipated by load
        var nPos = _sim.CreateNode();

        var vs = _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r = _sim.AddResistor(nPos, _sim.Ground, 100.0);

        _sim.Step(0.001);

        double sourceEnergy = _sim.GetVoltageSourceEnergy(vs);
        double loadEnergy = _sim.GetResistorEnergy(r);

        Assert.That(sourceEnergy, Is.EqualTo(loadEnergy).Within(Tolerances.Loose));
    }

    [Test]
    public void EnergyConservation_VoltageDivider_SourceEqualsSum() {
        // 10V -- R1(100) -- n1 -- R2(100) -- GND
        // Source energy = R1 energy + R2 energy
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        var vs = _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(nPos, n1, 100.0);
        var r2 = _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        double sourceEnergy = _sim.GetVoltageSourceEnergy(vs);
        double totalLoadEnergy = _sim.GetResistorEnergy(r1) + _sim.GetResistorEnergy(r2);

        Assert.That(sourceEnergy, Is.EqualTo(totalLoadEnergy).Within(Tolerances.Loose));
    }

    #endregion

    #region Line Optimization Tests

    [Test]
    public void LineOptimizedResistors_EnergyDistributedByResistanceRatio() {
        // Three resistors in series: R1(100) -- R2(200) -- R3(300)
        // Total R = 600, current I = 10/600 = 1/60 A
        // Power distribution: P_i = I² × R_i
        // P1 : P2 : P3 = 100 : 200 : 300 = 1 : 2 : 3
        _sim.EnableLineOptimization = true;

        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(nPos, n1, 100.0);
        var r2 = _sim.AddResistor(n1, n2, 200.0);
        var r3 = _sim.AddResistor(n2, _sim.Ground, 300.0);

        _sim.Step(0.001);

        double e1 = _sim.GetResistorEnergy(r1);
        double e2 = _sim.GetResistorEnergy(r2);
        double e3 = _sim.GetResistorEnergy(r3);

        // Verify ratio: E1:E2:E3 should be 100:200:300 = 1:2:3
        Assert.That(e2, Is.EqualTo(e1 * 2).Within(Tolerances.Loose));
        Assert.That(e3, Is.EqualTo(e1 * 3).Within(Tolerances.Loose));
    }

    [Test]
    public void LineOptimizedResistors_TotalEnergyMatchesEquivalent() {
        // Compare optimized chain to single equivalent resistor
        _sim.EnableLineOptimization = true;

        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var vs = _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        var r1 = _sim.AddResistor(nPos, n1, 100.0);
        var r2 = _sim.AddResistor(n1, n2, 200.0);
        var r3 = _sim.AddResistor(n2, _sim.Ground, 300.0);

        _sim.Step(0.001);

        double totalResistorEnergy =
            _sim.GetResistorEnergy(r1) + _sim.GetResistorEnergy(r2) + _sim.GetResistorEnergy(r3);
        double sourceEnergy = _sim.GetVoltageSourceEnergy(vs);

        // Total resistor energy should equal source energy
        Assert.That(totalResistorEnergy, Is.EqualTo(sourceEnergy).Within(Tolerances.Loose));
    }

    #endregion

    #region Transient Tests

    [Test]
    public void CapacitorEnergy_Charging_PositiveNetAbsorption() {
        // RC charging circuit: capacitor absorbs energy while charging
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 1000.0);
        var c = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

        // Charge for several time constants
        for (int i = 0; i < 50; i++) {
            _sim.Step(1e-4);
        }

        // Net energy absorbed by capacitor should be positive (charging)
        double capEnergy = _sim.GetCapacitorEnergy(c);
        Assert.That(capEnergy, Is.GreaterThan(0));
    }

    [Test]
    public void InductorEnergy_Charging_PositiveNetAbsorption() {
        // RL charging circuit: inductor absorbs energy while current builds
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
        _sim.AddResistor(nPos, n1, 100.0);
        var l = _sim.AddInductor(n1, _sim.Ground, 0.1);

        // Let current build up
        for (int i = 0; i < 50; i++) {
            _sim.Step(1e-4);
        }

        // Net energy absorbed by inductor should be positive (storing)
        double indEnergy = _sim.GetInductorEnergy(l);
        Assert.That(indEnergy, Is.GreaterThan(0));
    }

    #endregion

    #region Current Source Tests

    [Test]
    public void CurrentSourceEnergy_DeliveringPower_TracksCorrectly() {
        // Current source pushing current through resistor
        // Current flows from nodeIn (Ground) to nodeOut (n1)
        // n1 is at higher potential (10V) than Ground (0V)
        // So current source is delivering power (pushing current uphill)
        var n1 = _sim.CreateNode();

        var cs = _sim.AddCurrentSource(_sim.Ground, n1, 0.1); // 100mA from GND to n1
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // P = V × I where V = V_nodeIn - V_nodeOut = 0 - 10 = -10V
        // The current source pushes against the voltage drop
        // Power delivered by source = |V| × I = 1W (magnitude)
        // Sign convention: P = V × I = -10 × 0.1 = -1W (absorbing in source convention)
        // Or equivalently: the source is doing work delivering power to the circuit
        double csEnergy = _sim.GetCurrentSourceEnergy(cs);
        double expectedMagnitude = 0.001; // 1W × 1ms = 0.001J
        Assert.That(Math.Abs(csEnergy), Is.EqualTo(expectedMagnitude).Within(Tolerances.Loose));
    }

    #endregion

    #region Invalid ID Tests

    [Test]
    public void GetResistorEnergy_InvalidId_ThrowsException() {
        var invalidId = new ResistorId(999);
        Assert.Throws<InvalidComponentException>(() => _sim.GetResistorEnergy(invalidId));
    }

    [Test]
    public void GetVoltageSourceEnergy_InvalidId_ThrowsException() {
        var invalidId = new VoltageSourceId(999);
        Assert.Throws<InvalidComponentException>(() => _sim.GetVoltageSourceEnergy(invalidId));
    }

    [Test]
    public void GetCurrentSourceEnergy_InvalidId_ThrowsException() {
        var invalidId = new CurrentSourceId(999);
        Assert.Throws<InvalidComponentException>(() => _sim.GetCurrentSourceEnergy(invalidId));
    }

    [Test]
    public void GetCapacitorEnergy_InvalidId_ThrowsException() {
        var invalidId = new CapacitorId(999);
        Assert.Throws<InvalidComponentException>(() => _sim.GetCapacitorEnergy(invalidId));
    }

    [Test]
    public void GetInductorEnergy_InvalidId_ThrowsException() {
        var invalidId = new InductorId(999);
        Assert.Throws<InvalidComponentException>(() => _sim.GetInductorEnergy(invalidId));
    }

    [Test]
    public void GetDiodeEnergy_InvalidId_ThrowsException() {
        var invalidId = new DiodeId(999);
        Assert.Throws<InvalidComponentException>(() => _sim.GetDiodeEnergy(invalidId));
    }

    [Test]
    public void ResetEnergyCounter_InvalidId_ThrowsException() {
        var invalidId = new ResistorId(999);
        Assert.Throws<InvalidComponentException>(() => _sim.ResetEnergyCounter(invalidId));
    }

    #endregion

    #region Diode Energy Tests

    [Test]
    public void DiodeEnergy_ForwardBiased_DissipatesEnergy() {
        // Forward biased diode dissipates energy as heat
        var nPos = _sim.CreateNode();
        var n1 = _sim.CreateNode();

        _sim.AddVoltageSource(nPos, _sim.Ground, 5.0);
        _sim.AddResistor(nPos, n1, 1000.0); // Current limiting resistor
        var d = _sim.AddDiode(n1, _sim.Ground);

        _sim.Step(0.001);

        // Diode should dissipate some energy
        double diodeEnergy = _sim.GetDiodeEnergy(d);
        Assert.That(diodeEnergy, Is.GreaterThan(0));
    }

    #endregion
}
