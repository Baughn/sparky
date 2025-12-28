using NUnit.Framework;
using Sparky.Mna.Topology.CableLaying;
using Sparky.Voxel;

namespace Sparky.Tests.Game.CableLaying;

[TestFixture]
public class CablePathfinderTests {
    private MockWorldVoxelCache _cache = null!;
    private VoxelPos _origin;

    [SetUp]
    public void SetUp() {
        _origin = new VoxelPos(50, 50, 50);
        _cache = new MockWorldVoxelCache(_origin);
    }

    #region CrossSection Tests

    [Test]
    public void CrossSection_IsSquare_CorrectForAllSizes() {
        Assert.That(CrossSection.Size1x1.IsSquare, Is.True);
        Assert.That(CrossSection.Size1x2.IsSquare, Is.False);
        Assert.That(CrossSection.Size2x2.IsSquare, Is.True);
        Assert.That(CrossSection.Size2x3.IsSquare, Is.False);
        Assert.That(CrossSection.Size3x5.IsSquare, Is.False);
    }

    [Test]
    public void CrossSection_GetVoxelPositions_1x1_ReturnsSingleVoxel() {
        var anchor = new VoxelPos(10, 10, 10);
        var positions = CrossSection.Size1x1
            .GetVoxelPositions(anchor, VoxelDirection.XPos, CrossSectionOrientation.Flat)
            .ToList();

        Assert.That(positions, Has.Count.EqualTo(1));
        Assert.That(positions[0], Is.EqualTo(anchor));
    }

    [Test]
    public void CrossSection_GetVoxelPositions_2x3_Returns6Voxels() {
        var anchor = new VoxelPos(10, 10, 10);
        var positions = CrossSection.Size2x3
            .GetVoxelPositions(anchor, VoxelDirection.XPos, CrossSectionOrientation.Flat)
            .ToList();

        Assert.That(positions, Has.Count.EqualTo(6));
    }

    #endregion

    #region Straight Line Path Tests

    [Test]
    public void FindPath_StraightLine_XAxis_1x1() {
        // Create a floor for support
        CreateFloor(45, 40, 60, 40, 60); // Y=45, X=40-60, Z=40-60

        var start = new VoxelPos(45, 46, 50);
        var goal = new VoxelPos(55, 46, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        Assert.That(result.Path.Count, Is.GreaterThanOrEqualTo(10)); // At least 10 voxels
        ValidatePath(result, CrossSection.Size1x1);
    }

    [Test]
    public void FindPath_StraightLine_YAxis_1x1() {
        // Create a wall for support
        CreateWall(45, 40, 60, 40, 60, axis: 0); // X=45, Y=40-60, Z=40-60

        var start = new VoxelPos(46, 45, 50);
        var goal = new VoxelPos(46, 55, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1, VoxelDirection.YPos), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size1x1);
    }

