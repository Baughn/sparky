using System.Collections.Generic;
using Sparky.Mna.Api;
using Sparky.Voxel;

namespace Sparky.Mna.Topology.ComponentTypes;

/// <summary>
/// Ground component - forces connected conductor region to MNA ground (node 0).
/// </summary>
/// <remarks>
/// Ground has a single terminal region. All conductor voxels connected to this
/// terminal (directly or transitively) will be at 0V.
/// </remarks>
public class GroundComponent : Component {
    private readonly TerminalRegion _terminal;

    public override ComponentType Type => ComponentType.Ground;

    public override IReadOnlyList<TerminalRegion> Terminals { get; }

    /// <summary>
    /// Creates a ground component at the given position with specified terminal voxels.
    /// </summary>
    /// <param name="origin">Component origin position.</param>
    /// <param name="terminalVoxels">The conductor voxels forming the ground terminal.</param>
    public GroundComponent(VoxelPos origin, IEnumerable<VoxelPos> terminalVoxels)
        : base(origin) {
        _terminal = new TerminalRegion("ground", terminalVoxels);
        Terminals = [_terminal];
    }

    /// <summary>
    /// Creates a ground component with a single terminal voxel.
    /// </summary>
    public GroundComponent(VoxelPos terminalVoxel)
        : this(terminalVoxel, [terminalVoxel]) {
    }

    /// <summary>
    /// Ground doesn't add any MNA components - it just forces its terminal to node 0.
    /// The TopologyBuilder handles this by assigning sim.Ground to the region.
    /// </summary>
    public override void CreateMnaComponents(
        ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes) {
        // Ground is handled specially by TopologyBuilder - no MNA components needed
    }

    public override void RemoveMnaComponents(ISimulation sim) {
        // Nothing to remove
    }

    public override ComponentVisualState ComputeVisualState(ISimulation sim) {
        return ComponentVisualState.Default;
    }
}
