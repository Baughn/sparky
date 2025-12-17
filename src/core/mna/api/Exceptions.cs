using System;

namespace Sparky.MNA.Api;

/// <summary>Base exception for simulation-related errors.</summary>
public class SimulationException : Exception {
    public SimulationException(string message)
        : base(message) { }

    public SimulationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Thrown when an operation references a node that does not exist.</summary>
public class InvalidNodeException : SimulationException {
    public NodeId NodeId { get; }

    public InvalidNodeException(NodeId nodeId)
        : base($"Node {nodeId} does not exist.") {
        NodeId = nodeId;
    }
}

/// <summary>Thrown when an operation references a component that does not exist.</summary>
public class InvalidComponentException : SimulationException {
    public string ComponentType { get; }
    public int ComponentId { get; }

    public InvalidComponentException(string componentType, int componentId)
        : base($"{componentType} with ID {componentId} does not exist.") {
        ComponentType = componentType;
        ComponentId = componentId;
    }

    public static InvalidComponentException ForResistor(ResistorId id) => new("Resistor", id.Value);

    public static InvalidComponentException ForVoltageSource(VoltageSourceId id) =>
        new("VoltageSource", id.Value);

    public static InvalidComponentException ForCurrentSource(CurrentSourceId id) =>
        new("CurrentSource", id.Value);

    public static InvalidComponentException ForCapacitor(CapacitorId id) =>
        new("Capacitor", id.Value);

    public static InvalidComponentException ForInductor(InductorId id) => new("Inductor", id.Value);

    public static InvalidComponentException ForDiode(DiodeId id) => new("Diode", id.Value);

    public static InvalidComponentException ForTransformer(TransformerId id) =>
        new("Transformer", id.Value);

    public static InvalidComponentException ForSwitch(SwitchId id) => new("Switch", id.Value);

    public static InvalidComponentException ForVCVS(VcvsId id) => new("VCVS", id.Value);

    public static InvalidComponentException ForVCCS(VccsId id) => new("VCCS", id.Value);

    public static InvalidComponentException ForCCVS(CcvsId id) => new("CCVS", id.Value);

    public static InvalidComponentException ForCCCS(CccsId id) => new("CCCS", id.Value);
}

/// <summary>Thrown when a component parameter value is invalid (e.g., negative resistance).</summary>
public class InvalidParameterException : SimulationException {
    public string ParameterName { get; }
    public double Value { get; }
    public string Constraint { get; }

    public InvalidParameterException(string parameterName, double value, string constraint)
        : base($"Invalid value {value} for parameter '{parameterName}': {constraint}") {
        ParameterName = parameterName;
        Value = value;
        Constraint = constraint;
    }
}

/// <summary>Thrown when attempting to remove a node that still has connected components.</summary>
public class NodeInUseException : SimulationException {
    public NodeId NodeId { get; }
    public int ConnectionCount { get; }

    public NodeInUseException(NodeId nodeId, int connectionCount)
        : base($"Cannot remove {nodeId}: it has {connectionCount} connected component(s).") {
        NodeId = nodeId;
        ConnectionCount = connectionCount;
    }
}
