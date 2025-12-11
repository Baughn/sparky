using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core.CellTypes;

/// <summary>
/// Battery cell: voltage source with configurable voltage.
/// </summary>
/// <remarks>
/// The battery has two ports: positive (Right in local coords) and negative (Left).
/// Rotation affects which world direction these map to.
/// </remarks>
public class BatteryCell : Cell, IElectricalCell
{
    public override CellType Type => CellType.Battery;

    /// <summary>
    /// The voltage of the battery in volts.
    /// </summary>
    public double Voltage { get; set; } = 5.0;

    // Positive on Right, Negative on Left (in local coordinates)
    private static readonly FaceDirection[] PortDirections =
    {
        FaceDirection.Right, // Positive terminal
        FaceDirection.Left, // Negative terminal
    };

    private VoltageSourceId? _vsId;
    private NodeId _posNode;
    private NodeId _negNode;
    private bool _hasComponents;

    public IReadOnlyList<FaceDirection> GetLocalPortDirections() => PortDirections;

    public void CreateComponents(ISimulation sim, IReadOnlyDictionary<FaceDirection, NodeId> ports)
    {
        // Get world directions after rotation
        var posWorld = LocalToWorld(FaceDirection.Right);
        var negWorld = LocalToWorld(FaceDirection.Left);

        if (
            !ports.TryGetValue(posWorld, out _posNode) || !ports.TryGetValue(negWorld, out _negNode)
        )
        {
            _hasComponents = false;
            return;
        }

        _vsId = sim.AddVoltageSource(_posNode, _negNode, Voltage);
        _hasComponents = true;
    }

    public void RemoveComponents(ISimulation sim)
    {
        if (_vsId.HasValue && sim.VoltageSourceExists(_vsId.Value))
        {
            sim.RemoveVoltageSource(_vsId.Value);
        }
        _vsId = null;
        _hasComponents = false;
    }

    public bool TryUpdateComponents(ISimulation sim)
    {
        if (!_vsId.HasValue || !_hasComponents)
            return false;

        sim.UpdateVoltageSource(_vsId.Value, Voltage);
        return true;
    }

    public CellVisualState ComputeVisualState(ISimulation sim)
    {
        if (!_vsId.HasValue || !_hasComponents)
            return CellVisualState.Default;

        double current = sim.GetVoltageSourceCurrent(_vsId.Value);

        // Determine current flow direction
        FaceDirection? flowDir = null;
        if (Math.Abs(current) > 1e-9)
        {
            // Current flows from + to - through external circuit
            // So in the source, current flows from - to +
            flowDir = current > 0 ? FaceDirection.Right : FaceDirection.Left;
        }

        return new CellVisualState(
            VoltageNormalized: 1.0f, // Battery is always at "high" potential
            CurrentMagnitude: (float)Math.Abs(current),
            CurrentFlowDirection: flowDir,
            PowerDissipation: 0, // Ideal source, no dissipation
            ChargeLevel: 1.0f, // "Full charge" for visualization
            IsActive: true
        );
    }

    /// <summary>
    /// Gets the ID of the underlying voltage source, if created.
    /// </summary>
    public VoltageSourceId? VoltageSourceId => _vsId;
}
