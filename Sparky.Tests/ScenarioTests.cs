using NUnit.Framework;
using Sparky.MNA;
using System;

namespace Sparky.Tests
{
    [TestFixture]
    public class ScenarioTests
    {
        [Test]
        public void TestTheGenerator_VoltageSag()
        {
            // "The Generator": Voltage Source with Internal Resistance.
            // As load current increases, terminal voltage should drop.

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nLoad = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double V_gen = 100.0;
            double R_int = 5.0; // Internal resistance

            circuit.AddComponent(new VoltageSource(nSrc, ground, V_gen));
            circuit.AddComponent(new Resistor(nSrc, nLoad, R_int));

            // We need to simulate varying load. 
            // Since we can't easily change topology, we'll create two separate circuits or 
            // just verify one point on the curve vs open circuit.

            // Case 1: Open Circuit (Infinite Load) -> V_load = V_gen
            // (No resistor added)
            circuit.Solve(0);
            Assert.That(nLoad.Voltage, Is.EqualTo(V_gen).Within(1e-6));

            // Case 2: Heavy Load (e.g. 5 Ohms) -> V_load = V_gen * 5 / (5+5) = 50V
            circuit.AddComponent(new Resistor(nLoad, ground, 5.0));
            circuit.Solve(0);
            Assert.That(nLoad.Voltage, Is.EqualTo(50.0).Within(1e-6));
        }

        [Test]
        public void TestTheBattery_ChargeDischarge()
        {
            // "The Battery": Capacitor charging and discharging.
            // Source -> Switch (Resistor) -> Capacitor

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nCap = circuit.AddNode();
            var ground = circuit.Nodes[0];

            double V_src = 12.0;
            double R_charge = 100.0;
            double C = 100e-6; // 100uF

            var src = new VoltageSource(nSrc, ground, V_src);
            var r = new Resistor(nSrc, nCap, R_charge);
            var cap = new Capacitor(nCap, ground, C);

            circuit.AddComponent(src);
            circuit.AddComponent(r);
            circuit.AddComponent(cap);

            double dt = 0.001; // 1ms
            double tau = R_charge * C; // 10ms

            // 1. Charge
            // Increase time to ensure full charge (10 tau = 100ms)
            for (int i = 0; i < 100; i++)
            {
                circuit.Solve(dt);
            }
            // Should be fully charged
            Assert.That(nCap.Voltage, Is.EqualTo(V_src).Within(0.1));

            // 2. Discharge
            // To simulate disconnect, we can set R to a very high value (Open switch)
            // But to simulate discharge, we connect a load.
            // Let's modify the circuit by changing the source voltage to 0 (short to ground) 
            // effectively discharging through the same resistor.
            src.Voltage = 0.0;
            // Note: This requires VoltageSource to have a public setter for Voltage.
            // Checking VoltageSource.cs... it's a property but might be readonly?
            // Assuming it's settable for now based on typical patterns, if not I'll fix it.

            // Increase discharge time to ensure full discharge (was 50, now 100)
            for (int i = 0; i < 100; i++)
            {
                circuit.Solve(dt);
            }

            // Should be discharged
            Assert.That(nCap.Voltage, Is.LessThan(0.1));
        }

        [Test]
        public void TestTheRectifier_ACtoDC()
        {
            // AC Source -> Diode -> Capacitor -> Load

            var circuit = new Circuit();
            var nSrc = circuit.AddNode();
            var nRect = circuit.AddNode();
            var ground = circuit.Nodes[0];

            var src = new VoltageSource(nSrc, ground, 0); // Will update in loop
            circuit.AddComponent(src);
            circuit.AddComponent(new Diode(nSrc, nRect)); // Diode pointing to nRect
            circuit.AddComponent(new Capacitor(nRect, ground, 100e-6)); // Smoothing Cap
            circuit.AddComponent(new Resistor(nRect, ground, 1000.0)); // Load

            double dt = 0.0001; // Reduce time step to 0.1ms for better resolution
            double freq = 50.0; // 50Hz
            double amplitude = 10.0;

            double maxVoltage = 0;
            double minVoltage = 100;

            // Run for 3 cycles (3 * 20ms = 60ms)
            for (double t = 0; t < 0.06; t += dt)
            {
                src.Voltage = amplitude * Math.Sin(2 * Math.PI * freq * t);
                circuit.Solve(dt);

                // Start recording after first cycle to let it settle
                if (t > 0.02)
                {
                    maxVoltage = Math.Max(maxVoltage, nRect.Voltage);
                    minVoltage = Math.Min(minVoltage, nRect.Voltage);
                }
            }

            Console.WriteLine($"Rectifier: Max={maxVoltage}, Min={minVoltage}, Ripple={maxVoltage - minVoltage}");

            // Peak should be close to Amplitude - DiodeDrop (~0.7V)
            Assert.That(maxVoltage, Is.EqualTo(amplitude - 0.7).Within(0.5));

            // Ripple should be small but present
            // V_ripple approx I_load / (f * C) = (9.3V/1k) / (50 * 100u) = 9.3mA / 0.005 = 1.8V
            double ripple = maxVoltage - minVoltage;
            Assert.That(ripple, Is.LessThan(2.5).And.GreaterThan(0.5));
        }

