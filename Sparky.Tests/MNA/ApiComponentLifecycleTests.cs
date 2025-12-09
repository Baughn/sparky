using NUnit.Framework;
using Sparky.MNA.Api;

namespace Sparky.Tests.MNA
{
    [TestFixture]
    public class ApiComponentLifecycleTests
    {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationManager();
        }

        #region Current Source Tests

        [Test]
        public void CurrentSource_AddUpdateRemove_WorksCorrectly()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            // Add
            var csId = _sim.AddCurrentSource(n1, n2, 1.0);
            Assert.That(_sim.CurrentSourceExists(csId), Is.True);

            // Update
            _sim.UpdateCurrentSource(csId, 2.0);
            Assert.That(_sim.GetCurrentSourceValue(csId), Is.EqualTo(2.0).Within(1e-6));

            // Remove
            _sim.RemoveCurrentSource(csId);
            Assert.That(_sim.CurrentSourceExists(csId), Is.False);
        }

        [Test]
        public void CurrentSource_GetValue_ReturnsCorrectCurrent()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var csId = _sim.AddCurrentSource(n1, n2, 0.5);
            Assert.That(_sim.GetCurrentSourceValue(csId), Is.EqualTo(0.5).Within(1e-6));
        }

        #endregion

        #region Capacitor Tests

        [Test]
        public void Capacitor_AddUpdateRemove_WorksCorrectly()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            // Add
            var capId = _sim.AddCapacitor(n1, n2, 1e-6);
            Assert.That(_sim.CapacitorExists(capId), Is.True);

            // Update
            _sim.UpdateCapacitor(capId, 2e-6);
            Assert.That(_sim.GetCapacitance(capId), Is.EqualTo(2e-6).Within(1e-12));

            // Remove
            _sim.RemoveCapacitor(capId);
            Assert.That(_sim.CapacitorExists(capId), Is.False);
        }

        [Test]
        public void Capacitor_GetCapacitance_ReturnsCorrectValue()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var capId = _sim.AddCapacitor(n1, n2, 4.7e-6);
            Assert.That(_sim.GetCapacitance(capId), Is.EqualTo(4.7e-6).Within(1e-12));
        }

        [Test]
        public void Capacitor_GetVoltage_ReturnsVoltageDifference()
        {
            // 10V -- R(100) -- N1 -- C -- GND
            // After steady state, capacitor charges to 10V
            var nPos = _sim.CreateNode();
            var n1 = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, n1, 100.0);
            var capId = _sim.AddCapacitor(n1, _sim.Ground, 1e-6);

            // Step many times to reach steady state (RC = 100 * 1e-6 = 0.1ms)
            for (int i = 0; i < 100; i++)
            {
                _sim.Step(0.001); // 1ms steps, total 100ms >> 5*RC
            }

            Assert.That(_sim.GetCapacitorVoltage(capId), Is.EqualTo(10.0).Within(1e-3));
        }

        #endregion

        #region Inductor Tests

        [Test]
        public void Inductor_AddUpdateRemove_WorksCorrectly()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            // Add
            var indId = _sim.AddInductor(n1, n2, 1e-3);
            Assert.That(_sim.InductorExists(indId), Is.True);

            // Update
            _sim.UpdateInductor(indId, 2e-3);
            Assert.That(_sim.GetInductance(indId), Is.EqualTo(2e-3).Within(1e-9));

            // Remove
            _sim.RemoveInductor(indId);
            Assert.That(_sim.InductorExists(indId), Is.False);
        }

        [Test]
        public void Inductor_GetInductance_ReturnsCorrectValue()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var indId = _sim.AddInductor(n1, n2, 10e-3);
            Assert.That(_sim.GetInductance(indId), Is.EqualTo(10e-3).Within(1e-9));
        }

        #endregion

        #region Diode Tests

        [Test]
        public void Diode_AddRemove_WorksCorrectly()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            // Add
            var diodeId = _sim.AddDiode(n1, n2);
            Assert.That(_sim.DiodeExists(diodeId), Is.True);

            // No update method for diodes

            // Remove
            _sim.RemoveDiode(diodeId);
            Assert.That(_sim.DiodeExists(diodeId), Is.False);
        }

        [Test]
        public void Diode_GetVoltage_ReturnsAnodeCathodeDifference()
        {
            // 10V -- R(1k) -- anode -- D -- cathode(GND)
            // Forward biased diode, Vd ~ 0.7V
            var nPos = _sim.CreateNode();
            var nAnode = _sim.CreateNode();

            _sim.AddVoltageSource(nPos, _sim.Ground, 10.0);
            _sim.AddResistor(nPos, nAnode, 1000.0);
            var diodeId = _sim.AddDiode(nAnode, _sim.Ground);

            _sim.Step(0.001);

            // Diode forward voltage should be around 0.6-0.7V
            var vd = _sim.GetDiodeVoltage(diodeId);
            Assert.That(vd, Is.GreaterThan(0.5).And.LessThan(0.8));
        }

        #endregion

        #region Transformer Tests

        [Test]
        public void Transformer_AddUpdateRemove_WorksCorrectly()
        {
            var p1 = _sim.CreateNode();
            var p2 = _sim.CreateNode();
            var s1 = _sim.CreateNode();
            var s2 = _sim.CreateNode();

            // Add
            var xfmrId = _sim.AddTransformer(p1, p2, s1, s2, 2.0);
            Assert.That(_sim.TransformerExists(xfmrId), Is.True);

            // Update
            _sim.UpdateTransformer(xfmrId, 3.0);
            Assert.That(_sim.GetTransformerRatio(xfmrId), Is.EqualTo(3.0).Within(1e-6));

            // Remove
            _sim.RemoveTransformer(xfmrId);
            Assert.That(_sim.TransformerExists(xfmrId), Is.False);
        }

        [Test]
        public void Transformer_GetRatio_ReturnsCorrectValue()
        {
            var p1 = _sim.CreateNode();
            var p2 = _sim.CreateNode();
            var s1 = _sim.CreateNode();
            var s2 = _sim.CreateNode();

            var xfmrId = _sim.AddTransformer(p1, p2, s1, s2, 0.5);
            Assert.That(_sim.GetTransformerRatio(xfmrId), Is.EqualTo(0.5).Within(1e-6));
        }

        [Test]
        public void Transformer_GetCurrents_ReturnsPrimaryAndSecondary()
        {
            // Primary: 10V -- p1 -- XFMR -- p2 -- GND
            // Secondary: s1 -- XFMR -- s2 -- R(100) -- s1 (loop)
            // Ratio 2:1, so secondary voltage = 5V, secondary current = 0.05A
            // Primary current = secondary current / ratio = 0.025A
            var p1 = _sim.CreateNode();
            var s1 = _sim.CreateNode();
            var s2 = _sim.CreateNode();

            _sim.AddVoltageSource(p1, _sim.Ground, 10.0);
            var xfmrId = _sim.AddTransformer(p1, _sim.Ground, s1, s2, 2.0);
            _sim.AddResistor(s1, s2, 100.0);

            _sim.Step(0.001);

            var (iPrimary, iSecondary) = _sim.GetTransformerCurrents(xfmrId);

            // Secondary voltage = 10V * 2 = 20V, current = 20V/100ohm = 0.2A
            // Primary current = 0.2A * 2 = 0.4A (power conservation)
            Assert.That(Math.Abs(iSecondary), Is.EqualTo(0.2).Within(1e-3));
            Assert.That(Math.Abs(iPrimary), Is.EqualTo(0.4).Within(1e-3));
        }

        #endregion

        #region Node Tests

        [Test]
        public void Node_CreateAndRemove_WorksCorrectly()
        {
            var n1 = _sim.CreateNode();
            Assert.That(_sim.NodeExists(n1), Is.True);

            _sim.RemoveNode(n1);
            Assert.That(_sim.NodeExists(n1), Is.False);
        }

        [Test]
        public void Node_RemoveAfterComponentRemoval_Succeeds()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var rId = _sim.AddResistor(n1, n2, 100.0);

            // Can't remove node with connected components
            Assert.Throws<NodeInUseException>(() => _sim.RemoveNode(n1));

            // Remove component first
            _sim.RemoveResistor(rId);

            // Now node removal should succeed
            Assert.DoesNotThrow(() => _sim.RemoveNode(n1));
            Assert.That(_sim.NodeExists(n1), Is.False);
        }

        #endregion

        #region Clear Tests

        [Test]
        public void Clear_RemovesAllComponentsAndNodes()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var rId = _sim.AddResistor(n1, n2, 100.0);
            var vsId = _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            var csId = _sim.AddCurrentSource(n1, n2, 1.0);
            var capId = _sim.AddCapacitor(n1, n2, 1e-6);

            _sim.Clear();

            Assert.That(_sim.NodeExists(n1), Is.False);
            Assert.That(_sim.NodeExists(n2), Is.False);
            Assert.That(_sim.ResistorExists(rId), Is.False);
            Assert.That(_sim.VoltageSourceExists(vsId), Is.False);
            Assert.That(_sim.CurrentSourceExists(csId), Is.False);
            Assert.That(_sim.CapacitorExists(capId), Is.False);

            // Ground should still exist
            Assert.That(_sim.NodeExists(_sim.Ground), Is.True);
        }

        #endregion

        #region Existence Check Tests

        [Test]
        public void ComponentExists_ReturnsTrueForExisting()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();
            var n3 = _sim.CreateNode();
            var n4 = _sim.CreateNode();

            var rId = _sim.AddResistor(n1, n2, 100.0);
            var vsId = _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            var csId = _sim.AddCurrentSource(n1, n2, 1.0);
            var capId = _sim.AddCapacitor(n1, n2, 1e-6);
            var indId = _sim.AddInductor(n1, n2, 1e-3);
            var diodeId = _sim.AddDiode(n1, n2);
            var xfmrId = _sim.AddTransformer(n1, n2, n3, n4, 1.0);

            Assert.That(_sim.ResistorExists(rId), Is.True);
            Assert.That(_sim.VoltageSourceExists(vsId), Is.True);
            Assert.That(_sim.CurrentSourceExists(csId), Is.True);
            Assert.That(_sim.CapacitorExists(capId), Is.True);
            Assert.That(_sim.InductorExists(indId), Is.True);
            Assert.That(_sim.DiodeExists(diodeId), Is.True);
            Assert.That(_sim.TransformerExists(xfmrId), Is.True);
        }

        [Test]
        public void ComponentExists_ReturnsFalseAfterRemoval()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();
            var n3 = _sim.CreateNode();
            var n4 = _sim.CreateNode();

            var rId = _sim.AddResistor(n1, n2, 100.0);
            var vsId = _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            var csId = _sim.AddCurrentSource(n1, n2, 1.0);
            var capId = _sim.AddCapacitor(n1, n2, 1e-6);
            var indId = _sim.AddInductor(n1, n2, 1e-3);
            var diodeId = _sim.AddDiode(n1, n2);
            var xfmrId = _sim.AddTransformer(n1, n2, n3, n4, 1.0);

            _sim.RemoveResistor(rId);
            _sim.RemoveVoltageSource(vsId);
            _sim.RemoveCurrentSource(csId);
            _sim.RemoveCapacitor(capId);
            _sim.RemoveInductor(indId);
            _sim.RemoveDiode(diodeId);
            _sim.RemoveTransformer(xfmrId);

            Assert.That(_sim.ResistorExists(rId), Is.False);
            Assert.That(_sim.VoltageSourceExists(vsId), Is.False);
            Assert.That(_sim.CurrentSourceExists(csId), Is.False);
            Assert.That(_sim.CapacitorExists(capId), Is.False);
            Assert.That(_sim.InductorExists(indId), Is.False);
            Assert.That(_sim.DiodeExists(diodeId), Is.False);
            Assert.That(_sim.TransformerExists(xfmrId), Is.False);
        }

        #endregion
    }
}
