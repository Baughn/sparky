using NUnit.Framework;
using Sparky.MNA.Api;
using System;

namespace Sparky.Tests.MNA
{
    [TestFixture]
    public class ApiTests
    {
        private SimulationManager _sim;

        public ApiTests() {
          _sim = new SimulationManager();
        }

        [Test]
        public void TestSimpleResistorDivider()
        {
            // 10V -- R1(10) -- N1 -- R2(10) -- GND
            var n1 = _sim.CreateNode();
            var nPos = _sim.CreateNode();
            var nGnd = new NodeId(0); // Assuming 0 is ground, or we need a way to get it. 
                                      // SimulationManager doesn't expose Ground NodeId directly but we assumed 0.
                                      // Let's use 0.

            _sim.AddVoltageSource(nPos, nGnd, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, nGnd, 10.0);

            _sim.Step(0.1);

            Assert.That(_sim.GetVoltage(nPos), Is.EqualTo(10.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(5.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(nGnd), Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void TestLineOptimization()
        {
            // 10V -- R1(10) -- N1 -- R2(10) -- N2 -- R3(10) -- GND
            // Total 30 Ohm. Current = 1/3 A.
            // V(N1) = 20/3 = 6.666...
            // V(N2) = 10/3 = 3.333...

            _sim.EnableLineOptimization = true;

            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();
            var nGnd = new NodeId(0);

            _sim.AddVoltageSource(nPos, nGnd, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            _sim.AddResistor(n1, n2, 10.0);
            _sim.AddResistor(n2, nGnd, 10.0);

            _sim.Step(0.1);

            Assert.That(_sim.GetVoltage(nPos), Is.EqualTo(10.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(20.0 / 3.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0 / 3.0).Within(1e-6));
        }

        [Test]
        public void TestPartitioning()
        {
            // Circuit 1: 10V -- R1(10) -- GND
            // Circuit 2: 5V -- R2(5) -- GND

            var c1_Pos = _sim.CreateNode();
            var c1_Gnd = new NodeId(0);
            _sim.AddVoltageSource(c1_Pos, c1_Gnd, 10.0);
            _sim.AddResistor(c1_Pos, c1_Gnd, 10.0);

            var c2_Pos = _sim.CreateNode();
            var c2_Gnd = new NodeId(0);
            _sim.AddVoltageSource(c2_Pos, c2_Gnd, 5.0);
            _sim.AddResistor(c2_Pos, c2_Gnd, 5.0);

            _sim.Step(0.1);

            Assert.That(_sim.GetVoltage(c1_Pos), Is.EqualTo(10.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(c2_Pos), Is.EqualTo(5.0).Within(1e-6));
        }

        [Test]
        public void TestIncrementalUpdate_ValueChange()
        {
            // 10V -- R1(10) -- GND
            var nPos = _sim.CreateNode();
            var nGnd = new NodeId(0);

            _sim.AddVoltageSource(nPos, nGnd, 10.0);
            var rId = _sim.AddResistor(nPos, nGnd, 10.0);

            _sim.Step(0.1);
            Assert.That(_sim.GetVoltage(nPos), Is.EqualTo(10.0).Within(1e-6));

            // Change R to 5 (doesn't change voltage of ideal source, but let's add a divider)
            // 10V -- R1 -- N1 -- R2 -- GND
            _sim.Clear();
            nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            nGnd = new NodeId(0);

            _sim.AddVoltageSource(nPos, nGnd, 10.0);
            var r1 = _sim.AddResistor(nPos, n1, 10.0);
            var r2 = _sim.AddResistor(n1, nGnd, 10.0);

            _sim.Step(0.1);
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(5.0).Within(1e-6));

            // Change R1 to 30. Total 40. V(N1) = 10 * 10/40 = 2.5
            _sim.UpdateResistor(r1, 30.0);
            _sim.Step(0.1);
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(2.5).Within(1e-6));
        }

        [Test]
        public void TestIncrementalUpdate_TopologyChange()
        {
            // 10V -- R1(10) -- N1 -- GND
            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();
            var nGnd = new NodeId(0);

            _sim.AddVoltageSource(nPos, nGnd, 10.0);
            _sim.AddResistor(nPos, n1, 10.0);
            var r2 = _sim.AddResistor(n1, nGnd, 10.0);

            _sim.Step(0.1);
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(5.0).Within(1e-6));

            // Remove R2. N1 is now floating (connected to 10V via R1).
            // MNA might have issues with floating nodes if no path to ground?
            // Actually, N1 is connected to 10V source via R1.
            // So N1 should be 10V (no current flows).

            _sim.RemoveResistor(r2);
            _sim.Step(0.1);
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(1e-6));
        }
    }
}
