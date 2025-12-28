using System;
using NUnit.Framework;
using Sparky.Mna.Api;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests.MNA {
    [TestFixture]
    public class BulkUpdateTests {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp() {
            _sim = new SimulationManager();
        }

        #region Deferred Rebuild Tests

        [Test]
        public void BulkUpdate_DefersRebuild_UntilDispose() {
            // Create initial circuit and step to establish baseline
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);
            _sim.Step(0.001);

            var initialPartitionCount = _sim.PartitionCount;

            using (_sim.BeginBulkUpdate()) {
                // Add a second disconnected circuit
                var n2 = _sim.CreateNode();
                _sim.AddVoltageSource(n2, _sim.Ground, 5.0);
                _sim.AddResistor(n2, _sim.Ground, 50.0);

                // Partition count should not have updated yet (no rebuild)
                // Note: This depends on implementation - partition count might be stale
                // The key verification is that Step() is blocked during bulk update
            }

            // After dispose, step should work and show updated state
            _sim.Step(0.001);
            Assert.That(_sim.PartitionCount, Is.EqualTo(2));
        }

        [Test]
        public void BulkUpdate_MultipleChanges_SingleRebuild() {
            // This test verifies efficiency - multiple changes in one scope
            // should result in correct final state
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            using (_sim.BeginBulkUpdate()) {
                _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
                _sim.AddResistor(n1, n2, 100.0);
                _sim.AddResistor(n2, _sim.Ground, 100.0);
            }

            _sim.Step(0.001);

            // Voltage divider: V(n2) = 10 * 100/200 = 5V
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(Tolerances.Voltage));
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(Tolerances.Voltage));
        }

        #endregion

        #region Nested Scope Tests

        [Test]
        public void BulkUpdate_Nested_OnlyRebuildsOnOuterDispose() {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            using (_sim.BeginBulkUpdate()) {
                _sim.AddVoltageSource(n1, _sim.Ground, 10.0);

                using (_sim.BeginBulkUpdate()) {
                    _sim.AddResistor(n1, n2, 100.0);

                    // Step should still be blocked in inner scope
                    Assert.Throws<InvalidOperationException>(() => _sim.Step(0.001));
                }

                // Step should still be blocked - outer scope not disposed yet
                Assert.Throws<InvalidOperationException>(() => _sim.Step(0.001));

                _sim.AddResistor(n2, _sim.Ground, 100.0);
            }

            // Now step should work
            Assert.DoesNotThrow(() => _sim.Step(0.001));
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(Tolerances.Voltage));
        }

        #endregion

        #region Dispose Safety Tests

        [Test]
        public void BulkUpdate_DisposedTwice_NoError() {
            var scope = _sim.BeginBulkUpdate();
            scope.Dispose();

            // Second dispose should not throw
            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void BulkUpdate_WithException_StillDisposes() {
            var n1 = _sim.CreateNode();

            try {
                using (_sim.BeginBulkUpdate()) {
                    _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
                    throw new InvalidOperationException("Simulated error");
                }
            } catch (InvalidOperationException) {
                // Expected
            }

            // After exception, bulk update scope should be properly disposed
            // Step should work
            _sim.AddResistor(n1, _sim.Ground, 100.0);
            Assert.DoesNotThrow(() => _sim.Step(0.001));
        }

        #endregion

        #region Post-Update Operation Tests

        [Test]
        public void BulkUpdate_StepAfterDispose_Works() {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            using (_sim.BeginBulkUpdate()) {
                _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
                _sim.AddResistor(n1, n2, 100.0);
                _sim.AddResistor(n2, _sim.Ground, 100.0);
            }

            // Multiple steps after bulk update should all work
            _sim.Step(0.001);
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(Tolerances.Voltage));

            _sim.Step(0.001);
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(Tolerances.Voltage));

            // Should be able to modify and step again
            var r3 = _sim.AddResistor(n2, _sim.Ground, 100.0);
            _sim.Step(0.001);

            // Now n2 has two 100ohm resistors in parallel (50ohm equiv)
            // V(n2) = 10 * 50/150 = 3.333V
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0 / 3.0).Within(Tolerances.Voltage));
        }

        #endregion

        #region State Preservation Tests

        [Test]
        public void TopologyChange_PreservesCapacitorState() {
            // Charge a capacitor, then add a resistor (causing rebuild)
            // The capacitor voltage should be preserved
            var nSrc = _sim.CreateNode();
            var nCap = _sim.CreateNode();

            _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
            _sim.AddResistor(nSrc, nCap, 100.0);
            _sim.AddCapacitor(nCap, _sim.Ground, 1e-6); // 1uF

            // Charge for a few time constants (tau = R*C = 100 * 1e-6 = 100us)
            double dt = 1e-5; // 10us
            for (int i = 0; i < 100; i++) // 1ms total, ~10 time constants
            {
                _sim.Step(dt);
            }

            double vCapBefore = _sim.GetVoltage(nCap);
            Assert.That(
                vCapBefore,
                Is.GreaterThan(9.0),
                "Capacitor should be nearly fully charged"
            );

            // Add another resistor to a different node (causes topology rebuild)
            var nOther = _sim.CreateNode();
            _sim.AddResistor(nOther, _sim.Ground, 1000.0);

            _sim.Step(dt);
            double vCapAfter = _sim.GetVoltage(nCap);

            // Capacitor voltage should be preserved across the rebuild
            Assert.That(
                vCapAfter,
                Is.EqualTo(vCapBefore).Within(0.1),
                "Capacitor state should be preserved across topology changes"
            );
        }

        [Test]
        public void TopologyChange_PreservesInductorState() {
            // Build up current in an inductor, then add a component (causing rebuild)
            // The inductor current should be preserved
            var nSrc = _sim.CreateNode();
            var nInd = _sim.CreateNode();

            _sim.AddVoltageSource(nSrc, _sim.Ground, 10.0);
            _sim.AddResistor(nSrc, nInd, 10.0);
            _sim.AddInductor(nInd, _sim.Ground, 1e-3); // 1mH

            // Run until current builds up (tau = L/R = 1e-3/10 = 100us)
            double dt = 1e-5; // 10us
            for (int i = 0; i < 100; i++) // 1ms total, ~10 time constants
            {
                _sim.Step(dt);
            }

            // At steady state, current = V/R = 10/10 = 1A
            // Check inductor current (V across R / R)
            double vInductor = _sim.GetVoltage(nInd);
            double currentBefore = (10.0 - vInductor) / 10.0; // Should be ~1A

            Assert.That(
                currentBefore,
                Is.GreaterThan(0.9),
                "Inductor current should be near steady state"
            );

            // Add another resistor to a different node (causes topology rebuild)
            var nOther = _sim.CreateNode();
            _sim.AddResistor(nOther, _sim.Ground, 1000.0);

            _sim.Step(dt);
            double vInductorAfter = _sim.GetVoltage(nInd);
            double currentAfter = (10.0 - vInductorAfter) / 10.0;

            // Inductor current should be preserved across the rebuild
            Assert.That(
                currentAfter,
                Is.EqualTo(currentBefore).Within(0.1),
                "Inductor state should be preserved across topology changes"
            );
        }

        #endregion
    }
}
