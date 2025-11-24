using NUnit.Framework;
using Sparky.MNA;
using System.Linq;
using System;

namespace Sparky.Tests
{
    [TestFixture]
    public class CircuitLawTests
    {
        [Test]
        public void TestKCL()
        {
            // Verify KCL at a central node.
            // Source -> R1 -> Node 1 -> R2 -> Ground
            //                        -> R3 -> Ground
            // Current in = Current out 1 + Current out 2
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double V = 10.0;
            double R1 = 100.0;
            double R2 = 200.0;
            double R3 = 300.0;

            circuit.AddComponent(new VoltageSource(nSrc, ground, V));
            circuit.AddComponent(new Resistor(nSrc, n1, R1));
            circuit.AddComponent(new Resistor(n1, ground, R2));
            circuit.AddComponent(new Resistor(n1, ground, R3));

            circuit.Solve(0);

            // Calculate currents manually based on node voltages
            double v_n1 = n1.Voltage;
            double v_src = nSrc.Voltage;

            double i_in = (v_src - v_n1) / R1;
            double i_out1 = (v_n1 - 0) / R2;
            double i_out2 = (v_n1 - 0) / R3;

            Assert.That(i_in, Is.EqualTo(i_out1 + i_out2).Within(1e-6));
        }

        [Test]
        public void TestKVL()
        {
            // Verify KVL around a loop.
            // Source -> R1 -> R2 -> R3 -> Ground
            // V_source = V_R1 + V_R2 + V_R3
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var n1 = circuit.AddNode();
            var n2 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            circuit.AddComponent(new VoltageSource(nSrc, ground, 12.0));
            circuit.AddComponent(new Resistor(nSrc, n1, 10.0));
            circuit.AddComponent(new Resistor(n1, n2, 20.0));
            circuit.AddComponent(new Resistor(n2, ground, 30.0));

            circuit.Solve(0);

            double v_r1 = nSrc.Voltage - n1.Voltage;
            double v_r2 = n1.Voltage - n2.Voltage;
            double v_r3 = n2.Voltage - 0;

            Assert.That(12.0, Is.EqualTo(v_r1 + v_r2 + v_r3).Within(1e-6));
        }

        [Test]
        public void TestPowerConservation()
        {
            // Total Power Generated = Total Power Consumed
            // Source (10V) -> R (100) -> Ground
            // Power Gen = V * I = 10 * (10/100) = 1W
            // Power Consumed = I^2 * R = (0.1)^2 * 100 = 1W
            
            // Note: We don't have a direct "GetPower()" method on components yet,
            // so we calculate it manually for this test.
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double V = 10.0;
            double R = 100.0;

            circuit.AddComponent(new VoltageSource(nSrc, ground, V));
            circuit.AddComponent(new Resistor(nSrc, ground, R));

            circuit.Solve(0);

            double i = (nSrc.Voltage - 0) / R;
            double p_consumed = i * i * R;
            double p_gen = V * i;

            Assert.That(p_gen, Is.EqualTo(p_consumed).Within(1e-6));
        }
    }
}
