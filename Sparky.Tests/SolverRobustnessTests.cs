using System;
using NUnit.Framework;
using Sparky.MNA;
using CSparse.Storage;

namespace Sparky.Tests
{
    // Test helper component that intentionally prevents Newton from converging
    internal class TogglingConductance : Component
    {
        private double _g = 1.0;

        public TogglingConductance(Node n1, Node n2) : base(n1, n2) { }

        public override bool RequiresIteration => true;

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0)
        {
            // Flip sign every iteration so the operating point oscillates
            _g = -_g;

            int n1 = Node1.Id;
            int n2 = Node2.Id;

            if (n1 != 0)
            {
                A.At(n1, n1, _g);
                if (n2 != 0) A.At(n1, n2, -_g);
            }

            if (n2 != 0)
            {
                A.At(n2, n2, _g);
                if (n1 != 0) A.At(n2, n1, -_g);
            }
        }
    }

    [TestFixture]
    public class SolverRobustnessTests
    {
        [Test]
        public void SolveAnchorsGroundAndAvoidsSingularMatrix()
        {
            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Ground;

            circuit.AddComponent(new Resistor(n1, ground, 100.0));
            circuit.AddComponent(new CurrentSource(n1, ground, 1.0));

            Assert.DoesNotThrow(() => circuit.Solve(0));
            Assert.That(n1.Voltage, Is.EqualTo(-100.0).Within(1e-6));
            Assert.That(ground.Voltage, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void NewtonIterationThrowsOnNonConvergence()
        {
            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Ground;

            circuit.AddComponent(new VoltageSource(n1, ground, 1.0));
            circuit.AddComponent(new TogglingConductance(n1, ground));

            var ex = Assert.Throws<InvalidOperationException>(() => circuit.Solve(0));
            Assert.That(ex?.Message, Does.Contain("converge"));
        }

        [Test]
        public void FloatingNetworkStillSolvesWithGminAnchoring()
        {
            // No explicit path to ground: only gmin should keep the matrix well-conditioned.
            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var n2 = circuit.AddNode();

            circuit.AddComponent(new CurrentSource(n1, n2, 1e-12));

            Assert.DoesNotThrow(() => circuit.Solve(0));

            Assert.That(circuit.Ground.Voltage, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(n1.Voltage, Is.EqualTo(-1.0).Within(1e-6));
            Assert.That(n2.Voltage, Is.EqualTo(1.0).Within(1e-6));
        }
    }
}
