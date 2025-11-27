using NUnit.Framework;
using Sparky.MNA;

namespace Sparky.Tests
{
    public class DiodeTests
    {
        [Test]
        public void TestDiodeForwardBias()
        {
            // 10V Source -> Resistor (1k) -> Node 1 -> Diode -> Ground
            // Expected: Node 1 approx 0.6V - 0.8V (Diode drop)
            
            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            var nSrc = circuit.AddNode();
            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(new Resistor(nSrc, n1, 1000.0));
            circuit.AddComponent(new Diode(n1, ground));

            circuit.Solve(0); // DC solve

            // Diode drop should be around 0.6-0.8V
            NUnit.Framework.Assert.That(n1.Voltage, Is.GreaterThan(0.5).And.LessThan(0.9));
            Assert.That(circuit.LastIterations, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void TestDiodeReverseBias()
        {
            // -10V Source -> Resistor (1k) -> Node 1 -> Diode -> Ground
            // Expected: Node 1 approx -10V (Diode is open circuit)
            
            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];
            var nSrc = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSrc, ground, -10.0));
            circuit.AddComponent(new Resistor(nSrc, n1, 1000.0));
            circuit.AddComponent(new Diode(n1, ground));

            circuit.Solve(0);

            // Diode is open, so no current flows. V_n1 should be equal to V_src (-10V)
            // Actually, leakage current is very small (Is = 1e-12).
            // V_n1 should be very close to -10V.
            NUnit.Framework.Assert.That(n1.Voltage, Is.EqualTo(-10.0).Within(1e-3));
            Assert.That(circuit.LastIterations, Is.GreaterThanOrEqualTo(2));
        }
    }
}
