using NUnit.Framework;
using Sparky.MNA;

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
    }
}