        [Test]
        public void TestOffGridCottage()
        {
            // "The Off-Grid Cottage"
            // Diesel Gen (100V, 1 Ohm) -> Switch -> Main Bus
            // Wind Turbine (480V AC) -> Transformer (4:1) -> Diode -> Main Bus
            // Battery (10mF) -> Main Bus
            // Lights (10 Ohm) -> Switch -> Main Bus

            var circuit = new Circuit();
            var nBus = circuit.AddNode();
            var ground = circuit.Nodes[0];

            // Diesel Generator
            var nGen = circuit.AddNode();
            var genSrc = new VoltageSource(nGen, ground, 100.0);
            var genRes = new Resistor(nGen, nBus, 1.0); // Internal R
            genRes.Resistance = 1e9; // Start OFF
            circuit.AddComponent(genSrc);
            circuit.AddComponent(genRes);

            // Battery
            var battery = new Capacitor(nBus, ground, 0.01);
            circuit.AddComponent(battery);

            // Wind Turbine + Transformer
            var nWindPri = circuit.AddNode(); // Primary side of transformer
            var nWindSec = circuit.AddNode(); // Secondary side of transformer

            // Source (480V) -> Primary
            var windSrc = new VoltageSource(nWindPri, ground, 0.0); // Start OFF
            circuit.AddComponent(windSrc);

            // Transformer (4:1)
            // Primary: nWindPri -> Ground
            // Secondary: nWindSec -> Ground
            var transformer = new Transformer(nWindPri, ground, nWindSec, ground, 4.0);
            circuit.AddComponent(transformer);

            // Diode: Secondary -> Bus
            var windDiode = new Diode(nWindSec, nBus);
            circuit.AddComponent(windDiode);

            // Lights (Load)
            var nLoad = circuit.AddNode();
            var loadRes = new Resistor(nLoad, ground, 10.0);
            var loadSwitch = new Resistor(nBus, nLoad, 1e9); // Start OFF
            circuit.AddComponent(loadRes);
            circuit.AddComponent(loadSwitch);

            double dt = 0.001; // 1ms
            double time = 0;

            // 1. Start (Everything OFF)
            for (int i = 0; i < 100; i++) circuit.Solve(dt); // 0.1s
            Assert.That(nBus.Voltage, Is.LessThan(0.1));

            // 2. Turn ON Diesel Gen
            genRes.Resistance = 1.0; // Connect Gen
            for (int i = 0; i < 400; i++) circuit.Solve(dt); // 0.4s
            // Should charge to ~100V
            Assert.That(nBus.Voltage, Is.EqualTo(100.0).Within(1.0));

            // 3. Turn ON Lights
            loadSwitch.Resistance = 0.01; // Connect Load
            for (int i = 0; i < 500; i++) circuit.Solve(dt); // 0.5s
            // Voltage Sag: 100V * (10 / (10+1)) = 90.9V
            Assert.That(nBus.Voltage, Is.EqualTo(90.9).Within(1.0));

            // 4. Turn ON Wind Turbine
            // 480V Peak, 50Hz. Transformer 4:1 -> 120V Peak at secondary.
            double freq = 50.0;
            double maxV = 0;
            for (int i = 0; i < 500; i++) // 0.5s
            {
                time += dt;
                windSrc.Voltage = 480.0 * Math.Sin(2 * Math.PI * freq * time);
                circuit.Solve(dt);
                maxV = Math.Max(maxV, nBus.Voltage);
            }
            // Peak should be higher than 90.9V because Wind (120V eff peak) > Gen (100V)
            // Peak ~ 120 - 0.7 (Diode) = 119.3V
            Assert.That(maxV, Is.GreaterThan(110.0));

            // 5. Turn OFF Diesel Gen
            genRes.Resistance = 1e9; // Disconnect Gen
            // Now discharging battery + intermittent wind
            for (int i = 0; i < 500; i++) // 0.5s
            {
                time += dt;
                windSrc.Voltage = 480.0 * Math.Sin(2 * Math.PI * freq * time);
                circuit.Solve(dt);
            }
            // Battery should be discharging but Wind is keeping it somewhat up.
            Assert.That(nBus.Voltage, Is.GreaterThan(10.0));
        }
    }
}
