using System;
using System.Collections.Generic;
using Sparky.Mna.Api;
using Sparky.Voxel;

namespace Sparky.Mna.Topology;

/// <summary>
/// Type of electrical component.
/// </summary>
public enum ComponentType {
    Ground,
    Battery,
    Resistor,
    Diode,
    Capacitor,
    Inductor,
    Switch
}

/// <summary>
/// Strongly-typed identifier for components.
/// </summary>
public readonly record struct ComponentId(Guid Value) {
    public static ComponentId New() => new(Guid.NewGuid());
    public bool IsValid => Value != Guid.Empty;
}

/// <summary>
/// Base class for multi-voxel electrical components.
/// </summary>
/// <remarks>
/// Components are multi-voxel structures with terminal regions that interface
/// with external wiring. The component's internal behavior (voltage source,
/// resistor, etc.) connects between its terminals.
///
/// Component bodies don't participate in voxel connectivity - only terminals do.
/// This allows components to have insulating bodies that prevent internal shorts.
/// </remarks>
public abstract class Component {
    /// <summary>
    /// Unique identifier for this component instance.
    /// </summary>
    public ComponentId Id { get; }

    /// <summary>
    /// The position of this component's origin in voxel space.
    /// </summary>
    public VoxelPos Origin { get; }

    /// <summary>
    /// The type of this component.
    /// </summary>
    public abstract ComponentType Type { get; }

    /// <summary>
    /// The terminal regions of this component.
    /// </summary>
    public abstract IReadOnlyList<TerminalRegion> Terminals { get; }

    protected Component(VoxelPos origin) {
        Id = ComponentId.New();
        Origin = origin;
    }

    /// <summary>
    /// Creates MNA components connecting the terminal nodes.
    /// Called during topology rebuild after terminal nodes are resolved.
    /// </summary>
    /// <param name="sim">The MNA simulation.</param>
    /// <param name="terminalNodes">Map from terminal name to MNA node ID.</param>
    public abstract void CreateMnaComponents(
        ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes);

    /// <summary>
    /// Removes MNA components from the simulation.
    /// Called during topology rebuild or component removal.
    /// </summary>
    public abstract void RemoveMnaComponents(ISimulation sim);

    /// <summary>
    /// Computes visual state after simulation step.
    /// </summary>
    public abstract ComponentVisualState ComputeVisualState(ISimulation sim);

    /// <summary>
    /// Returns the terminal region with the given name, or null if not found.
    /// </summary>
    public TerminalRegion? GetTerminal(string name) {
        foreach (var terminal in Terminals) {
            if (terminal.Name == name)
                return terminal;
        }
        return null;
    }
}

/// <summary>
/// Visual state data for rendering a component.
/// </summary>
public readonly record struct ComponentVisualState(
    float VoltageNormalized = 0f,
    float CurrentNormalized = 0f,
    float PowerNormalized = 0f,
    float Temperature = 20f
) {
    public static ComponentVisualState Default => new();
}