    [Test]
    public void FindPath_StraightLine_ZAxis_1x1() {
        // Create a floor for support
        CreateFloor(45, 40, 60, 40, 60);

        var start = new VoxelPos(50, 46, 45);
        var goal = new VoxelPos(50, 46, 55);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1, VoxelDirection.ZPos), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size1x1);
    }

    #endregion

    #region Turn Tests

    [Test]
    public void FindPath_SingleCorner_1x1_Succeeds() {
        // Create L-shaped floor
        CreateFloor(45, 45, 55, 45, 55); // Main floor
        CreateFloor(45, 55, 65, 45, 55); // Extension

        var start = new VoxelPos(50, 46, 45);
        var goal = new VoxelPos(60, 46, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1, VoxelDirection.ZPos), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size1x1);
    }

    [Test]
    public void FindPath_TurnRejected_WhenUnderMinDistance_2x2() {
        // Create floor
        CreateFloor(45, 40, 60, 40, 60);

        // Start position
        var start = new VoxelPos(45, 46, 50);
        // Goal requires turn after only 2 steps (min for 2x2 is 4)
        var goal = new VoxelPos(47, 46, 53);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size2x2);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size2x2, VoxelDirection.XPos), goal);

        // Should either find a longer path or return partial
        // The direct path would require a turn too soon
        Assert.That(result.Path.Count, Is.GreaterThan(0));
        ValidatePath(result, CrossSection.Size2x2);
    }

    [Test]
    public void FindPath_TurnAllowed_WhenOverMinDistance_2x2() {
        // Create L-shaped floor large enough for 2x2 cable
        CreateFloor(45, 40, 60, 40, 60);

        var start = new VoxelPos(45, 46, 50);
        // Goal after sufficient straight distance (4+ voxels for 2x2)
        var goal = new VoxelPos(55, 46, 55);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size2x2);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size2x2), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size2x2);
    }

    [Test]
    public void FindPath_180DegreeTurn_NeverGenerated() {
        // Create a corridor that might tempt a 180° turn
        CreateFloor(45, 45, 55, 48, 52); // Narrow corridor

        var start = new VoxelPos(50, 46, 48);
        var goal = new VoxelPos(50, 46, 52);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1, VoxelDirection.ZPos), goal);

        // Path should go forward, not back
        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        // Verify no position appears twice (would indicate backtracking)
        var distinct = result.Path.Distinct().ToList();
        Assert.That(distinct.Count, Is.EqualTo(result.Path.Count));
        ValidatePath(result, CrossSection.Size1x1);
    }

    #endregion

    #region Obstacle Tests

    [Test]
    public void FindPath_AroundObstacle_Succeeds() {
        // Create floor with obstacle in middle
        CreateFloor(45, 40, 60, 40, 60);
        CreateObstacle(48, 55, 46, 50, 48, 52); // Block in the middle

        var start = new VoxelPos(45, 46, 50);
        var goal = new VoxelPos(58, 46, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size1x1);
    }

    [Test]
    public void FindPath_ThroughNarrowGap_ExactWidth() {
        // Create floor with gap exactly 2 voxels wide (z=49-50)
        CreateFloor(45, 40, 60, 40, 60);
        CreateObstacle(48, 55, 46, 50, 40, 48); // Left wall up to z=48
        CreateObstacle(48, 55, 46, 50, 51, 60); // Right wall from z=51

        var start = new VoxelPos(45, 46, 49); // Start in line with the gap
        var goal = new VoxelPos(58, 46, 49);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x2);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x2), goal);

        // 1x2 cable with lay-flat orientation (Width=1 Y, Height=2 Z) fits through 2-voxel gap
        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size1x2);
    }

    #endregion

    #region Blocked Path Tests

    [Test]
    public void FindPath_CompletelyBlocked_ReturnsNoProgress() {
        // Create enclosed space with no way out
        CreateFloor(45, 48, 52, 48, 52);
        CreateObstacle(46, 55, 46, 55, 47, 53); // Walls all around

        var start = new VoxelPos(50, 46, 50);
        var goal = new VoxelPos(60, 46, 60);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.NoProgress).Or.EqualTo(PathResultType.Partial));
        ValidatePath(result, CrossSection.Size1x1); // Validates partial paths, skips NoProgress
    }

    [Test]
    public void FindPath_PartialPath_WhenGoalUnreachable() {
        // Create floor that doesn't extend to goal
        CreateFloor(45, 40, 55, 40, 60);

        var start = new VoxelPos(45, 46, 50);
        var goal = new VoxelPos(70, 46, 50); // Way beyond floor

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Partial).Or.EqualTo(PathResultType.NoProgress));
        if (result.Type == PathResultType.Partial) {
            // Partial should get closer than start
            Assert.That(result.DistanceToGoal, Is.LessThan(ManhattanDistance(start, goal)));
        }
        ValidatePath(result, CrossSection.Size1x1);
    }

    #endregion

    #region Initial Direction Tests

    [Test]
    public void FindPath_WithInitialDirection_RespectsConstraint() {
        // Create floor
        CreateFloor(45, 40, 60, 40, 60);

        var start = new VoxelPos(50, 46, 50);
        var goal = new VoxelPos(55, 46, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        // Direction is now encoded in the positions
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1, VoxelDirection.XPos), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size1x1);
    }

    [Test]
    public void FindPath_UnconstrainedStart_ChoosesBestDirection() {
        // Create floor
        CreateFloor(45, 40, 60, 40, 60);

        var start = new VoxelPos(50, 46, 50);
        var goal = new VoxelPos(55, 46, 50); // Goal is in +X direction

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        // For 1x1, any direction works since it's a single voxel
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size1x1);
    }

    #endregion

    #region Support Validation Tests

    [Test]
    public void FindPath_RequiresSupport_CannotFloatInAir() {
        // No floor - just empty space
        var start = new VoxelPos(50, 50, 50);
        var goal = new VoxelPos(55, 50, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        // Should fail because there's no support
        Assert.That(result.Type, Is.EqualTo(PathResultType.NoProgress));
    }

    [Test]
    public void FindPath_AvoidsPreExistingConductors() {
        // Create floor with conductor nearby
        CreateFloor(45, 40, 60, 40, 60);
        _cache.SetState(new VoxelPos(50, 46, 51), CacheVoxelState.PreExistingConductor);

        var start = new VoxelPos(45, 46, 50);
        var goal = new VoxelPos(55, 46, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        // Path should exist but avoid z=50 near x=50
        if (result.Type == PathResultType.Complete) {
            // Verify no path voxel is adjacent to the conductor
            var conductor = new VoxelPos(50, 46, 51);
            foreach (var pos in result.Path) {
                Assert.That(ManhattanDistance(pos, conductor), Is.GreaterThan(1),
                    $"Path voxel {pos} is adjacent to conductor at {conductor}");
            }
        }
        ValidatePath(result, CrossSection.Size1x1);
    }

    #endregion

    #region Different Cross-Section Sizes

    [Test]
    public void FindPath_2x3CrossSection_StraightLine() {
        // Create wide floor for 2x3 cable
        CreateFloor(45, 40, 60, 40, 60);

        var start = new VoxelPos(45, 46, 50);
        var goal = new VoxelPos(55, 46, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size2x3);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size2x3), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        // 2x3 cable should have 6 voxels per step
        Assert.That(result.Path.Count, Is.GreaterThanOrEqualTo(6));
        ValidatePath(result, CrossSection.Size2x3);
    }

    [Test]
    public void FindPath_3x5CrossSection_RequiresWideSpace() {
        // Create extra wide floor for 3x5 cable
        CreateFloor(45, 30, 70, 30, 70);

        var start = new VoxelPos(40, 46, 50);
        var goal = new VoxelPos(60, 46, 50);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size3x5);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size3x5), goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete));
        ValidatePath(result, CrossSection.Size3x5);
    }

    #endregion

    #region Cube Surface Routing Tests

    /// <summary>
    /// Tests routing between all pairs of faces on a floating cube.
    /// This is a regression test for inefficient routing that took 56 voxels
    /// for a path that should be ~16-20 voxels.
    /// </summary>
    [Test]
    public void FindPath_CubeSurface_AllFacePairs_EfficientRouting() {
        const int cubeSize = 16;
        var cubeOrigin = new VoxelPos(42, 42, 42); // Cube from (42,42,42) to (57,57,57)

        // Create the cube (solid insulator block floating in space)
        CreateCube(cubeOrigin, cubeSize);

        // Face center positions (1 voxel outside each face, at face center)
        var center = cubeSize / 2; // 8
        var faceCenters = new Dictionary<string, VoxelPos> {
            ["XNeg"] = new(cubeOrigin.X - 1, cubeOrigin.Y + center, cubeOrigin.Z + center),
            ["XPos"] = new(cubeOrigin.X + cubeSize, cubeOrigin.Y + center, cubeOrigin.Z + center),
            ["YNeg"] = new(cubeOrigin.X + center, cubeOrigin.Y - 1, cubeOrigin.Z + center),
            ["YPos"] = new(cubeOrigin.X + center, cubeOrigin.Y + cubeSize, cubeOrigin.Z + center),
            ["ZNeg"] = new(cubeOrigin.X + center, cubeOrigin.Y + center, cubeOrigin.Z - 1),
            ["ZPos"] = new(cubeOrigin.X + center, cubeOrigin.Y + center, cubeOrigin.Z + cubeSize),
        };

        // Expected max path lengths:
        // - Adjacent faces (share an edge): ~center + center = cubeSize voxels
        // - Opposite faces: need to go around, so ~2 * cubeSize voxels
        // Add some margin for corner handling
        int adjacentMaxPath = cubeSize + 4;
        int oppositeMaxPath = cubeSize * 2 + 4;

        var faceNames = faceCenters.Keys.ToList();
        var failures = new List<string>();

        for (int i = 0; i < faceNames.Count; i++) {
            for (int j = i + 1; j < faceNames.Count; j++) {
                var face1 = faceNames[i];
                var face2 = faceNames[j];
                var start = faceCenters[face1];
                var goal = faceCenters[face2];

                bool isOpposite = AreOppositeFaces(face1, face2);
                int expectedMaxPath = isOpposite ? oppositeMaxPath : adjacentMaxPath;

                var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
                var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

                // Validate path
                if (result.Type == PathResultType.NoProgress) {
                    failures.Add($"{face1}->{face2}: NoProgress (start={start}, goal={goal})");
                    continue;
                }

                try {
                    ValidatePath(result, CrossSection.Size1x1);
                } catch (Exception ex) {
                    failures.Add($"{face1}->{face2}: Validation failed - {ex.Message}");
                    continue;
                }

                if (result.Path.Count > expectedMaxPath) {
                    failures.Add($"{face1}->{face2}: Path too long - got {result.Path.Count}, expected <= {expectedMaxPath}");
                }

                // Clear cable conductors for next iteration
                _cache.ClearCableConductors();
            }
        }

        if (failures.Count > 0) {
            Assert.Fail($"Cube routing failures:\n{string.Join("\n", failures)}");
        }
    }

    /// <summary>
    /// Tests a specific case: routing from XNeg face to YNeg face (adjacent faces).
    /// </summary>
    [Test]
    public void FindPath_CubeSurface_AdjacentFaces_XNegToYNeg() {
        const int cubeSize = 16;
        var cubeOrigin = new VoxelPos(42, 42, 42);
        CreateCube(cubeOrigin, cubeSize);

        var center = cubeSize / 2;
        var start = new VoxelPos(cubeOrigin.X - 1, cubeOrigin.Y + center, cubeOrigin.Z + center);
        var goal = new VoxelPos(cubeOrigin.X + center, cubeOrigin.Y - 1, cubeOrigin.Z + center);

        // Enable logging for this test
        var logs = new List<string>();
        CablePathfinder.Log = msg => logs.Add(msg);

        var pathfinder = new CablePathfinder(_cache, CrossSection.Size1x1);
        var result = pathfinder.FindPath(GetStartPositions(start, CrossSection.Size1x1), goal);

        CablePathfinder.Log = null;

        // Print logs for debugging
        Console.WriteLine($"Start: {start}, Goal: {goal}");
        Console.WriteLine($"Result: {result.Type}, Path count: {result.Path.Count}");
        if (result.Type == PathResultType.Partial) {
            Console.WriteLine($"End position: {result.EndPosition}, Distance to goal: {result.DistanceToGoal}");
        }
        foreach (var log in logs.TakeLast(20)) {
            Console.WriteLine(log);
        }

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete), "Should find complete path");
        ValidatePath(result, CrossSection.Size1x1);

        // Path should be roughly cubeSize voxels (center + center = 8 + 8 = 16, plus corner)
        int expectedMaxPath = cubeSize + 4;
        Assert.That(result.Path.Count, Is.LessThanOrEqualTo(expectedMaxPath),
            $"Path too long: got {result.Path.Count} voxels, expected <= {expectedMaxPath}");
    }

    private static bool AreOppositeFaces(string face1, string face2) {
        return (face1 == "XNeg" && face2 == "XPos") ||
               (face1 == "XPos" && face2 == "XNeg") ||
               (face1 == "YNeg" && face2 == "YPos") ||
               (face1 == "YPos" && face2 == "YNeg") ||
               (face1 == "ZNeg" && face2 == "ZPos") ||
               (face1 == "ZPos" && face2 == "ZNeg");
    }

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

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets start positions for pathfinding from an anchor position.
    /// </summary>
    private static IReadOnlyList<VoxelPos> GetStartPositions(
        VoxelPos anchor,
        CrossSection crossSection,
        VoxelDirection direction = VoxelDirection.XPos) {
        return crossSection.GetVoxelPositions(anchor, direction, CrossSectionOrientation.Flat).ToList();
    }

    /// <summary>Creates a floor (insulation) at Y level.</summary>
    private void CreateFloor(int y, int xMin, int xMax, int zMin, int zMax) {
        for (int x = xMin; x <= xMax; x++) {
            for (int z = zMin; z <= zMax; z++) {
                _cache.SetState(new VoxelPos(x, y, z), CacheVoxelState.Insulation);
            }
        }
    }

    /// <summary>Creates a wall (insulation) perpendicular to an axis.</summary>
    private void CreateWall(int fixedCoord, int aMin, int aMax, int bMin, int bMax, int axis) {
        for (int a = aMin; a <= aMax; a++) {
            for (int b = bMin; b <= bMax; b++) {
                var pos = axis switch {
                    0 => new VoxelPos(fixedCoord, a, b), // X fixed
                    1 => new VoxelPos(a, fixedCoord, b), // Y fixed
                    _ => new VoxelPos(a, b, fixedCoord)  // Z fixed
                };
                _cache.SetState(pos, CacheVoxelState.Insulation);
            }
        }
    }

    /// <summary>Creates a solid obstacle (insulation block).</summary>
    private void CreateObstacle(int xMin, int xMax, int yMin, int yMax, int zMin, int zMax) {
        for (int x = xMin; x <= xMax; x++) {
            for (int y = yMin; y <= yMax; y++) {
                for (int z = zMin; z <= zMax; z++) {
                    _cache.SetState(new VoxelPos(x, y, z), CacheVoxelState.Insulation);
                }
            }
        }
    }

    private static int ManhattanDistance(VoxelPos a, VoxelPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    /// <summary>
    /// Validates a path result using CableValidator.
    /// Only validates Complete and Partial paths (NoProgress has no path to validate).
    /// </summary>
    private void ValidatePath(PathResult result, CrossSection crossSection) {
        if (result.Type == PathResultType.NoProgress)
            return; // Empty path, nothing to validate

        CableValidator.ValidatePath(result.Path, crossSection, _cache);
    }

    #endregion
}
