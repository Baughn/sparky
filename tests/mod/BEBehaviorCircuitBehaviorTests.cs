using NUnit.Framework;
using Sparky.VSIntegration;
using Vintagestory.API.Common;

namespace Sparky.Tests.Mod;

[TestFixture]
public class BEBehaviorCircuitBehaviorTests {
    [Test]
    public void BlockSupportsCircuitBehavior_WhenBehaviorPresent_ReturnsTrue() {
        var block = new Block {
            BlockId = 123,
            BlockEntityBehaviors = new[]
            {
                new BlockEntityBehaviorType { Name = BEBehaviorCircuit.BehaviorName }
            }
        };

        Assert.That(BEBehaviorCircuit.BlockSupportsCircuitBehavior(block), Is.True);
    }

    [Test]
    public void BlockSupportsCircuitBehavior_WhenBehaviorMissing_ReturnsFalse() {
        var block = new Block {
            BlockId = 123,
            BlockEntityBehaviors = new[]
            {
                new BlockEntityBehaviorType { Name = "other:behavior" }
            }
        };

        Assert.That(BEBehaviorCircuit.BlockSupportsCircuitBehavior(block), Is.False);
    }
}
