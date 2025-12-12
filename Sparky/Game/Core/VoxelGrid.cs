using System.Collections.Generic;

namespace Sparky.Game.Core;

/// <summary>
/// Sparse storage for voxels. Only stores non-Air voxels to save memory.
/// </summary>
/// <remarks>
/// A full 16³ array per VS block = 4096 entries per block. With sparse storage,
/// we only allocate for actual conductor/insulator voxels.
/// </remarks>
public class VoxelGrid
{
    private readonly Dictionary<VoxelPos, Voxel> _voxels = new();

    /// <summary>
    /// Gets the number of non-Air voxels in the grid.
    /// </summary>
    public int VoxelCount => _voxels.Count;

    /// <summary>
    /// Sets or removes a voxel at the given position.
    /// </summary>
    /// <param name="pos">The voxel position.</param>
    /// <param name="type">The voxel type. Air removes the voxel from storage.</param>
    public void SetVoxel(VoxelPos pos, VoxelType type)
    {
        if (type == VoxelType.Air)
        {
            _voxels.Remove(pos);
        }
        else
        {
            _voxels[pos] = new Voxel(type);
        }
    }

    /// <summary>
    /// Gets the voxel at the given position, or null if Air (not stored).
    /// </summary>
    public Voxel? GetVoxel(VoxelPos pos)
    {
        return _voxels.TryGetValue(pos, out var voxel) ? voxel : null;
    }

    /// <summary>
    /// Gets the voxel type at the given position. Returns Air for unstored positions.
    /// </summary>
    public VoxelType GetVoxelType(VoxelPos pos)
    {
        return _voxels.TryGetValue(pos, out var voxel) ? voxel.Type : VoxelType.Air;
    }

    /// <summary>
    /// Returns true if a conductor voxel exists at the position.
    /// </summary>
    public bool IsConductor(VoxelPos pos)
    {
        return _voxels.TryGetValue(pos, out var voxel) && voxel.Type == VoxelType.Conductor;
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
        return _voxels;
    }

    /// <summary>
    /// Returns all conductor voxels in the grid.
    /// </summary>
    public IEnumerable<VoxelPos> GetAllConductors()
    {
        foreach (var (pos, voxel) in _voxels)
        {
            if (voxel.Type == VoxelType.Conductor)
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
        _voxels.Clear();
    }
}

/// <summary>
/// A voxel with its type. In Phase 3, this will also include Material.
/// </summary>
/// <param name="Type">The voxel type (Conductor or Insulator).</param>
public readonly record struct Voxel(VoxelType Type);
