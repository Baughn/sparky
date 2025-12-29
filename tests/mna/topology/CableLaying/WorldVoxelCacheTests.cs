using NUnit.Framework;
using Sparky.Voxel.MnaTopology.CableLaying;
using Sparky.Voxel;
using System.Collections.Generic;

namespace Sparky.Tests.Game.CableLaying;

/// <summary>
/// Mock implementation of IWorldVoxelCache for testing.
/// Uses the same octree storage as WorldVoxelCache but allows direct state setting.
/// </summary>
public class MockWorldVoxelCache : IWorldVoxelCache {
    private readonly SparseVoxelOctree<CacheVoxelState> _octree;
    private readonly VoxelPos _origin;
    private readonly HashSet<VoxelPos> _cableConductors = new();

    public const int CacheRadius = 7;
    public const int PathfindingRadius = 6;

    public MockWorldVoxelCache(VoxelPos origin) {
        _origin = origin;
        _octree = new SparseVoxelOctree<CacheVoxelState>(CacheVoxelState.Empty);
    }

    public VoxelPos Origin => _origin;

    public CacheVoxelState GetState(VoxelPos pos) => _octree.Get(pos);

    /// <summary>
    /// Sets a voxel state directly for testing purposes.
    /// </summary>
    public void SetState(VoxelPos pos, CacheVoxelState state) {
        _octree.Set(pos, state);
    }

    public bool AllEmpty(VoxelPos min, VoxelPos max) {
        for (int z = min.Z; z < max.Z; z++) {
            for (int y = min.Y; y < max.Y; y++) {
                for (int x = min.X; x < max.X; x++) {
                    if (_octree.Get(new VoxelPos(x, y, z)) != CacheVoxelState.Empty)
                        return false;
                }
            }
        }
        return true;
    }

    public bool AnyCardinalNeighbor(VoxelPos pos, CacheVoxelState state) {
        foreach (var dir in VoxelDirectionExtensions.All) {
            var neighbor = pos.Neighbor(dir);
            if (_octree.Get(neighbor) == state)
                return true;
        }
        return false;
    }

    public bool IsInPathfindingBounds(VoxelPos pos) {
        int dx = System.Math.Abs(pos.X - _origin.X);
        int dy = System.Math.Abs(pos.Y - _origin.Y);
        int dz = System.Math.Abs(pos.Z - _origin.Z);
        int maxDist = PathfindingRadius * 16;
        return dx < maxDist && dy < maxDist && dz < maxDist;
    }

    public bool IsInCacheBounds(VoxelPos pos) {
        int dx = System.Math.Abs(pos.X - _origin.X);
        int dy = System.Math.Abs(pos.Y - _origin.Y);
        int dz = System.Math.Abs(pos.Z - _origin.Z);
        int maxDist = CacheRadius * 16;
        return dx < maxDist && dy < maxDist && dz < maxDist;
    }

    public void SetCableConductor(VoxelPos pos) {
        _octree.Set(pos, CacheVoxelState.CableConductor);
        _cableConductors.Add(pos);
    }

    public void ClearCableConductors() {
        foreach (var pos in _cableConductors) {
            _octree.Set(pos, CacheVoxelState.Empty);
        }
        _cableConductors.Clear();
    }

    public int DistanceToInsulation(VoxelPos pos, int maxDistance) {
        // Expanding search by Manhattan distance
        for (int d = 1; d <= maxDistance; d++) {
            // Check all positions at Manhattan distance d
            for (int dx = -d; dx <= d; dx++) {
                for (int dy = -(d - System.Math.Abs(dx)); dy <= d - System.Math.Abs(dx); dy++) {
                    int remainingDist = d - System.Math.Abs(dx) - System.Math.Abs(dy);
                    // Two possible dz values for this Manhattan distance (positive and negative)
                    foreach (int dz in new[] { remainingDist, -remainingDist }) {
                        if (remainingDist == 0 && dz != 0)
                            continue; // Avoid duplicate at dz=0
                        var checkPos = pos.Offset(dx, dy, dz);
                        if (_octree.Get(checkPos) == CacheVoxelState.Insulation)
                            return d;
                    }
                }
            }
        }
        return maxDistance + 1;
    }
}

[TestFixture]
public class WorldVoxelCacheTests {
    private MockWorldVoxelCache _cache = null!;
    private VoxelPos _origin;

    [SetUp]
    public void SetUp() {
        // Origin at center of block (0,0,0)
        _origin = new VoxelPos(8, 8, 8);
        _cache = new MockWorldVoxelCache(_origin);
    }

    #region CacheVoxelState Tests

    [Test]
    public void CacheVoxelState_Equals_Object_Works() {
        object a = CacheVoxelState.Empty;
        object b = CacheVoxelState.Empty;
        object c = CacheVoxelState.Insulation;

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
        Assert.That(a.Equals("not a state"), Is.False);
    }

