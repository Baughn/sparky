using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;

namespace Sparky.Tests.Game.CableLaying;

/// <summary>
/// Tests for snap position finding when placing cables on surfaces.
/// These tests validate that snap positions properly account for cable cross-section size.
/// </summary>
[TestFixture]
public class SnapPositionTests {
    // 16³ cube of insulation floating in space
    private const int CubeOrigin = 50;
    private const int CubeSize = 16;
    private const int CubeEnd = CubeOrigin + CubeSize - 1; // 65 (inclusive)

    private MockWorldVoxelCache _cache = null!;

    [SetUp]
    public void SetUp() {
        // Create cache centered on the cube
        var center = new VoxelPos(CubeOrigin + CubeSize / 2, CubeOrigin + CubeSize / 2, CubeOrigin + CubeSize / 2);
        _cache = new MockWorldVoxelCache(center);

        // Create 16³ cube of insulation
        CreateCube(new VoxelPos(CubeOrigin, CubeOrigin, CubeOrigin), CubeSize);
    }

    /// <summary>
    /// Test data: all combinations of cross-sections and cube faces.
    /// </summary>
    public static IEnumerable<TestCaseData> CrossSectionsAndFaces() {
        foreach (var crossSection in CrossSection.AllSizes) {
            foreach (var face in VoxelDirectionExtensions.All) {
                yield return new TestCaseData(crossSection, face)
                    .SetName($"{crossSection}_Face_{face}");
            }
        }
    }

    /// <summary>
    /// For each cable cross-section and cube face, validates that:
    /// 1. The snap position has precisely max(width, height) voxels of the cross-section adjacent to the surface.
    /// 2. All voxel positions are in empty space.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(CrossSectionsAndFaces))]
    public void SnapPosition_TouchesSurface_WithCorrectContactCount(
        CrossSection crossSection,
        VoxelDirection face) {
        // Get click position just outside the face center
        var clickPos = GetFaceCenterClickPosition(face);

        // Find the snapped voxel positions
        var positions = SnapPositionFinder.FindBestPosition(clickPos, _cache, crossSection, 0f);

        // Count how many voxels touch insulation
        int insulationContact = 0;
        foreach (var pos in positions) {
            if (_cache.AnyCardinalNeighbor(pos, CacheVoxelState.Insulation))
                insulationContact++;
        }

        int expectedContact = Math.Max(crossSection.Width, crossSection.Height);

        // Assert (a): touches the surface at all
        Assert.That(insulationContact, Is.GreaterThan(0),
            $"Snapped positions for {crossSection} cable on face {face} " +
            $"(click at {clickPos}) should touch the surface");

        // Assert (b): touches at exactly max(Width, Height) voxels
        Assert.That(insulationContact, Is.EqualTo(expectedContact),
            $"Snapped positions for {crossSection} cable on face {face} " +
            $"should touch {expectedContact} voxels, but touches {insulationContact}");
    }

    /// <summary>
    /// For each cable cross-section and cube face, validates that:
    /// All N×M voxels of the snapped cross-section are in Empty locations (not embedded in the wall).
    /// </summary>
    [Test]
    [TestCaseSource(nameof(CrossSectionsAndFaces))]
    public void SnapPosition_AllVoxelsInEmptySpace(
        CrossSection crossSection,
        VoxelDirection face) {
        // Get click position just outside the face center
        var clickPos = GetFaceCenterClickPosition(face);

        // Find the snapped voxel positions
        var positions = SnapPositionFinder.FindBestPosition(clickPos, _cache, crossSection, 0f);

        // Check all positions are empty
        var embeddedPositions = new List<VoxelPos>();
        foreach (var pos in positions) {
            var state = _cache.GetState(pos);
            if (state != CacheVoxelState.Empty)
                embeddedPositions.Add(pos);
        }

        int expectedTotal = crossSection.Width * crossSection.Height;

        // Assert: correct number of voxels returned
        Assert.That(positions.Count, Is.EqualTo(expectedTotal),
            $"Expected {expectedTotal} voxels for {crossSection} cable, but got {positions.Count}");

        // Assert: all voxels should be in Empty locations
        Assert.That(embeddedPositions, Is.Empty,
            $"Snapped positions for {crossSection} cable on face {face}: " +
            $"expected all voxels empty, but these are embedded: [{string.Join(", ", embeddedPositions)}]");
    }

    /// <summary>
    /// Gets the click position at the center of a cube face, just outside the surface.
    /// </summary>
    private VoxelPos GetFaceCenterClickPosition(VoxelDirection face) {
        int centerYZ = CubeOrigin + CubeSize / 2; // Center in Y and Z
        int centerXZ = CubeOrigin + CubeSize / 2; // Center in X and Z
        int centerXY = CubeOrigin + CubeSize / 2; // Center in X and Y

        return face switch {
            VoxelDirection.XPos => new VoxelPos(CubeEnd + 1, centerYZ, centerYZ),     // +X face
            VoxelDirection.XNeg => new VoxelPos(CubeOrigin - 1, centerYZ, centerYZ), // -X face
            VoxelDirection.YPos => new VoxelPos(centerXZ, CubeEnd + 1, centerXZ),     // +Y face
            VoxelDirection.YNeg => new VoxelPos(centerXZ, CubeOrigin - 1, centerXZ), // -Y face
            VoxelDirection.ZPos => new VoxelPos(centerXY, centerXY, CubeEnd + 1),     // +Z face
            VoxelDirection.ZNeg => new VoxelPos(centerXY, centerXY, CubeOrigin - 1), // -Z face
            _ => throw new ArgumentException($"Unknown face: {face}")
        };
    }

    /// <summary>
    /// Creates a cube of insulation at the given origin.
    /// </summary>
    private void CreateCube(VoxelPos origin, int size) {
        for (int x = 0; x < size; x++) {
            for (int y = 0; y < size; y++) {
                for (int z = 0; z < size; z++) {
                    _cache.SetState(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z),
                        CacheVoxelState.Insulation);
                }
            }
        }
    }
}
