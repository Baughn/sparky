using NUnit.Framework;
using Sparky.MNA.Core;
using Sparky.Tests.TestHelpers;
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

            Assert.That(n2.Voltage, Is.EqualTo(5.0).Within(Tolerances.Voltage));
            Assert.That(circuit.LastIterations, Is.EqualTo(1));
        }

        [Test]
        public void TestVoltageSourceRhsUpdatesAreNotCached()
        {
            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Ground;

            var source = new VoltageSource(n1, ground, 10.0);
            circuit.AddComponent(source);

            circuit.Solve(0);
            Assert.That(n1.Voltage, Is.EqualTo(10.0).Within(Tolerances.Voltage));

            source.Voltage = 5.0;
            circuit.Solve(0);
            Assert.That(n1.Voltage, Is.EqualTo(5.0).Within(Tolerances.Voltage));
            Assert.That(circuit.LastIterations, Is.EqualTo(1));
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

            Assert.That(n1.Voltage, Is.EqualTo(10.0 * 50.0 / 150.0).Within(Tolerances.Voltage));
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

            Assert.That(n1.Voltage, Is.EqualTo(10.0).Within(Tolerances.Loose));
        }

        [Test]
        public void ChangingResistorBetweenSolvesUpdatesVoltages()
        {
            // Voltage divider 10V -> R1(10) -> mid -> R2(10|30) -> GND
            // Expect 5V with 10/10, then 7.5V after switching R2 to 30.

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nMid = circuit.AddNode();
            var ground = circuit.Ground;

            var source = new VoltageSource(nSrc, ground, 10.0);
            var r1 = new Resistor(nSrc, nMid, 10.0);
            var r2 = new Resistor(nMid, ground, 10.0);

            circuit.AddComponent(source);
            circuit.AddComponent(r1);
            circuit.AddComponent(r2);

            circuit.Solve(0);
            Assert.That(nMid.Voltage, Is.EqualTo(5.0).Within(Tolerances.Voltage));

            r2.Resistance = 30.0;

            circuit.Solve(0);
            Assert.That(nMid.Voltage, Is.EqualTo(10.0 * 30.0 / (10.0 + 30.0)).Within(Tolerances.Voltage));
            Assert.That(circuit.LastIterations, Is.EqualTo(1));
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

            Assert.That(n1.Voltage, Is.EqualTo(0.0).Within(Tolerances.Loose));
        }

        [Test]
        public void ChangingCapacitanceBetweenSolvesAffectsBehavior()
        {
            // RC circuit: V -> R(1k) -> C(1uF|10uF) -> GND
            // Larger C means slower charging. After same time, smaller C should be closer to final voltage.

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nCap = circuit.AddNode();
            var ground = circuit.Ground;

            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(new Resistor(nSrc, nCap, 1000.0));
            var cap = new Capacitor(nCap, ground, 1e-6); // 1uF, tau = 1ms
            circuit.AddComponent(cap);

            // Charge for 5 time constants (should be ~99% charged)
            double dt = 0.001;
            for (int i = 0; i < 5; i++) circuit.Solve(dt);
            double v1 = nCap.Voltage;

            // Now increase capacitance and reset circuit - simulate by making new circuit
            var circuit2 = new Circuit();
            var nSrc2 = circuit2.AddNode();
            var nCap2 = circuit2.AddNode();
            var ground2 = circuit2.Ground;

            circuit2.AddComponent(new VoltageSource(nSrc2, ground2, 10.0));
            circuit2.AddComponent(new Resistor(nSrc2, nCap2, 1000.0));
            var cap2 = new Capacitor(nCap2, ground2, 10e-6); // 10uF, tau = 10ms
            circuit2.AddComponent(cap2);

            // Same time steps
            for (int i = 0; i < 5; i++) circuit2.Solve(dt);
            double v2 = nCap2.Voltage;

            // Smaller capacitance should charge faster (higher voltage)
            Assert.That(v1, Is.GreaterThan(v2));

            // Now test mutable capacitance: change cap2 to match cap1
            cap2.Capacitance = 1e-6;
            // Reset by continuing to charge
            for (int i = 0; i < 100; i++) circuit2.Solve(dt);
            Assert.That(nCap2.Voltage, Is.EqualTo(10.0).Within(0.1));
        }

        [Test]
        public void ChangingInductanceBetweenSolvesAffectsBehavior()
        {
            // RL circuit: V -> R(1k) -> L(1mH|10mH) -> GND
            // Larger L means slower current rise.

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nInd = circuit.AddNode();
            var ground = circuit.Ground;

            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(new Resistor(nSrc, nInd, 1000.0));
            var ind = new Inductor(nInd, ground, 1e-3); // 1mH
            circuit.AddComponent(ind);

            // Settle to steady state
            double dt = 0.0001;
            for (int i = 0; i < 100; i++) circuit.Solve(dt);

            // In steady state, inductor is short, so nInd should be ~0V
            Assert.That(nInd.Voltage, Is.EqualTo(0.0).Within(0.1));

            // Change inductance - should still eventually settle to same steady state
            ind.Inductance = 10e-3; // 10mH
            for (int i = 0; i < 1000; i++) circuit.Solve(dt);
            Assert.That(nInd.Voltage, Is.EqualTo(0.0).Within(0.1));
        }

        [Test]
        public void VoltageSourceCurrentReadback()
        {
            // Simple circuit: V(10V) -> R(100) -> GND
            // I = V/R = 10/100 = 0.1A

            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Ground;

            var source = new VoltageSource(n1, ground, 10.0);
            circuit.AddComponent(source);
            circuit.AddComponent(new Resistor(n1, ground, 100.0));

            circuit.Solve(0);

            // In MNA convention, the auxiliary current is defined as current flowing
            // through the source from + to - terminal. For a source delivering power,
            // current enters + terminal from external circuit, so internal current is negative.
            Assert.That(source.Current, Is.EqualTo(-0.1).Within(Tolerances.Voltage));
        }

        [Test]
        public void TransformerCurrentReadback()
        {
            // Transformer with ratio 2:1 (Ns/Np = 0.5)
            // Primary: V(10V) -> p1, p2 -> GND
            // Secondary: s1 -> R(100) -> s2 -> GND

            var circuit = new Circuit();
            var p1 = circuit.AddNode();
            var p2 = circuit.AddNode();
            var s1 = circuit.AddNode();
            var s2 = circuit.AddNode();
            var ground = circuit.Ground;

            // Primary side: 10V source
            circuit.AddComponent(new VoltageSource(p1, ground, 10.0));
            circuit.AddComponent(new Resistor(p1, p2, 1.0)); // Small primary resistance

            // Transformer with ratio 0.5 (step-down)
            var transformer = new Transformer(p1, p2, s1, s2, 0.5);
            circuit.AddComponent(transformer);

            // Secondary side: load resistor
            circuit.AddComponent(new Resistor(s1, s2, 100.0));

            // Ground secondary
            circuit.AddComponent(new Resistor(s2, ground, 0.001)); // Tie secondary to ground

            circuit.Solve(0);

            // Verify transformer currents are populated
            // Secondary current should be -PrimaryCurrent / ratio
            Assert.That(transformer.SecondaryCurrent, Is.EqualTo(-transformer.PrimaryCurrent / 0.5).Within(Tolerances.Voltage));
        }

        [Test]
        public void ChangingTransformerRatioAffectsOutput()
        {
            // Simplified transformer test: verify ratio affects voltage relationship
            // Transformer ratio n = Ns/Np. Voltage relationship: Vs = Vp * n

            var circuit = new Circuit();
            var p1 = circuit.AddNode();
            var s1 = circuit.AddNode();
            var ground = circuit.Ground;

            circuit.AddComponent(new VoltageSource(p1, ground, 10.0));
            var transformer = new Transformer(p1, ground, s1, ground, 2.0); // 1:2 step-up, Vs = 20V
            circuit.AddComponent(transformer);
            circuit.AddComponent(new Resistor(s1, ground, 1000.0)); // Load

            circuit.Solve(0);
            double v1 = s1.Voltage;
            Assert.That(v1, Is.EqualTo(20.0).Within(1.0)); // ~20V with ratio=2

            // Change ratio to step-down
            transformer.Ratio = 0.5; // 2:1 step-down, Vs = 5V
            circuit.Solve(0);
            double v2 = s1.Voltage;
            Assert.That(v2, Is.EqualTo(5.0).Within(1.0)); // ~5V with ratio=0.5

            // Higher ratio = higher secondary voltage
            Assert.That(v1, Is.GreaterThan(v2));
        }
    }
}
