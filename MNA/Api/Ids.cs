namespace Sparky.MNA.Api;

/// <summary>Strongly-typed identifier for a circuit node.</summary>
public readonly record struct NodeId(int Value) {
    public override string ToString() => $"Node({Value})";
}

/// <summary>Strongly-typed identifier for a resistor component.</summary>
public readonly record struct ResistorId(int Value) {
    public override string ToString() => $"Resistor({Value})";
}

/// <summary>Strongly-typed identifier for a voltage source component.</summary>
public readonly record struct VoltageSourceId(int Value) {
    public override string ToString() => $"VoltageSource({Value})";
}

/// <summary>Strongly-typed identifier for a current source component.</summary>
public readonly record struct CurrentSourceId(int Value) {
    public override string ToString() => $"CurrentSource({Value})";
}

/// <summary>Strongly-typed identifier for a capacitor component.</summary>
public readonly record struct CapacitorId(int Value) {
    public override string ToString() => $"Capacitor({Value})";
}

/// <summary>Strongly-typed identifier for an inductor component.</summary>
public readonly record struct InductorId(int Value) {
    public override string ToString() => $"Inductor({Value})";
}

/// <summary>Strongly-typed identifier for a diode component.</summary>
public readonly record struct DiodeId(int Value) {
    public override string ToString() => $"Diode({Value})";
}

/// <summary>Strongly-typed identifier for a transformer component.</summary>
public readonly record struct TransformerId(int Value) {
    public override string ToString() => $"Transformer({Value})";
}

/// <summary>Strongly-typed identifier for a switch component.</summary>
public readonly record struct SwitchId(int Value) {
    public override string ToString() => $"Switch({Value})";
}

/// <summary>Strongly-typed identifier for a Voltage-Controlled Voltage Source.</summary>
public readonly record struct VcvsId(int Value) {
    public override string ToString() => $"VCVS({Value})";
}

/// <summary>Strongly-typed identifier for a Voltage-Controlled Current Source.</summary>
public readonly record struct VccsId(int Value) {
    public override string ToString() => $"VCCS({Value})";
}

/// <summary>Strongly-typed identifier for a Current-Controlled Voltage Source.</summary>
public readonly record struct CcvsId(int Value) {
    public override string ToString() => $"CCVS({Value})";
}

/// <summary>Strongly-typed identifier for a Current-Controlled Current Source.</summary>
public readonly record struct CccsId(int Value) {
    public override string ToString() => $"CCCS({Value})";
}
