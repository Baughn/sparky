using NUnit.Framework;
using Sparky.Voxel;

namespace Sparky.Tests.Game;

[TestFixture]
public class VoxelPositionHelperTests {
    // Face normals (same as VS uses)
    private static readonly (float X, float Y, float Z) North = (0, 0, -1);
    private static readonly (float X, float Y, float Z) South = (0, 0, 1);
    private static readonly (float X, float Y, float Z) East = (1, 0, 0);
    private static readonly (float X, float Y, float Z) West = (-1, 0, 0);
    private static readonly (float X, float Y, float Z) Up = (0, 1, 0);
    private static readonly (float X, float Y, float Z) Down = (0, -1, 0);

    #region GetClickedVoxel Tests

    [Test]
    public void GetClickedVoxel_CenterOfUpFace_ReturnsMiddleVoxel() {
        // Clicking the center of the top face of voxel (8,7,8)
        var (x, y, z) = VoxelPositionHelper.GetClickedVoxel(
            0.5, 0.5, 0.5,  // Center of block
            Up.X, Up.Y, Up.Z);

        Assert.That(x, Is.EqualTo(8));
        Assert.That(y, Is.EqualTo(7)); // Offset inward from up face
        Assert.That(z, Is.EqualTo(8));
    }

    [Test]
    public void GetClickedVoxel_EastFaceOfVoxel5_ReturnsVoxel5() {
        // Click east face of voxel at x=5: hit at x=6/16=0.375
        var (x, y, z) = VoxelPositionHelper.GetClickedVoxel(
            0.375, 0.0625, 0.5,
            East.X, East.Y, East.Z);

        Assert.That(x, Is.EqualTo(5)); // Should return the voxel behind the face
    }

    [Test]
    public void GetClickedVoxel_WestFaceOfVoxel5_ReturnsVoxel5() {
        // Click west face of voxel at x=5: hit at x=5/16=0.3125
        var (x, y, z) = VoxelPositionHelper.GetClickedVoxel(
            0.3125, 0.0625, 0.5,
            West.X, West.Y, West.Z);

        Assert.That(x, Is.EqualTo(5)); // Should return the voxel behind the face
    }

    [Test]
    public void GetClickedVoxel_NorthFaceOfVoxel8_ReturnsVoxel8() {
        // Click north face of voxel at z=8: hit at z=8/16=0.5
        var (x, y, z) = VoxelPositionHelper.GetClickedVoxel(
            0.5, 0.0625, 0.5,
            North.X, North.Y, North.Z);

        Assert.That(z, Is.EqualTo(8));
    }

    [Test]
    public void GetClickedVoxel_SouthFaceOfVoxel8_ReturnsVoxel8() {
        // Click south face of voxel at z=8: hit at z=9/16=0.5625
        var (x, y, z) = VoxelPositionHelper.GetClickedVoxel(
            0.5, 0.0625, 0.5625,
            South.X, South.Y, South.Z);

        Assert.That(z, Is.EqualTo(8));
    }

    [Test]
    public void GetClickedVoxel_DownFaceOfVoxel5_ReturnsVoxel5() {
        // Click down face of voxel at y=5: hit at y=5/16=0.3125
        var (x, y, z) = VoxelPositionHelper.GetClickedVoxel(
            0.5, 0.3125, 0.5,
            Down.X, Down.Y, Down.Z);

        Assert.That(y, Is.EqualTo(5));
    }

    [Test]
    public void GetClickedVoxel_UpFaceOfVoxel5_ReturnsVoxel5() {
        // Click up face of voxel at y=5: hit at y=6/16=0.375
        var (x, y, z) = VoxelPositionHelper.GetClickedVoxel(
            0.5, 0.375, 0.5,
            Up.X, Up.Y, Up.Z);

        Assert.That(y, Is.EqualTo(5));
    }

    [Test]
    public void GetClickedVoxel_CornerVoxel0_ReturnsVoxel0() {
        // Click the corner voxel at (0,0,0) from various faces
        var (x1, y1, z1) = VoxelPositionHelper.GetClickedVoxel(
            0.0, 0.0625, 0.0625,
            West.X, West.Y, West.Z);
        Assert.That(x1, Is.EqualTo(0));

        var (x2, y2, z2) = VoxelPositionHelper.GetClickedVoxel(
            0.0625, 0.0, 0.0625,
            Down.X, Down.Y, Down.Z);
        Assert.That(y2, Is.EqualTo(0));

        var (x3, y3, z3) = VoxelPositionHelper.GetClickedVoxel(
            0.0625, 0.0625, 0.0,
            North.X, North.Y, North.Z);
        Assert.That(z3, Is.EqualTo(0));
    }

