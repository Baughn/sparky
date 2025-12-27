using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sparky.Voxel;

/// <summary>
/// A sparse voxel octree for efficient storage and O(log n) access to voxel data.
/// Supports arbitrary signed coordinates and automatically grows as needed.
/// </summary>
/// <typeparam name="T">The value type to store at each voxel position.</typeparam>
/// <remarks>
/// Key optimizations:
/// - Uniform nodes collapse (a 4³ cube of identical values = 1 node)
/// - Lazy allocation (empty regions = null, returns default value)
/// </remarks>
public class SparseVoxelOctree<T> where T : struct, IEquatable<T> {
    private readonly T _defaultValue;
    private OctreeNode<T>? _root;
    private int _rootSize;      // Size of root node (power of 2)
    private int _rootOriginX;   // Origin of root node (corner with smallest coordinates)
    private int _rootOriginY;
    private int _rootOriginZ;
    private int _voxelCount;

    /// <summary>
    /// Creates a new sparse voxel octree.
    /// </summary>
    /// <param name="defaultValue">The value returned for unset positions. Setting to this value removes the voxel.</param>
    public SparseVoxelOctree(T defaultValue = default) {
        _defaultValue = defaultValue;
    }

    /// <summary>
    /// Gets the total number of non-default voxels stored.
    /// </summary>
    public int VoxelCount => _voxelCount;

    /// <summary>
    /// Gets the default value for unset positions.
    /// </summary>
    public T DefaultValue => _defaultValue;

    /// <summary>
    /// Sets a voxel at the given position.
    /// </summary>
    public void Set(VoxelPos pos, T value) {
        if (value.Equals(_defaultValue)) {
            Remove(pos);
            return;
        }

        EnsureContains(pos.X, pos.Y, pos.Z);
        var wasDefault = SetInternal(pos.X, pos.Y, pos.Z, value);
        if (wasDefault)
            _voxelCount++;
    }

    /// <summary>
    /// Removes a voxel (sets to default) at the given position.
    /// </summary>
    public void Remove(VoxelPos pos) {
        if (_root == null)
            return;

        var wasDefault = SetInternal(pos.X, pos.Y, pos.Z, _defaultValue);
        if (!wasDefault)
            _voxelCount--;
    }

    /// <summary>
    /// Gets the voxel data at the given position.
    /// Returns the default value for empty positions.
    /// </summary>
    public T Get(VoxelPos pos) {
        if (_root == null)
            return _defaultValue;

        if (!Contains(pos.X, pos.Y, pos.Z))
            return _defaultValue;

        return GetInternal(_root, _rootSize, _rootOriginX, _rootOriginY, _rootOriginZ, pos.X, pos.Y, pos.Z);
    }

    /// <summary>
    /// Sets multiple voxels in a batch (more efficient than individual calls).
    /// </summary>
    public void SetBatch(IEnumerable<(VoxelPos Pos, T Value)> voxels) {
        foreach (var (pos, value) in voxels) {
            Set(pos, value);
        }
    }

    /// <summary>
    /// Enumerates all non-default voxels in the octree.
    /// </summary>
    public IEnumerable<(VoxelPos Pos, T Value)> GetAllVoxels() {
        if (_root == null)
            yield break;

        foreach (var v in EnumerateVoxels(_root, _rootSize, _rootOriginX, _rootOriginY, _rootOriginZ)) {
            yield return v;
        }
    }

    /// <summary>
    /// Clears all voxels from the octree.
    /// </summary>
    public void Clear() {
        _root = null;
        _rootSize = 0;
        _voxelCount = 0;
    }

    /// <summary>
    /// Enumerates all leaf nodes (uniform regions or single voxels).
    /// Useful for building prisms from coalesced regions.
    /// </summary>
    public IEnumerable<OctreeLeaf<T>> GetLeafNodes() {
        if (_root == null)
            yield break;

        foreach (var leaf in EnumerateLeaves(_root, _rootSize, _rootOriginX, _rootOriginY, _rootOriginZ)) {
            yield return leaf;
        }
    }

