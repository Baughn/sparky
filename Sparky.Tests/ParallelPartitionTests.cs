using NUnit.Framework;
using Sparky.MNA.Api;

namespace Sparky.Tests
{
    [TestFixture]
    public class ParallelPartitionTests
    {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationManager();
        }

        [Test]
        public void TwoPartitions_BothSolveCorrectly()
        {
            // Create two independent circuits (only connected through ground)
            // Verify both solve correctly and don't interfere with each other

            // Circuit 1: 10V source with voltage divider
            var n1 = _sim.CreateNode();
            var n1Mid = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, n1Mid, 100.0);
            _sim.AddResistor(n1Mid, _sim.Ground, 100.0);

            // Circuit 2: 5V source with different divider ratio
            var n2 = _sim.CreateNode();
            var n2Mid = _sim.CreateNode();
            _sim.AddVoltageSource(n2, _sim.Ground, 5.0);
            _sim.AddResistor(n2, n2Mid, 100.0);
            _sim.AddResistor(n2Mid, _sim.Ground, 300.0);

            _sim.Step(0.001);

            // Verify partition count
            Assert.That(_sim.PartitionCount, Is.EqualTo(2),
                "Two disconnected circuits should create two partitions");

            // Verify Circuit 1 solved correctly: 10V / 2 = 5V at mid point
            Assert.That(_sim.GetVoltage(n1Mid), Is.EqualTo(5.0).Within(1e-6),
                "Circuit 1 voltage divider should give 5V");

            // Verify Circuit 2 solved correctly: 5V * 300/(100+300) = 3.75V at mid point
            Assert.That(_sim.GetVoltage(n2Mid), Is.EqualTo(3.75).Within(1e-6),
                "Circuit 2 voltage divider should give 3.75V");

            // Verify source voltages are independent
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(1e-6));
        }

        [Test]
        public void ManyPartitions_AllSolveCorrectly()
        {
            // Create 10 independent partitions with different voltage sources
            // Verify all solve correctly in parallel

            const int partitionCount = 10;
            var sourceNodes = new NodeId[partitionCount];
            var midNodes = new NodeId[partitionCount];
            var expectedVoltages = new double[partitionCount];

            for (int i = 0; i < partitionCount; i++)
            {
                double sourceVoltage = (i + 1) * 5.0;  // 5V, 10V, 15V, ... 50V
                double r1 = 100.0;
                double r2 = 100.0 * (i + 1);  // Different ratio for each

                sourceNodes[i] = _sim.CreateNode();
                midNodes[i] = _sim.CreateNode();

                _sim.AddVoltageSource(sourceNodes[i], _sim.Ground, sourceVoltage);
                _sim.AddResistor(sourceNodes[i], midNodes[i], r1);
                _sim.AddResistor(midNodes[i], _sim.Ground, r2);

                // Expected: V_mid = V_src * R2 / (R1 + R2)
                expectedVoltages[i] = sourceVoltage * r2 / (r1 + r2);
            }

            _sim.Step(0.001);

            // Verify partition count
            Assert.That(_sim.PartitionCount, Is.EqualTo(partitionCount),
                $"Should have {partitionCount} separate partitions");

            // Verify all partitions solved correctly
            for (int i = 0; i < partitionCount; i++)
            {
                Assert.That(_sim.GetVoltage(midNodes[i]), Is.EqualTo(expectedVoltages[i]).Within(1e-6),
                    $"Partition {i + 1} should solve correctly");
            }
        }

        [Test]
        public void PartitionsWithDifferentComplexity_AllComplete()
        {
            // Partition A: Simple linear circuit (1 iteration)
            // Partition B: Nonlinear circuit with diode (multiple Newton-Raphson iterations)
            // Both should solve correctly despite different iteration counts

            // Partition A: Linear voltage divider
            var nLinear = _sim.CreateNode();
            var nLinearMid = _sim.CreateNode();
            _sim.AddVoltageSource(nLinear, _sim.Ground, 10.0);
            _sim.AddResistor(nLinear, nLinearMid, 100.0);
            _sim.AddResistor(nLinearMid, _sim.Ground, 100.0);

            // Partition B: Nonlinear circuit with diode
            var nDiode = _sim.CreateNode();
            var nDiodeOut = _sim.CreateNode();
            _sim.AddVoltageSource(nDiode, _sim.Ground, 5.0);
            _sim.AddResistor(nDiode, nDiodeOut, 1000.0);
            _sim.AddDiode(nDiodeOut, _sim.Ground);

            _sim.Step(0.001);

            // Verify partition count
            Assert.That(_sim.PartitionCount, Is.EqualTo(2),
                "Should have two partitions");

            // Verify linear partition solved correctly
            Assert.That(_sim.GetVoltage(nLinearMid), Is.EqualTo(5.0).Within(1e-6),
                "Linear circuit should solve correctly");

            // Verify nonlinear partition solved correctly (diode forward voltage ~0.6-0.8V)
            Assert.That(_sim.GetVoltage(nDiodeOut), Is.GreaterThan(0.5).And.LessThan(0.9),
                "Diode circuit should solve to forward voltage");

            // Verify total iterations reflects the nonlinear solve
            var stats = _sim.GetStats();
            Assert.That(stats.TotalIterations, Is.GreaterThan(2),
                "Total iterations should reflect Newton-Raphson for diode");
        }

        [Test]
        public void ConnectingPartitions_MergesIntoOne()
        {
            // Start with two separate partitions
            // Add a resistor connecting them
            // Verify partition count drops to 1 and combined solution is correct

            // Partition 1: 10V source
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);

            // Partition 2: 5V source
            var n2 = _sim.CreateNode();
            _sim.AddVoltageSource(n2, _sim.Ground, 5.0);
            _sim.AddResistor(n2, _sim.Ground, 100.0);

            _sim.Step(0.001);

            // Verify initially two partitions
            Assert.That(_sim.PartitionCount, Is.EqualTo(2),
                "Initially should have two partitions");

            double v1Before = _sim.GetVoltage(n1);
            double v2Before = _sim.GetVoltage(n2);
            Assert.That(v1Before, Is.EqualTo(10.0).Within(1e-6));
            Assert.That(v2Before, Is.EqualTo(5.0).Within(1e-6));

            // Connect the two partitions with a resistor
            _sim.AddResistor(n1, n2, 100.0);

            _sim.Step(0.001);

            // Verify now one partition
            Assert.That(_sim.PartitionCount, Is.EqualTo(1),
                "After connecting, should have one partition");

            // Both nodes should still be at their respective source voltages
            // (since they're connected to voltage sources)
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(1e-6),
                "Node 1 should stay at 10V (voltage source)");
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(1e-6),
                "Node 2 should stay at 5V (voltage source)");
        }
    }
}
