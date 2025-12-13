using System.Collections.Generic;

namespace Sparky.Game.Core;

/// <summary>
/// Sparse storage for voxels using SVO + prism coalescing for memory efficiency.
/// </summary>
/// <remarks>
/// Internally uses a Sparse Voxel Octree for O(log n) get/set operations,
/// with lazy prism building for topology construction.
///
/// Key optimizations:
/// - O(log n) SetVoxel using SVO (vs O(4096) expand-rebuild)
/// - Per-block prism caching with lazy rebuild
/// - Only affected blocks are invalidated on voxel changes
/// </remarks>
public class VoxelGrid
{
    private readonly IncrementalPrismBuilder _builder = new();

    /// <summary>
    /// Gets the total number of voxels.
    /// </summary>
    public int VoxelCount => _builder.VoxelCount;

    /// <summary>
    /// Sets or removes a voxel at the given position.
    /// </summary>
    /// <param name="pos">The voxel position.</param>
    /// <param name="type">The voxel type. Air removes the voxel from storage.</param>
    /// <remarks>
    /// Conductor voxels default to Copper material. Use the overload with Material
    /// parameter to specify a different material.
    /// </remarks>
    public void SetVoxel(VoxelPos pos, VoxelType type)
    {
        Material? material = type switch
        {
            VoxelType.Conductor => Material.Copper,
            VoxelType.ResistiveConductor => Material.Copper,
            _ => null
        };
        _builder.SetVoxel(pos, type, material);
    }

    /// <summary>
    /// Sets a conductor voxel with a specific material.
    /// </summary>
    /// <param name="pos">The voxel position.</param>
    /// <param name="material">The conductor material.</param>
    public void SetVoxel(VoxelPos pos, Material material)
    {
        _builder.SetVoxel(pos, VoxelType.Conductor, material);
    }

    /// <summary>
    /// Sets multiple voxels in a batch (more efficient than individual calls).
    /// </summary>
    public void SetVoxels(IEnumerable<(VoxelPos Pos, VoxelType Type, Material? Material)> voxels)
    {
        _builder.SetVoxels(voxels);
    }

    /// <summary>
    /// Gets the voxel at the given position, or null if Air (not stored).
    /// </summary>
    public Voxel? GetVoxel(VoxelPos pos)
    {
        var (type, material) = _builder.GetVoxel(pos);
        if (type == VoxelType.Air)
            return null;
        return new Voxel(type, material);
    }

    /// <summary>
    /// Gets the voxel type at the given position. Returns Air for unstored positions.
    /// </summary>
    public VoxelType GetVoxelType(VoxelPos pos)
    {
        return _builder.GetVoxelType(pos);
    }

    /// <summary>
    /// Returns true if a conductor voxel exists at the position.
    /// </summary>
    public bool IsConductor(VoxelPos pos)
    {
        var type = GetVoxelType(pos);
        return type == VoxelType.Conductor || type == VoxelType.ResistiveConductor;
    }

    /// <summary>
    /// Gets the material at the given position, or null if Air/Insulator.
    /// </summary>
    public Material? GetMaterial(VoxelPos pos)
    {
        return _builder.GetMaterial(pos);
    }

    /// <summary>
    /// Returns all conductor voxels adjacent to the given position.
    /// </summary>
    public IEnumerable<VoxelPos> GetAdjacentConductors(VoxelPos pos)
    {
        foreach (var dir in VoxelDirectionExtensions.All)
        {
            var neighbor = pos.Neighbor(dir);
            if (IsConductor(neighbor))
            {
                yield return neighbor;
            }
        }
    }

    /// <summary>
    /// Returns all non-Air voxels in the grid.
    /// </summary>
    public IEnumerable<KeyValuePair<VoxelPos, Voxel>> GetAllVoxels()
    {
        foreach (var (pos, type, material) in _builder.GetAllVoxels())
        {
            yield return new KeyValuePair<VoxelPos, Voxel>(pos, new Voxel(type, material));
        }
    }

    /// <summary>
    /// Returns all conductor voxels in the grid.
    /// </summary>
    public IEnumerable<VoxelPos> GetAllConductors()
    {
        foreach (var (pos, type, _) in _builder.GetAllVoxels())
        {
            if (type == VoxelType.Conductor || type == VoxelType.ResistiveConductor)
            {
                yield return pos;
            }
        }
    }

    /// <summary>
    /// Removes all voxels from the grid.
    /// </summary>
    public void Clear()
    {
        _builder.Clear();
    }

    /// <summary>
    /// Gets all prisms in the grid.
    /// </summary>
    public IEnumerable<(BlockPos Block, Prism Prism)> GetAllPrisms()
    {
        return _builder.GetAllPrisms();
    }

    /// <summary>
    /// Gets all prisms in a specific block.
    /// </summary>
    public IEnumerable<Prism> GetPrismsInBlock(BlockPos blockPos)
    {
        return _builder.GetPrismsInBlock(blockPos);
    }

    /// <summary>
    /// Gets the number of blocks with voxel data.
    /// </summary>
    public int BlockCount => _builder.BlockCount;

    /// <summary>
    /// Gets the total number of prisms across all blocks.
    /// </summary>
    public int PrismCount => _builder.PrismCount;

    /// <summary>
    /// Gets the set of blocks that have been modified since the last rebuild.
    /// </summary>
    /// <remarks>
    /// Use this for incremental topology updates to detect which blocks changed.
    /// </remarks>
    public IReadOnlySet<BlockPos> DirtyBlocks => _builder.DirtyBlocks;

    /// <summary>
    /// Returns true if any blocks are dirty (need prism rebuild).
    /// </summary>
    public bool HasDirtyBlocks => _builder.HasDirtyBlocks;

    /// <summary>
    /// A version number that increments every time a voxel is modified.
    /// Use this to detect if the grid changed since the last topology build.
    /// </summary>
    public long Version => _builder.Version;

    /// <summary>
    /// Gets cached prisms for a block WITHOUT triggering rebuild.
    /// Returns the OLD prisms if the block is dirty.
    /// </summary>
    public IReadOnlyList<Prism> GetCachedPrisms(BlockPos block)
    {
        return _builder.GetCachedPrisms(block);
    }

    /// <summary>
    /// Rebuilds a single dirty block and returns both old and new prisms.
    /// </summary>
    public (IReadOnlyList<Prism> OldPrisms, IReadOnlyList<Prism> NewPrisms) RebuildBlockIncremental(BlockPos block)
    {
        return _builder.RebuildBlockIncremental(block);
    }
}

/// <summary>
/// A voxel with its type and optional material.
/// </summary>
/// <param name="Type">The voxel type (Conductor or Insulator).</param>
/// <param name="Material">The conductor material (null for Air/Insulator).</param>
public readonly record struct Voxel(VoxelType Type, Material? Material = null);
