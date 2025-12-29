using System;
using System.Collections.Generic;
using Sparky.Mna.Api;
using Sparky.Voxel;

namespace Sparky.Voxel.MnaTopology.ComponentTypes;

/// <summary>
/// Battery component - voltage source between positive and negative terminals.
/// </summary>
/// <remarks>
/// A battery has two terminal regions: "negative" and "positive".
/// The MNA voltage source maintains a fixed voltage difference between them.
/// </remarks>
public class BatteryComponent : Component {
    private readonly TerminalRegion _negative;
    private readonly TerminalRegion _positive;

    private VoltageSourceId? _voltageSourceId;

    public override ComponentType Type => ComponentType.Battery;

    public override IReadOnlyList<TerminalRegion> Terminals { get; }

    /// <summary>
    /// The battery voltage in volts. Positive terminal is higher.
    /// </summary>
    public double Voltage { get; set; }

    /// <summary>
    /// Creates a battery component.
    /// </summary>
    /// <param name="origin">Component origin position.</param>
    /// <param name="negativeVoxels">Conductor voxels for negative terminal.</param>
    /// <param name="positiveVoxels">Conductor voxels for positive terminal.</param>
    /// <param name="voltage">Initial voltage.</param>
    public BatteryComponent(
        VoxelPos origin,
        IEnumerable<VoxelPos> negativeVoxels,
        IEnumerable<VoxelPos> positiveVoxels,
        double voltage)
        : base(origin) {
        _negative = new TerminalRegion("negative", negativeVoxels);
        _positive = new TerminalRegion("positive", positiveVoxels);
        Terminals = [_negative, _positive];
        Voltage = voltage;
    }

    /// <summary>
    /// Creates a battery with single-voxel terminals.
    /// </summary>
    public BatteryComponent(VoxelPos negativeVoxel, VoxelPos positiveVoxel, double voltage)
        : this(negativeVoxel, [negativeVoxel], [positiveVoxel], voltage) {
    }

    public override void CreateMnaComponents(
        ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes) {
        if (!terminalNodes.TryGetValue("negative", out var negNode))
            throw new InvalidOperationException("Battery missing negative terminal node");
        if (!terminalNodes.TryGetValue("positive", out var posNode))
            throw new InvalidOperationException("Battery missing positive terminal node");

        // Create voltage source: V(posNode) - V(negNode) = Voltage
        _voltageSourceId = sim.AddVoltageSource(posNode, negNode, Voltage);
    }

    public override void RemoveMnaComponents(ISimulation sim) {
        if (_voltageSourceId.HasValue) {
            sim.RemoveVoltageSource(_voltageSourceId.Value);
            _voltageSourceId = null;
        }
    }

    public override ComponentVisualState ComputeVisualState(ISimulation sim) {
        if (!_voltageSourceId.HasValue)
            return ComponentVisualState.Default;

        var current = sim.GetVoltageSourceCurrent(_voltageSourceId.Value);
        var power = Math.Abs(Voltage * current);

        return new ComponentVisualState(
            VoltageNormalized: (float)(Voltage / 10.0),  // Normalize to 10V reference
            CurrentNormalized: (float)(Math.Abs(current) / 1.0),  // Normalize to 1A reference
            PowerNormalized: (float)(power / 10.0)  // Normalize to 10W reference
        );
    }

    /// <summary>
    /// Gets the MNA voltage source ID, if created.
    /// </summary>
    public VoltageSourceId? VoltageSourceId => _voltageSourceId;

    /// <summary>
    /// Updates the voltage in the MNA simulation (fast path, no topology rebuild).
    /// </summary>
    public void UpdateMnaValue(ISimulation sim) {
        if (_voltageSourceId.HasValue) {
            sim.UpdateVoltageSource(_voltageSourceId.Value, Voltage);
        }
    }
}
