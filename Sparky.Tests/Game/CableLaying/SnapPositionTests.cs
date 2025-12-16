using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;

namespace Sparky.Tests.Game.CableLaying;

/// <summary>
/// Tests for snap position finding when placing cables on surfaces.
/// These tests validate that snap positions properly account for cable cross-section size.
/// </summary>
[TestFixture]
public class SnapPositionTests
{
    // 16³ cube of insulation floating in space
    private const int CubeOrigin = 50;
    private const int CubeSize = 16;
    private const int CubeEnd = CubeOrigin + CubeSize - 1; // 65 (inclusive)

    private MockWorldVoxelCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        // Create cache centered on the cube
        var center = new VoxelPos(CubeOrigin + CubeSize / 2, CubeOrigin + CubeSize / 2, CubeOrigin + CubeSize / 2);
        _cache = new MockWorldVoxelCache(center);

        // Create 16³ cube of insulation
        CreateCube(new VoxelPos(CubeOrigin, CubeOrigin, CubeOrigin), CubeSize);
    }

    /// <summary>
    /// Test data: all combinations of cross-sections and cube faces.
    /// </summary>
    public static IEnumerable<TestCaseData> CrossSectionsAndFaces()
    {
        foreach (var crossSection in CrossSection.AllSizes)
        {
            foreach (var face in VoxelDirectionExtensions.All)
            {
                yield return new TestCaseData(crossSection, face)
                    .SetName($"{crossSection}_Face_{face}");
            }
        }
    }

    /// <summary>
    /// For each cable cross-section and cube face, validates that:
    /// 1. The snapped position touches the surface (at least one voxel adjacent to insulation)
    /// 2. The snap position allows max(Width, Height) voxels of the cross-section to touch the surface
    /// </summary>
    [Test]
    [TestCaseSource(nameof(CrossSectionsAndFaces))]
    public void SnapPosition_TouchesSurface_WithCorrectContactCount(
        CrossSection crossSection,
        VoxelDirection face)
    {
        // Get click position just outside the face center
        var clickPos = GetFaceCenterClickPosition(face);

        // Find the snapped position
        var (snappedPos, _) = SnapPositionFinder.FindBestStartPosition(clickPos, _cache, crossSection);

        // The cable travels INTO the face (opposite direction)
        var travelDirection = face.Opposite();

        // Count how many voxels of the cross-section touch insulation
        // Try both orientations and take the better one
        int contactFlat = CountInsulationContact(snappedPos, crossSection, travelDirection, CrossSectionOrientation.Flat);
        int contactUpright = CountInsulationContact(snappedPos, crossSection, travelDirection, CrossSectionOrientation.Upright);
        int bestContact = Math.Max(contactFlat, contactUpright);

        int expectedContact = Math.Max(crossSection.Width, crossSection.Height);

        // Assert (a): touches the surface at all
        Assert.That(bestContact, Is.GreaterThan(0),
            $"Snapped position {snappedPos} for {crossSection} cable on face {face} " +
            $"(click at {clickPos}) should touch the surface");

        // Assert (b): touches at exactly max(Width, Height) voxels
        Assert.That(bestContact, Is.EqualTo(expectedContact),
            $"Snapped position {snappedPos} for {crossSection} cable on face {face} " +
            $"should touch {expectedContact} voxels, but touches {bestContact}. " +
            $"(Flat contact: {contactFlat}, Upright contact: {contactUpright})");
    }

    /// <summary>
    /// Gets the click position at the center of a cube face, just outside the surface.
    /// </summary>
    private VoxelPos GetFaceCenterClickPosition(VoxelDirection face)
    {
        int centerYZ = CubeOrigin + CubeSize / 2; // Center in Y and Z
        int centerXZ = CubeOrigin + CubeSize / 2; // Center in X and Z
        int centerXY = CubeOrigin + CubeSize / 2; // Center in X and Y

        return face switch
        {
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
    /// Counts how many voxels of a cross-section at the given position are adjacent to insulation.
    /// </summary>
    private int CountInsulationContact(
        VoxelPos anchor,
        CrossSection crossSection,
        VoxelDirection travelDirection,
        CrossSectionOrientation orientation)
    {
        int count = 0;

        foreach (var voxelPos in crossSection.GetVoxelPositions(anchor, travelDirection, orientation))
        {
            // Check if any cardinal neighbor is insulation
            if (_cache.AnyCardinalNeighbor(voxelPos, CacheVoxelState.Insulation))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Creates a cube of insulation at the given origin.
    /// </summary>
    private void CreateCube(VoxelPos origin, int size)
    {
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    _cache.SetState(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z),
                        CacheVoxelState.Insulation);
                }
            }
        }
    }
}
