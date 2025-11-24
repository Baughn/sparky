using NUnit.Framework;
using Sparky.MNA;
using System;

namespace Sparky.Tests
{
    [TestFixture]
    public class VerificationTests
    {
        [Test]
        public void TestInductorDC_ShortCircuitBehavior()
        {
            // DC Source -> Inductor -> Resistor -> Ground
            // At DC (dt=0), Inductor should act as a short circuit (0V drop).
            // Current should be V / R.

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nInd = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double V = 10.0;
            double R = 100.0;
            double L = 1.0;

            circuit.AddComponent(new VoltageSource(nSrc, ground, V));
            circuit.AddComponent(new Inductor(nSrc, nInd, L));
            circuit.AddComponent(new Resistor(nInd, ground, R));

            // Solve for DC Operating Point (dt = 0)
            circuit.Solve(0);

            // Check Voltage at nInd. Should be equal to nSrc (10V) because Inductor is a short.
            Assert.That(nInd.Voltage, Is.EqualTo(V).Within(1e-6), "Inductor should be a short circuit at DC");
        }

        [Test]
        public void TestDiodeStability_HighVoltage()
        {
            // High Voltage Source -> Diode -> Resistor -> Ground
            // This stresses the exponential model.
            // 100V source. Diode drop ~0.7-1.0V. Rest across resistor.

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nDiode = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double V = 100.0; // High voltage
            double R = 100.0;

            circuit.AddComponent(new VoltageSource(nSrc, ground, V));
            circuit.AddComponent(new Diode(nSrc, nDiode));
            circuit.AddComponent(new Resistor(nDiode, ground, R));

            // Solve DC
            circuit.Solve(0);

            // Check that it converged to a reasonable value
            // V_diode = V_src - V_res
            // V_res approx 99V
            Assert.That(nDiode.Voltage, Is.LessThan(99.5).And.GreaterThan(98.0));
        }
    }
}
