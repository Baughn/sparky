using NUnit.Framework;
using Sparky.MNA;
using System.Collections.Generic;

namespace Sparky.Tests
{
    [TestFixture]
    public class ComponentTests
    {
        [Test]
        public void TestResistorsInSeries()
        {
            // 10V -> R1 (100) -> R2 (100) -> Ground
            // Total R = 200. I = 10/200 = 0.05A.
            // V_mid = 10 - 0.05*100 = 5V.
            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var n2 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            circuit.AddComponent(new VoltageSource(n1, ground, 10.0));
            circuit.AddComponent(new Resistor(n1, n2, 100.0));
            circuit.AddComponent(new Resistor(n2, ground, 100.0));

            circuit.Solve(0);

            Assert.That(n2.Voltage, Is.EqualTo(5.0).Within(1e-6));
        }

        [Test]
        public void TestResistorsInParallel()
        {
            // 10V -> Node 1 -> R1 (100) -> Ground
            //               -> R2 (100) -> Ground
            // Req = 50. I_total = 10/50 = 0.2A.
            // But we check voltages. Node 1 should be 10V (connected to source).
            // Let's put a series resistor to make it interesting.
            // 10V -> R_series (100) -> Node 1 -> R1 (100) || R2 (100) -> Ground
            // Req_parallel = 50. Total R = 150.
            // V_node1 = 10 * (50 / 150) = 3.333V.

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(new Resistor(nSrc, n1, 100.0));
            circuit.AddComponent(new Resistor(n1, ground, 100.0));
            circuit.AddComponent(new Resistor(n1, ground, 100.0));

            circuit.Solve(0);

            Assert.That(n1.Voltage, Is.EqualTo(10.0 * 50.0 / 150.0).Within(1e-6));
        }

        [Test]
        public void TestCapacitorDCBlocking()
        {
            // DC Source -> Resistor -> Capacitor -> Ground
            // Steady state: Capacitor is open circuit. No current flows.
            // V_cap = V_source.
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(new Resistor(nSrc, n1, 1000.0));
            circuit.AddComponent(new Capacitor(n1, ground, 1e-6));

            // Run for enough time to charge
            double dt = 0.01;
            for(int i=0; i<100; i++) circuit.Solve(dt);

            Assert.That(n1.Voltage, Is.EqualTo(10.0).Within(1e-3));
        }

        [Test]
        public void TestInductorDCShort()
        {
            // DC Source -> Resistor -> Inductor -> Ground
            // Steady state: Inductor is short circuit.
            // V_node_above_inductor = 0 (connected to ground via short).
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(new Resistor(nSrc, n1, 1000.0));
            circuit.AddComponent(new Inductor(n1, ground, 1e-3));

            // Run for enough time to settle
            double dt = 0.01;
            for(int i=0; i<100; i++) circuit.Solve(dt);

            Assert.That(n1.Voltage, Is.EqualTo(0.0).Within(1e-3));
        }
    }
}