    [Test]
    public void CacheVoxelState_GetHashCode_DifferentForDifferentStates() {
        Assert.That(CacheVoxelState.Empty.GetHashCode(), Is.Not.EqualTo(CacheVoxelState.Insulation.GetHashCode()));
        Assert.That(CacheVoxelState.Empty.GetHashCode(), Is.EqualTo(CacheVoxelState.Empty.GetHashCode()));
    }

    [Test]
    public void CacheVoxelState_ToString_ReturnsEnumName() {
        Assert.That(CacheVoxelState.Empty.ToString(), Is.EqualTo("Empty"));
        Assert.That(CacheVoxelState.Insulation.ToString(), Is.EqualTo("Insulation"));
        Assert.That(CacheVoxelState.PreExistingConductor.ToString(), Is.EqualTo("PreExistingConductor"));
        Assert.That(CacheVoxelState.CableConductor.ToString(), Is.EqualTo("CableConductor"));
        Assert.That(CacheVoxelState.Unroutable.ToString(), Is.EqualTo("Unroutable"));
    }

    #endregion

    #region GetState Tests

    [Test]
    public void GetState_EmptyCache_ReturnsEmpty() {
        var pos = new VoxelPos(0, 0, 0);
        Assert.That(_cache.GetState(pos), Is.EqualTo(CacheVoxelState.Empty));
    }

    [Test]
    public void GetState_AfterSetState_ReturnsCorrectState() {
        var pos = new VoxelPos(10, 10, 10);
        _cache.SetState(pos, CacheVoxelState.Insulation);
        Assert.That(_cache.GetState(pos), Is.EqualTo(CacheVoxelState.Insulation));
    }

    [Test]
    public void GetState_DifferentPositions_Independent() {
        var pos1 = new VoxelPos(0, 0, 0);
        var pos2 = new VoxelPos(5, 5, 5);
        var pos3 = new VoxelPos(-10, -10, -10);

        _cache.SetState(pos1, CacheVoxelState.Insulation);
        _cache.SetState(pos2, CacheVoxelState.PreExistingConductor);
        _cache.SetState(pos3, CacheVoxelState.Unroutable);

        Assert.That(_cache.GetState(pos1), Is.EqualTo(CacheVoxelState.Insulation));
        Assert.That(_cache.GetState(pos2), Is.EqualTo(CacheVoxelState.PreExistingConductor));
        Assert.That(_cache.GetState(pos3), Is.EqualTo(CacheVoxelState.Unroutable));
    }

    [Test]
    public void SetState_ToEmpty_RemovesFromOctree() {
        var pos = new VoxelPos(5, 5, 5);
        _cache.SetState(pos, CacheVoxelState.Insulation);
        _cache.SetState(pos, CacheVoxelState.Empty);
        Assert.That(_cache.GetState(pos), Is.EqualTo(CacheVoxelState.Empty));
    }

    #endregion

    #region AllEmpty Tests

    [Test]
    public void AllEmpty_EmptyRegion_ReturnsTrue() {
        var min = new VoxelPos(0, 0, 0);
        var max = new VoxelPos(10, 10, 10);
        Assert.That(_cache.AllEmpty(min, max), Is.True);
    }

    [Test]
    public void AllEmpty_RegionWithInsulation_ReturnsFalse() {
        _cache.SetState(new VoxelPos(5, 5, 5), CacheVoxelState.Insulation);

        var min = new VoxelPos(0, 0, 0);
        var max = new VoxelPos(10, 10, 10);
        Assert.That(_cache.AllEmpty(min, max), Is.False);
    }

    [Test]
    public void AllEmpty_ObstacleOutsideRegion_ReturnsTrue() {
        _cache.SetState(new VoxelPos(20, 20, 20), CacheVoxelState.Insulation);

        var min = new VoxelPos(0, 0, 0);
        var max = new VoxelPos(10, 10, 10);
        Assert.That(_cache.AllEmpty(min, max), Is.True);
    }

    [Test]
    public void AllEmpty_SingleVoxelRegion_Works() {
        var min = new VoxelPos(5, 5, 5);
        var max = new VoxelPos(6, 6, 6);

        Assert.That(_cache.AllEmpty(min, max), Is.True);

        _cache.SetState(min, CacheVoxelState.Insulation);
        Assert.That(_cache.AllEmpty(min, max), Is.False);
    }

    #endregion

    #region AnyCardinalNeighbor Tests

    [Test]
    public void AnyCardinalNeighbor_NoNeighbors_ReturnsFalse() {
        var center = new VoxelPos(10, 10, 10);
        Assert.That(_cache.AnyCardinalNeighbor(center, CacheVoxelState.Insulation), Is.False);
    }