    #region Internal Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Contains(int x, int y, int z) {
        return x >= _rootOriginX && x < _rootOriginX + _rootSize &&
               y >= _rootOriginY && y < _rootOriginY + _rootSize &&
               z >= _rootOriginZ && z < _rootOriginZ + _rootSize;
    }

    private void EnsureContains(int x, int y, int z) {
        if (_root == null) {
            // Initialize with size 16 centered roughly on the point
            _rootSize = 16;
            _rootOriginX = (x >> 4) << 4;  // Align to 16
            _rootOriginY = (y >> 4) << 4;
            _rootOriginZ = (z >> 4) << 4;
            _root = new OctreeNode<T> { LeafValue = _defaultValue };
            return;
        }

        // Expand root until it contains the point
        while (!Contains(x, y, z)) {
            ExpandRoot(x, y, z);
        }
    }

    private void ExpandRoot(int targetX, int targetY, int targetZ) {
        // Double the root size and reposition
        int newSize = _rootSize * 2;
        int halfSize = _rootSize;

        // Determine which octant the old root should go into
        int octantX = targetX < _rootOriginX ? 1 : 0;
        int octantY = targetY < _rootOriginY ? 1 : 0;
        int octantZ = targetZ < _rootOriginZ ? 1 : 0;

        int newOriginX = _rootOriginX - octantX * halfSize;
        int newOriginY = _rootOriginY - octantY * halfSize;
        int newOriginZ = _rootOriginZ - octantZ * halfSize;

        var newRoot = new OctreeNode<T> { IsLeaf = false, LeafValue = _defaultValue };
        // Old root goes into the octant opposite to the expansion direction
        // If target is below (octant=1), we shift origin down, old root goes to +side (bit=1)
        // If target is above (octant=0), origin stays, old root goes to -side (bit=0)
        int oldOctantIndex = octantX | (octantY << 1) | (octantZ << 2);
        newRoot.Children[oldOctantIndex] = _root;

        _root = newRoot;
        _rootSize = newSize;
        _rootOriginX = newOriginX;
        _rootOriginY = newOriginY;
        _rootOriginZ = newOriginZ;
    }

    /// <summary>
    /// Sets a value at the given position. Returns true if the old value was the default.
    /// </summary>
    private bool SetInternal(int x, int y, int z, T value) {
        return SetRecursive(ref _root!, _rootSize, _rootOriginX, _rootOriginY, _rootOriginZ, x, y, z, value);
    }

    private bool SetRecursive(ref OctreeNode<T> node, int size, int originX, int originY, int originZ,
        int x, int y, int z, T newValue) {
        if (size == 1) {
            // Leaf level - single voxel
            var wasDefault = node.LeafValue.Equals(_defaultValue);
            node.LeafValue = newValue;
            node.IsLeaf = true;
            return wasDefault;
        }

        // If this is a uniform leaf, we need to split it
        if (node.IsLeaf) {
            // Check if we're setting to the same value - no change needed
            if (node.LeafValue.Equals(newValue))
                return node.LeafValue.Equals(_defaultValue);

            // Split: create 8 children with the current uniform value
            var oldValue = node.LeafValue;
            node.IsLeaf = false;

            int halfSize = size / 2;
            for (int i = 0; i < 8; i++) {
                var child = new OctreeNode<T> {
                    IsLeaf = true,
                    LeafValue = oldValue
                };
                node.Children[i] = child;
            }
        }

        // Recurse into appropriate child
        int halfSize2 = size / 2;
        int childX = (x - originX) >= halfSize2 ? 1 : 0;
        int childY = (y - originY) >= halfSize2 ? 1 : 0;
        int childZ = (z - originZ) >= halfSize2 ? 1 : 0;
        int childIndex = childX | (childY << 1) | (childZ << 2);

        int childOriginX = originX + childX * halfSize2;
        int childOriginY = originY + childY * halfSize2;
        int childOriginZ = originZ + childZ * halfSize2;

        node.Children[childIndex] ??= new OctreeNode<T> { LeafValue = _defaultValue };
        var result = SetRecursive(ref node.Children[childIndex]!, halfSize2, childOriginX, childOriginY, childOriginZ,
            x, y, z, newValue);

        // Try to collapse if all children are uniform and identical
        TryCollapse(ref node);

        return result;
    }

    private void TryCollapse(ref OctreeNode<T> node) {
        if (node.IsLeaf)
            return;

        // Check if all children exist and are uniform leaves with same value
        var firstChild = node.Children[0];
        if (firstChild == null || !firstChild.IsLeaf)
            return;

        var value = firstChild.LeafValue;

        for (int i = 1; i < 8; i++) {
            var child = node.Children[i];
            if (child == null || !child.IsLeaf)
                return;
            if (!child.LeafValue.Equals(value))
                return;
        }

        // All children are identical uniform leaves - collapse
        node.IsLeaf = true;
        node.LeafValue = value;
        for (int i = 0; i < 8; i++)
            node.Children[i] = null;
    }

    private T GetInternal(OctreeNode<T> node, int size, int originX, int originY, int originZ,
        int x, int y, int z) {
        while (true) {
            if (node.IsLeaf)
                return node.LeafValue;

            int halfSize = size / 2;
            int childX = (x - originX) >= halfSize ? 1 : 0;
            int childY = (y - originY) >= halfSize ? 1 : 0;
            int childZ = (z - originZ) >= halfSize ? 1 : 0;
            int childIndex = childX | (childY << 1) | (childZ << 2);

            var child = node.Children[childIndex];
            if (child == null)
                return _defaultValue;

            node = child;
            size = halfSize;
            originX = originX + childX * halfSize;
            originY = originY + childY * halfSize;
            originZ = originZ + childZ * halfSize;
        }
    }

    private IEnumerable<(VoxelPos, T)> EnumerateVoxels(
        OctreeNode<T> node, int size, int originX, int originY, int originZ) {
        if (node.IsLeaf) {
            if (node.LeafValue.Equals(_defaultValue))
                yield break;

            // Enumerate all voxels in this uniform region
            for (int z = 0; z < size; z++) {
                for (int y = 0; y < size; y++) {
                    for (int x = 0; x < size; x++) {
                        yield return (new VoxelPos(originX + x, originY + y, originZ + z),
                            node.LeafValue);
                    }
                }
            }
            yield break;
        }

        int halfSize = size / 2;
        for (int i = 0; i < 8; i++) {
            var child = node.Children[i];
            if (child == null)
                continue;

            int childX = i & 1;
            int childY = (i >> 1) & 1;
            int childZ = (i >> 2) & 1;
            int childOriginX = originX + childX * halfSize;
            int childOriginY = originY + childY * halfSize;
            int childOriginZ = originZ + childZ * halfSize;

            foreach (var v in EnumerateVoxels(child, halfSize, childOriginX, childOriginY, childOriginZ)) {
                yield return v;
            }
        }
    }

    private IEnumerable<OctreeLeaf<T>> EnumerateLeaves(
        OctreeNode<T> node, int size, int originX, int originY, int originZ) {
        if (node.IsLeaf) {
            if (!node.LeafValue.Equals(_defaultValue)) {
                yield return new OctreeLeaf<T>(
                    new VoxelPos(originX, originY, originZ),
                    size,
                    node.LeafValue);
            }
            yield break;
        }

        int halfSize = size / 2;
        for (int i = 0; i < 8; i++) {
            var child = node.Children[i];
            if (child == null)
                continue;

            int childX = i & 1;
            int childY = (i >> 1) & 1;
            int childZ = (i >> 2) & 1;
            int childOriginX = originX + childX * halfSize;
            int childOriginY = originY + childY * halfSize;
            int childOriginZ = originZ + childZ * halfSize;

            foreach (var leaf in EnumerateLeaves(child, halfSize, childOriginX, childOriginY, childOriginZ)) {
                yield return leaf;
            }
        }
    }

    #endregion
}

/// <summary>
/// Internal node of the sparse voxel octree.
/// Can be either a branch (has children) or a leaf (uniform value).
/// </summary>
public class OctreeNode<T> where T : struct {
    public OctreeNode<T>?[] Children { get; } = new OctreeNode<T>?[8];
    public bool IsLeaf { get; set; } = true;
    public T LeafValue { get; set; }
}

/// <summary>
/// Represents a uniform leaf region in the octree.
/// </summary>
/// <param name="Origin">Corner with smallest coordinates.</param>
/// <param name="Size">Size of the cubic region (power of 2).</param>
/// <param name="Value">Value of all voxels in this region.</param>
public readonly record struct OctreeLeaf<T>(VoxelPos Origin, int Size, T Value);
