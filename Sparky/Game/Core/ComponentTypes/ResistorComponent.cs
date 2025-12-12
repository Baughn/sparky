using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core.ComponentTypes;

/// <summary>
/// Resistor component - resistance between two terminals.
/// </summary>
/// <remarks>
/// A resistor has two terminal regions: "a" and "b".
/// Current flows through the resistor proportional to voltage difference (Ohm's law).
/// </remarks>
public class ResistorComponent : Component
{
    private readonly TerminalRegion _terminalA;
    private readonly TerminalRegion _terminalB;

    private ResistorId? _resistorId;

    public override ComponentType Type => ComponentType.Resistor;

    public override IReadOnlyList<TerminalRegion> Terminals { get; }

    /// <summary>
    /// The resistance in ohms.
    /// </summary>
    public double Resistance { get; set; }

    /// <summary>
    /// Creates a resistor component.
    /// </summary>
    /// <param name="origin">Component origin position.</param>
    /// <param name="terminalAVoxels">Conductor voxels for terminal A.</param>
    /// <param name="terminalBVoxels">Conductor voxels for terminal B.</param>
    /// <param name="resistance">Resistance in ohms.</param>
    public ResistorComponent(
        VoxelPos origin,
        IEnumerable<VoxelPos> terminalAVoxels,
        IEnumerable<VoxelPos> terminalBVoxels,
        double resistance)
        : base(origin)
    {
        _terminalA = new TerminalRegion("a", terminalAVoxels);
        _terminalB = new TerminalRegion("b", terminalBVoxels);
        Terminals = [_terminalA, _terminalB];
        Resistance = resistance;
    }

    /// <summary>
    /// Creates a resistor with single-voxel terminals.
    /// </summary>
    public ResistorComponent(VoxelPos terminalA, VoxelPos terminalB, double resistance)
        : this(terminalA, [terminalA], [terminalB], resistance)
    {
    }

    public override void CreateMnaComponents(
        ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes)
    {
        if (!terminalNodes.TryGetValue("a", out var nodeA))
            throw new InvalidOperationException("Resistor missing terminal A node");
        if (!terminalNodes.TryGetValue("b", out var nodeB))
            throw new InvalidOperationException("Resistor missing terminal B node");

        _resistorId = sim.AddResistor(nodeA, nodeB, Resistance);
    }

    public override void RemoveMnaComponents(ISimulation sim)
    {
        if (_resistorId.HasValue)
        {
            sim.RemoveResistor(_resistorId.Value);
            _resistorId = null;
        }
    }

    public override ComponentVisualState ComputeVisualState(ISimulation sim)
    {
        if (!_resistorId.HasValue)
            return ComponentVisualState.Default;

        var current = sim.GetResistorCurrent(_resistorId.Value);
        var power = sim.GetResistorPower(_resistorId.Value);
        var voltage = current * Resistance;

        return new ComponentVisualState(
            VoltageNormalized: (float)(Math.Abs(voltage) / 10.0),  // Normalize to 10V reference
            CurrentNormalized: (float)(Math.Abs(current) / 1.0),   // Normalize to 1A reference
            PowerNormalized: (float)(power / 10.0)                  // Normalize to 10W reference
        );
    }

    /// <summary>
    /// Gets the MNA resistor ID, if created.
    /// </summary>
    public ResistorId? ResistorId => _resistorId;
}
