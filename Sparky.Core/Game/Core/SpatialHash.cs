using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sparky.Game.Core;

/// <summary>
/// A spatial hash grid for efficient spatial queries.
/// Items can span multiple cells and are tracked in all cells they intersect.
/// </summary>
/// <typeparam name="T">The type of items to store.</typeparam>
/// <remarks>
/// Used for prism indexing to support fast "which prisms touch position P?" queries.
/// Cell size of 16 matches the VS block size for locality.
/// </remarks>
public class SpatialHash<T> where T : notnull
{
    private readonly int _cellSize;
    private readonly int _cellShift;
    private readonly Dictionary<CellPos, HashSet<T>> _cells = new();
    private readonly Dictionary<T, List<CellPos>> _itemCells = new();

    /// <summary>
    /// Creates a spatial hash with the given cell size (must be power of 2).
    /// </summary>
    /// <param name="cellSize">Size of each cell (default 16, must be power of 2).</param>
    public SpatialHash(int cellSize = 16)
    {
        if (cellSize <= 0 || (cellSize & (cellSize - 1)) != 0)
            throw new ArgumentException("Cell size must be a positive power of 2", nameof(cellSize));

        _cellSize = cellSize;
        _cellShift = BitOperations.Log2((uint)cellSize);
    }

    /// <summary>
    /// Gets the cell size.
    /// </summary>
    public int CellSize => _cellSize;

    /// <summary>
    /// Gets the number of items in the hash.
    /// </summary>
    public int Count => _itemCells.Count;

    /// <summary>
    /// Gets the number of occupied cells.
    /// </summary>
    public int CellCount => _cells.Count;

    /// <summary>
    /// Adds an item with a single-point extent.
    /// </summary>
    public void Add(T item, VoxelPos pos)
    {
        Add(item, pos, pos);
    }

    /// <summary>
    /// Adds an item with an axis-aligned bounding box.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="min">Minimum corner (inclusive).</param>
    /// <param name="max">Maximum corner (inclusive).</param>
    public void Add(T item, VoxelPos min, VoxelPos max)
    {
        if (_itemCells.ContainsKey(item))
            throw new ArgumentException("Item already exists in spatial hash", nameof(item));

        var cells = GetIntersectingCells(min, max);
        _itemCells[item] = cells;

        foreach (var cell in cells)
        {
            if (!_cells.TryGetValue(cell, out var set))
            {
                set = new HashSet<T>();
                _cells[cell] = set;
            }
            set.Add(item);
        }
    }

    /// <summary>
    /// Removes an item from the spatial hash.
    /// </summary>
    /// <returns>True if the item was found and removed.</returns>
    public bool Remove(T item)
    {
        if (!_itemCells.TryGetValue(item, out var cells))
            return false;

        foreach (var cell in cells)
        {
            if (_cells.TryGetValue(cell, out var set))
            {
                set.Remove(item);
                if (set.Count == 0)
                    _cells.Remove(cell);
            }
        }

        _itemCells.Remove(item);
        return true;
    }

    /// <summary>
    /// Updates an item's bounds in the spatial hash.
    /// </summary>
    public void Update(T item, VoxelPos min, VoxelPos max)
    {
        Remove(item);
        Add(item, min, max);
    }

    /// <summary>
    /// Gets all items that might intersect the given point.
    /// </summary>
    /// <remarks>
    /// Returns items in cells that contain the point. Caller should perform
    /// precise intersection tests if needed.
    /// </remarks>
    public IEnumerable<T> Query(VoxelPos pos)
    {
        var cell = GetCellPos(pos);
        if (_cells.TryGetValue(cell, out var set))
        {
            foreach (var item in set)
                yield return item;
        }
    }

    /// <summary>
    /// Gets all items that might intersect the given region.
    /// </summary>
    /// <param name="min">Minimum corner (inclusive).</param>
    /// <param name="max">Maximum corner (inclusive).</param>
    /// <remarks>
    /// Returns items in cells that intersect the region. May return duplicates
    /// if an item spans multiple cells in the query region.
    /// Use QueryDistinct for deduplicated results.
    /// </remarks>
    public IEnumerable<T> Query(VoxelPos min, VoxelPos max)
    {
        var cells = GetIntersectingCells(min, max);
        foreach (var cell in cells)
        {
            if (_cells.TryGetValue(cell, out var set))
            {
                foreach (var item in set)
                    yield return item;
            }
        }
    }

    /// <summary>
    /// Gets all distinct items that might intersect the given region.
    /// </summary>
    /// <param name="min">Minimum corner (inclusive).</param>
    /// <param name="max">Maximum corner (inclusive).</param>
    public IEnumerable<T> QueryDistinct(VoxelPos min, VoxelPos max)
    {
        var seen = new HashSet<T>();
        foreach (var item in Query(min, max))
        {
            if (seen.Add(item))
                yield return item;
        }
    }

    /// <summary>
    /// Returns all items in the spatial hash.
    /// </summary>
    public IEnumerable<T> GetAll()
    {
        return _itemCells.Keys;
    }

    /// <summary>
    /// Clears all items from the spatial hash.
    /// </summary>
    public void Clear()
    {
        _cells.Clear();
        _itemCells.Clear();
    }

    /// <summary>
    /// Checks if an item exists in the spatial hash.
    /// </summary>
    public bool Contains(T item)
    {
        return _itemCells.ContainsKey(item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CellPos GetCellPos(VoxelPos pos)
    {
        return new CellPos(pos.X >> _cellShift, pos.Y >> _cellShift, pos.Z >> _cellShift);
    }

    private List<CellPos> GetIntersectingCells(VoxelPos min, VoxelPos max)
    {
        var minCell = GetCellPos(min);
        var maxCell = GetCellPos(max);

        var cells = new List<CellPos>();
        for (int z = minCell.Z; z <= maxCell.Z; z++)
        {
            for (int y = minCell.Y; y <= maxCell.Y; y++)
            {
                for (int x = minCell.X; x <= maxCell.X; x++)
                {
                    cells.Add(new CellPos(x, y, z));
                }
            }
        }
        return cells;
    }

    /// <summary>
    /// A cell position in the spatial hash grid.
    /// </summary>
    private readonly record struct CellPos(int X, int Y, int Z);
}

/// <summary>
/// Bit manipulation utilities.
/// </summary>
internal static class BitOperations
{
    /// <summary>
    /// Returns the log base 2 of a value (position of highest set bit).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log2(uint value)
    {
        // Use de Bruijn sequence for O(1) log2
        // This is faster than a loop and works for powers of 2
        int r = 0;
        while (value > 1)
        {
            value >>= 1;
            r++;
        }
        return r;
    }
}
