using NUnit.Framework;
using Sparky.Game.Core;
using System;
using System.Linq;

namespace Sparky.Tests.Game;

[TestFixture]
public class SpatialHashTests {
    private SpatialHash<string> _hash = null!;

    [SetUp]
    public void SetUp() {
        _hash = new SpatialHash<string>(16);
    }

    #region Basic Operations

    [Test]
    public void NewHash_IsEmpty() {
        Assert.That(_hash.Count, Is.EqualTo(0));
        Assert.That(_hash.CellCount, Is.EqualTo(0));
    }

    [Test]
    public void Add_SinglePoint_CanBeQueried() {
        var pos = new VoxelPos(5, 5, 5);
        _hash.Add("item1", pos);

        Assert.That(_hash.Count, Is.EqualTo(1));
        var results = _hash.Query(pos).ToList();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo("item1"));
    }

    [Test]
    public void Add_Region_CanBeQueried() {
        var min = new VoxelPos(0, 0, 0);
        var max = new VoxelPos(10, 10, 10);
        _hash.Add("item1", min, max);

        Assert.That(_hash.Count, Is.EqualTo(1));

        // Query corner
        var results = _hash.Query(min).ToList();
        Assert.That(results, Contains.Item("item1"));

        // Query center
        results = _hash.Query(new VoxelPos(5, 5, 5)).ToList();
        Assert.That(results, Contains.Item("item1"));
    }

    [Test]
    public void Query_EmptyPosition_ReturnsEmpty() {
        _hash.Add("item1", new VoxelPos(100, 100, 100));

        var results = _hash.Query(new VoxelPos(0, 0, 0)).ToList();

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void Remove_Item_NoLongerQueried() {
        var pos = new VoxelPos(5, 5, 5);
        _hash.Add("item1", pos);

        _hash.Remove("item1");

        Assert.That(_hash.Count, Is.EqualTo(0));
        var results = _hash.Query(pos).ToList();
        Assert.That(results, Is.Empty);
    }

    [Test]
    public void Remove_NonexistentItem_ReturnsFalse() {
        var result = _hash.Remove("nonexistent");

        Assert.That(result, Is.False);
    }

    [Test]
    public void Add_DuplicateItem_Throws() {
        _hash.Add("item1", new VoxelPos(0, 0, 0));

        Assert.Throws<ArgumentException>(() =>
            _hash.Add("item1", new VoxelPos(10, 10, 10)));
    }

    #endregion

    #region Cross-Cell Items

    [Test]
    public void Add_CrossCellItem_QueryableFromAllCells() {
        // Item spans from cell (0,0,0) to cell (1,0,0)
        var min = new VoxelPos(8, 0, 0);   // In cell 0
        var max = new VoxelPos(24, 0, 0);  // In cell 1
        _hash.Add("crossItem", min, max);

        // Query cell 0
        var results1 = _hash.Query(new VoxelPos(8, 0, 0)).ToList();
        Assert.That(results1, Contains.Item("crossItem"));

        // Query cell 1
        var results2 = _hash.Query(new VoxelPos(20, 0, 0)).ToList();
        Assert.That(results2, Contains.Item("crossItem"));

        // Should occupy 2 cells
        Assert.That(_hash.CellCount, Is.EqualTo(2));
    }

    [Test]
    public void Add_LargeItem_SpansManyCells() {
        // Item spans 3x3x3 cells
        var min = new VoxelPos(0, 0, 0);
        var max = new VoxelPos(47, 47, 47);  // Spans cells 0-2 in each dimension
        _hash.Add("largeItem", min, max);

        Assert.That(_hash.CellCount, Is.EqualTo(27)); // 3x3x3
    }

    #endregion

    #region Multiple Items

    [Test]
    public void Query_MultipleItemsInSameCell_ReturnsAll() {
        _hash.Add("item1", new VoxelPos(5, 5, 5));
        _hash.Add("item2", new VoxelPos(6, 6, 6));
        _hash.Add("item3", new VoxelPos(7, 7, 7));

        var results = _hash.Query(new VoxelPos(5, 5, 5)).ToList();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results, Contains.Item("item1"));
        Assert.That(results, Contains.Item("item2"));
        Assert.That(results, Contains.Item("item3"));
    }

    [Test]
    public void QueryDistinct_CrossCellItem_ReturnedOnce() {
        // Item spans 2 cells
        _hash.Add("crossItem", new VoxelPos(8, 0, 0), new VoxelPos(24, 0, 0));

        // Query spanning both cells
        var results = _hash.QueryDistinct(
            new VoxelPos(0, 0, 0),
            new VoxelPos(31, 0, 0)).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo("crossItem"));
    }

    #endregion

    #region Negative Coordinates

    [Test]
    public void Add_NegativeCoordinates_Works() {
        var pos = new VoxelPos(-10, -20, -30);
        _hash.Add("negItem", pos);

        var results = _hash.Query(pos).ToList();

        Assert.That(results, Contains.Item("negItem"));
    }

    [Test]
    public void Add_CrossingZero_Works() {
        // Item spans from negative to positive
        var min = new VoxelPos(-10, -10, -10);
        var max = new VoxelPos(10, 10, 10);
        _hash.Add("crossZero", min, max);

        // Query negative cell
        var results1 = _hash.Query(new VoxelPos(-5, -5, -5)).ToList();
        Assert.That(results1, Contains.Item("crossZero"));

        // Query positive cell
        var results2 = _hash.Query(new VoxelPos(5, 5, 5)).ToList();
        Assert.That(results2, Contains.Item("crossZero"));
    }

    #endregion

    #region Update

    [Test]
    public void Update_MovesItemToDifferentCell() {
        _hash.Add("item1", new VoxelPos(5, 5, 5));

        _hash.Update("item1", new VoxelPos(100, 100, 100), new VoxelPos(100, 100, 100));

        // Old position should be empty
        var oldResults = _hash.Query(new VoxelPos(5, 5, 5)).ToList();
        Assert.That(oldResults, Is.Empty);

        // New position should have item
        var newResults = _hash.Query(new VoxelPos(100, 100, 100)).ToList();
        Assert.That(newResults, Contains.Item("item1"));
    }

    #endregion

    #region GetAll and Clear

    [Test]
    public void GetAll_ReturnsAllItems() {
        _hash.Add("item1", new VoxelPos(0, 0, 0));
        _hash.Add("item2", new VoxelPos(100, 100, 100));
        _hash.Add("item3", new VoxelPos(-50, -50, -50));

        var all = _hash.GetAll().ToList();

        Assert.That(all, Has.Count.EqualTo(3));
        Assert.That(all, Contains.Item("item1"));
        Assert.That(all, Contains.Item("item2"));
        Assert.That(all, Contains.Item("item3"));
    }

    [Test]
    public void Clear_RemovesAllItems() {
        _hash.Add("item1", new VoxelPos(0, 0, 0));
        _hash.Add("item2", new VoxelPos(100, 100, 100));

        _hash.Clear();

        Assert.That(_hash.Count, Is.EqualTo(0));
        Assert.That(_hash.CellCount, Is.EqualTo(0));
    }

    [Test]
    public void Contains_ExistingItem_ReturnsTrue() {
        _hash.Add("item1", new VoxelPos(0, 0, 0));

        Assert.That(_hash.Contains("item1"), Is.True);
        Assert.That(_hash.Contains("item2"), Is.False);
    }

    #endregion

    #region Constructor Validation

    [Test]
    public void Constructor_NonPowerOfTwo_Throws() {
        Assert.Throws<ArgumentException>(() => new SpatialHash<string>(15));
        Assert.Throws<ArgumentException>(() => new SpatialHash<string>(17));
        Assert.Throws<ArgumentException>(() => new SpatialHash<string>(0));
        Assert.Throws<ArgumentException>(() => new SpatialHash<string>(-1));
    }

    [Test]
    public void Constructor_PowersOfTwo_Works() {
        Assert.DoesNotThrow(() => new SpatialHash<string>(1));
        Assert.DoesNotThrow(() => new SpatialHash<string>(2));
        Assert.DoesNotThrow(() => new SpatialHash<string>(4));
        Assert.DoesNotThrow(() => new SpatialHash<string>(8));
        Assert.DoesNotThrow(() => new SpatialHash<string>(16));
        Assert.DoesNotThrow(() => new SpatialHash<string>(32));
    }

    #endregion
}
