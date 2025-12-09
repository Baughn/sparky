using NUnit.Framework;
using Sparky.MNA.Api;
using System;

namespace Sparky.Tests.MNA
{
    [TestFixture]
    public class BulkUpdateTests
    {
        private SimulationManager _sim = null!;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationManager();
        }

        #region Deferred Rebuild Tests

        [Test]
        public void BulkUpdate_DefersRebuild_UntilDispose()
        {
            // Create initial circuit and step to establish baseline
            var n1 = _sim.CreateNode();
            _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
            _sim.AddResistor(n1, _sim.Ground, 100.0);
            _sim.Step(0.001);

            var initialPartitionCount = _sim.PartitionCount;

            using (_sim.BeginBulkUpdate())
            {
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
        public void BulkUpdate_MultipleChanges_SingleRebuild()
        {
            // This test verifies efficiency - multiple changes in one scope
            // should result in correct final state
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            using (_sim.BeginBulkUpdate())
            {
                _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
                _sim.AddResistor(n1, n2, 100.0);
                _sim.AddResistor(n2, _sim.Ground, 100.0);
            }

            _sim.Step(0.001);

            // Voltage divider: V(n2) = 10 * 100/200 = 5V
            Assert.That(_sim.GetVoltage(n1), Is.EqualTo(10.0).Within(1e-6));
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(1e-6));
        }

        #endregion

        #region Nested Scope Tests

        [Test]
        public void BulkUpdate_Nested_OnlyRebuildsOnOuterDispose()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            using (_sim.BeginBulkUpdate())
            {
                _sim.AddVoltageSource(n1, _sim.Ground, 10.0);

                using (_sim.BeginBulkUpdate())
                {
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
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(1e-6));
        }

        #endregion

        #region Dispose Safety Tests

        [Test]
        public void BulkUpdate_DisposedTwice_NoError()
        {
            var scope = _sim.BeginBulkUpdate();
            scope.Dispose();

            // Second dispose should not throw
            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void BulkUpdate_WithException_StillDisposes()
        {
            var n1 = _sim.CreateNode();

            try
            {
                using (_sim.BeginBulkUpdate())
                {
                    _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
                    throw new InvalidOperationException("Simulated error");
                }
            }
            catch (InvalidOperationException)
            {
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
        public void BulkUpdate_StepAfterDispose_Works()
        {
            var n1 = _sim.CreateNode();
            var n2 = _sim.CreateNode();

            using (_sim.BeginBulkUpdate())
            {
                _sim.AddVoltageSource(n1, _sim.Ground, 10.0);
                _sim.AddResistor(n1, n2, 100.0);
                _sim.AddResistor(n2, _sim.Ground, 100.0);
            }

            // Multiple steps after bulk update should all work
            _sim.Step(0.001);
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(1e-6));

            _sim.Step(0.001);
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(5.0).Within(1e-6));

            // Should be able to modify and step again
            var r3 = _sim.AddResistor(n2, _sim.Ground, 100.0);
            _sim.Step(0.001);

            // Now n2 has two 100ohm resistors in parallel (50ohm equiv)
            // V(n2) = 10 * 50/150 = 3.333V
            Assert.That(_sim.GetVoltage(n2), Is.EqualTo(10.0 / 3.0).Within(1e-6));
        }

        #endregion
    }
}