    [Test]
    public void AnyCardinalNeighbor_InsulationOnRight_ReturnsTrue() {
        var center = new VoxelPos(10, 10, 10);
        _cache.SetState(new VoxelPos(11, 10, 10), CacheVoxelState.Insulation);
        Assert.That(_cache.AnyCardinalNeighbor(center, CacheVoxelState.Insulation), Is.True);
    }

    [Test]
    public void AnyCardinalNeighbor_AllSixDirections() {
        var center = new VoxelPos(10, 10, 10);

        // Test all 6 cardinal directions
        var neighbors = new[]
        {
            new VoxelPos(11, 10, 10), // +X
            new VoxelPos(9, 10, 10),  // -X
            new VoxelPos(10, 11, 10), // +Y
            new VoxelPos(10, 9, 10),  // -Y
            new VoxelPos(10, 10, 11), // +Z
            new VoxelPos(10, 10, 9),  // -Z
        };

        foreach (var neighbor in neighbors) {
            var testCache = new MockWorldVoxelCache(_origin);
            testCache.SetState(neighbor, CacheVoxelState.Insulation);
            Assert.That(testCache.AnyCardinalNeighbor(center, CacheVoxelState.Insulation), Is.True,
                $"Failed for neighbor at {neighbor}");
        }
    }

    [Test]
    public void AnyCardinalNeighbor_DiagonalNeighbor_ReturnsFalse() {
        var center = new VoxelPos(10, 10, 10);
        // Diagonal neighbor (not cardinal)
        _cache.SetState(new VoxelPos(11, 11, 10), CacheVoxelState.Insulation);
        Assert.That(_cache.AnyCardinalNeighbor(center, CacheVoxelState.Insulation), Is.False);
    }

    [Test]
    public void AnyCardinalNeighbor_DifferentState_ReturnsFalse() {
        var center = new VoxelPos(10, 10, 10);
        _cache.SetState(new VoxelPos(11, 10, 10), CacheVoxelState.Unroutable);
        Assert.That(_cache.AnyCardinalNeighbor(center, CacheVoxelState.Insulation), Is.False);
    }

    [Test]
    public void AnyCardinalNeighbor_PreExistingConductor_DetectsConductors() {
        var center = new VoxelPos(10, 10, 10);
        _cache.SetState(new VoxelPos(10, 11, 10), CacheVoxelState.PreExistingConductor);
        Assert.That(_cache.AnyCardinalNeighbor(center, CacheVoxelState.PreExistingConductor), Is.True);
    }

    #endregion

    #region Bounds Tests

    [Test]
    public void IsInPathfindingBounds_AtOrigin_ReturnsTrue() {
        Assert.That(_cache.IsInPathfindingBounds(_origin), Is.True);
    }

    [Test]
    public void IsInPathfindingBounds_WithinRadius_ReturnsTrue() {
        // 6 blocks = 96 voxels, so 95 should be in bounds
        var pos = new VoxelPos(_origin.X + 95, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInPathfindingBounds(pos), Is.True);
    }

    [Test]
    public void IsInPathfindingBounds_AtBoundary_ReturnsFalse() {
        // 6 blocks = 96 voxels, so 96 should be out of bounds
        var pos = new VoxelPos(_origin.X + 96, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInPathfindingBounds(pos), Is.False);
    }

    [Test]
    public void IsInPathfindingBounds_BeyondRadius_ReturnsFalse() {
        var pos = new VoxelPos(_origin.X + 100, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInPathfindingBounds(pos), Is.False);
    }

    [Test]
    public void IsInCacheBounds_AtOrigin_ReturnsTrue() {
        Assert.That(_cache.IsInCacheBounds(_origin), Is.True);
    }

    [Test]
    public void IsInCacheBounds_WithinRadius_ReturnsTrue() {
        // 7 blocks = 112 voxels, so 111 should be in bounds
        var pos = new VoxelPos(_origin.X + 111, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInCacheBounds(pos), Is.True);
    }

    [Test]
    public void IsInCacheBounds_AtBoundary_ReturnsFalse() {
        // 7 blocks = 112 voxels, so 112 should be out of bounds
        var pos = new VoxelPos(_origin.X + 112, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInCacheBounds(pos), Is.False);
    }

    [Test]
    public void IsInCacheBounds_LargerThanPathfindingBounds() {
        // Position that's outside pathfinding but inside cache bounds
        var pos = new VoxelPos(_origin.X + 100, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInPathfindingBounds(pos), Is.False);
        Assert.That(_cache.IsInCacheBounds(pos), Is.True);
    }

    [Test]
    public void Bounds_NegativeDirection_Works() {
        // Test negative directions too
        var pos = new VoxelPos(_origin.X - 95, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInPathfindingBounds(pos), Is.True);

        var pos2 = new VoxelPos(_origin.X - 96, _origin.Y, _origin.Z);
        Assert.That(_cache.IsInPathfindingBounds(pos2), Is.False);
    }

