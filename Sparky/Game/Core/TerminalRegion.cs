using System.Collections.Generic;

namespace Sparky.Game.Core;

/// <summary>
/// A named region of conductor voxels that forms a component terminal.
/// </summary>
/// <remarks>
/// Terminal regions define where a component connects to external wiring.
/// For example, a battery has "positive" and "negative" terminal regions.
/// All conductor voxels in a terminal region are connected (same MNA node).
/// </remarks>
public class TerminalRegion
{
    private readonly HashSet<VoxelPos> _voxels;

    /// <summary>
    /// The name of this terminal (e.g., "positive", "negative", "anode", "cathode").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The conductor voxels in this terminal region (absolute positions).
    /// </summary>
    public IReadOnlySet<VoxelPos> Voxels => _voxels;

    /// <summary>
    /// Creates a new terminal region.
    /// </summary>
    /// <param name="name">The terminal name.</param>
    /// <param name="voxels">The conductor voxels forming this terminal.</param>
    public TerminalRegion(string name, IEnumerable<VoxelPos> voxels)
    {
        Name = name;
        _voxels = new HashSet<VoxelPos>(voxels);
    }

    /// <summary>
    /// Creates a terminal region with a single voxel.
    /// </summary>
    public TerminalRegion(string name, VoxelPos voxel)
        : this(name, [voxel])
    {
    }

    /// <summary>
    /// Returns true if any voxel in this terminal is adjacent to the given position.
    /// </summary>
    public bool IsAdjacentTo(VoxelPos pos)
    {
        foreach (var dir in VoxelDirectionExtensions.All)
        {
            if (_voxels.Contains(pos.Neighbor(dir)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns true if this terminal contains the given voxel.
    /// </summary>
    public bool Contains(VoxelPos pos) => _voxels.Contains(pos);
}
