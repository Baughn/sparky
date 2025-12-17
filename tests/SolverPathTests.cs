using NUnit.Framework;
using Sparky.MNA.Core;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests {
    [TestFixture]
    public class SolverPathTests {
        [Test]
        public void SmallCircuit_UsesDenseSolver() {
            // Circuit with < 96 nodes should use dense solver
            var circuit = new Circuit();

            // Create a small circuit (< 96 nodes)
            var nodes = new Node[10];
            for (int i = 0; i < 10; i++) {
                nodes[i] = circuit.AddNode();
            }

            // Chain of resistors
            circuit.AddComponent(new VoltageSource(nodes[0], circuit.Ground, 10.0));
            for (int i = 0; i < 9; i++) {
                circuit.AddComponent(new Resistor(nodes[i], nodes[i + 1], 100.0));
            }
            circuit.AddComponent(new Resistor(nodes[9], circuit.Ground, 100.0));

            circuit.BuildSystem();
            circuit.Solve(0.001);

            Assert.That(circuit.LastUsedDenseSolver, Is.True);
        }

        [Test]
        public void LargeCircuit_UsesSparseSOlver() {
            // Circuit with > 96 nodes should use sparse solver
            var circuit = new Circuit();

            // Create a large circuit (> 96 nodes)
            var nodes = new Node[100];
            for (int i = 0; i < 100; i++) {
                nodes[i] = circuit.AddNode();
            }

            // Chain of resistors
            circuit.AddComponent(new VoltageSource(nodes[0], circuit.Ground, 10.0));
            for (int i = 0; i < 99; i++) {
                circuit.AddComponent(new Resistor(nodes[i], nodes[i + 1], 100.0));
            }
            circuit.AddComponent(new Resistor(nodes[99], circuit.Ground, 100.0));

            circuit.BuildSystem();
            circuit.Solve(0.001);

            Assert.That(circuit.LastUsedDenseSolver, Is.False);
        }

        [Test]
        public void AtThreshold_UsesDenseSolver() {
            // Matrix size = nodeCount + extraEqCount
            // Threshold is 96, so we need matrix size <= 96
            // Ground is node 0, voltage source adds 1 extra equation
            // So: 94 new nodes + 1 ground + 1 voltage source eq = 96 matrix size
            var circuit = new Circuit();

            var nodes = new Node[94];
            for (int i = 0; i < 94; i++) {
                nodes[i] = circuit.AddNode();
            }

            // Chain of resistors with current source (no extra equation)
            circuit.AddComponent(new VoltageSource(nodes[0], circuit.Ground, 10.0));
            for (int i = 0; i < 93; i++) {
                circuit.AddComponent(new Resistor(nodes[i], nodes[i + 1], 100.0));
            }
            circuit.AddComponent(new Resistor(nodes[93], circuit.Ground, 100.0));

            circuit.BuildSystem();
            circuit.Solve(0.001);

            Assert.That(circuit.LastUsedDenseSolver, Is.True);
        }

        [Test]
        public void JustAboveThreshold_UsesSparse() {
            // Circuit with 97 nodes should use sparse solver
            var circuit = new Circuit();

            // Create 96 nodes (ground is index 0, so 97 total)
            var nodes = new Node[96];
            for (int i = 0; i < 96; i++) {
                nodes[i] = circuit.AddNode();
            }

            // Chain of resistors (sparse structure)
            circuit.AddComponent(new VoltageSource(nodes[0], circuit.Ground, 10.0));
            for (int i = 0; i < 95; i++) {
                circuit.AddComponent(new Resistor(nodes[i], nodes[i + 1], 100.0));
            }
            circuit.AddComponent(new Resistor(nodes[95], circuit.Ground, 100.0));

            circuit.BuildSystem();
            circuit.Solve(0.001);

            Assert.That(circuit.LastUsedDenseSolver, Is.False);
        }

        [Test]
        public void DenseMatrix_AboveThreshold_StillUsesDense() {
            // Large circuit but with high density (>= 0.18) should still use dense
            var circuit = new Circuit();

            // Create 100 nodes
            var nodes = new Node[100];
            for (int i = 0; i < 100; i++) {
                nodes[i] = circuit.AddNode();
            }

            // Densely connected: connect many nodes to each other
            // To achieve density >= 0.18: need ~1800+ non-zeros in 101x101 matrix
            // Each resistor adds 4 entries to the matrix (2 diagonal, 2 off-diagonal)
            circuit.AddComponent(new VoltageSource(nodes[0], circuit.Ground, 10.0));

            // Create a mesh: connect each node to ground and to next few nodes
            for (int i = 0; i < 100; i++) {
                circuit.AddComponent(new Resistor(nodes[i], circuit.Ground, 1000.0));
                // Connect to next 5 nodes (creates dense structure)
                for (int j = 1; j <= 5 && i + j < 100; j++) {
                    circuit.AddComponent(new Resistor(nodes[i], nodes[i + j], 100.0));
                }
            }

            circuit.BuildSystem();
            circuit.Solve(0.001);

            // With dense mesh, density should exceed threshold and use dense solver
            Assert.That(circuit.LastUsedDenseSolver, Is.True);
        }

        [Test]
        public void BothPaths_ProduceSameResult() {
            // Create same circuit at different sizes to force different solver paths
            // Results should be numerically equivalent

            // Small circuit (dense solver)
            var smallCircuit = new Circuit();
            var smallN1 = smallCircuit.AddNode();
            var smallN2 = smallCircuit.AddNode();
            smallCircuit.AddComponent(new VoltageSource(smallN1, smallCircuit.Ground, 10.0));
            smallCircuit.AddComponent(new Resistor(smallN1, smallN2, 100.0));
            smallCircuit.AddComponent(new Resistor(smallN2, smallCircuit.Ground, 100.0));
            smallCircuit.BuildSystem();
            smallCircuit.Solve(0.001);

            double smallVoltage = smallN2.Voltage;
            Assert.That(smallCircuit.LastUsedDenseSolver, Is.True);

            // Large circuit with same voltage divider embedded
            var largeCircuit = new Circuit();

            // Add many dummy nodes to force sparse solver
            var dummyNodes = new Node[100];
            for (int i = 0; i < 100; i++) {
                dummyNodes[i] = largeCircuit.AddNode();
            }

            // Dummy chain (isolated from main circuit)
            largeCircuit.AddComponent(new VoltageSource(dummyNodes[0], largeCircuit.Ground, 5.0));
            for (int i = 0; i < 99; i++) {
                largeCircuit.AddComponent(new Resistor(dummyNodes[i], dummyNodes[i + 1], 100.0));
            }
            largeCircuit.AddComponent(new Resistor(dummyNodes[99], largeCircuit.Ground, 100.0));

            // Same voltage divider as small circuit
            var largeN1 = largeCircuit.AddNode();
            var largeN2 = largeCircuit.AddNode();
            largeCircuit.AddComponent(new VoltageSource(largeN1, largeCircuit.Ground, 10.0));
            largeCircuit.AddComponent(new Resistor(largeN1, largeN2, 100.0));
            largeCircuit.AddComponent(new Resistor(largeN2, largeCircuit.Ground, 100.0));

            largeCircuit.BuildSystem();
            largeCircuit.Solve(0.001);

            double largeVoltage = largeN2.Voltage;
            Assert.That(largeCircuit.LastUsedDenseSolver, Is.False);

            // Both should produce the same result for the voltage divider
            Assert.That(largeVoltage, Is.EqualTo(smallVoltage).Within(Tolerances.Voltage));
            Assert.That(largeVoltage, Is.EqualTo(5.0).Within(Tolerances.Voltage)); // 10V / 2 = 5V
        }
    }
}
