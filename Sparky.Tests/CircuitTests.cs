using NUnit.Framework;
using Sparky.MNA;
using System.Collections.Generic;

namespace Sparky.Tests
{
    [TestFixture]
    public class CircuitTests
    {
        [Test]
        public void TestVoltageDivider()
        {
            // 10V Source -> R1 (100) -> Node 1 -> R2 (100) -> Ground
            // Expected: Node 1 = 5V

            var circuit = new Circuit();
            var n1 = circuit.AddNode(); // Node 1

            // Ground is Node 0
            var ground = circuit.Nodes[0];

            // Voltage Source: Ground -> Node 1? No, usually Source -> R -> Ground
            // Let's do: Ground -> Source -> Node 1 -> R1 -> Node 2 -> R2 -> Ground

            // Simple divider:
            // V1 connected between Ground and Node 1 (10V)
            // R1 connected between Node 1 and Node 2 (100 Ohm)
            // R2 connected between Node 2 and Ground (100 Ohm)
            // Expected: Node 1 = 10V, Node 2 = 5V

            var n2 = circuit.AddNode();

            var v1 = new VoltageSource(ground, n1, 10.0); // V_n1 - V_gnd = 10 -> V_n1 = 10
                                                          // Wait, VoltageSource(n1, n2, V) means V_n1 - V_n2 = V.
                                                          // So VoltageSource(n1, ground, 10) means V_n1 - 0 = 10 -> V_n1 = 10.

            circuit.AddComponent(new VoltageSource(n1, ground, 10.0));
            circuit.AddComponent(new Resistor(n1, n2, 100.0));
            circuit.AddComponent(new Resistor(n2, ground, 100.0));

            circuit.Solve(0);

            NUnit.Framework.Assert.That(n1.Voltage, Is.EqualTo(10.0).Within(1e-6));
            NUnit.Framework.Assert.That(n2.Voltage, Is.EqualTo(5.0).Within(1e-6));
        }

        [Test]
        public void DenseSolveHandlesResistorLadder()
        {
            // Ladder of 10x 100 Ohm resistors from 12V source to ground.
            // Current = 12 / (10 * 100) = 0.012A.
            // Node after i resistors should be V = 12 - I * R * i.

            var circuit = new Circuit();
            var ground = circuit.Ground;
            var nSrc = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSrc, ground, 12.0));

            const int segments = 10;
            const double r = 100.0;
            double current = 12.0 / (segments * r);

            var ladderNodes = new List<Node> { nSrc };
            Node prev = nSrc;
            for (int i = 0; i < segments - 1; i++)
            {
                var next = circuit.AddNode();
                circuit.AddComponent(new Resistor(prev, next, r));
                prev = next;
                ladderNodes.Add(next);
            }
            circuit.AddComponent(new Resistor(prev, ground, r));

            circuit.Solve(0);

            double expected = 12.0;
            for (int i = 1; i < ladderNodes.Count; i++)
            {
                var node = ladderNodes[i];
                expected -= current * r;
                Assert.That(node.Voltage, Is.EqualTo(expected).Within(1e-6));
            }
        }

        [Test]
        public void SparseSolveHandlesLargeResistorLadder()
        {
            // Large ladder to exercise the sparse path (>96 unknowns).
            // 12V source feeding 150 segments of 2 Ohms each.
            // Current = 12 / (150 * 2) = 0.04A.

            var circuit = new Circuit();
            var ground = circuit.Ground;
            var nSrc = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSrc, ground, 12.0));

            const int segments = 150;
            const double r = 2.0;
            double current = 12.0 / (segments * r);

            Node prev = nSrc;
            for (int i = 0; i < segments - 1; i++)
            {
                var next = circuit.AddNode();
                circuit.AddComponent(new Resistor(prev, next, r));
                prev = next;
            }
            circuit.AddComponent(new Resistor(prev, ground, r));

            circuit.Solve(0);

            // Check a few positions across the ladder.
            double[] checkpoints = { 1, segments / 2.0, segments - 1 };
            foreach (double step in checkpoints)
            {
                int nodeIndex = (int)step + 1; // ground=0, source=1
                double expected = 12.0 - current * r * step;
                Assert.That(circuit.Nodes[nodeIndex].Voltage, Is.EqualTo(expected).Within(1e-6));
            }
        }
    }
}
