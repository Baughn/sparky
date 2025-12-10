using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core.CellTypes;

/// <summary>
/// Resistor cell: resistance between two opposite ports.
/// </summary>
/// <remarks>
/// The resistor has two ports: Right and Left (in local coordinates).
/// Resistance can be updated without topology rebuild.
/// </remarks>
public class ResistorCell : Cell, IElectricalCell
{
    public override CellType Type => CellType.Resistor;

    /// <summary>
    /// The resistance in ohms.
    /// </summary>
    public double Resistance { get; set; } = 100.0;

    // Ports on Right and Left (in local coordinates)
    private static readonly FaceDirection[] PortDirections =
    {
        FaceDirection.Right,
        FaceDirection.Left
    };

    private ResistorId? _resistorId;
    private NodeId _nodeA;
    private NodeId _nodeB;
    private bool _hasComponents;

    public IReadOnlyList<FaceDirection> GetLocalPortDirections() => PortDirections;

    public void CreateComponents(ISimulation sim, IReadOnlyDictionary<FaceDirection, NodeId> ports)
    {
        var worldRight = LocalToWorld(FaceDirection.Right);
        var worldLeft = LocalToWorld(FaceDirection.Left);

        if (!ports.TryGetValue(worldRight, out _nodeA) ||
            !ports.TryGetValue(worldLeft, out _nodeB))
        {
            _hasComponents = false;
            return;
        }

        // Mark as variable so updates don't require topology rebuild
        _resistorId = sim.AddResistor(_nodeA, _nodeB, Resistance, isVariable: true);
        _hasComponents = true;
    }

    public void RemoveComponents(ISimulation sim)
    {
        if (_resistorId.HasValue && sim.ResistorExists(_resistorId.Value))
        {
            sim.RemoveResistor(_resistorId.Value);
        }
        _resistorId = null;
        _hasComponents = false;
    }

    public bool TryUpdateComponents(ISimulation sim)
    {
        if (!_resistorId.HasValue || !_hasComponents)
            return false;

        sim.UpdateResistor(_resistorId.Value, Resistance);
        return true;
    }

    public CellVisualState ComputeVisualState(ISimulation sim)
    {
        if (!_resistorId.HasValue || !_hasComponents)
            return CellVisualState.Default;

        double current = sim.GetResistorCurrent(_resistorId.Value);
        double power = sim.GetResistorPower(_resistorId.Value);

        // Get average voltage for color
        double va = sim.GetVoltage(_nodeA);
        double vb = sim.GetVoltage(_nodeB);
        double avgVoltage = (va + vb) / 2.0;
        float normalized = (float)Math.Clamp(avgVoltage / 10.0, 0, 1);

        // Determine current flow direction
        FaceDirection? flowDir = null;
        if (Math.Abs(current) > 1e-9)
        {
            // Positive current means flow from nodeA to nodeB
            flowDir = current > 0 ? FaceDirection.Left : FaceDirection.Right;
        }

        return CellVisualState.ForResistor(normalized, (float)current, flowDir, (float)power);
    }

    /// <summary>
    /// Gets the ID of the underlying resistor, if created.
    /// </summary>
    public ResistorId? ResistorId => _resistorId;
}
