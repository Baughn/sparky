using System;
using NUnit.Framework;
using Sparky.MNA.Api;

namespace Sparky.Tests.MNA {
    [TestFixture]
    public class ApiExceptionTests {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp() {
            _sim = new SimulationManager();
        }

        #region Invalid Node Tests

        [Test]
        public void AddResistor_WithInvalidNode_ThrowsInvalidNodeException() {
            var validNode = _sim.CreateNode();
            var invalidNode = new NodeId(999);

            var ex = Assert.Throws<InvalidNodeException>(() =>
                _sim.AddResistor(validNode, invalidNode, 100.0)
            );

            Assert.That(ex!.NodeId, Is.EqualTo(invalidNode));
            Assert.That(ex.Message, Does.Contain("999"));
        }

        [Test]
        public void GetVoltage_InvalidNode_ThrowsInvalidNodeException() {
            var invalidNode = new NodeId(999);
            _sim.Step(0.1); // Need to step to trigger the check

            var ex = Assert.Throws<InvalidNodeException>(() => _sim.GetVoltage(invalidNode));

            Assert.That(ex!.NodeId, Is.EqualTo(invalidNode));
        }

        #endregion

        #region Invalid Parameter Tests - Resistance

        [Test]
        public void AddResistor_WithNegativeResistance_ThrowsInvalidParameterException() {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var ex = Assert.Throws<InvalidParameterException>(() =>
                _sim.AddResistor(n1, n2, -10.0)
            );

            Assert.That(ex!.ParameterName, Is.EqualTo("resistance"));
            Assert.That(ex.Value, Is.EqualTo(-10.0));
            Assert.That(ex.Constraint, Does.Contain("positive"));
        }

        [Test]
        public void AddResistor_WithZeroResistance_ThrowsInvalidParameterException() {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var ex = Assert.Throws<InvalidParameterException>(() => _sim.AddResistor(n1, n2, 0.0));

            Assert.That(ex!.ParameterName, Is.EqualTo("resistance"));
            Assert.That(ex.Value, Is.EqualTo(0.0));
        }

        #endregion

        #region Invalid Parameter Tests - Capacitance

        [Test]
        public void AddCapacitor_WithNegativeCapacitance_ThrowsInvalidParameterException() {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var ex = Assert.Throws<InvalidParameterException>(() =>
                _sim.AddCapacitor(n1, n2, -1e-6)
            );

            Assert.That(ex!.ParameterName, Is.EqualTo("capacitance"));
            Assert.That(ex.Value, Is.EqualTo(-1e-6));
            Assert.That(ex.Constraint, Does.Contain("positive"));
        }

        #endregion

        #region Invalid Parameter Tests - Inductance

        [Test]
        public void AddInductor_WithNegativeInductance_ThrowsInvalidParameterException() {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            var ex = Assert.Throws<InvalidParameterException>(() =>
                _sim.AddInductor(n1, n2, -1e-3)
            );

            Assert.That(ex!.ParameterName, Is.EqualTo("inductance"));
            Assert.That(ex.Value, Is.EqualTo(-1e-3));
            Assert.That(ex.Constraint, Does.Contain("positive"));
        }

        #endregion

        #region Invalid Parameter Tests - Transformer Ratio

        [Test]
        public void AddTransformer_WithZeroRatio_ThrowsInvalidParameterException() {
            var p1 = _sim.CreateNode();
            var p2 = _sim.CreateNode();
            var s1 = _sim.CreateNode();
            var s2 = _sim.CreateNode();

            var ex = Assert.Throws<InvalidParameterException>(() =>
                _sim.AddTransformer(p1, p2, s1, s2, 0.0)
            );

            Assert.That(ex!.ParameterName, Is.EqualTo("ratio"));
            Assert.That(ex.Value, Is.EqualTo(0.0));
        }

        [Test]
        public void AddTransformer_WithNegativeRatio_ThrowsInvalidParameterException() {
            var p1 = _sim.CreateNode();
            var p2 = _sim.CreateNode();
            var s1 = _sim.CreateNode();
            var s2 = _sim.CreateNode();

            var ex = Assert.Throws<InvalidParameterException>(() =>
                _sim.AddTransformer(p1, p2, s1, s2, -1.0)
            );

            Assert.That(ex!.ParameterName, Is.EqualTo("ratio"));
            Assert.That(ex.Value, Is.EqualTo(-1.0));
        }

        #endregion

        #region Invalid Component Tests

        [Test]
        public void UpdateResistor_WithInvalidId_ThrowsInvalidComponentException() {
            var invalidId = new ResistorId(999);

            var ex = Assert.Throws<InvalidComponentException>(() =>
                _sim.UpdateResistor(invalidId, 100.0)
            );

            Assert.That(ex!.ComponentType, Is.EqualTo("Resistor"));
            Assert.That(ex.ComponentId, Is.EqualTo(999));
        }

        [Test]
        public void RemoveResistor_WithInvalidId_ThrowsInvalidComponentException() {
            var invalidId = new ResistorId(999);

            var ex = Assert.Throws<InvalidComponentException>(() => _sim.RemoveResistor(invalidId));

            Assert.That(ex!.ComponentType, Is.EqualTo("Resistor"));
            Assert.That(ex.ComponentId, Is.EqualTo(999));
        }

        #endregion

        #region Node Removal Tests

        [Test]
        public void RemoveNode_WithConnections_ThrowsNodeInUseException() {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();
            _sim.AddResistor(n1, n2, 100.0);

            var ex = Assert.Throws<NodeInUseException>(() => _sim.RemoveNode(n1));

            Assert.That(ex!.NodeId, Is.EqualTo(n1));
            Assert.That(ex.ConnectionCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveNode_Ground_ThrowsInvalidOperationException() {
            var groundNode = _sim.Ground;

            var ex = Assert.Throws<InvalidOperationException>(() => _sim.RemoveNode(groundNode));

            Assert.That(ex!.Message, Does.Contain("ground"));
        }

        #endregion

        #region Bulk Update Tests

        [Test]
        public void Step_DuringBulkUpdate_ThrowsInvalidOperationException() {
            using (_sim.BeginBulkUpdate()) {
                var ex = Assert.Throws<InvalidOperationException>(() => _sim.Step(0.1));

                Assert.That(ex!.Message, Does.Contain("bulk update"));
            }
        }

        #endregion
    }
}
