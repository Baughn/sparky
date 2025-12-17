namespace Sparky.MNA.Api.Limits;

/// <summary>
/// A type-erased reference to a simulation component.
/// Used as a key for limit storage and in event payloads.
/// </summary>
public readonly record struct ComponentRef {
    /// <summary>The component type name (e.g., "Resistor", "VoltageSource").</summary>
    public required string ComponentType { get; init; }

    /// <summary>The component's integer ID.</summary>
    public required int Id { get; init; }

    public override string ToString() => $"{ComponentType}({Id})";

    // Factory methods for electrical components

    public static ComponentRef From(ResistorId id) =>
        new() { ComponentType = "Resistor", Id = id.Value };

    public static ComponentRef From(VoltageSourceId id) =>
        new() { ComponentType = "VoltageSource", Id = id.Value };

    public static ComponentRef From(CurrentSourceId id) =>
        new() { ComponentType = "CurrentSource", Id = id.Value };

    public static ComponentRef From(CapacitorId id) =>
        new() { ComponentType = "Capacitor", Id = id.Value };

    public static ComponentRef From(InductorId id) =>
        new() { ComponentType = "Inductor", Id = id.Value };

    public static ComponentRef From(DiodeId id) => new() { ComponentType = "Diode", Id = id.Value };

    public static ComponentRef From(TransformerId id) =>
        new() { ComponentType = "Transformer", Id = id.Value };

    public static ComponentRef From(SwitchId id) =>
        new() { ComponentType = "Switch", Id = id.Value };

    public static ComponentRef From(VcvsId id) => new() { ComponentType = "VCVS", Id = id.Value };

    public static ComponentRef From(VccsId id) => new() { ComponentType = "VCCS", Id = id.Value };

    public static ComponentRef From(CcvsId id) => new() { ComponentType = "CCVS", Id = id.Value };

    public static ComponentRef From(CccsId id) => new() { ComponentType = "CCCS", Id = id.Value };
}
