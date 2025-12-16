using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;

namespace Sparky.Tests.Game.CableLaying;

/// <summary>
/// Integration tests validating that cable paths produce correct prism topology.
/// Uses fuzz-style test generation to catch cornering bugs and other geometry issues.
/// </summary>
[TestFixture]
public class CablePrismIntegrationTests
{
    private MockWorldVoxelCache _cache = null!;
    private VoxelPos _origin;

    [SetUp]
    public void SetUp()
    {
        _origin = new VoxelPos(50, 50, 50);
        _cache = new MockWorldVoxelCache(_origin);
    }

    #region Prism Validation Tests

    /// <summary>
    /// Fuzz-style test: generates many random cable paths and validates prism properties.
    /// Criterion 1: Prism dimensions match cross-section
    /// Criterion 2: Contact areas between adjacent prisms match cross-section
    /// </summary>
    [Test]
    [TestCase(1, 1, Description = "1x1 cross-section")]
    [TestCase(1, 2, Description = "1x2 cross-section")]
    [TestCase(2, 2, Description = "2x2 cross-section")]
    [TestCase(2, 3, Description = "2x3 cross-section")]
    public void AllGeneratedCables_HaveValidPrisms(int width, int height)
    {
        var crossSection = new CrossSection(width, height);
        var random = new Random(42); // Deterministic seed for reproducibility
        const int numTests = 50;

        // Create a large floor for pathfinding
        CreateFloor(45, 20, 80, 20, 80);

        var failures = new List<string>();

        for (int i = 0; i < numTests; i++)
        {
	    // Stop if we already have enough failures.
	    if (failures.Count > 1) {
	      break;
	    }
	
            // Generate random start/goal within the floor area
            var (start, goal) = GenerateRandomStartGoal(random, 25, 75, 46);

            // Skip if too close (meaningless test)
            if (ManhattanDistance(start, goal) < crossSection.MinTurnDistance + 2)
                continue;

            var pathfinder = new CablePathfinder(_cache, crossSection);
            var result = pathfinder.FindPath(start, goal);

            // Skip unsolvable cases
            if (result.Type == PathResultType.NoProgress || result.Path.Count == 0)
            {
                _cache.ClearCableConductors();
                continue;
            }

            // Build prisms using test helper (no 16³ block limits)
            var prisms = TestPrismBuilder.BuildPrisms(result.Path);

            // Validate Criterion 1: Prism dimensions
            try
            {
                TestPrismBuilder.ValidatePrismDimensions(prisms, crossSection);
            }
            catch (CableValidationException ex)
            {
                failures.Add($"Test {i} ({start}->{goal}): Dimension validation failed - {ex.Message}");
                _cache.ClearCableConductors();
                continue;
            }

            // Validate Criterion 2: Contact areas
            try
            {
                TestPrismBuilder.ValidatePrismContactAreas(prisms, crossSection);
            }
            catch (CableValidationException ex)
            {
                failures.Add($"Test {i} ({start}->{goal}): Contact area validation failed - {ex.Message}");
            }

            _cache.ClearCableConductors();
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"Prism validation failures ({failures.Count}):\n{string.Join("\n", failures)}");
        }
    }

    /// <summary>
    /// Tests cables with 90-degree turns - a known problem area for prism generation.
    /// </summary>
    [Test]
    [TestCase(1, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 2)]
    [TestCase(2, 3)]
    public void CablesWithTurns_HaveValidPrisms(int width, int height)
    {
        var crossSection = new CrossSection(width, height);

        // Create L-shaped floor to force turns
        CreateFloor(45, 25, 50, 40, 60); // First arm X=25-50
        CreateFloor(45, 50, 75, 25, 60); // Second arm X=50-75, wider Z range

        // Test multiple turn scenarios
        var testCases = new[]
        {
            (new VoxelPos(30, 46, 50), new VoxelPos(65, 46, 35)), // L-turn
            (new VoxelPos(30, 46, 50), new VoxelPos(65, 46, 55)), // Different L-turn
            (new VoxelPos(30, 46, 45), new VoxelPos(30, 46, 55)), // Short Z run
        };

        var failures = new List<string>();

        foreach (var (start, goal) in testCases)
        {
            var pathfinder = new CablePathfinder(_cache, crossSection);
            var result = pathfinder.FindPath(start, goal);

            if (result.Type == PathResultType.NoProgress || result.Path.Count == 0)
            {
                _cache.ClearCableConductors();
                continue;
            }

            // Build prisms using test helper (no 16³ block limits)
            var prisms = TestPrismBuilder.BuildPrisms(result.Path);

            try
            {
                TestPrismBuilder.ValidatePrismDimensions(prisms, crossSection);
                TestPrismBuilder.ValidatePrismContactAreas(prisms, crossSection);
            }
            catch (CableValidationException ex)
            {
                failures.Add($"{start}->{goal}: {ex.Message}");
            }

            _cache.ClearCableConductors();
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"Turn test failures ({crossSection}):\n{string.Join("\n", failures)}");
        }
    }

    /// <summary>
    /// Tests a specific corner case: cable around an exterior corner of a floating object.
    /// This exercises the distance-based support feature.
    /// </summary>
    [Test]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    public void CableAroundExteriorCorner_HasValidPrisms(int width, int height)
    {
        var crossSection = new CrossSection(width, height);

        // Create a floating cube to route around
        CreateCube(new VoxelPos(40, 45, 40), 16);

        // Route from one face around the corner to an adjacent face
        // Positions account for cross-section size: need clearance from cube surface
        var start = new VoxelPos(39 - width + 1, 53, 50); // Outside -X face, with clearance
        var goal = new VoxelPos(50, 61 + height - 1, 50);  // Outside +Y face, with clearance

        var pathfinder = new CablePathfinder(_cache, crossSection);
        var result = pathfinder.FindPath(start, goal);

        // This path requires corner routing using distance-based support
        Assert.That(result.Type, Is.Not.EqualTo(PathResultType.NoProgress),
            "Should find a path around the exterior corner");

        if (result.Path.Count == 0)
            return;

        // Build prisms using test helper (no 16³ block limits)
        var prisms = TestPrismBuilder.BuildPrisms(result.Path);

        // Validate prisms
        TestPrismBuilder.ValidatePrismDimensions(prisms, crossSection);
        TestPrismBuilder.ValidatePrismContactAreas(prisms, crossSection);
    }

    /// <summary>
    /// Regression test: straight cables should produce a single prism per block-segment.
    /// </summary>
    [Test]
    [TestCase(1, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 2)]
    [TestCase(2, 3)]
    public void StraightCable_ProducesExpectedPrismCount(int width, int height)
    {
        var crossSection = new CrossSection(width, height);

        // Create floor for a long straight cable
        CreateFloor(45, 20, 80, 45, 55);

        var start = new VoxelPos(25, 46, 50);
        var goal = new VoxelPos(75, 46, 50);

        var pathfinder = new CablePathfinder(_cache, crossSection);
        var result = pathfinder.FindPath(start, goal);

        Assert.That(result.Type, Is.EqualTo(PathResultType.Complete),
            "Straight cable should complete");

        // Build prisms using test helper (no 16³ block limits)
        var prisms = TestPrismBuilder.BuildPrisms(result.Path);

        // Validate prisms
        TestPrismBuilder.ValidatePrismDimensions(prisms, crossSection);
        TestPrismBuilder.ValidatePrismContactAreas(prisms, crossSection);

        // For a straight cable, expect exactly 1 prism (no block boundary splits)
        Assert.That(prisms.Count, Is.EqualTo(1),
            $"Straight cable should produce exactly 1 prism, got {prisms.Count}");
    }

    #endregion

    #region Helper Methods

    private (VoxelPos Start, VoxelPos Goal) GenerateRandomStartGoal(Random random, int min, int max, int y)
    {
        var startX = random.Next(min, max);
        var startZ = random.Next(min, max);
        var goalX = random.Next(min, max);
        var goalZ = random.Next(min, max);

        return (new VoxelPos(startX, y, startZ), new VoxelPos(goalX, y, goalZ));
    }

    private void CreateFloor(int y, int xMin, int xMax, int zMin, int zMax)
    {
        for (int x = xMin; x <= xMax; x++)
        {
            for (int z = zMin; z <= zMax; z++)
            {
                _cache.SetState(new VoxelPos(x, y, z), CacheVoxelState.Insulation);
            }
        }
    }

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

    private static int ManhattanDistance(VoxelPos a, VoxelPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    #endregion
}
