using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core.CellTypes;

/// <summary>
/// Ground cell: provides a 0V reference point.
/// </summary>
/// <remarks>
/// The ground cell forces its port(s) to connect to the simulation's ground node.
/// This is handled specially by the Grid during topology rebuild:
/// when it sees a GroundCell, it assigns sim.Ground to that edge.
/// </remarks>
public class GroundCell : Cell, IElectricalCell
{
    public override CellType Type => CellType.Ground;

    // Ground symbol traditionally has one connection point (top)
    private static readonly FaceDirection[] SingleDirection = { FaceDirection.Top };

    public IReadOnlyList<FaceDirection> GetLocalPortDirections() => SingleDirection;

    public void CreateComponents(ISimulation sim, IReadOnlyDictionary<FaceDirection, NodeId> ports)
    {
        // Ground cell doesn't create MNA components.
        // The Grid handles forcing ports to ground during topology rebuild.
    }

    public void RemoveComponents(ISimulation sim)
    {
        // Nothing to remove
    }

    public bool TryUpdateComponents(ISimulation sim)
    {
        // Ground has no updateable parameters
        return true;
    }

    public CellVisualState ComputeVisualState(ISimulation sim)
    {
        // Ground is always at 0V, always "active"
        return new CellVisualState(
            VoltageNormalized: 0,
            CurrentMagnitude: 0,
            CurrentFlowDirection: null,
            PowerDissipation: 0,
            ChargeLevel: 0,
            IsActive: true
        );
    }
}
