using NUnit.Framework;
using Sparky.MNA.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA
{
    [TestFixture]
    public class LineOptimizationTests
    {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationManager();
        }

        [Test]
        public void Optimization_ThreeResistorChain_CorrectInterpolation()
        {
            // 10V -- R1(10) -- N1 -- R2(10) -- N2 -- R3(10) -- GND
            // Total 30 Ohm. Current = 1/3 A.
            // V(N1) = 20/3 = 6.666...
            // V(N2) = 10/3 = 3.333...

            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.1);

            Assert.That(_sim.GetVoltage(nPos), Is.EqualTo(10.0).Within(Tolerances.Voltage));
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(20.0 / 3.0).Within(Tolerances.Voltage));
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0 / 3.0).Within(Tolerances.Voltage));
        }

        [Test]
        public void Optimization_ChainBrokenByCapacitor_PartialMerge()
        {
            // 10V -- R1 -- n1 -- C -- n2 -- R2 -- GND
            // Capacitor breaks the resistor chain
            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddCapacitor(n1, n2, 1e-6);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);

            // n1 and n2 should NOT be optimized (capacitor breaks chain)
            Assert.That(_sim.IsNodeOptimized(n1), Is.False);
            Assert.That(_sim.IsNodeOptimized(n2), Is.False);
        }

        [Test]
        public void Optimization_ChainBrokenByVoltageSource_PartialMerge()
        {
            // 10V -- R1 -- n1 -- V(5V) -- n2 -- R2 -- GND
            // Voltage source breaks the resistor chain
            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddVoltageSource(n1, n2, 5.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);

            // n1 and n2 should NOT be optimized (voltage source breaks chain)
            Assert.That(_sim.IsNodeOptimized(n1), Is.False);
            Assert.That(_sim.IsNodeOptimized(n2), Is.False);
        }

        [Test]
        public void Optimization_SingleResistor_NoMerge()
        {
            // 10V -- R -- GND (single resistor, nothing to optimize)
            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, _sim.Ground, 10.0);

            _sim.Step(0.001);

            // Single resistor: nPos should NOT be optimized
            Assert.That(_sim.IsNodeOptimized(nPos), Is.False);
        }

        [Test]
        public void Optimization_BranchingNetwork_OnlyMergesLines()
        {
            // T-junction: node n1 has 3 connections, cannot be optimized
            //     R
            //     |
            // R --n1-- R -- GND
            //     |
            //    10V
            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();
            var n3 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n1, n3, 10.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);
            _sim.AddResistor(n3, _sim.Ground, 10.0);

            _sim.Step(0.001);

            // n1 is a junction (3 resistor connections), should NOT be optimized
            Assert.That(_sim.IsNodeOptimized(n1), Is.False);
        }

        [Test]
        public void Optimization_Disabled_NoInterpolation()
        {
            // Same circuit as ThreeResistorChain but with optimization disabled
            _sim.EnableLineOptimization = false;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);

            // No nodes should be optimized
            Assert.That(_sim.IsNodeOptimized(n1), Is.False);
            Assert.That(_sim.IsNodeOptimized(n2), Is.False);
            Assert.That(_sim.GetStats().OptimizedNodeCount, Is.EqualTo(0));
        }

        [Test]
        public void Optimization_EnabledMidSimulation_Rebuilds()
        {
            // Start disabled, step, then enable and trigger rebuild via topology change
            _sim.EnableLineOptimization = false;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            var r3 = _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);
            Assert.That(_sim.GetStats().OptimizedNodeCount, Is.EqualTo(0));

            // Enable optimization and trigger rebuild by modifying topology
            _sim.EnableLineOptimization = true;
            // Remove and re-add a resistor to trigger rebuild
            _sim.RemoveResistor(r3);
            _sim.AddResistor(n2, _sim.Ground, 10.0);
            _sim.Step(0.001);

            // Now nodes should be optimized
            Assert.That(_sim.GetStats().OptimizedNodeCount, Is.GreaterThan(0));
        }

        [Test]
        public void Optimization_InterpolatedVoltage_MatchesExpected()
        {
            // Compare results with optimization enabled vs disabled
            // They should produce the same voltage values

            // Build circuit with optimization disabled
            _sim.EnableLineOptimization = false;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);

            double v1_noOpt = _sim.GetVoltage(n1);
            double v2_noOpt = _sim.GetVoltage(n2);

            // Rebuild with optimization enabled
            _sim.Clear();
            _sim.EnableLineOptimization = true;

            nPos = _sim.CreateNode();
            n1 = _sim.CreateNode();
            n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);

            double v1_opt = _sim.GetVoltage(n1);
            double v2_opt = _sim.GetVoltage(n2);

            // Results should match within tolerance
            Assert.That(v1_opt, Is.EqualTo(v1_noOpt).Within(Tolerances.Voltage));
            Assert.That(v2_opt, Is.EqualTo(v2_noOpt).Within(Tolerances.Voltage));
        }

        [Test]
        public void Optimization_IsNodeOptimized_ReturnsTrue()
        {
            // Verify IsNodeOptimized returns true for middle nodes in a chain
            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n2, _sim.Ground, 10.0);

            _sim.Step(0.001);

            // n1 and n2 are middle nodes in resistor chain, should be optimized
            Assert.That(_sim.IsNodeOptimized(n1), Is.True);
            Assert.That(_sim.IsNodeOptimized(n2), Is.True);

            // Endpoint nodes should NOT be optimized
            Assert.That(_sim.IsNodeOptimized(nPos), Is.False);
            Assert.That(_sim.IsNodeOptimized(_sim.Ground), Is.False);
        }
    }
}
