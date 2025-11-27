using NUnit.Framework;
using Sparky.MNA;
using System.Collections.Generic;

namespace Sparky.Tests
{
    public class TransientTests
    {
        [Test]
        public void TestRCCircuit()
        {
            // RC Circuit:
            // 10V Source -> Resistor (1k) -> Node 1 -> Capacitor (1uF) -> Ground
            // Time constant tau = R * C = 1000 * 1e-6 = 1ms = 0.001s
            // V_c(t) = V_source * (1 - e^(-t/tau))

            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double R = 1000.0;
            double C = 1e-6;
            double V = 10.0;

            var nSource = circuit.AddNode();
            circuit.AddComponent(new VoltageSource(nSource, ground, V));
            circuit.AddComponent(new Resistor(nSource, n1, R));
            circuit.AddComponent(new Capacitor(n1, ground, C));

            // Simulation
            double dt = 0.0001; // 0.1ms (1/10th of tau)
            double time = 0;

            // First step: t=0. Capacitor is uncharged (0V).
            // Actually, we need to initialize. 
            // If we start with 0V across C, then at t=0+, V_c should start rising.

            // Run for 5 tau (5ms)
            for (int i = 0; i < 50; i++)
            {
                circuit.Solve(dt);
                time += dt;

                double expected = V * (1.0 - System.Math.Exp(-time / (R * C)));
                Assert.That(n1.Voltage, Is.EqualTo(expected).Within(0.5)); // Loose tolerance for Backward Euler
            }
        }
    }
}
