using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core.CellTypes;

/// <summary>
/// Wire cell: connects all four edges into a single electrical node.
/// </summary>
/// <remarks>
/// A wire doesn't create any MNA components — it simply ensures that
/// all adjacent cells sharing an edge with this wire connect to the same node.
/// The Grid's edge-based node registry handles this automatically.
/// </remarks>
public class WireCell : Cell, IElectricalCell
{
    public override CellType Type => CellType.Wire;

    // Wires connect all four directions
    private static readonly FaceDirection[] AllDirections =
    {
        FaceDirection.Top,
        FaceDirection.Right,
        FaceDirection.Bottom,
        FaceDirection.Left,
    };

    // The node shared by all ports (set during CreateComponents)
    private NodeId _node;
    private bool _hasNode;

    public IReadOnlyList<FaceDirection> GetLocalPortDirections() => AllDirections;

    public void CreateComponents(ISimulation sim, IReadOnlyDictionary<FaceDirection, NodeId> ports)
    {
        // Wire doesn't add MNA components — it just ensures all ports share the same node.
        // The Grid's edge registry should already make all ports point to the same node.
        // We store one of them for voltage lookup.
        _hasNode = false;
        foreach (var (_, node) in ports)
        {
            _node = node;
            _hasNode = true;
            break;
        }
    }

    public void RemoveComponents(ISimulation sim)
    {
        // Wire doesn't add MNA components, so nothing to remove
        _hasNode = false;
    }

    public bool TryUpdateComponents(ISimulation sim)
    {
        // Wire has no updateable parameters
        return true;
    }

    public CellVisualState ComputeVisualState(ISimulation sim)
    {
        if (!_hasNode)
            return CellVisualState.Default;

        // Get voltage at the wire's node
        double voltage = sim.GetVoltage(_node);

        // Normalize voltage (assume 0-10V range for visualization)
        float normalized = (float)Math.Clamp(voltage / 10.0, 0, 1);

        // Wire itself doesn't have "current" in a single direction —
        // it's a junction. For now, report as conductor with no specific direction.
        return CellVisualState.ForConductor(normalized, 0, null);
    }
}
