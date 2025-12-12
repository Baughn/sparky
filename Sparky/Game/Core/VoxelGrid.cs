using System.Collections.Generic;

namespace Sparky.Game.Core;

/// <summary>
/// Sparse storage for voxels using prism coalescing for memory efficiency.
/// </summary>
/// <remarks>
/// Instead of storing individual voxels (~50 bytes each), voxels are coalesced
/// into axis-aligned prisms (~14 bytes each). A 4×4×160 cable compresses from
/// ~128KB to ~140 bytes.
///
/// Point queries are O(1) block lookup + O(k) prism scan where k ≈ 10-20.
/// </remarks>
public class VoxelGrid
{
    private readonly Dictionary<BlockPos, BlockVoxelData> _blocks = new();

    /// <summary>
    /// Gets the total number of voxels (computed by summing prism volumes).
    /// </summary>
    public int VoxelCount
    {
        get
        {
            int count = 0;
            foreach (var block in _blocks.Values)
            {
                foreach (var prism in block.Prisms)
                {
                    count += prism.Volume;
                }
            }
            return count;
        }
    }

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
        Material? material = type == VoxelType.Conductor ? Material.Copper : null;
        SetVoxelInternal(pos, type, material);
    }

    /// <summary>
    /// Sets a conductor voxel with a specific material.
    /// </summary>
    /// <param name="pos">The voxel position.</param>
    /// <param name="material">The conductor material.</param>
    public void SetVoxel(VoxelPos pos, Material material)
    {
        SetVoxelInternal(pos, VoxelType.Conductor, material);
    }

    private void SetVoxelInternal(VoxelPos pos, VoxelType type, Material? material)
    {
        var blockPos = pos.Block;
        var local = pos.Local;

        // Get or create block data
        if (!_blocks.TryGetValue(blockPos, out var block))
        {
            if (type == VoxelType.Air)
                return; // Nothing to do - already air

            block = new BlockVoxelData();
            _blocks[blockPos] = block;
        }

        // Expand prisms to voxel array, modify, and rebuild
        var voxels = block.ExpandToVoxels();
        voxels[local.X + local.Y * 16 + local.Z * 256] = (type, material);
        block.RebuildFromVoxels(voxels);

        // Remove empty blocks
        if (block.IsEmpty)
        {
            _blocks.Remove(blockPos);
        }
    }

    /// <summary>
    /// Gets the voxel at the given position, or null if Air (not stored).
    /// </summary>
    public Voxel? GetVoxel(VoxelPos pos)
    {
        if (!_blocks.TryGetValue(pos.Block, out var block))
            return null;

        var local = pos.Local;
        var prism = block.FindPrism(local.X, local.Y, local.Z);
        if (prism == null)
            return null;

        return new Voxel(prism.Value.Type, prism.Value.Material);
    }

    /// <summary>
    /// Gets the voxel type at the given position. Returns Air for unstored positions.
    /// </summary>
    public VoxelType GetVoxelType(VoxelPos pos)
    {
        if (!_blocks.TryGetValue(pos.Block, out var block))
            return VoxelType.Air;

        var local = pos.Local;
        return block.GetVoxelType(local.X, local.Y, local.Z);
    }

    /// <summary>
    /// Returns true if a conductor voxel exists at the position.
    /// </summary>
    public bool IsConductor(VoxelPos pos)
    {
        return GetVoxelType(pos) == VoxelType.Conductor;
    }

    /// <summary>
    /// Gets the material at the given position, or null if Air/Insulator.
    /// </summary>
    public Material? GetMaterial(VoxelPos pos)
    {
        if (!_blocks.TryGetValue(pos.Block, out var block))
            return null;

        var local = pos.Local;
        return block.GetMaterial(local.X, local.Y, local.Z);
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
        foreach (var (blockPos, block) in _blocks)
        {
            foreach (var prism in block.Prisms)
            {
                var end = prism.End;
                for (int z = prism.LocalZ; z < end.Z; z++)
                {
                    for (int y = prism.LocalY; y < end.Y; y++)
                    {
                        for (int x = prism.LocalX; x < end.X; x++)
                        {
                            var voxelPos = VoxelPos.FromBlockLocal(blockPos, x, y, z);
                            yield return new KeyValuePair<VoxelPos, Voxel>(
                                voxelPos,
                                new Voxel(prism.Type, prism.Material));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns all conductor voxels in the grid.
    /// </summary>
    public IEnumerable<VoxelPos> GetAllConductors()
    {
        foreach (var (blockPos, block) in _blocks)
        {
            foreach (var prism in block.Prisms)
            {
                if (prism.Type != VoxelType.Conductor)
                    continue;

                var end = prism.End;
                for (int z = prism.LocalZ; z < end.Z; z++)
                {
                    for (int y = prism.LocalY; y < end.Y; y++)
                    {
                        for (int x = prism.LocalX; x < end.X; x++)
                        {
                            yield return VoxelPos.FromBlockLocal(blockPos, x, y, z);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes all voxels from the grid.
    /// </summary>
    public void Clear()
    {
        _blocks.Clear();
    }

    /// <summary>
    /// Gets all prisms in the grid.
    /// </summary>
    public IEnumerable<(BlockPos Block, Prism Prism)> GetAllPrisms()
    {
        foreach (var (blockPos, block) in _blocks)
        {
            foreach (var prism in block.Prisms)
            {
                yield return (blockPos, prism);
            }
        }
    }

    /// <summary>
    /// Gets all prisms in a specific block.
    /// </summary>
    public IEnumerable<Prism> GetPrismsInBlock(BlockPos blockPos)
    {
        if (_blocks.TryGetValue(blockPos, out var block))
        {
            return block.Prisms;
        }
        return [];
    }

    /// <summary>
    /// Gets the number of blocks with voxel data.
    /// </summary>
    public int BlockCount => _blocks.Count;

    /// <summary>
    /// Gets the total number of prisms across all blocks.
    /// </summary>
    public int PrismCount
    {
        get
        {
            int count = 0;
            foreach (var block in _blocks.Values)
            {
                count += block.PrismCount;
            }
            return count;
        }
    }
}

/// <summary>
/// A voxel with its type and optional material.
/// </summary>
/// <param name="Type">The voxel type (Conductor or Insulator).</param>
/// <param name="Material">The conductor material (null for Air/Insulator).</param>
public readonly record struct Voxel(VoxelType Type, Material? Material = null);
