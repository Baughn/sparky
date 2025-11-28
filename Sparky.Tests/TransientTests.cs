using NUnit.Framework;
using Sparky.MNA;
using System;
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
            double alpha = dt / (R * C);
            double denom = 1.0 + alpha;
            double expected = 0.0;

            // First step: t=0. Capacitor is uncharged (0V).
            // Actually, we need to initialize. 
            // If we start with 0V across C, then at t=0+, V_c should start rising.

            // Run for 5 tau (5ms)
            for (int i = 0; i < 50; i++)
            {
                circuit.Solve(dt);
                time += dt;

                // Backward Euler discrete expectation: v_n = (v_{n-1} + alpha * V) / (1 + alpha)
                expected = (expected + alpha * V) / denom;
                Assert.That(n1.Voltage, Is.EqualTo(expected).Within(1e-3));
            }

            // Should be within 1% of final value after 5 tau
            Assert.That(n1.Voltage, Is.GreaterThan(0.99 * V));
        }

        [Test]
        public void TestRLCircuitCurrentRiseMatchesBackwardEuler()
        {
            // RL Circuit:
            // 5V Source -> Resistor (10 Ohm) -> Inductor (1mH) -> Ground
            // tau = L / R = 0.0001s. Steady-state current = 0.5A.

            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double R = 10.0;
            double L = 1e-3;
            double V = 5.0;

            var nSource = circuit.AddNode();
            circuit.AddComponent(new VoltageSource(nSource, ground, V));
            circuit.AddComponent(new Resistor(nSource, n1, R));
            circuit.AddComponent(new Inductor(n1, ground, L));

            double dt = 1e-5; // 10us = 0.1*tau
            double alpha = dt * R / L;
            double denom = 1.0 + alpha;
            double expectedCurrent = 0.0;

            // Run for 50 tau to reach steady state
            for (int i = 0; i < 500; i++)
            {
                circuit.Solve(dt);

                // Backward Euler discrete expectation for current:
                // i_n = (i_{n-1} + dt/L * V) / (1 + dt*R/L)
                expectedCurrent = (expectedCurrent + (dt * V / L)) / denom;

                double actualCurrent = (V - n1.Voltage) / R;
                Assert.That(actualCurrent, Is.EqualTo(expectedCurrent).Within(1e-4));
            }

            Assert.That(expectedCurrent, Is.EqualTo(V / R).Within(1e-3));
            Assert.That(n1.Voltage, Is.EqualTo(V - expectedCurrent * R).Within(1e-3));
        }

        [Test]
        public void TestRCVariableTimeStepStillMatchesBackwardEuler()
        {
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var n1 = circuit.AddNode();
            var ground = circuit.Ground;

            double V = 5.0;
            double R = 1000.0;
            double C = 1e-6;

            circuit.AddComponent(new VoltageSource(nSrc, ground, V));
            circuit.AddComponent(new Resistor(nSrc, n1, R));
            circuit.AddComponent(new Capacitor(n1, ground, C));

            double expected = 0.0;

            double dt1 = 1e-4;
            double alpha1 = dt1 / (R * C);
            double denom1 = 1.0 + alpha1;
            for (int i = 0; i < 10; i++)
            {
                circuit.Solve(dt1);
                expected = (expected + alpha1 * V) / denom1;
                Assert.That(n1.Voltage, Is.EqualTo(expected).Within(1e-4));
            }

            double dt2 = 2e-4;
            double alpha2 = dt2 / (R * C);
            double denom2 = 1.0 + alpha2;
            for (int i = 0; i < 10; i++)
            {
                circuit.Solve(dt2);
                expected = (expected + alpha2 * V) / denom2;
                Assert.That(n1.Voltage, Is.EqualTo(expected).Within(1e-4));
            }
        }

        [Test]
        public void TestCurrentSourceStepIsRestampedEachSolve()
        {
            // Ground -> I -> Node -> C -> Ground
            // Expect V to integrate I*dt/C each step even when I changes.

            var circuit = new Circuit();
            var n1 = circuit.AddNode();
            var ground = circuit.Ground;

            var source = new CurrentSource(ground, n1, 0.1);
            circuit.AddComponent(source);
            circuit.AddComponent(new Capacitor(n1, ground, 1e-3));

            double dt = 1e-3;
            double expected = 0.0;

            circuit.Solve(dt);
            expected += 0.1 * dt / 1e-3;
            Assert.That(n1.Voltage, Is.EqualTo(expected).Within(1e-6));

            source.Current = 0.05;

            circuit.Solve(dt);
            expected += 0.05 * dt / 1e-3;
            Assert.That(n1.Voltage, Is.EqualTo(expected).Within(1e-6));
        }
    }
}
