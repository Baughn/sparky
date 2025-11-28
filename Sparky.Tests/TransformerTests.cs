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
            
            // Test at peak
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

        [Test]
        public void TestPowerAndCurrentRatio()
        {
            // Primary: 20V source with 2 Ohm series resistor
            // Transformer: 2:1 step up (n = 2)
            // Secondary: 8 Ohm load

            var circuit = new Circuit();
            var ground = circuit.Ground;

            var nSrc = circuit.AddNode();
            var nPri = circuit.AddNode();
            var nSec = circuit.AddNode();

            double ratio = 2.0;
            double vs = 20.0;
            double rSeries = 2.0;
            double rLoad = 8.0;

            circuit.AddComponent(new VoltageSource(nSrc, ground, vs));
            circuit.AddComponent(new Resistor(nSrc, nPri, rSeries));
            circuit.AddComponent(new Transformer(nPri, ground, nSec, ground, ratio));
            circuit.AddComponent(new Resistor(nSec, ground, rLoad));

            circuit.Solve(0);

            double vPri = nPri.Voltage;
            double vSec = nSec.Voltage;

            double iSec = vSec / rLoad;
            double iPri = (nSrc.Voltage - vPri) / rSeries;

            // Voltage scales by n, current scales by n
            Assert.That(vSec, Is.EqualTo(vPri * ratio).Within(1e-6));
            Assert.That(iPri, Is.EqualTo(iSec * ratio).Within(1e-6));

            double pIn = vPri * iPri;
            double pOut = vSec * iSec;
            Assert.That(pIn, Is.EqualTo(pOut).Within(1e-6));
        }

        [Test]
        public void TestFloatingSecondaryKeepsRatioAndPolarity()
        {
            const double ratio = 2.5;
            const double vPrimary = 5.0;

            var circuit = new Circuit();
            var ground = circuit.Ground;
            var nPri = circuit.AddNode();
            var nSecTop = circuit.AddNode();
            var nSecBot = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nPri, ground, vPrimary));
            circuit.AddComponent(new Transformer(nPri, ground, nSecTop, nSecBot, ratio));
            circuit.AddComponent(new Resistor(nSecTop, nSecBot, 50.0)); // Floating load only

            circuit.Solve(0);

            double secDiff = nSecTop.Voltage - nSecBot.Voltage;
            Assert.That(secDiff, Is.EqualTo(vPrimary * ratio).Within(1e-6));

            // Swap secondary leads to check polarity inversion on a floating winding.
            circuit = new Circuit();
            ground = circuit.Ground;
            nPri = circuit.AddNode();
            nSecTop = circuit.AddNode();
            nSecBot = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nPri, ground, vPrimary));
            circuit.AddComponent(new Transformer(nPri, ground, nSecBot, nSecTop, ratio));
            circuit.AddComponent(new Resistor(nSecTop, nSecBot, 50.0));

            circuit.Solve(0);

            secDiff = nSecTop.Voltage - nSecBot.Voltage;
            Assert.That(secDiff, Is.EqualTo(-vPrimary * ratio).Within(1e-6));
        }
    }
}
