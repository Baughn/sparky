using NUnit.Framework;
using Sparky.Mna.Api;

namespace Sparky.Tests {
    [TestFixture]
    public class DiagnosticsTests {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp() {
            _sim = new SimulationManager();
        }

        [Test]
        public void GetStats_ReturnsCorrectPartitionCount() {
            // Create two disconnected circuits
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);

            var n2 = _sim.CreateNode();
            _sim.AddVoltageSource(n2, _sim.Ground, 5.0);
            _sim.AddResistor(n2, _sim.Ground, 100.0);

            _sim.Step(0.001);

            var stats = _sim.GetStats();
            Assert.That(stats.PartitionCount, Is.EqualTo(_sim.PartitionCount));
            Assert.That(stats.PartitionCount, Is.EqualTo(2));
        }

        [Test]
        public void GetStats_ReturnsCorrectPhysicalNodeCount() {
            // Simple divider: 2 physical nodes (plus ground)
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, n2, 100.0);
            _sim.AddResistor(n2, _sim.Ground, 100.0);

            _sim.EnableLineOptimization = false;
            _sim.Step(0.001);

            var stats = _sim.GetStats();
            // n1, n2, and ground = 3 physical nodes, but ground may not be counted
            // Looking at SimulationManager.GetStats: returns _physicalNodes.Count
            Assert.That(stats.PhysicalNodeCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void GetStats_ReturnsCorrectOptimizedNodeCount() {
            // Chain of resistors: middle nodes get optimized
            // 10V -- R -- n1 -- R -- n2 -- R -- GND
            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);

            var stats = _sim.GetStats();
            // n1 and n2 are in a resistor chain and should be optimized (interpolated)
            Assert.That(stats.OptimizedNodeCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void GetStats_TotalIterations_SumsPartitions() {
            // Simple linear circuit should have 1 iteration per partition
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);

            _sim.Step(0.001);

            var stats = _sim.GetStats();
            Assert.That(stats.TotalIterations, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void PartitionCount_TwoDisconnectedCircuits_ReturnsTwo() {
            // Circuit 1
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);

            // Circuit 2
            var n2 = _sim.CreateNode();
            _sim.AddVoltageSource(n2, _sim.Ground, 5.0);
            _sim.AddResistor(n2, _sim.Ground, 50.0);

            _sim.Step(0.001);

            Assert.That(_sim.PartitionCount, Is.EqualTo(2));
        }

        [Test]
        public void PartitionCount_AfterClear_ReturnsZero() {
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);

            _sim.Step(0.001);
            Assert.That(_sim.PartitionCount, Is.GreaterThan(0));

            _sim.Clear();
            Assert.That(_sim.PartitionCount, Is.EqualTo(0));
        }

        [Test]
        public void LastIterations_LinearCircuit_ReturnsOne() {
            // Linear circuit (no diodes): exactly 1 Newton iteration
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);

            _sim.Step(0.001);

            // For linear circuits, GetStats().TotalIterations should be 1 per partition
            var stats = _sim.GetStats();
            Assert.That(stats.TotalIterations, Is.EqualTo(1));
        }

        [Test]
        public void LastIterations_WithDiode_ReturnsMultiple() {
            // Nonlinear circuit with diode requires Newton iterations
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 5.0);
            _sim.AddResistor(n1, _sim.Ground, 1000.0);
            _sim.AddDiode(n1, _sim.Ground);

            _sim.Step(0.001);

            // Diode circuit requires Newton-Raphson, should have > 1 iteration
            var stats = _sim.GetStats();
            Assert.That(stats.TotalIterations, Is.GreaterThan(1));
        }
    }
}
