using System;
using NUnit.Framework;
using Sparky.MNA.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests {
    [TestFixture]
    public class AdvancedScenarioTests {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp() {
            _sim = new SimulationManager();
        }

        [Test]
        public void FullWaveBridgeRectifier_ProducesDC() {
            // Full-wave bridge rectifier with 4 diodes
            // AC input simulated by stepping voltage source between +10V and -10V
            //
            //        +---(D1)---+---(D2)---+
            //        |          |          |
            //  Vin --+          + Vout     +-- Vin-
            //        |          |          |
            //        +---(D3)---+---(D4)---+
            //
            // When Vin > 0: current flows D1 -> Load -> D4
            // When Vin < 0: current flows D2 -> Load -> D3

            var nInPos = _sim.CreateNode();
            var nInNeg = _sim.CreateNode();
            var nOutPos = _sim.CreateNode();
            var nOutNeg = _sim.CreateNode();

            // Voltage source (will be updated to simulate AC)
            var srcId = _sim.AddVoltageSource(nInPos, nInNeg, 10.0);

            // Bridge diodes:
            // D1: nInPos (anode) -> nOutPos (cathode) - conducts when Vin > Vout
            // D2: nInNeg (anode) -> nOutPos (cathode) - conducts when Vin < 0
            // D3: nOutNeg (anode) -> nInPos (cathode) - conducts when Vin < 0
            // D4: nOutNeg (anode) -> nInNeg (cathode) - conducts when Vin > 0
            _sim.AddDiode(nInPos, nOutPos); // D1
            _sim.AddDiode(nInNeg, nOutPos); // D2
            _sim.AddDiode(nOutNeg, nInPos); // D3
            _sim.AddDiode(nOutNeg, nInNeg); // D4

            // Load resistor across output
            _sim.AddResistor(nOutPos, nOutNeg, 100.0);

            // Reference for output negative rail
            _sim.AddResistor(nOutNeg, _sim.Ground, 1e-6);

            double dt = 1e-4;
            int positiveOutputCount = 0;
            int totalSteps = 0;

            // Simulate several "cycles" by alternating the source voltage
            for (int cycle = 0; cycle < 4; cycle++) {
                // Positive half-cycle
                _sim.UpdateVoltageSource(srcId, 10.0);
                for (int i = 0; i < 10; i++) {
                    _sim.Step(dt);
                    double vOut = _sim.GetVoltage(nOutPos) - _sim.GetVoltage(nOutNeg);
                    if (vOut > 0)
                        positiveOutputCount++;
                    totalSteps++;
                }

                // Negative half-cycle
                _sim.UpdateVoltageSource(srcId, -10.0);
                for (int i = 0; i < 10; i++) {
                    _sim.Step(dt);
                    double vOut = _sim.GetVoltage(nOutPos) - _sim.GetVoltage(nOutNeg);
                    if (vOut > 0)
                        positiveOutputCount++;
                    totalSteps++;
                }
            }

            // Output should be positive (or near zero) for all steps
            Assert.That(
                positiveOutputCount,
                Is.GreaterThanOrEqualTo(totalSteps * 0.9),
                "Bridge rectifier output should be positive for most steps"
            );

            // Final output voltage should be approximately Vin - 2*Vd (two diode drops)
            // With 10V input and ~0.7V diode drops, expect ~8.6V output
            _sim.UpdateVoltageSource(srcId, 10.0);
            _sim.Step(dt);
            double finalVout = _sim.GetVoltage(nOutPos) - _sim.GetVoltage(nOutNeg);
            Assert.That(
                finalVout,
                Is.GreaterThan(7.0).And.LessThan(10.0),
                "Output should be input minus two diode drops"
            );
        }

        [Test]
        public void LCOscillator_Oscillates() {
            // LC tank circuit: energy oscillates between inductor and capacitor
            // f = 1 / (2 * pi * sqrt(L * C))
            // With L = 1mH, C = 1uF: period = 2*pi*sqrt(1e-3 * 1e-6) = ~6.28ms
            //
            // Use Core layer directly to control initial conditions properly.
            // The capacitor state is maintained internally and we can pre-charge it
            // by running a few steps with a current source.

            double L = 1e-3; // 1mH
            double C = 1e-6; // 1uF
            double theoreticalPeriod = 2.0 * Math.PI * Math.Sqrt(L * C);

            var circuit = new Sparky.MNA.Core.Circuit();
            var nCap = circuit.AddNode();
            var ground = circuit.Ground;

            var capacitor = new Sparky.MNA.Core.Capacitor(nCap, ground, C);
            var inductor = new Sparky.MNA.Core.Inductor(nCap, ground, L);

            circuit.AddComponent(capacitor);
            circuit.AddComponent(inductor);

            // Pre-charge capacitor using a current source for a brief time
            // Q = C*V, so to get 10V we need Q = 1e-6 * 10 = 10uC
            // Using I = 1A for dt = 10us gives Q = 10uC
            var chargeSource = new Sparky.MNA.Core.CurrentSource(ground, nCap, 1.0);
            circuit.AddComponent(chargeSource);

            // Charge for 10us
            circuit.Solve(1e-5);
            double initialV = nCap.Voltage;
            Assert.That(initialV, Is.GreaterThan(5.0), "Capacitor should be charged");

            // Remove current source to let circuit oscillate freely
            circuit.RemoveComponent(chargeSource);

            // Simulate for multiple periods
            double dt = 1e-6; // 1us timestep for better accuracy
            int steps = (int)(5 * theoreticalPeriod / dt); // 5 periods

            double maxVoltage = double.MinValue;
            double minVoltage = double.MaxValue;
            int zeroCrossings = 0;
            double prevVoltage = nCap.Voltage;

            for (int i = 0; i < steps; i++) {
                circuit.Solve(dt);
                double v = nCap.Voltage;

                maxVoltage = Math.Max(maxVoltage, v);
                minVoltage = Math.Min(minVoltage, v);

                // Count zero crossings (sign changes)
                if ((prevVoltage > 0 && v <= 0) || (prevVoltage < 0 && v >= 0)) {
                    zeroCrossings++;
                }
                prevVoltage = v;
            }

            // Should oscillate around zero
            Assert.That(maxVoltage, Is.GreaterThan(5.0), "Voltage should swing positive");
            Assert.That(minVoltage, Is.LessThan(-5.0), "Voltage should swing negative");

            // Should have multiple zero crossings (2 per period, 5 periods = ~10 crossings)
            Assert.That(
                zeroCrossings,
                Is.GreaterThanOrEqualTo(8),
                "Should have multiple oscillation cycles"
            );
        }

        [Test]
        public void RLCResonance_PeaksAtNaturalFrequency() {
            // Series RLC circuit with step input
            // Natural frequency: w0 = 1 / sqrt(L * C)
            // Damping determines oscillation decay

            double R = 10.0; // 10 Ohms (moderate damping)
            double L = 1e-3; // 1mH
            double C = 1e-6; // 1uF
            double V = 10.0;

            // Damping ratio: zeta = R / (2 * sqrt(L/C))
            double zeta = R / (2.0 * Math.Sqrt(L / C));
            // With these values: zeta = 10 / (2 * sqrt(1000)) = 10 / 63.2 = 0.158 (underdamped)

            var nSrc = _sim.CreateNode();
            var nMid = _sim.CreateNode(); // Between R and L
            var nCap = _sim.CreateNode(); // Between L and C

            _sim.AddVoltageSource(nSrc, _sim.Ground, V);
            _sim.AddResistor(nSrc, nMid, R);
            _sim.AddInductor(nMid, nCap, L);
            _sim.AddCapacitor(nCap, _sim.Ground, C);

            double dt = 1e-5;
            double theoreticalPeriod = 2.0 * Math.PI * Math.Sqrt(L * C);
            int steps = (int)(10 * theoreticalPeriod / dt);

            double maxVoltage = double.MinValue;
            double prevMax = 0;
            double currentMax = 0;
            int peakCount = 0;
            double prevVoltage = 0;
            bool rising = true;

            for (int i = 0; i < steps; i++) {
                _sim.Step(dt);
                double v = _sim.GetVoltage(nCap);

                // Detect peaks
                bool nowRising = v > prevVoltage;
                if (rising && !nowRising && v > V * 0.1) // Peak detected
                {
                    peakCount++;
                    prevMax = currentMax;
                    currentMax = v;
                    maxVoltage = Math.Max(maxVoltage, v);
                }
                rising = nowRising;
                prevVoltage = v;
            }

            // Underdamped RLC should overshoot the DC value
            Assert.That(maxVoltage, Is.GreaterThan(V), "Underdamped RLC should overshoot");

            // Should have multiple oscillation peaks
            Assert.That(
                peakCount,
                Is.GreaterThanOrEqualTo(3),
                "Should observe damped oscillation peaks"
            );

            // Final value should approach source voltage (capacitor fully charged)
            Assert.That(
                _sim.GetVoltage(nCap),
                Is.EqualTo(V).Within(V * 0.1),
                "Capacitor should approach source voltage"
            );
        }

        [Test]
        public void CascadedTransformers_MultiplyRatios() {
            // Two transformers in series multiply their ratios
            // 10V -> T1 (2:1) -> T2 (3:1) -> Load
            // Expected output: 10V * 2 * 3 = 60V

            double V = 10.0;
            double ratio1 = 2.0;
            double ratio2 = 3.0;
            double loadR = 100.0;

            var nSrc = _sim.CreateNode();
            var nT1Sec = _sim.CreateNode();
            var nT2Sec = _sim.CreateNode();

            _sim.AddVoltageSource(nSrc, _sim.Ground, V);
            _sim.AddTransformer(nSrc, _sim.Ground, nT1Sec, _sim.Ground, ratio1);
            _sim.AddTransformer(nT1Sec, _sim.Ground, nT2Sec, _sim.Ground, ratio2);
            _sim.AddResistor(nT2Sec, _sim.Ground, loadR);

            _sim.Step(0); // DC solve

            double expectedOutput = V * ratio1 * ratio2;
            Assert.That(
                _sim.GetVoltage(nT2Sec),
                Is.EqualTo(expectedOutput).Within(Tolerances.Voltage),
                "Cascaded transformers should multiply ratios"
            );

            // Intermediate voltage should be V * ratio1
            Assert.That(
                _sim.GetVoltage(nT1Sec),
                Is.EqualTo(V * ratio1).Within(Tolerances.Voltage),
                "First transformer output should be 20V"
            );
        }

        [Test]
        public void VoltageRegulator_ClampsOutput() {
            // Simple diode voltage clamp
            // Vin -> R -> Vout
            //              |
            //              D -> Ground
            // When Vin > ~0.7V, diode conducts and clamps Vout to ~0.7V
            // When Vin < ~0.7V, diode is off and Vout follows Vin

            var nIn = _sim.CreateNode();
            var nOut = _sim.CreateNode();

            var srcId = _sim.AddVoltageSource(nIn, _sim.Ground, 0.0);
            _sim.AddResistor(nIn, nOut, 100.0);
            _sim.AddDiode(nOut, _sim.Ground);

            // Test with low voltage (below diode threshold)
            _sim.UpdateVoltageSource(srcId, 0.3);
            _sim.Step(0);
            double vOutLow = _sim.GetVoltage(nOut);
            Assert.That(
                vOutLow,
                Is.EqualTo(0.3).Within(0.1),
                "Below threshold, output should follow input"
            );

            // Test with high voltage (above diode threshold)
            _sim.UpdateVoltageSource(srcId, 5.0);
            _sim.Step(0);
            double vOutHigh = _sim.GetVoltage(nOut);
            Assert.That(
                vOutHigh,
                Is.GreaterThan(0.5).And.LessThan(0.9),
                "Above threshold, output should clamp to diode forward voltage"
            );

            // Test with even higher voltage
            _sim.UpdateVoltageSource(srcId, 10.0);
            _sim.Step(0);
            double vOutVeryHigh = _sim.GetVoltage(nOut);
            Assert.That(
                vOutVeryHigh,
                Is.GreaterThan(0.5).And.LessThan(0.9),
                "Diode clamp voltage should stay approximately constant"
            );
        }

        [Test]
        public void MotorWithBackEMF_CurrentLimits() {
            // Motor modeled as R + L with back-EMF opposing current
            // Supply -> R_series -> L_motor -> BackEMF -> Ground
            //
            // Steady state: I = (Vsupply - Vemf) / R
            // Initial transient: current rises exponentially with time constant L/R

            double Vsupply = 12.0;
            double Vemf = 8.0; // Back-EMF (simulating motor at speed)
            double R = 2.0; // Series resistance
            double L = 1e-3; // Motor inductance (1mH)

            double expectedSteadyCurrent = (Vsupply - Vemf) / R; // 2A

            var nSupply = _sim.CreateNode();
            var nMotorIn = _sim.CreateNode();
            var nMotorOut = _sim.CreateNode();

            _sim.AddVoltageSource(nSupply, _sim.Ground, Vsupply);
            _sim.AddResistor(nSupply, nMotorIn, R);
            _sim.AddInductor(nMotorIn, nMotorOut, L);
            _sim.AddVoltageSource(nMotorOut, _sim.Ground, Vemf); // Back-EMF

            // Time constant tau = L/R = 0.5ms
            double tau = L / R;
            double dt = tau / 10.0; // 10 steps per time constant

            // Initial current should be low
            _sim.Step(dt);
            double vAcrossR = _sim.GetVoltage(nSupply) - _sim.GetVoltage(nMotorIn);
            double initialCurrent = vAcrossR / R;

            // Run for 5 time constants to reach steady state
            for (int i = 0; i < 50; i++) {
                _sim.Step(dt);
            }

            vAcrossR = _sim.GetVoltage(nSupply) - _sim.GetVoltage(nMotorIn);
            double steadyCurrent = vAcrossR / R;

            Assert.That(
                steadyCurrent,
                Is.EqualTo(expectedSteadyCurrent).Within(0.05),
                "Steady state current should be (Vsupply - Vemf) / R"
            );

            // Verify initial current was lower (transient)
            Assert.That(
                initialCurrent,
                Is.LessThan(steadyCurrent * 0.9),
                "Initial current should be lower due to inductor"
            );
        }
    }
}
