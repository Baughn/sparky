using NUnit.Framework;
using Sparky.MNA.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests {
    [TestFixture]
    public class CurrentSourceTests {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp() {
            _sim = new SimulationManager();
        }

        [Test]
        public void CurrentSource_SetsNodeVoltage() {
            // Current source (1A) through resistor (10 ohm) to ground
            // V = I * R = 1 * 10 = 10V
            var n1 = _sim.CreateNode();

            _sim.AddCurrentSource(_sim.Ground, n1, 1.0); // 1A flows into n1
            _sim.AddResistor(n1, _sim.Ground, 10.0);

            _sim.Step(0.001);

            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(Tolerances.Voltage));
        }

        [Test]
        public void CurrentSource_Polarity_PositiveCurrentFlowsInToOut() {
            // Current flows from nodeIn to nodeOut
            // nodeIn is lower potential, nodeOut is higher when driving into a resistor
            var n1 = _sim.CreateNode();

            // Current flows from Ground into n1 (making n1 positive relative to ground)
            _sim.AddCurrentSource(_sim.Ground, n1, 1.0);
            _sim.AddResistor(n1, _sim.Ground, 10.0);

            _sim.Step(0.001);

            // n1 should be at positive voltage (current flows into it from source)
            Assert.That(_sim.GetVoltage(n1), Is.GreaterThan(0));
        }

        [Test]
        public void CurrentSource_MultipleInParallel_CurrentsAdd() {
            // Two 1A current sources in parallel = 2A total
            // Through 10 ohm resistor: V = 2 * 10 = 20V
            var n1 = _sim.CreateNode();

            _sim.AddCurrentSource(_sim.Ground, n1, 1.0);
            _sim.AddCurrentSource(_sim.Ground, n1, 1.0);
            _sim.AddResistor(n1, _sim.Ground, 10.0);

            _sim.Step(0.001);

            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(20.0).Within(Tolerances.Voltage));
        }

        [Test]
        public void CurrentSource_WithCapacitor_ChargesLinearly() {
            // I = C * dV/dt => dV = I * dt / C
            // With I = 1A, C = 1F, dt = 0.1s: dV = 1 * 0.1 / 1 = 0.1V per step
            var n1 = _sim.CreateNode();

            _sim.AddCurrentSource(_sim.Ground, n1, 1.0);
            _sim.AddCapacitor(n1, _sim.Ground, 1.0); // 1 Farad

            double dt = 0.1;
            _sim.Step(dt);
            double v1 = _sim.GetVoltage(n1);

            _sim.Step(dt);
            double v2 = _sim.GetVoltage(n1);

            // Voltage should increase by approximately I*dt/C = 0.1V per step
            double expectedIncrease = 1.0 * dt / 1.0;
            Assert.That(v2 - v1, Is.EqualTo(expectedIncrease).Within(Tolerances.Loose));
        }

        [Test]
        public void CurrentSource_ZeroCurrent_NoEffect() {
            // 0A current source should not affect circuit
            var n1 = _sim.CreateNode();

            _sim.AddCurrentSource(_sim.Ground, n1, 0.0);
            _sim.AddResistor(n1, _sim.Ground, 10.0);

            _sim.Step(0.001);

            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(0.0).Within(Tolerances.Voltage));
        }

        [Test]
        public void CurrentSource_Update_AffectsNextStep() {
            // Start with 1A, change to 2A, verify voltage doubles
            var n1 = _sim.CreateNode();

            var csId = _sim.AddCurrentSource(_sim.Ground, n1, 1.0);
            _sim.AddResistor(n1, _sim.Ground, 10.0);

            _sim.Step(0.001);
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(Tolerances.Voltage));

            _sim.UpdateCurrentSource(csId, 2.0);
            _sim.Step(0.001);
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(20.0).Within(Tolerances.Voltage));
        }
    }
}
