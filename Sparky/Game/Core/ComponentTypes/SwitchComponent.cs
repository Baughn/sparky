using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core.ComponentTypes;

/// <summary>
/// Switch component - toggleable connection between two terminals.
/// </summary>
/// <remarks>
/// A switch has two terminal regions: "a" and "b".
/// When closed, it acts as a near-zero resistance connection.
/// When open, it acts as a near-infinite resistance (open circuit).
/// </remarks>
public class SwitchComponent : Component
{
    private readonly TerminalRegion _terminalA;
    private readonly TerminalRegion _terminalB;

    private SwitchId? _switchId;

    public override ComponentType Type => ComponentType.Switch;

    public override IReadOnlyList<TerminalRegion> Terminals { get; }

    /// <summary>
    /// Whether the switch is currently closed (conducting).
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Creates a switch component.
    /// </summary>
    /// <param name="origin">Component origin position.</param>
    /// <param name="terminalAVoxels">Conductor voxels for terminal A.</param>
    /// <param name="terminalBVoxels">Conductor voxels for terminal B.</param>
    /// <param name="initiallyClosed">Initial switch state.</param>
    public SwitchComponent(
        VoxelPos origin,
        IEnumerable<VoxelPos> terminalAVoxels,
        IEnumerable<VoxelPos> terminalBVoxels,
        bool initiallyClosed = false)
        : base(origin)
    {
        _terminalA = new TerminalRegion("a", terminalAVoxels);
        _terminalB = new TerminalRegion("b", terminalBVoxels);
        Terminals = [_terminalA, _terminalB];
        IsClosed = initiallyClosed;
    }

    /// <summary>
    /// Creates a switch with single-voxel terminals.
    /// </summary>
    public SwitchComponent(VoxelPos terminalA, VoxelPos terminalB, bool initiallyClosed = false)
        : this(terminalA, [terminalA], [terminalB], initiallyClosed)
    {
    }

    /// <summary>
    /// Creates a switch with a single voxel (both terminals at same position).
    /// </summary>
    public SwitchComponent(VoxelPos voxel, bool initiallyClosed = false)
        : this(voxel, [voxel], [voxel], initiallyClosed)
    {
    }

    public override void CreateMnaComponents(
        ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes)
    {
        if (!terminalNodes.TryGetValue("a", out var nodeA))
            throw new InvalidOperationException("Switch missing terminal A node");
        if (!terminalNodes.TryGetValue("b", out var nodeB))
            throw new InvalidOperationException("Switch missing terminal B node");

        _switchId = sim.AddSwitch(nodeA, nodeB, IsClosed);
    }

    public override void RemoveMnaComponents(ISimulation sim)
    {
        if (_switchId.HasValue)
        {
            sim.RemoveSwitch(_switchId.Value);
            _switchId = null;
        }
    }

    /// <summary>
    /// Toggles the switch state and updates the MNA simulation.
    /// </summary>
    public void Toggle(ISimulation sim)
    {
        IsClosed = !IsClosed;
        if (_switchId.HasValue)
        {
            sim.SetSwitchState(_switchId.Value, IsClosed);
        }
    }

    public override ComponentVisualState ComputeVisualState(ISimulation sim)
    {
        if (!_switchId.HasValue)
            return ComponentVisualState.Default;

        var current = sim.GetSwitchCurrent(_switchId.Value);

        return new ComponentVisualState(
            VoltageNormalized: 0f,  // Switch doesn't have significant voltage drop
            CurrentNormalized: (float)(Math.Abs(current) / 1.0),  // Normalize to 1A reference
            PowerNormalized: 0f  // Ideal switch has no power dissipation
        );
    }

    /// <summary>
    /// Gets the MNA switch ID, if created.
    /// </summary>
    public SwitchId? SwitchId => _switchId;
}