    [Test]
    public void GetClickedVoxel_CornerVoxel15_ReturnsVoxel15() {
        // Click the corner voxel at (15,15,15) from various faces
        var (x1, y1, z1) = VoxelPositionHelper.GetClickedVoxel(
            1.0, 0.9375, 0.9375,
            East.X, East.Y, East.Z);
        Assert.That(x1, Is.EqualTo(15));

        var (x2, y2, z2) = VoxelPositionHelper.GetClickedVoxel(
            0.9375, 1.0, 0.9375,
            Up.X, Up.Y, Up.Z);
        Assert.That(y2, Is.EqualTo(15));

        var (x3, y3, z3) = VoxelPositionHelper.GetClickedVoxel(
            0.9375, 0.9375, 1.0,
            South.X, South.Y, South.Z);
        Assert.That(z3, Is.EqualTo(15));
    }

    #endregion

    #region GetAdjacentVoxel Tests (no overflow)

    [Test]
    public void GetAdjacentVoxel_EastFaceOfVoxel5_ReturnsVoxel6() {
        var (x, y, z) = VoxelPositionHelper.GetAdjacentVoxel(
            0.375, 0.0625, 0.5,  // East face of voxel 5
            East.X, East.Y, East.Z);

        Assert.That(x, Is.EqualTo(6));
    }

    [Test]
    public void GetAdjacentVoxel_WestFaceOfVoxel5_ReturnsVoxel4() {
        var (x, y, z) = VoxelPositionHelper.GetAdjacentVoxel(
            0.3125, 0.0625, 0.5,  // West face of voxel 5
            West.X, West.Y, West.Z);

        Assert.That(x, Is.EqualTo(4));
    }

    [Test]
    public void GetAdjacentVoxel_NorthFaceOfVoxel8_ReturnsVoxel7() {
        var (x, y, z) = VoxelPositionHelper.GetAdjacentVoxel(
            0.5, 0.0625, 0.5,  // North face of voxel 8
            North.X, North.Y, North.Z);

        Assert.That(z, Is.EqualTo(7));
    }

    [Test]
    public void GetAdjacentVoxel_SouthFaceOfVoxel8_ReturnsVoxel9() {
        var (x, y, z) = VoxelPositionHelper.GetAdjacentVoxel(
            0.5, 0.0625, 0.5625,  // South face of voxel 8
            South.X, South.Y, South.Z);

        Assert.That(z, Is.EqualTo(9));
    }

    [Test]
    public void GetAdjacentVoxel_UpFaceOfVoxel5_ReturnsVoxel6() {
        var (x, y, z) = VoxelPositionHelper.GetAdjacentVoxel(
            0.5, 0.375, 0.5,  // Up face of voxel 5
            Up.X, Up.Y, Up.Z);

        Assert.That(y, Is.EqualTo(6));
    }

    [Test]
    public void GetAdjacentVoxel_DownFaceOfVoxel5_ReturnsVoxel4() {
        var (x, y, z) = VoxelPositionHelper.GetAdjacentVoxel(
            0.5, 0.3125, 0.5,  // Down face of voxel 5
            Down.X, Down.Y, Down.Z);

        Assert.That(y, Is.EqualTo(4));
    }

    #endregion

    #region GetAdjacentVoxelWithOverflow Tests (boundary crossings)

    [Test]
    public void GetAdjacentVoxelWithOverflow_EastFaceOfVoxel15_OverflowsToNextBlock() {
        // Click east face of voxel at x=15: hit at x=16/16=1.0
        var (x, y, z, outside) = VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            1.0, 0.0625, 0.5,
            East.X, East.Y, East.Z);

