using NUnit.Framework;
using Sparky.MNA.Api;

namespace Sparky.Tests
{
    [TestFixture]
    public class EdgeCaseCircuitTests
    {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationManager();
        }

        [Test]
        public void EmptyCircuit_Step_DoesNotThrow()
        {
            // No nodes created, no components added
            Assert.DoesNotThrow(() => _sim.Step(0.001));
        }

        [Test]
        public void SingleNode_NoComponents_StepSucceeds()
        {
            // Create an orphan node with no connections
            var orphan = _sim.CreateNode();

            Assert.DoesNotThrow(() => _sim.Step(0.001));

            // Orphan node should be at ground potential (0V)
            Assert.That(_sim.GetVoltage(orphan), Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void ResistorToGround_NoSource_ZeroVoltage()
        {
            // Resistor network with no excitation source
            // N1 -- R1(100) -- GND
            // N2 -- R2(100) -- N1
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            _sim.AddResistor(n1, _sim.Ground, 100.0);
            _sim.AddResistor(n2, n1, 100.0);

            _sim.Step(0.001);

            // No sources means all voltages should be 0
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void ParallelVoltageSources_SameVoltage_SingularMatrix()
        {
            // Two parallel ideal voltage sources create a singular matrix
            // even with identical voltages - this is correct MNA behavior
            // because the current split between them is indeterminate
            var nPos = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, _sim.Ground, 100.0); // Load to sink current

            var ex = Assert.Throws<System.InvalidOperationException>(() => _sim.Step(0.001));
            Assert.That(ex!.Message, Does.Contain("singular"));
        }

        [Test]
        public void ParallelVoltageSources_DifferentVoltage_Behavior()
        {
            // Two parallel voltage sources with different voltages
            // This is physically invalid - documents actual behavior
            var nPos = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddVoltageSource(nPos, _sim.Ground, 5.0);
            _sim.AddResistor(nPos, _sim.Ground, 100.0);

            // This configuration is invalid - MNA may throw or produce undefined results
            // We document the actual behavior rather than assert a specific outcome
            try
            {
                _sim.Step(0.001);
                // If it doesn't throw, the voltage will be some arbitrary value
                // Just verify it's finite
                var voltage = _sim.GetVoltage(nPos);
                Assert.That(double.IsFinite(voltage), Is.True,
                    "Conflicting voltage sources produced non-finite voltage");
            }
            catch (System.InvalidOperationException)
            {
                // Singular matrix is expected for conflicting voltage sources
                Assert.Pass("Conflicting voltage sources correctly detected as singular matrix");
            }
        }

        [Test]
        public void VeryLargeCircuit_1000Nodes_Succeeds()
        {
            // Stress test: resistor ladder with 1000 nodes
            // 10V -- R -- N1 -- R -- N2 -- ... -- N999 -- R -- GND
            var nSource = _sim.CreateNode();
            _sim.AddVoltageSource(nSource, _sim.Ground, 10.0);

            var prevNode = nSource;
            const int nodeCount = 1000;
            const double resistance = 1.0;

            for (int i = 0; i < nodeCount; i++)
            {
                var nextNode = (i == nodeCount - 1) ? _sim.Ground : _sim.CreateNode();
                _sim.AddResistor(prevNode, nextNode, resistance);
                prevNode = nextNode;
            }

            Assert.DoesNotThrow(() => _sim.Step(0.001));

            // Source node should be at 10V
            Assert.That(_sim.GetVoltage(nSource), Is.EqualTo(10.0).Within(1e-6));
        }

        [Test]
        public void VerySmallValues_PicoFarads_Succeeds()
        {
            // Test numerical stability with very small component values
            // RC circuit with 1 pF capacitor
            var nPos = _sim.CreateNode();
            var nCap = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 5.0);
            _sim.AddResistor(nPos, nCap, 1e6);        // 1 MOhm
            _sim.AddCapacitor(nCap, _sim.Ground, 1e-12); // 1 pF

            // Time constant tau = RC = 1e6 * 1e-12 = 1e-6 s = 1 us
            // Very fast circuit - use small timestep
            double dt = 1e-9; // 1 ns

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _sim.Step(dt);
                }
            });

            // After 100 ns (0.1 * tau), capacitor should have charged partially
            var vCap = _sim.GetVoltage(nCap);
            Assert.That(double.IsFinite(vCap), Is.True, "Voltage should be finite");
            Assert.That(vCap, Is.GreaterThan(0.0), "Capacitor should be charging");
            Assert.That(vCap, Is.LessThan(5.0), "Capacitor should not exceed source voltage");
        }

        [Test]
        public void VeryLargeValues_Megohms_Succeeds()
        {
            // Test numerical stability with very large resistance values
            // Voltage divider with megohm resistors
            var nPos = _sim.CreateNode();
            var nMid = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, nMid, 1e6);       // 1 MOhm
            _sim.AddResistor(nMid, _sim.Ground, 1e6); // 1 MOhm

            _sim.Step(0.001);

            // Standard voltage divider: V_mid = 10 * (1M / 2M) = 5V
            Assert.That(_sim.GetVoltage(nMid), Is.EqualTo(5.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(nPos), Is.EqualTo(10.0).Within(1e-6));
        }
    }
}
