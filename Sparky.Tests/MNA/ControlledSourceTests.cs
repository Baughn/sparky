using NUnit.Framework;
using Sparky.MNA.Api;

namespace Sparky.Tests.MNA;

[TestFixture]
public class ControlledSourceTests
{
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp()
    {
        _sim = new SimulationManager();
    }

    #region VCVS Tests

    [Test]
    public void VCVS_UnityGain_OutputMatchesInput()
    {
        // 10V -- (control) -- VCVS(gain=1) -- R(100) -- GND
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        _sim.AddVCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));
    }

    [Test]
    public void VCVS_Amplification_OutputIs10xInput()
    {
        // 1V control, gain 10 => 10V output
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 1.0);
        _sim.AddVCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 10.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));
    }

    [Test]
    public void VCVS_Attenuation_OutputIsHalfInput()
    {
        // 10V control, gain 0.5 => 5V output
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        _sim.AddVCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 0.5);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(5.0).Within(1e-6));
    }

    [Test]
    public void VCVS_NegativeGain_Inverts()
    {
        // 10V control, gain -1 => -10V output
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        _sim.AddVCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, -1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(-10.0).Within(1e-6));
    }

    [Test]
    public void VCVS_LoadedOutput_CurrentFlows()
    {
        // VCVS with load resistor, check current
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        var vcvsId = _sim.AddVCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // 10V across 100Ω = 0.1A delivered to load
        // Current convention: positive = from outPos to outNeg internally
        // When delivering to external load, current is negative (exiting through load)
        Assert.That(_sim.GetVCVSCurrent(vcvsId), Is.EqualTo(-0.1).Within(1e-6));
    }

    [Test]
    public void VCVS_DifferentialInput()
    {
        // Control voltage between two nodes (not ground-referenced)
        // 10V source creates 2V difference across a voltage divider
        var nSrc = _sim.CreateNode();
        var nCtrlP = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrlP, 100.0);  // Divider: 10V -> 5V
        _sim.AddResistor(nCtrlP, _sim.Ground, 100.0);

        // VCVS senses V(nCtrlP) - V(ground) = 5V, gain 2 => 10V output
        _sim.AddVCVS(nCtrlP, _sim.Ground, nOut, _sim.Ground, 2.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        Assert.That(_sim.GetVoltage(nCtrlP), Is.EqualTo(5.0).Within(1e-6));
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));
    }

    [Test]
    public void VCVS_Update_ChangesGain()
    {
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        var vcvsId = _sim.AddVCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));

        _sim.UpdateVCVS(vcvsId, 2.0);
        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(20.0).Within(1e-6));
    }

    #endregion

    #region VCCS Tests

    [Test]
    public void VCCS_BasicTransconductance()
    {
        // 1V control, gm=0.01 => 10mA output current
        // Output through 100Ω resistor => V = I*R = 0.01 * 100 = 1V
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 1.0);
        _sim.AddVCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 0.01);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // I = gm * Vin = 0.01 * 1 = 0.01A
        // V = I * R = 0.01 * 100 = 1V
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void VCCS_HigherTransconductance()
    {
        // 10V control, gm=0.1 => 1A current through 10Ω => 10V
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        _sim.AddVCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 0.1);
        _sim.AddResistor(nOut, _sim.Ground, 10.0);

        _sim.Step(0.001);

        // I = 0.1 * 10 = 1A, V = 1 * 10 = 10V
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));
    }

    [Test]
    public void VCCS_GetCurrent()
    {
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 5.0);
        var vccsId = _sim.AddVCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 0.02);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // I = 0.02 * 5 = 0.1A
        Assert.That(_sim.GetVCCSCurrent(vccsId), Is.EqualTo(0.1).Within(1e-6));
    }

    [Test]
    public void VCCS_Update_ChangesTransconductance()
    {
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        var vccsId = _sim.AddVCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 0.01);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));

        _sim.UpdateVCCS(vccsId, 0.02);
        _sim.Step(0.001);
        // I = 0.02 * 10 = 0.2A, V = 0.2 * 100 = 20V
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(20.0).Within(1e-6));
    }

    #endregion

    #region CCCS Tests

    [Test]
    public void CCCS_UnityGain_CurrentMirror()
    {
        // Simple current mirror: input current = output current
        // 10V -- R(100) -- CCCS_input -- GND
        //                  CCCS_output -- R(100) -- GND
        var nIn = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nIn, _sim.Ground, 10.0);
        // CCCS input is a short, so we need external resistor for current
        // But CCCS input is ctrlPos-ctrlNeg which is shorted internally
        // So we need: Vsrc -- R -- (ctrlPos) -- CCCS -- (ctrlNeg=GND)
        // The 10V through the 100Ω into the CCCS input creates 0.1A

        // Actually: 10V source to nIn, then resistor from nIn to CCCS control input
        var nCtrl = _sim.CreateNode();
        _sim.AddResistor(nIn, nCtrl, 100.0);  // Creates 0.1A current

        var cccsId = _sim.AddCCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Input current = 10V / 100Ω = 0.1A (CCCS input is a short)
        Assert.That(_sim.GetCCCSInputCurrent(cccsId), Is.EqualTo(0.1).Within(1e-6));
        // Output current = gain * input = 1 * 0.1 = 0.1A
        Assert.That(_sim.GetCCCSOutputCurrent(cccsId), Is.EqualTo(0.1).Within(1e-6));
        // Output voltage = 0.1A * 100Ω = 10V
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));
    }

    [Test]
    public void CCCS_Amplification_CurrentGain10()
    {
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 1.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);  // 1V/100Ω = 0.01A

        var cccsId = _sim.AddCCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 10.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Input current = 0.01A, gain = 10, output current = 0.1A
        Assert.That(_sim.GetCCCSInputCurrent(cccsId), Is.EqualTo(0.01).Within(1e-6));
        // Output voltage = 0.1A * 100Ω = 10V
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));
    }

    [Test]
    public void CCCS_InputAppearsAsShort()
    {
        // The control terminals should have ~0V across them
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);

        _sim.AddCCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // nCtrl should be at ~0V (shorted to ground through CCCS input)
        Assert.That(_sim.GetVoltage(nCtrl), Is.EqualTo(0.0).Within(1e-6));
    }

    [Test]
    public void CCCS_Update_ChangesGain()
    {
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);

        var cccsId = _sim.AddCCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));

        _sim.UpdateCCCS(cccsId, 2.0);
        _sim.Step(0.001);
        // Output current doubles => voltage doubles
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(20.0).Within(1e-6));
    }

    #endregion

    #region CCVS Tests

    [Test]
    public void CCVS_BasicTransresistance()
    {
        // 0.1A input current, rm=100 => 10V output
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);  // 10V/100Ω = 0.1A

        var ccvsId = _sim.AddCCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 100.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Input current = 0.1A, rm = 100 V/A, Vout = 0.1 * 100 = 10V
        Assert.That(_sim.GetCCVSInputCurrent(ccvsId), Is.EqualTo(0.1).Within(1e-6));
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));
    }

    [Test]
    public void CCVS_InputAppearsAsShort()
    {
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);

        _sim.AddCCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 100.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // nCtrl should be at ~0V (shorted to ground through CCVS input)
        Assert.That(_sim.GetVoltage(nCtrl), Is.EqualTo(0.0).Within(1e-6));
    }

    [Test]
    public void CCVS_OutputCurrent()
    {
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);  // 0.1A

        var ccvsId = _sim.AddCCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 100.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);

        // Vout = 10V, through 100Ω => I = 0.1A delivered to load
        // Current convention: negative when delivering to external load
        Assert.That(_sim.GetCCVSOutputCurrent(ccvsId), Is.EqualTo(-0.1).Within(1e-6));
    }

    [Test]
    public void CCVS_Update_ChangesTransresistance()
    {
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);  // 0.1A

        var ccvsId = _sim.AddCCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 100.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));

        _sim.UpdateCCVS(ccvsId, 200.0);
        _sim.Step(0.001);
        // Vout = 0.1A * 200 = 20V
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(20.0).Within(1e-6));
    }

    #endregion

    #region Lifecycle Tests

    [Test]
    public void VCVS_RemoveAndRecreate()
    {
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        var vcvsId = _sim.AddVCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));

        _sim.RemoveVCVS(vcvsId);
        Assert.That(_sim.VCVSExists(vcvsId), Is.False);

        // nOut should now be floating or 0 (only connected to resistor)
        _sim.Step(0.001);
        // With just a resistor to ground and no source, voltage is 0
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(0.0).Within(1e-6));
    }

    [Test]
    public void VCCS_Remove()
    {
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nCtrl, _sim.Ground, 10.0);
        var vccsId = _sim.AddVCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 0.01);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(10.0).Within(1e-6));

        _sim.RemoveVCCS(vccsId);
        Assert.That(_sim.VCCSExists(vccsId), Is.False);
    }

    [Test]
    public void CCCS_Remove()
    {
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);
        var cccsId = _sim.AddCCCS(nCtrl, _sim.Ground, nOut, _sim.Ground, 1.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        _sim.RemoveCCCS(cccsId);
        Assert.That(_sim.CCCSExists(cccsId), Is.False);
    }

    [Test]
    public void CCVS_Remove()
    {
        var nSrc = _sim.CreateNode();
        var nCtrl = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
        _sim.AddResistor(nSrc, nCtrl, 100.0);
        var ccvsId = _sim.AddCCVS(nCtrl, _sim.Ground, nOut, _sim.Ground, 100.0);
        _sim.AddResistor(nOut, _sim.Ground, 100.0);

        _sim.Step(0.001);
        _sim.RemoveCCVS(ccvsId);
        Assert.That(_sim.CCVSExists(ccvsId), Is.False);
    }

    [Test]
    public void InvalidId_Throws()
    {
        Assert.Throws<InvalidComponentException>(() => _sim.GetVCVSGain(new VcvsId(999)));
        Assert.Throws<InvalidComponentException>(() => _sim.GetVCCSTransconductance(new VccsId(999)));
        Assert.Throws<InvalidComponentException>(() => _sim.GetCCVSTransresistance(new CcvsId(999)));
        Assert.Throws<InvalidComponentException>(() => _sim.GetCCCSGain(new CccsId(999)));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void OpAmp_InvertingAmplifier()
    {
        // Model op-amp as high-gain VCVS (idealized)
        // Inverting amplifier: Vout = -Rf/Rin * Vin
        // With Rin = 10k, Rf = 100k, Vin = 1V => Vout = -10V
        var nVin = _sim.CreateNode();
        var nInv = _sim.CreateNode();  // Inverting input
        var nOut = _sim.CreateNode();

        _sim.AddVoltageSource(nVin, _sim.Ground, 1.0);  // 1V input
        _sim.AddResistor(nVin, nInv, 10000.0);  // Rin = 10k

        // Op-amp: Vout = A * (V+ - V-) where V+ = 0 (ground), V- = nInv
        // High gain VCVS sensing (Ground - nInv) = -nInv
        _sim.AddVCVS(_sim.Ground, nInv, nOut, _sim.Ground, 100000.0);  // A = 100k

        _sim.AddResistor(nInv, nOut, 100000.0);  // Rf = 100k

        // Output load
        _sim.AddResistor(nOut, _sim.Ground, 10000.0);

        _sim.Step(0.001);

        // With high open-loop gain, virtual short at inputs means:
        // nInv ≈ 0V (virtual ground)
        // Vout/Rf + Vin/Rin = 0 (assuming nInv = 0)
        // Vout = -Rf/Rin * Vin = -100k/10k * 1 = -10V
        Assert.That(_sim.GetVoltage(nOut), Is.EqualTo(-10.0).Within(0.1));
    }

    [Test]
    public void MultipleControlledSources_InSameCircuit()
    {
        // Cascade: VCVS -> VCCS -> resistor
        var nSrc = _sim.CreateNode();
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        _sim.AddVoltageSource(nSrc, _sim.Ground, 5.0);

        // VCVS: 5V * 2 = 10V at n1
        _sim.AddVCVS(nSrc, _sim.Ground, n1, _sim.Ground, 2.0);
        _sim.AddResistor(n1, _sim.Ground, 1000.0);  // Load for VCVS

        // VCCS: 10V * 0.01 = 0.1A into n2
        _sim.AddVCCS(n1, _sim.Ground, n2, _sim.Ground, 0.01);
        _sim.AddResistor(n2, _sim.Ground, 100.0);  // Load for VCCS

        _sim.Step(0.001);

        Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(1e-6));
        // n2 = 0.1A * 100Ω = 10V
        Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0).Within(1e-6));
    }

    #endregion
}
