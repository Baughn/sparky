using NUnit.Framework;
using Sparky.MNA;
using System;

namespace Sparky.Tests
{
    [TestFixture]
    public class TransformerTests
    {
        [Test]
        public void TestDCStepUp()
        {
            // Source: 10V DC
            // Transformer: Ratio = 2.0 (Step Up)
            // Load: 100 Ohm Resistor
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nPri = circuit.AddNode(); // Primary bottom (grounded for simplicity in this test?)
            // Actually let's ground the bottom of both sides for simplicity, 
            // but keep them as separate nodes to test full 4-node capability.
            
            var ground = circuit.Nodes[0];
            
            // Primary Circuit: Voltage Source -> Transformer Primary
            // We'll connect Primary Bottom to Ground.
            
            // Secondary Circuit: Transformer Secondary -> Resistor -> Ground
            var nSecTop = circuit.AddNode();
            
            // Transformer Nodes:
            // P1: nSrc
            // P2: Ground
            // S1: nSecTop
            // S2: Ground
            
            double ratio = 2.0;
            var transformer = new Transformer(nSrc, ground, nSecTop, ground, ratio);
            
            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(transformer);
            circuit.AddComponent(new Resistor(nSecTop, ground, 100.0));
            
            circuit.Solve(0);
            
            // Expect: V_sec = V_pri * Ratio = 10 * 2 = 20V
            Assert.That(nSecTop.Voltage, Is.EqualTo(20.0).Within(1e-6));
            
            // Power Check
            // P_out = V^2 / R = 400 / 100 = 4W
            // P_in = V * I
            // I_pri should be 0.4A
            // We can't easily get I_pri without checking the VoltageSource current or Transformer current.
            // But we can check conservation of power if we had access to currents.
            // For now, voltage check is sufficient for basic function.
        }

        [Test]
        public void TestACStepDown()
        {
            // Source: 120V AC
            // Transformer: Ratio = 0.1 (10:1 Step Down)
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nSec = circuit.AddNode();
            var ground = circuit.Nodes[0];
            
            var src = new VoltageSource(nSrc, ground, 0);
            var transformer = new Transformer(nSrc, ground, nSec, ground, 0.1);
            var load = new Resistor(nSec, ground, 10.0);
            
            circuit.AddComponent(src);
            circuit.AddComponent(transformer);
            circuit.AddComponent(load);
            
            double dt = 0.001;
            double t = 0;
            
            // Test at peak
            t = 0.005; // 1/4 cycle of 50Hz (20ms)
            src.Voltage = 120.0; // DC test at peak equivalent
            circuit.Solve(dt);
            
            Assert.That(nSec.Voltage, Is.EqualTo(12.0).Within(1e-6));
        }

        [Test]
        public void TestIsolationAndPolarity()
        {
            // Test that swapping secondary leads inverts polarity
            // and that it works floating (isolation).
            
            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nSecTop = circuit.AddNode();
            var nSecBot = circuit.AddNode();
            var ground = circuit.Nodes[0];
            
            // Source 10V
            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            
            // Transformer 1:1
            // Primary: nSrc -> Ground
            // Secondary: nSecTop -> nSecBot (Floating relative to ground initially)
            // But to solve, we need a reference? MNA matrix might be singular if secondary is purely floating.
            // Yes, standard MNA requires a path to ground for all nodes unless we use specific techniques.
            // Let's ground nSecBot to test polarity.
            
            circuit.AddComponent(new Resistor(nSecBot, ground, 1e-9)); // "Ground" it hard
            
            // Case 1: Normal Polarity
            // P: Top->Bot, S: Top->Bot
            var t1 = new Transformer(nSrc, ground, nSecTop, nSecBot, 1.0);
            circuit.AddComponent(t1);
            circuit.AddComponent(new Resistor(nSecTop, nSecBot, 100.0)); // Load across secondary
            
            circuit.Solve(0);
            
            // V_sec = V_pri = 10V
            // V_nSecTop - V_nSecBot = 10V
            Assert.That(nSecTop.Voltage - nSecBot.Voltage, Is.EqualTo(10.0).Within(1e-6));
            
            // Case 2: Inverted Polarity
            // Swap secondary connections in a new circuit or just re-verify logic.
            // Let's make a new circuit for inverted.
            
            circuit = new Circuit();
            nSrc = circuit.AddNode();
            nSecTop = circuit.AddNode();
            nSecBot = circuit.AddNode();
            ground = circuit.Nodes[0];
            
            circuit.AddComponent(new VoltageSource(nSrc, ground, 10.0));
            circuit.AddComponent(new Resistor(nSecBot, ground, 1e-9)); // Ground bottom
            
            // Connect Transformer Secondary INVERTED: S1=nSecBot, S2=nSecTop
            // So V_s = V_bot - V_top
            // Equation: V_p - V_s = 0 => V_p - (V_bot - V_top) = 0 => V_p = V_bot - V_top
            // => V_top - V_bot = -V_p
            var t2 = new Transformer(nSrc, ground, nSecBot, nSecTop, 1.0);
            circuit.AddComponent(t2);
            circuit.AddComponent(new Resistor(nSecTop, nSecBot, 100.0));
            
            circuit.Solve(0);
            
            Assert.That(nSecTop.Voltage - nSecBot.Voltage, Is.EqualTo(-10.0).Within(1e-6));
        }
    }
}
