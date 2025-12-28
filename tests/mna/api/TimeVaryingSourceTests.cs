using System;
using NUnit.Framework;
using Sparky.Mna.Api;
using Sparky.Mna.Utilities;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA;

/// <summary>
/// Tests for time-varying source utilities (AC, PWM).
/// </summary>
[TestFixture]
public class TimeVaryingSourceTests {
    private SimulationManager _sim = null!;

    [SetUp]
    public void SetUp() {
        _sim = new SimulationManager();
    }

    #region AC Voltage Source Tests

    [Test]
    public void AcVoltageSource_AtTimeZero_ReturnsOffsetPlusSineOfPhase() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            amplitude: 10.0,
            frequency: 1.0,
            phase: 0,
            offset: 0
        );

        // At t=0 with phase=0: sin(0) = 0
        Assert.That(ac.GetValue(0), Is.EqualTo(0.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void AcVoltageSource_AtQuarterPeriod_ReturnsPeakAmplitude() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);

        // At t=0.25 (quarter period of 1Hz): sin(π/2) = 1
        Assert.That(ac.GetValue(0.25), Is.EqualTo(10.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void AcVoltageSource_WithOffset_AddsOffsetToWaveform() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            amplitude: 10.0,
            frequency: 1.0,
            offset: 5.0
        );

        // At t=0.25: 5 + 10*sin(π/2) = 5 + 10 = 15
        Assert.That(ac.GetValue(0.25), Is.EqualTo(15.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void AcVoltageSource_WithPhase_ShiftsWaveform() {
        var n1 = _sim.CreateNode();
        // Phase = π/2 shifts the waveform by quarter period
        var ac = new AcVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            amplitude: 10.0,
            frequency: 1.0,
            phase: Math.PI / 2
        );

        // At t=0 with phase=π/2: sin(π/2) = 1
        Assert.That(ac.GetValue(0), Is.EqualTo(10.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void AcVoltageSource_Update_SetsVoltageToCurrentTime() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);

        // Add a load so the circuit is complete
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        // Advance to quarter period
        for (int i = 0; i < 25; i++) {
            ac.Update();
            _sim.Step(0.01);
        }

        // Now at t=0.25, node voltage should be ~10V (peak of sine)
        double voltage = _sim.GetVoltage(n1);
        Assert.That(voltage, Is.EqualTo(10.0).Within(0.1)); // Small tolerance for discrete steps
    }

    [Test]
    public void AcVoltageSource_ParameterChange_AffectsNextUpdate() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);

        // Change amplitude mid-simulation
        ac.Amplitude = 20.0;

        // At t=0.25: 20*sin(π/2) = 20
        Assert.That(ac.GetValue(0.25), Is.EqualTo(20.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void AcVoltageSource_Remove_RemovesFromSimulation() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);

        Assert.That(ac.Exists, Is.True);

        ac.Remove();

        Assert.That(ac.Exists, Is.False);
        Assert.That(_sim.VoltageSourceExists(ac.Id), Is.False);
    }

    #endregion

    #region AC Current Source Tests

    [Test]
    public void AcCurrentSource_AtQuarterPeriod_ReturnsPeakAmplitude() {
        var n1 = _sim.CreateNode();
        var ac = new AcCurrentSource(_sim, _sim.Ground, n1, amplitude: 0.1, frequency: 1.0);

        // At t=0.25 (quarter period of 1Hz): sin(π/2) = 1
        Assert.That(ac.GetValue(0.25), Is.EqualTo(0.1).Within(Tolerances.Current));
    }

    [Test]
    public void AcCurrentSource_Update_SetsCurrentToCurrentTime() {
        var n1 = _sim.CreateNode();
        var ac = new AcCurrentSource(_sim, _sim.Ground, n1, amplitude: 0.1, frequency: 1.0);

        // Add a load so we can measure voltage
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        // Advance to quarter period
        for (int i = 0; i < 25; i++) {
            ac.Update();
            _sim.Step(0.01);
        }

        // At t=0.25, current = 0.1A, voltage = IR = 0.1 * 100 = 10V
        double voltage = _sim.GetVoltage(n1);
        Assert.That(voltage, Is.EqualTo(10.0).Within(0.2));
    }

    #endregion

    #region PWM Voltage Source Tests

    [Test]
    public void PwmVoltageSource_DuringOnPhase_ReturnsVHigh() {
        var n1 = _sim.CreateNode();
        var pwm = new PwmVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            vHigh: 5.0,
            vLow: 0.0,
            frequency: 10.0,
            dutyCycle: 0.5
        );

        // Period = 0.1s, duty = 50%, so on for 0.05s
        // At t=0.02 (within first on phase)
        Assert.That(pwm.GetValue(0.02), Is.EqualTo(5.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void PwmVoltageSource_DuringOffPhase_ReturnsVLow() {
        var n1 = _sim.CreateNode();
        var pwm = new PwmVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            vHigh: 5.0,
            vLow: 0.0,
            frequency: 10.0,
            dutyCycle: 0.5
        );

        // Period = 0.1s, duty = 50%, so off after 0.05s
        // At t=0.07 (within off phase)
        Assert.That(pwm.GetValue(0.07), Is.EqualTo(0.0).Within(Tolerances.Voltage));
    }

    [Test]
    public void PwmVoltageSource_DutyCycle25Percent_OnForQuarterPeriod() {
        var n1 = _sim.CreateNode();
        var pwm = new PwmVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            vHigh: 5.0,
            vLow: 0.0,
            frequency: 10.0,
            dutyCycle: 0.25
        );

        // Period = 0.1s, duty = 25%, so on for 0.025s
        Assert.That(pwm.GetValue(0.02), Is.EqualTo(5.0).Within(Tolerances.Voltage)); // Still on
        Assert.That(pwm.GetValue(0.03), Is.EqualTo(0.0).Within(Tolerances.Voltage)); // Now off
    }

    [Test]
    public void PwmVoltageSource_SecondPeriod_RepeatsPattern() {
        var n1 = _sim.CreateNode();
        var pwm = new PwmVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            vHigh: 5.0,
            vLow: 0.0,
            frequency: 10.0,
            dutyCycle: 0.5
        );

        // Second period starts at t=0.1s
        Assert.That(pwm.GetValue(0.12), Is.EqualTo(5.0).Within(Tolerances.Voltage)); // On phase
        Assert.That(pwm.GetValue(0.17), Is.EqualTo(0.0).Within(Tolerances.Voltage)); // Off phase
    }

    [Test]
    public void PwmVoltageSource_Update_TogglesBetweenHighAndLow() {
        var n1 = _sim.CreateNode();
        var pwm = new PwmVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            vHigh: 5.0,
            vLow: 0.0,
            frequency: 100.0,
            dutyCycle: 0.5
        );

        // Add load
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        // Run through several cycles
        bool sawHigh = false;
        bool sawLow = false;

        for (int i = 0; i < 100; i++) {
            pwm.Update();
            _sim.Step(1e-4);

            double v = _sim.GetVoltage(n1);
            if (v > 4.0)
                sawHigh = true;
            if (v < 1.0)
                sawLow = true;
        }

        Assert.That(sawHigh, Is.True, "Should see high voltage during on phase");
        Assert.That(sawLow, Is.True, "Should see low voltage during off phase");
    }

    #endregion

    #region SourceUpdater Tests

    [Test]
    public void SourceUpdater_UpdateAll_UpdatesAllRegisteredSources() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        // Use same frequency so both are at predictable points
        var ac1 = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);
        var ac2 = new AcVoltageSource(
            _sim,
            n2,
            _sim.Ground,
            amplitude: 5.0,
            frequency: 1.0,
            phase: Math.PI / 2
        ); // 90° phase shift

        // Add loads
        _sim.AddResistor(n1, _sim.Ground, 100.0);
        _sim.AddResistor(n2, _sim.Ground, 100.0);

        var updater = new SourceUpdater();
        updater.Add(ac1);
        updater.Add(ac2);

        // Advance to t=0.25 (quarter period)
        for (int i = 0; i < 25; i++) {
            updater.UpdateAll();
            _sim.Step(0.01);
        }

        // ac1: 10*sin(2π*1*0.25) = 10*sin(π/2) ≈ 10
        // ac2: 5*sin(2π*1*0.25 + π/2) = 5*sin(π) ≈ 0 (cosine at quarter period)
        // Both should have been updated (verify values are different)
        double v1 = _sim.GetVoltage(n1);
        double v2 = _sim.GetVoltage(n2);

        Assert.That(v1, Is.EqualTo(10.0).Within(0.5));
        Assert.That(Math.Abs(v2), Is.LessThan(1.0)); // Near zero (sin(π) ≈ 0)
    }

    [Test]
    public void SourceUpdater_Remove_StopsUpdatingSource() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        var updater = new SourceUpdater();
        updater.Add(ac);

        Assert.That(updater.Count, Is.EqualTo(1));

        updater.Remove(ac);

        Assert.That(updater.Count, Is.EqualTo(0));
    }

    [Test]
    public void SourceUpdater_RemoveAll_RemovesSourcesFromSimulation() {
        var n1 = _sim.CreateNode();
        var n2 = _sim.CreateNode();

        var ac1 = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);
        var ac2 = new AcVoltageSource(_sim, n2, _sim.Ground, amplitude: 5.0, frequency: 1.0);

        var updater = new SourceUpdater();
        updater.Add(ac1);
        updater.Add(ac2);

        updater.RemoveAll();

        Assert.That(updater.Count, Is.EqualTo(0));
        Assert.That(ac1.Exists, Is.False);
        Assert.That(ac2.Exists, Is.False);
    }

    [Test]
    public void SourceUpdater_UpdateAll_SkipsRemovedSources() {
        var n1 = _sim.CreateNode();
        var ac = new AcVoltageSource(_sim, n1, _sim.Ground, amplitude: 10.0, frequency: 1.0);
        _sim.AddResistor(n1, _sim.Ground, 100.0);

        var updater = new SourceUpdater();
        updater.Add(ac);

        // Remove from simulation but not from updater
        ac.Remove();

        // Should not throw
        Assert.DoesNotThrow(() => updater.UpdateAll());
    }

    #endregion

    #region Integration Tests

    [Test]
    public void AcVoltageSource_FullWaveRectifier_ProducesPositiveVoltage() {
        // Simple half-wave rectifier: AC -> Diode -> Load
        var nAc = _sim.CreateNode();
        var nOut = _sim.CreateNode();

        var ac = new AcVoltageSource(_sim, nAc, _sim.Ground, amplitude: 10.0, frequency: 60.0);

        _sim.AddDiode(nAc, nOut);
        _sim.AddResistor(nOut, _sim.Ground, 1000.0);

        double maxNegative = 0;

        // Run for several cycles
        for (int i = 0; i < 500; i++) {
            ac.Update();
            _sim.Step(1e-5);

            double vOut = _sim.GetVoltage(nOut);
            if (vOut < maxNegative)
                maxNegative = vOut;
        }

        // Output should never go significantly negative (diode blocks)
        Assert.That(maxNegative, Is.GreaterThan(-0.1), "Rectifier output should not go negative");
    }

    [Test]
    public void PwmVoltageSource_AverageVoltage_MatchesDutyCycle() {
        var n1 = _sim.CreateNode();
        var pwm = new PwmVoltageSource(
            _sim,
            n1,
            _sim.Ground,
            vHigh: 10.0,
            vLow: 0.0,
            frequency: 1000.0,
            dutyCycle: 0.3
        );

        _sim.AddResistor(n1, _sim.Ground, 100.0);

        double sum = 0;
        int samples = 0;

        // Run for many cycles to get good average
        for (int i = 0; i < 10000; i++) {
            pwm.Update();
            _sim.Step(1e-6);
            sum += _sim.GetVoltage(n1);
            samples++;
        }

        double average = sum / samples;
        double expected = 0.3 * 10.0; // duty * VHigh

        Assert.That(average, Is.EqualTo(expected).Within(0.5));
    }

    #endregion
}