        Assert.That(outside, Is.True, "Should detect overflow to adjacent block");
        Assert.That(x, Is.EqualTo(0), "X should wrap to 0 for adjacent block");
    }

    [Test]
    public void GetAdjacentVoxelWithOverflow_WestFaceOfVoxel0_OverflowsToNextBlock() {
        // Click west face of voxel at x=0: hit at x=0/16=0.0
        var (x, y, z, outside) = VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            0.0, 0.0625, 0.5,
            West.X, West.Y, West.Z);

        Assert.That(outside, Is.True, "Should detect overflow to adjacent block");
        Assert.That(x, Is.EqualTo(15), "X should wrap to 15 for adjacent block");
    }

    [Test]
    public void GetAdjacentVoxelWithOverflow_NorthFaceOfVoxel0_OverflowsToNextBlock() {
        var (x, y, z, outside) = VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            0.5, 0.0625, 0.0,
            North.X, North.Y, North.Z);

        Assert.That(outside, Is.True, "Should detect overflow to adjacent block");
        Assert.That(z, Is.EqualTo(15), "Z should wrap to 15 for adjacent block");
    }

    [Test]
    public void GetAdjacentVoxelWithOverflow_SouthFaceOfVoxel15_OverflowsToNextBlock() {
        var (x, y, z, outside) = VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            0.5, 0.0625, 1.0,
            South.X, South.Y, South.Z);

        Assert.That(outside, Is.True, "Should detect overflow to adjacent block");
        Assert.That(z, Is.EqualTo(0), "Z should wrap to 0 for adjacent block");
    }

    [Test]
    public void GetAdjacentVoxelWithOverflow_UpFaceOfVoxel15_OverflowsToNextBlock() {
        var (x, y, z, outside) = VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            0.5, 1.0, 0.5,
            Up.X, Up.Y, Up.Z);

        Assert.That(outside, Is.True, "Should detect overflow to adjacent block");
        Assert.That(y, Is.EqualTo(0), "Y should wrap to 0 for adjacent block");
    }

    [Test]
    public void GetAdjacentVoxelWithOverflow_DownFaceOfVoxel0_OverflowsToNextBlock() {
        var (x, y, z, outside) = VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            0.5, 0.0, 0.5,
            Down.X, Down.Y, Down.Z);

        Assert.That(outside, Is.True, "Should detect overflow to adjacent block");
        Assert.That(y, Is.EqualTo(15), "Y should wrap to 15 for adjacent block");
    }

    [Test]
    public void GetAdjacentVoxelWithOverflow_InteriorVoxel_NoOverflow() {
        // Click east face of voxel at x=5, which should place at x=6 (no overflow)
        var (x, y, z, outside) = VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            0.375, 0.0625, 0.5,
            East.X, East.Y, East.Z);

        Assert.That(outside, Is.False, "Interior voxel should not overflow");
        Assert.That(x, Is.EqualTo(6));
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public void GetClickedVoxel_ExactlyOnVoxelBoundary_ReturnsCorrectVoxel() {
        // Hit exactly at x = 0.5 (voxel boundary between 7 and 8)
        // Clicking east face should return voxel 7 (behind the boundary)
        var (x, _, _) = VoxelPositionHelper.GetClickedVoxel(
            0.5, 0.5, 0.5,
            East.X, East.Y, East.Z);

        Assert.That(x, Is.EqualTo(7));
    }

    [Test]
    public void GetAdjacentVoxel_ExactlyOnVoxelBoundary_ReturnsCorrectVoxel() {
        // Hit exactly at x = 0.5 (voxel boundary between 7 and 8)
        // Clicking east face should place at voxel 8 (in front of the boundary)
        var (x, _, _) = VoxelPositionHelper.GetAdjacentVoxel(
            0.5, 0.5, 0.5,
            East.X, East.Y, East.Z);

        Assert.That(x, Is.EqualTo(8));
    }

    [Test]
    public void GetClickedVoxel_AllFacesOfSameVoxel_ReturnsSameVoxel() {
        // Voxel at (8, 8, 8) - click each face and verify we get the same voxel
        // Center of voxel is at (8.5/16, 8.5/16, 8.5/16) = (0.53125, 0.53125, 0.53125)

        var faces = new[] { North, South, East, West, Up, Down };
        var expected = (8, 8, 8);

        foreach (var face in faces) {
            // Adjust hit position to be on the appropriate face
            double hitX = 0.53125 + face.X * 0.03125;  // Center + half voxel width in face direction
            double hitY = 0.53125 + face.Y * 0.03125;
            double hitZ = 0.53125 + face.Z * 0.03125;

            var result = VoxelPositionHelper.GetClickedVoxel(hitX, hitY, hitZ, face.X, face.Y, face.Z);
            Assert.That(result, Is.EqualTo(expected), $"Failed for face ({face.X}, {face.Y}, {face.Z})");
        }
    }

    #endregion

    #region Solid Block Click Tests (placement in adjacent block)

    // These tests simulate the scenario where you click a solid block's face
    // to place a voxel in an adjacent circuit block. The voxel should appear
    // on the face of the circuit block that touches the solid block.

    /// <summary>
    /// Simulates GetVoxelPositionOnFace logic from ItemWireTool.
    /// Maps hit position from solid block to adjacent circuit block's coordinate space.
    /// </summary>
    private static (int X, int Y, int Z) GetVoxelPositionOnFace(
        double hitX, double hitY, double hitZ,
        float faceNormalX, float faceNormalY, float faceNormalZ) {
        // Determine which axis the face is on
        bool isXAxis = Math.Abs(faceNormalX) > 0.5f;
        bool isYAxis = Math.Abs(faceNormalY) > 0.5f;
        bool isZAxis = Math.Abs(faceNormalZ) > 0.5f;

        // Map hit coordinates to the adjacent block's space
        double adjX = isXAxis ? (faceNormalX > 0 ? 0.0 : 1.0) : hitX;
        double adjY = isYAxis ? (faceNormalY > 0 ? 0.0 : 1.0) : hitY;
        double adjZ = isZAxis ? (faceNormalZ > 0 ? 0.0 : 1.0) : hitZ;

        // Use GetClickedVoxel with the opposite face normal
        return VoxelPositionHelper.GetClickedVoxel(
            adjX, adjY, adjZ,
            -faceNormalX, -faceNormalY, -faceNormalZ);
    }

    [Test]
    public void SolidBlockClick_EastFace_PlacesVoxelAtX0() {
        // Click the east face of a solid block at hit position (1.0, 0.5, 0.5)
        // Should place voxel at x=0 in the adjacent (eastern) circuit block
        var (x, y, z) = GetVoxelPositionOnFace(
            1.0, 0.5, 0.5,  // Hit on east face (but these coords don't matter for X)
            East.X, East.Y, East.Z);

        Assert.That(x, Is.EqualTo(0), "Voxel should be at x=0 (west face of adjacent block)");
        Assert.That(y, Is.EqualTo(8));
        Assert.That(z, Is.EqualTo(8));
    }

    [Test]
    public void SolidBlockClick_WestFace_PlacesVoxelAtX15() {
        // Click the west face of a solid block
        // Should place voxel at x=15 in the adjacent (western) circuit block
        var (x, y, z) = GetVoxelPositionOnFace(
            0.0, 0.5, 0.5,
            West.X, West.Y, West.Z);

        Assert.That(x, Is.EqualTo(15), "Voxel should be at x=15 (east face of adjacent block)");
    }

    [Test]
    public void SolidBlockClick_NorthFace_PlacesVoxelAtZ15() {
        // Click the north face of a solid block
        // Should place voxel at z=15 in the adjacent (northern) circuit block
        var (x, y, z) = GetVoxelPositionOnFace(
            0.5, 0.5, 0.0,
            North.X, North.Y, North.Z);

        Assert.That(z, Is.EqualTo(15), "Voxel should be at z=15 (south face of adjacent block)");
    }

    [Test]
    public void SolidBlockClick_SouthFace_PlacesVoxelAtZ0() {
        // Click the south face of a solid block
        // Should place voxel at z=0 in the adjacent (southern) circuit block
        var (x, y, z) = GetVoxelPositionOnFace(
            0.5, 0.5, 1.0,
            South.X, South.Y, South.Z);

        Assert.That(z, Is.EqualTo(0), "Voxel should be at z=0 (north face of adjacent block)");
    }

    [Test]
    public void SolidBlockClick_UpFace_PlacesVoxelAtY0() {
        // Click the up face of a solid block
        // Should place voxel at y=0 in the adjacent (upper) circuit block
        var (x, y, z) = GetVoxelPositionOnFace(
            0.5, 1.0, 0.5,
            Up.X, Up.Y, Up.Z);

        Assert.That(y, Is.EqualTo(0), "Voxel should be at y=0 (bottom face of adjacent block)");
    }

    [Test]
    public void SolidBlockClick_DownFace_PlacesVoxelAtY15() {
        // Click the down face of a solid block
        // Should place voxel at y=15 in the adjacent (lower) circuit block
        var (x, y, z) = GetVoxelPositionOnFace(
            0.5, 0.0, 0.5,
            Down.X, Down.Y, Down.Z);

        Assert.That(y, Is.EqualTo(15), "Voxel should be at y=15 (top face of adjacent block)");
    }

    [Test]
    public void SolidBlockClick_PreservesOtherAxes() {
        // Click near a corner of the east face
        // Y and Z should be preserved from the hit position
        var (x, y, z) = GetVoxelPositionOnFace(
            1.0, 0.25, 0.75,  // hit at y=4, z=12 in voxel coords
            East.X, East.Y, East.Z);

        Assert.That(x, Is.EqualTo(0));
        Assert.That(y, Is.EqualTo(4), "Y should be preserved from hit position");
        Assert.That(z, Is.EqualTo(12), "Z should be preserved from hit position");
    }

    #endregion
}