    #endregion

    #region SetCableConductor/ClearCableConductors Tests

    [Test]
    public void SetCableConductor_SetsState() {
        var pos = new VoxelPos(10, 10, 10);
        _cache.SetCableConductor(pos);
        Assert.That(_cache.GetState(pos), Is.EqualTo(CacheVoxelState.CableConductor));
    }

    [Test]
    public void ClearCableConductors_ResetsToEmpty() {
        var pos1 = new VoxelPos(10, 10, 10);
        var pos2 = new VoxelPos(11, 10, 10);
        var pos3 = new VoxelPos(12, 10, 10);

        _cache.SetCableConductor(pos1);
        _cache.SetCableConductor(pos2);
        _cache.SetCableConductor(pos3);

        _cache.ClearCableConductors();

        Assert.That(_cache.GetState(pos1), Is.EqualTo(CacheVoxelState.Empty));
        Assert.That(_cache.GetState(pos2), Is.EqualTo(CacheVoxelState.Empty));
        Assert.That(_cache.GetState(pos3), Is.EqualTo(CacheVoxelState.Empty));
    }

    [Test]
    public void ClearCableConductors_DoesNotAffectOtherStates() {
        var cablePos = new VoxelPos(10, 10, 10);
        var insulationPos = new VoxelPos(20, 20, 20);

        _cache.SetCableConductor(cablePos);
        _cache.SetState(insulationPos, CacheVoxelState.Insulation);

        _cache.ClearCableConductors();

        Assert.That(_cache.GetState(cablePos), Is.EqualTo(CacheVoxelState.Empty));
        Assert.That(_cache.GetState(insulationPos), Is.EqualTo(CacheVoxelState.Insulation));
    }

    [Test]
    public void SetCableConductor_OverwritesExistingState() {
        var pos = new VoxelPos(10, 10, 10);
        _cache.SetState(pos, CacheVoxelState.Insulation);
        _cache.SetCableConductor(pos);
        Assert.That(_cache.GetState(pos), Is.EqualTo(CacheVoxelState.CableConductor));
    }

    [Test]
    public void ClearCableConductors_MultipleCalls_Safe() {
        var pos = new VoxelPos(10, 10, 10);
        _cache.SetCableConductor(pos);

        _cache.ClearCableConductors();
        _cache.ClearCableConductors(); // Should not throw

        Assert.That(_cache.GetState(pos), Is.EqualTo(CacheVoxelState.Empty));
    }

    #endregion

    #region Origin Tests

    [Test]
    public void Origin_ReturnsConfiguredOrigin() {
        Assert.That(_cache.Origin, Is.EqualTo(_origin));
    }

    [Test]
    public void Origin_DifferentValues() {
        var customOrigin = new VoxelPos(100, 200, 300);
        var cache = new MockWorldVoxelCache(customOrigin);
        Assert.That(cache.Origin, Is.EqualTo(customOrigin));
    }

    #endregion

    #region Octree Efficiency Tests

    [Test]
    public void UniformRegion_CollapsesInOctree() {
        // Fill a 4x4x4 cube with the same state
        for (int z = 0; z < 4; z++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    _cache.SetState(new VoxelPos(x, y, z), CacheVoxelState.Insulation);

        // All should still be retrievable
        for (int z = 0; z < 4; z++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    Assert.That(_cache.GetState(new VoxelPos(x, y, z)), Is.EqualTo(CacheVoxelState.Insulation));
    }

    [Test]
    public void MixedStates_AllAccessible() {
        _cache.SetState(new VoxelPos(0, 0, 0), CacheVoxelState.Empty);
        _cache.SetState(new VoxelPos(1, 0, 0), CacheVoxelState.Insulation);
        _cache.SetState(new VoxelPos(2, 0, 0), CacheVoxelState.PreExistingConductor);
        _cache.SetState(new VoxelPos(3, 0, 0), CacheVoxelState.CableConductor);
        _cache.SetState(new VoxelPos(4, 0, 0), CacheVoxelState.Unroutable);

        Assert.That(_cache.GetState(new VoxelPos(0, 0, 0)), Is.EqualTo(CacheVoxelState.Empty));
        Assert.That(_cache.GetState(new VoxelPos(1, 0, 0)), Is.EqualTo(CacheVoxelState.Insulation));
        Assert.That(_cache.GetState(new VoxelPos(2, 0, 0)), Is.EqualTo(CacheVoxelState.PreExistingConductor));
        Assert.That(_cache.GetState(new VoxelPos(3, 0, 0)), Is.EqualTo(CacheVoxelState.CableConductor));
        Assert.That(_cache.GetState(new VoxelPos(4, 0, 0)), Is.EqualTo(CacheVoxelState.Unroutable));
    }

    #endregion
}
