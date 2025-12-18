using System.Collections.Generic;
using NUnit.Framework;
using Sparky.VSIntegration;

namespace Sparky.Tests.Mod;

[TestFixture]
public class BEBehaviorCircuitSelectionBoxesTests {
    [Test]
    public void BuildSelectionBoxes_Empty_ReturnsEmpty() {
        var boxes = BEBehaviorCircuit.BuildSelectionBoxes(new List<uint>());

        Assert.That(boxes, Is.Not.Null);
        Assert.That(boxes, Is.Empty);
    }

    [Test]
    public void BuildSelectionBoxes_SingleVoxel_MapsToNormalizedBounds() {
        var cuboid = BEBehaviorCircuit.ToUint(2, 3, 4, 3, 4, 5, 0);
        var boxes = BEBehaviorCircuit.BuildSelectionBoxes(new List<uint> { cuboid });

        Assert.That(boxes.Length, Is.EqualTo(1));

        var box = boxes[0];
        Assert.That(box.X1, Is.EqualTo(2f / 16f).Within(1e-6f));
        Assert.That(box.Y1, Is.EqualTo(3f / 16f).Within(1e-6f));
        Assert.That(box.Z1, Is.EqualTo(4f / 16f).Within(1e-6f));
        Assert.That(box.X2, Is.EqualTo(3f / 16f).Within(1e-6f));
        Assert.That(box.Y2, Is.EqualTo(4f / 16f).Within(1e-6f));
        Assert.That(box.Z2, Is.EqualTo(5f / 16f).Within(1e-6f));
    }
}
