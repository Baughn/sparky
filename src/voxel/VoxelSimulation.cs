using System;
using System.Collections.Generic;
using Sparky.Mna.Api;
using Sparky.Voxel.MnaTopology;
using Sparky.Voxel.MnaTopology.ComponentTypes;

namespace Sparky.Voxel;

/// <summary>
/// Unified facade for spatial simulation combining voxel storage with domain solvers.
/// </summary>
/// <remarks>
/// VoxelSimulation owns:
/// - VoxelGrid (spatial state)
/// - ISimulation (MNA electrical solver)
/// - Topology builders (convert voxels to solver inputs)
///
/// Consumers query spatial positions, not solver-internal IDs.
/// </remarks>
public class VoxelSimulation {
    private readonly VoxelGrid _grid = new();
    private readonly SimulationManager _mnaSimulation = new();
    private readonly TopologyBuilder _topologyBuilder = new();
    private readonly List<Component> _components = new();

    private Dictionary<VoxelPos, TopologyBuilder.ConductorRegion> _regions = new();
    private bool _topologyDirty = true;

    /// <summary>
    /// The voxel grid containing spatial state.
    /// </summary>
    public VoxelGrid Grid => _grid;

    /// <summary>
    /// Whether electrical simulation is enabled.
    /// </summary>
    public bool ElectricalEnabled { get; set; } = true;

    /// <summary>
    /// Marks the topology as needing rebuild.
    /// </summary>
    public void MarkDirty() {
        _topologyDirty = true;
    }

    /// <summary>
    /// Rebuilds the MNA topology from the current voxel state.
    /// </summary>
    public void RebuildTopology() {
        _regions = _topologyBuilder.BuildTopology(_grid, _components, _mnaSimulation);
        _topologyDirty = false;
    }

    /// <summary>
    /// Advances all enabled simulations by dt seconds.
    /// </summary>
    public void Step(double dt) {
        if (_topologyDirty) {
            RebuildTopology();
        }
        if (ElectricalEnabled) {
            _mnaSimulation.Step(dt);
        }
    }

    /// <summary>
    /// Gets the voltage at a voxel position.
    /// Returns 0.0 if the position is not part of a conductor region.
    /// </summary>
    public double GetVoltageAt(VoxelPos pos) {
        if (_regions.TryGetValue(pos, out var region)) {
            return _mnaSimulation.GetVoltage(region.NodeId);
        }
        return 0.0;
    }

    /// <summary>
    /// Gets the current flowing through a voxel position.
    /// For resistive conductors, returns the current through adjacent resistors.
    /// Returns 0.0 if no conductor exists or no current is flowing.
    /// </summary>
    public double GetCurrentThrough(VoxelPos pos) {
        if (!_regions.TryGetValue(pos, out var region)) {
            return 0.0;
        }

        // Get max current from adjacent resistors
        double maxCurrent = 0;
        foreach (var resistorId in region.AdjacentResistors) {
            var current = Math.Abs(_mnaSimulation.GetResistorCurrent(resistorId));
            maxCurrent = Math.Max(maxCurrent, current);
        }

        return maxCurrent;
    }

    /// <summary>
    /// Adds a ground component at the specified position.
    /// </summary>
    public void AddGround(VoxelPos pos) {
        var ground = new GroundComponent(pos);
        _components.Add(ground);
        _topologyDirty = true;
    }

    /// <summary>
    /// Adds a voltage source between two positions.
    /// </summary>
    /// <param name="positive">The positive terminal position.</param>
    /// <param name="negative">The negative terminal position.</param>
    /// <param name="voltage">The voltage in volts.</param>
    public void AddVoltageSource(VoxelPos positive, VoxelPos negative, double voltage) {
        var battery = new BatteryComponent(negative, positive, voltage);
        _components.Add(battery);
        _topologyDirty = true;
    }

    // ============================================================
    // TEMPORARY ACCESSORS FOR MIGRATION
    // These will be removed once spatial queries fully replace direct access
    // ============================================================

    /// <summary>
    /// Gets the conductor regions map.
    /// TEMPORARY: Exposed for migration, will be removed.
    /// </summary>
    [Obsolete("Use spatial query methods instead. Will be removed.")]
    public IReadOnlyDictionary<VoxelPos, TopologyBuilder.ConductorRegion> Regions => _regions;

    /// <summary>
    /// Gets the underlying MNA simulation.
    /// TEMPORARY: Exposed for migration, will be removed.
    /// </summary>
    [Obsolete("Use spatial query methods instead. Will be removed.")]
    public ISimulation MnaSimulation => _mnaSimulation;

    /// <summary>
    /// Gets the component list for direct manipulation.
    /// TEMPORARY: Exposed for migration, will be removed.
    /// </summary>
    [Obsolete("Use Add* methods instead. Will be removed.")]
    public IList<Component> Components => _components;
}
