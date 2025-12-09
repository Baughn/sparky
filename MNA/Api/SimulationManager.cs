using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sparky.MNA.Core;

namespace Sparky.MNA.Api;

/// <summary>
/// The simulation manager. Thread safety: All methods must be called from a single thread,
/// except Step() which may be called from a worker thread after all modifications are complete.
/// </summary>
public class SimulationManager : ISimulation
{
    private int _nextNodeId = 1;
    private int _nextResistorId = 1;
    private int _nextVoltageSourceId = 1;
    private int _nextCurrentSourceId = 1;
    private int _nextCapacitorId = 1;
    private int _nextInductorId = 1;
    private int _nextDiodeId = 1;
    private int _nextTransformerId = 1;

    // Logical Graph
    private readonly Dictionary<NodeId, LogicalNode> _logicalNodes = new();
    private readonly Dictionary<ResistorId, LogicalResistor> _resistors = new();
    private readonly Dictionary<VoltageSourceId, LogicalVoltageSource> _voltageSources = new();
    private readonly Dictionary<CurrentSourceId, LogicalCurrentSource> _currentSources = new();
    private readonly Dictionary<CapacitorId, LogicalCapacitor> _capacitors = new();
    private readonly Dictionary<InductorId, LogicalInductor> _inductors = new();
    private readonly Dictionary<DiodeId, LogicalDiode> _diodes = new();
    private readonly Dictionary<TransformerId, LogicalTransformer> _transformers = new();

    // Physical Circuits (Partitions)
    private readonly List<Circuit> _partitions = new();

    // Mapping: Logical -> Physical
    private readonly Dictionary<NodeId, Node> _physicalNodes = new();
    private readonly Dictionary<NodeId, InterpolationInfo> _interpolationMap = new();

    // Physical component maps for in-place updates
    private readonly Dictionary<ResistorId, Resistor> _physicalResistors = new();
    private readonly Dictionary<VoltageSourceId, VoltageSource> _physicalVoltageSources = new();
    private readonly Dictionary<CurrentSourceId, CurrentSource> _physicalCurrentSources = new();
    private readonly Dictionary<CapacitorId, Capacitor> _physicalCapacitors = new();
    private readonly Dictionary<InductorId, Inductor> _physicalInductors = new();
    private readonly Dictionary<DiodeId, Diode> _physicalDiodes = new();
    private readonly Dictionary<TransformerId, Transformer> _physicalTransformers = new();

    // Optimization tracking
    private readonly HashSet<ResistorId> _optimizedResistors = new();

    private readonly record struct InterpolationInfo(NodeId NodeA, NodeId NodeB, double Ratio);

    // Optimization
    public bool EnableLineOptimization { get; set; } = true;
    private bool _isDirty = false;

    // Bulk update
    private int _bulkUpdateDepth = 0;

    // Ground node
    public NodeId Ground => new NodeId(0);

    #region Internal Data Structures

    private class LogicalNode
    {
        public NodeId Id { get; }
        public List<ILogicalComponent> Connections { get; } = new();
        public LogicalNode(NodeId id) => Id = id;
    }

    private interface ILogicalComponent
    {
        bool IsOptimizable { get; }
    }

    private class LogicalResistor : ILogicalComponent
    {
        public ResistorId Id { get; }
        public NodeId NodeA { get; }
        public NodeId NodeB { get; }
        public double Resistance { get; set; }
        public bool IsOptimizable => true;

        public LogicalResistor(ResistorId id, NodeId a, NodeId b, double r)
        {
            Id = id; NodeA = a; NodeB = b; Resistance = r;
        }
    }

    private class LogicalVoltageSource : ILogicalComponent
    {
        public VoltageSourceId Id { get; }
        public NodeId NodePos { get; }
        public NodeId NodeNeg { get; }
        public double Voltage { get; set; }
        public bool IsOptimizable => false;

        public LogicalVoltageSource(VoltageSourceId id, NodeId pos, NodeId neg, double v)
        {
            Id = id; NodePos = pos; NodeNeg = neg; Voltage = v;
        }
    }

    private class LogicalCurrentSource : ILogicalComponent
    {
        public CurrentSourceId Id { get; }
        public NodeId NodeIn { get; }
        public NodeId NodeOut { get; }
        public double Current { get; set; }
        public bool IsOptimizable => false;

        public LogicalCurrentSource(CurrentSourceId id, NodeId @in, NodeId @out, double i)
        {
            Id = id; NodeIn = @in; NodeOut = @out; Current = i;
        }
    }

    private class LogicalCapacitor : ILogicalComponent
    {
        public CapacitorId Id { get; }
        public NodeId NodeA { get; }
        public NodeId NodeB { get; }
        public double Capacitance { get; set; }
        public double VoltageAcross { get; set; }  // Preserved across rebuilds
        public bool IsOptimizable => false;

        public LogicalCapacitor(CapacitorId id, NodeId a, NodeId b, double c)
        {
            Id = id; NodeA = a; NodeB = b; Capacitance = c;
        }
    }

    private class LogicalInductor : ILogicalComponent
    {
        public InductorId Id { get; }
        public NodeId NodeA { get; }
        public NodeId NodeB { get; }
        public double Inductance { get; set; }
        public double CurrentThrough { get; set; }  // Preserved across rebuilds
        public bool IsOptimizable => false;

        public LogicalInductor(InductorId id, NodeId a, NodeId b, double l)
        {
            Id = id; NodeA = a; NodeB = b; Inductance = l;
        }
    }

    private class LogicalDiode : ILogicalComponent
    {
        public DiodeId Id { get; }
        public NodeId Anode { get; }
        public NodeId Cathode { get; }
        public bool IsOptimizable => false;

        public LogicalDiode(DiodeId id, NodeId anode, NodeId cathode)
        {
            Id = id; Anode = anode; Cathode = cathode;
        }
    }

    private class LogicalTransformer : ILogicalComponent
    {
        public TransformerId Id { get; }
        public NodeId P1 { get; }
        public NodeId P2 { get; }
        public NodeId S1 { get; }
        public NodeId S2 { get; }
        public double Ratio { get; set; }
        public bool IsOptimizable => false;

        public LogicalTransformer(TransformerId id, NodeId p1, NodeId p2, NodeId s1, NodeId s2, double ratio)
        {
            Id = id; P1 = p1; P2 = p2; S1 = s1; S2 = s2; Ratio = ratio;
        }
    }

    #endregion

    #region Validation Helpers

    private void ValidateNodeExists(NodeId id)
    {
        if (id.Value != 0 && !_logicalNodes.ContainsKey(id))
            throw new InvalidNodeException(id);
    }

    private void ValidateResistance(double r)
    {
        if (r <= 0)
            throw new InvalidParameterException("resistance", r, "must be positive");
    }

    private void ValidateCapacitance(double c)
    {
        if (c <= 0)
            throw new InvalidParameterException("capacitance", c, "must be positive");
    }

    private void ValidateInductance(double l)
    {
        if (l <= 0)
            throw new InvalidParameterException("inductance", l, "must be positive");
    }

    private void ValidateRatio(double r)
    {
        if (r <= 0)
            throw new InvalidParameterException("ratio", r, "must be positive");
    }

    #endregion

    #region Node Management

    public NodeId CreateNode()
    {
        var id = new NodeId(_nextNodeId++);
        _logicalNodes[id] = new LogicalNode(id);
        return id;
    }

    public void RemoveNode(NodeId id)
    {
        if (id.Value == 0)
            throw new InvalidOperationException("Cannot remove ground node");
        if (!_logicalNodes.TryGetValue(id, out var node))
            throw new InvalidNodeException(id);
        if (node.Connections.Count > 0)
            throw new NodeInUseException(id, node.Connections.Count);
        _logicalNodes.Remove(id);
    }

    public bool NodeExists(NodeId id) =>
        id.Value == 0 || _logicalNodes.ContainsKey(id);

    #endregion

    #region Resistors

    public ResistorId AddResistor(NodeId nodeA, NodeId nodeB, double resistance)
    {
        ValidateNodeExists(nodeA);
        ValidateNodeExists(nodeB);
        ValidateResistance(resistance);

        var id = new ResistorId(_nextResistorId++);
        var component = new LogicalResistor(id, nodeA, nodeB, resistance);
        _resistors[id] = component;
        Connect(nodeA, component);
        Connect(nodeB, component);
        _isDirty = true;
        return id;
    }

    public void UpdateResistor(ResistorId id, double resistance)
    {
        if (!_resistors.TryGetValue(id, out var r))
            throw InvalidComponentException.ForResistor(id);
        ValidateResistance(resistance);

        r.Resistance = resistance;

        // Fast path: update physical component directly if not optimized
        if (!_optimizedResistors.Contains(id) && _physicalResistors.TryGetValue(id, out var phys))
        {
            phys.Resistance = resistance;
            return;
        }

        _isDirty = true;
    }

    public void RemoveResistor(ResistorId id)
    {
        if (!_resistors.TryGetValue(id, out var r))
            throw InvalidComponentException.ForResistor(id);

        Disconnect(r.NodeA, r);
        Disconnect(r.NodeB, r);
        _resistors.Remove(id);
        _physicalResistors.Remove(id);
        _optimizedResistors.Remove(id);
        _isDirty = true;
    }

    public bool ResistorExists(ResistorId id) => _resistors.ContainsKey(id);

    public double GetResistance(ResistorId id)
    {
        if (!_resistors.TryGetValue(id, out var r))
            throw InvalidComponentException.ForResistor(id);
        return r.Resistance;
    }

    public double GetResistorCurrent(ResistorId id)
    {
        if (!_resistors.TryGetValue(id, out var r))
            throw InvalidComponentException.ForResistor(id);

        double vA = GetVoltage(r.NodeA);
        double vB = GetVoltage(r.NodeB);
        return (vA - vB) / r.Resistance;
    }

    public double GetResistorPower(ResistorId id)
    {
        double current = GetResistorCurrent(id);
        double resistance = GetResistance(id);
        return current * current * resistance;
    }

    #endregion

    #region Voltage Sources

    public VoltageSourceId AddVoltageSource(NodeId nodePos, NodeId nodeNeg, double voltage)
    {
        ValidateNodeExists(nodePos);
        ValidateNodeExists(nodeNeg);

        var id = new VoltageSourceId(_nextVoltageSourceId++);
        var component = new LogicalVoltageSource(id, nodePos, nodeNeg, voltage);
        _voltageSources[id] = component;
        Connect(nodePos, component);
        Connect(nodeNeg, component);
        _isDirty = true;
        return id;
    }

    public void UpdateVoltageSource(VoltageSourceId id, double voltage)
    {
        if (!_voltageSources.TryGetValue(id, out var v))
            throw InvalidComponentException.ForVoltageSource(id);

        v.Voltage = voltage;

        // Fast path: voltage sources restamp every iteration
        if (_physicalVoltageSources.TryGetValue(id, out var phys))
        {
            phys.Voltage = voltage;
            return;
        }

        _isDirty = true;
    }

    public void RemoveVoltageSource(VoltageSourceId id)
    {
        if (!_voltageSources.TryGetValue(id, out var v))
            throw InvalidComponentException.ForVoltageSource(id);

        Disconnect(v.NodePos, v);
        Disconnect(v.NodeNeg, v);
        _voltageSources.Remove(id);
        _physicalVoltageSources.Remove(id);
        _isDirty = true;
    }

    public bool VoltageSourceExists(VoltageSourceId id) => _voltageSources.ContainsKey(id);

    public double GetVoltageSourceValue(VoltageSourceId id)
    {
        if (!_voltageSources.TryGetValue(id, out var v))
            throw InvalidComponentException.ForVoltageSource(id);
        return v.Voltage;
    }

    public double GetVoltageSourceCurrent(VoltageSourceId id)
    {
        if (!_voltageSources.TryGetValue(id, out _))
            throw InvalidComponentException.ForVoltageSource(id);

        if (_physicalVoltageSources.TryGetValue(id, out var phys))
            return phys.Current;

        return 0.0;
    }

    #endregion

    #region Current Sources

    public CurrentSourceId AddCurrentSource(NodeId nodeIn, NodeId nodeOut, double current)
    {
        ValidateNodeExists(nodeIn);
        ValidateNodeExists(nodeOut);

        var id = new CurrentSourceId(_nextCurrentSourceId++);
        var component = new LogicalCurrentSource(id, nodeIn, nodeOut, current);
        _currentSources[id] = component;
        Connect(nodeIn, component);
        Connect(nodeOut, component);
        _isDirty = true;
        return id;
    }

    public void UpdateCurrentSource(CurrentSourceId id, double current)
    {
        if (!_currentSources.TryGetValue(id, out var c))
            throw InvalidComponentException.ForCurrentSource(id);

        c.Current = current;

        // Fast path: current sources restamp every iteration
        if (_physicalCurrentSources.TryGetValue(id, out var phys))
        {
            phys.Current = current;
            return;
        }

        _isDirty = true;
    }

    public void RemoveCurrentSource(CurrentSourceId id)
    {
        if (!_currentSources.TryGetValue(id, out var c))
            throw InvalidComponentException.ForCurrentSource(id);

        Disconnect(c.NodeIn, c);
        Disconnect(c.NodeOut, c);
        _currentSources.Remove(id);
        _physicalCurrentSources.Remove(id);
        _isDirty = true;
    }

    public bool CurrentSourceExists(CurrentSourceId id) => _currentSources.ContainsKey(id);

    public double GetCurrentSourceValue(CurrentSourceId id)
    {
        if (!_currentSources.TryGetValue(id, out var c))
            throw InvalidComponentException.ForCurrentSource(id);
        return c.Current;
    }

    #endregion

    #region Capacitors

    public CapacitorId AddCapacitor(NodeId nodeA, NodeId nodeB, double capacitance)
    {
        ValidateNodeExists(nodeA);
        ValidateNodeExists(nodeB);
        ValidateCapacitance(capacitance);

        var id = new CapacitorId(_nextCapacitorId++);
        var component = new LogicalCapacitor(id, nodeA, nodeB, capacitance);
        _capacitors[id] = component;
        Connect(nodeA, component);
        Connect(nodeB, component);
        _isDirty = true;
        return id;
    }

    public void UpdateCapacitor(CapacitorId id, double capacitance)
    {
        if (!_capacitors.TryGetValue(id, out var c))
            throw InvalidComponentException.ForCapacitor(id);
        ValidateCapacitance(capacitance);

        c.Capacitance = capacitance;

        if (_physicalCapacitors.TryGetValue(id, out var phys))
        {
            phys.Capacitance = capacitance;
            return;
        }

        _isDirty = true;
    }

    public void RemoveCapacitor(CapacitorId id)
    {
        if (!_capacitors.TryGetValue(id, out var c))
            throw InvalidComponentException.ForCapacitor(id);

        Disconnect(c.NodeA, c);
        Disconnect(c.NodeB, c);
        _capacitors.Remove(id);
        _physicalCapacitors.Remove(id);
        _isDirty = true;
    }

    public bool CapacitorExists(CapacitorId id) => _capacitors.ContainsKey(id);

    public double GetCapacitance(CapacitorId id)
    {
        if (!_capacitors.TryGetValue(id, out var c))
            throw InvalidComponentException.ForCapacitor(id);
        return c.Capacitance;
    }

    public double GetCapacitorVoltage(CapacitorId id)
    {
        if (!_capacitors.TryGetValue(id, out var c))
            throw InvalidComponentException.ForCapacitor(id);

        return GetVoltage(c.NodeA) - GetVoltage(c.NodeB);
    }

    public double GetCapacitorCurrent(CapacitorId id)
    {
        // Capacitor current is C * dV/dt, but we don't track dt here
        // Return 0 for now - proper implementation would track state
        if (!_capacitors.ContainsKey(id))
            throw InvalidComponentException.ForCapacitor(id);
        return 0.0;
    }

    #endregion

    #region Inductors

    public InductorId AddInductor(NodeId nodeA, NodeId nodeB, double inductance)
    {
        ValidateNodeExists(nodeA);
        ValidateNodeExists(nodeB);
        ValidateInductance(inductance);

        var id = new InductorId(_nextInductorId++);
        var component = new LogicalInductor(id, nodeA, nodeB, inductance);
        _inductors[id] = component;
        Connect(nodeA, component);
        Connect(nodeB, component);
        _isDirty = true;
        return id;
    }

    public void UpdateInductor(InductorId id, double inductance)
    {
        if (!_inductors.TryGetValue(id, out var l))
            throw InvalidComponentException.ForInductor(id);
        ValidateInductance(inductance);

        l.Inductance = inductance;

        if (_physicalInductors.TryGetValue(id, out var phys))
        {
            phys.Inductance = inductance;
            return;
        }

        _isDirty = true;
    }

    public void RemoveInductor(InductorId id)
    {
        if (!_inductors.TryGetValue(id, out var l))
            throw InvalidComponentException.ForInductor(id);

        Disconnect(l.NodeA, l);
        Disconnect(l.NodeB, l);
        _inductors.Remove(id);
        _physicalInductors.Remove(id);
        _isDirty = true;
    }

    public bool InductorExists(InductorId id) => _inductors.ContainsKey(id);

    public double GetInductance(InductorId id)
    {
        if (!_inductors.TryGetValue(id, out var l))
            throw InvalidComponentException.ForInductor(id);
        return l.Inductance;
    }

    public double GetInductorCurrent(InductorId id)
    {
        // Inductor current state is tracked internally by the Core component
        // Return 0 for now - proper implementation would expose internal state
        if (!_inductors.ContainsKey(id))
            throw InvalidComponentException.ForInductor(id);
        return 0.0;
    }

    #endregion

    #region Diodes

    public DiodeId AddDiode(NodeId anode, NodeId cathode)
    {
        ValidateNodeExists(anode);
        ValidateNodeExists(cathode);

        var id = new DiodeId(_nextDiodeId++);
        var component = new LogicalDiode(id, anode, cathode);
        _diodes[id] = component;
        Connect(anode, component);
        Connect(cathode, component);
        _isDirty = true;
        return id;
    }

    public void RemoveDiode(DiodeId id)
    {
        if (!_diodes.TryGetValue(id, out var d))
            throw InvalidComponentException.ForDiode(id);

        Disconnect(d.Anode, d);
        Disconnect(d.Cathode, d);
        _diodes.Remove(id);
        _physicalDiodes.Remove(id);
        _isDirty = true;
    }

    public bool DiodeExists(DiodeId id) => _diodes.ContainsKey(id);

    public double GetDiodeCurrent(DiodeId id)
    {
        if (!_diodes.ContainsKey(id))
            throw InvalidComponentException.ForDiode(id);
        // Diode current depends on voltage and Shockley equation
        // Return 0 for now
        return 0.0;
    }

    public double GetDiodeVoltage(DiodeId id)
    {
        if (!_diodes.TryGetValue(id, out var d))
            throw InvalidComponentException.ForDiode(id);

        return GetVoltage(d.Anode) - GetVoltage(d.Cathode);
    }

    #endregion

    #region Transformers

    public TransformerId AddTransformer(NodeId p1, NodeId p2, NodeId s1, NodeId s2, double ratio)
    {
        ValidateNodeExists(p1);
        ValidateNodeExists(p2);
        ValidateNodeExists(s1);
        ValidateNodeExists(s2);
        ValidateRatio(ratio);

        var id = new TransformerId(_nextTransformerId++);
        var component = new LogicalTransformer(id, p1, p2, s1, s2, ratio);
        _transformers[id] = component;
        Connect(p1, component);
        Connect(p2, component);
        Connect(s1, component);
        Connect(s2, component);
        _isDirty = true;
        return id;
    }

    public void UpdateTransformer(TransformerId id, double ratio)
    {
        if (!_transformers.TryGetValue(id, out var t))
            throw InvalidComponentException.ForTransformer(id);
        ValidateRatio(ratio);

        t.Ratio = ratio;

        if (_physicalTransformers.TryGetValue(id, out var phys))
        {
            phys.Ratio = ratio;
            return;
        }

        _isDirty = true;
    }

    public void RemoveTransformer(TransformerId id)
    {
        if (!_transformers.TryGetValue(id, out var t))
            throw InvalidComponentException.ForTransformer(id);

        Disconnect(t.P1, t);
        Disconnect(t.P2, t);
        Disconnect(t.S1, t);
        Disconnect(t.S2, t);
        _transformers.Remove(id);
        _physicalTransformers.Remove(id);
        _isDirty = true;
    }

    public bool TransformerExists(TransformerId id) => _transformers.ContainsKey(id);

    public double GetTransformerRatio(TransformerId id)
    {
        if (!_transformers.TryGetValue(id, out var t))
            throw InvalidComponentException.ForTransformer(id);
        return t.Ratio;
    }

    public (double Primary, double Secondary) GetTransformerCurrents(TransformerId id)
    {
        if (!_transformers.TryGetValue(id, out _))
            throw InvalidComponentException.ForTransformer(id);

        if (_physicalTransformers.TryGetValue(id, out var phys))
            return (phys.PrimaryCurrent, phys.SecondaryCurrent);

        return (0.0, 0.0);
    }

    #endregion

    #region Simulation Control

    public void Step(double dt)
    {
        if (_bulkUpdateDepth > 0)
            throw new InvalidOperationException("Cannot call Step during bulk update");

        if (_isDirty)
        {
            Rebuild();
        }

        // Short-circuit for trivial cases; Parallel.ForEach has setup overhead
        if (_partitions.Count <= 1)
        {
            foreach (var circuit in _partitions)
            {
                circuit.Solve(dt);
            }
        }
        else
        {
            Parallel.ForEach(_partitions, circuit => circuit.Solve(dt));
        }
    }

    public void Clear()
    {
        _logicalNodes.Clear();
        _resistors.Clear();
        _voltageSources.Clear();
        _currentSources.Clear();
        _capacitors.Clear();
        _inductors.Clear();
        _diodes.Clear();
        _transformers.Clear();
        _partitions.Clear();
        _physicalNodes.Clear();
        _interpolationMap.Clear();
        _physicalResistors.Clear();
        _physicalVoltageSources.Clear();
        _physicalCurrentSources.Clear();
        _physicalCapacitors.Clear();
        _physicalInductors.Clear();
        _physicalDiodes.Clear();
        _physicalTransformers.Clear();
        _optimizedResistors.Clear();

        _nextNodeId = 1;
        _nextResistorId = 1;
        _nextVoltageSourceId = 1;
        _nextCurrentSourceId = 1;
        _nextCapacitorId = 1;
        _nextInductorId = 1;
        _nextDiodeId = 1;
        _nextTransformerId = 1;

        _isDirty = false;
    }

    public IDisposable BeginBulkUpdate()
    {
        _bulkUpdateDepth++;
        return new BulkUpdateScope(this);
    }

    private class BulkUpdateScope : IDisposable
    {
        private readonly SimulationManager _manager;
        private bool _disposed;

        public BulkUpdateScope(SimulationManager m) => _manager = m;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manager._bulkUpdateDepth--;
        }
    }

    #endregion

    #region State Readout

    public double GetVoltage(NodeId nodeId)
    {
        if (nodeId.Value == 0) return 0.0;

        if (_physicalNodes.TryGetValue(nodeId, out var node))
        {
            return node.Voltage;
        }

        if (_interpolationMap.TryGetValue(nodeId, out var info))
        {
            double vA = GetVoltage(info.NodeA);
            double vB = GetVoltage(info.NodeB);
            return vA + (vB - vA) * info.Ratio;
        }

        if (!_logicalNodes.ContainsKey(nodeId))
            throw new InvalidNodeException(nodeId);

        return 0.0;
    }

    #endregion

    #region Diagnostics

    public int PartitionCount => _partitions.Count;

    public bool IsNodeOptimized(NodeId id) => _interpolationMap.ContainsKey(id);

    public SimulationStats GetStats()
    {
        return new SimulationStats(
            _partitions.Sum(p => p.LastIterations),
            _partitions.Count,
            _physicalNodes.Count,
            _interpolationMap.Count
        );
    }

    #endregion

    #region Internal Helpers

    private void Connect(NodeId nodeId, ILogicalComponent component)
    {
        if (nodeId.Value == 0) return; // Ground doesn't track connections
        if (_logicalNodes.TryGetValue(nodeId, out var node))
        {
            node.Connections.Add(component);
        }
    }

    private void Disconnect(NodeId nodeId, ILogicalComponent component)
    {
        if (nodeId.Value == 0) return;
        if (_logicalNodes.TryGetValue(nodeId, out var node))
        {
            node.Connections.Remove(component);
        }
    }

    private void SaveTransientState()
    {
        // Save capacitor state from physical to logical
        foreach (var kvp in _physicalCapacitors)
        {
            if (_capacitors.TryGetValue(kvp.Key, out var logical))
            {
                logical.VoltageAcross = kvp.Value.VoltageAcross;
            }
        }

        // Save inductor state from physical to logical
        foreach (var kvp in _physicalInductors)
        {
            if (_inductors.TryGetValue(kvp.Key, out var logical))
            {
                logical.CurrentThrough = kvp.Value.CurrentThrough;
            }
        }
    }

    private void Rebuild()
    {
        // Save state from physical components to logical components before clearing
        SaveTransientState();

        _partitions.Clear();
        _physicalNodes.Clear();
        _interpolationMap.Clear();
        _physicalResistors.Clear();
        _physicalVoltageSources.Clear();
        _physicalCurrentSources.Clear();
        _physicalCapacitors.Clear();
        _physicalInductors.Clear();
        _physicalDiodes.Clear();
        _physicalTransformers.Clear();
        _optimizedResistors.Clear();

        // 1. Optimization Phase
        var (optimizedComponents, optimizedAdjacency) = Optimize();

        // 2. Partitioning Phase
        var visited = new HashSet<NodeId>();

        foreach (var startNodeId in optimizedAdjacency.Keys)
        {
            if (startNodeId.Value == 0) continue;
            if (visited.Contains(startNodeId)) continue;

            var partitionNodes = new HashSet<NodeId>();
            var queue = new Queue<NodeId>();

            queue.Enqueue(startNodeId);
            visited.Add(startNodeId);
            partitionNodes.Add(startNodeId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (optimizedAdjacency.TryGetValue(current, out var connectedComponents))
                {
                    foreach (var component in connectedComponents)
                    {
                        var neighbors = GetNeighbors(component, current);
                        foreach (var neighbor in neighbors)
                        {
                            if (neighbor.Value == 0)
                            {
                                partitionNodes.Add(neighbor);
                            }
                            else if (!visited.Contains(neighbor))
                            {
                                visited.Add(neighbor);
                                partitionNodes.Add(neighbor);
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }

            BuildPartition(partitionNodes, optimizedComponents);
        }

        _isDirty = false;
    }

    private IEnumerable<NodeId> GetNeighbors(ILogicalComponent component, NodeId current)
    {
        switch (component)
        {
            case LogicalResistor r:
                yield return r.NodeA == current ? r.NodeB : r.NodeA;
                break;
            case LogicalVoltageSource v:
                yield return v.NodePos == current ? v.NodeNeg : v.NodePos;
                break;
            case LogicalCurrentSource c:
                yield return c.NodeIn == current ? c.NodeOut : c.NodeIn;
                break;
            case LogicalCapacitor cap:
                yield return cap.NodeA == current ? cap.NodeB : cap.NodeA;
                break;
            case LogicalInductor ind:
                yield return ind.NodeA == current ? ind.NodeB : ind.NodeA;
                break;
            case LogicalDiode d:
                yield return d.Anode == current ? d.Cathode : d.Anode;
                break;
            case LogicalTransformer t:
                if (t.P1 == current) yield return t.P2;
                if (t.P2 == current) yield return t.P1;
                if (t.S1 == current) yield return t.S2;
                if (t.S2 == current) yield return t.S1;
                if (t.P1 == current || t.P2 == current)
                {
                    yield return t.S1;
                    yield return t.S2;
                }
                if (t.S1 == current || t.S2 == current)
                {
                    yield return t.P1;
                    yield return t.P2;
                }
                break;
        }
    }

    private (List<ILogicalComponent>, Dictionary<NodeId, List<ILogicalComponent>>) Optimize()
    {
        var allComponents = _resistors.Values.Cast<ILogicalComponent>()
            .Concat(_voltageSources.Values)
            .Concat(_currentSources.Values)
            .Concat(_capacitors.Values)
            .Concat(_inductors.Values)
            .Concat(_diodes.Values)
            .Concat(_transformers.Values)
            .ToList();

        if (!EnableLineOptimization)
        {
            var adj = new Dictionary<NodeId, List<ILogicalComponent>>();
            foreach (var node in _logicalNodes)
            {
                adj[node.Key] = new List<ILogicalComponent>(node.Value.Connections);
            }
            return (allComponents, adj);
        }

        var optimizedComponents = new List<ILogicalComponent>();
        var consumedComponents = new HashSet<ILogicalComponent>();
        var optimizedAdjacency = new Dictionary<NodeId, List<ILogicalComponent>>();

        void AddToAdj(NodeId n, ILogicalComponent c)
        {
            if (!optimizedAdjacency.TryGetValue(n, out var list))
            {
                list = new List<ILogicalComponent>();
                optimizedAdjacency[n] = list;
            }
            list.Add(c);
        }

        // Add non-resistor components (they can't be optimized)
        foreach (var c in _voltageSources.Values) { optimizedComponents.Add(c); AddToAdj(c.NodePos, c); AddToAdj(c.NodeNeg, c); }
        foreach (var c in _currentSources.Values) { optimizedComponents.Add(c); AddToAdj(c.NodeIn, c); AddToAdj(c.NodeOut, c); }
        foreach (var c in _capacitors.Values) { optimizedComponents.Add(c); AddToAdj(c.NodeA, c); AddToAdj(c.NodeB, c); }
        foreach (var c in _inductors.Values) { optimizedComponents.Add(c); AddToAdj(c.NodeA, c); AddToAdj(c.NodeB, c); }
        foreach (var c in _diodes.Values) { optimizedComponents.Add(c); AddToAdj(c.Anode, c); AddToAdj(c.Cathode, c); }
        foreach (var c in _transformers.Values) { optimizedComponents.Add(c); AddToAdj(c.P1, c); AddToAdj(c.P2, c); AddToAdj(c.S1, c); AddToAdj(c.S2, c); }

        foreach (var r in _resistors.Values)
        {
            if (consumedComponents.Contains(r)) continue;

            var chainNodes = new LinkedList<NodeId>();
            var chainResistors = new LinkedList<LogicalResistor>();

            chainNodes.AddLast(r.NodeA);
            chainNodes.AddLast(r.NodeB);
            chainResistors.AddLast(r);
            consumedComponents.Add(r);

            // Extend Forward (from NodeB)
            var currentForwardNode = chainNodes.Last!.Value;
            while (currentForwardNode.Value != 0 && _logicalNodes.ContainsKey(currentForwardNode) && IsLineNode(_logicalNodes[currentForwardNode]))
            {
                var currentNodeLogical = _logicalNodes[currentForwardNode];
                var connectedResistors = currentNodeLogical.Connections.OfType<LogicalResistor>().ToList();

                if (connectedResistors.Count != 2) break;

                var nextR = connectedResistors.FirstOrDefault(x => x != chainResistors.Last!.Value);
                if (nextR == null || consumedComponents.Contains(nextR)) break;

                chainResistors.AddLast(nextR);
                consumedComponents.Add(nextR);
                currentForwardNode = nextR.NodeA == currentNodeLogical.Id ? nextR.NodeB : nextR.NodeA;
                chainNodes.AddLast(currentForwardNode);
            }

            // Extend Backward (from NodeA)
            var currentBackwardNode = chainNodes.First!.Value;
            while (currentBackwardNode.Value != 0 && _logicalNodes.ContainsKey(currentBackwardNode) && IsLineNode(_logicalNodes[currentBackwardNode]))
            {
                var currentNodeLogical = _logicalNodes[currentBackwardNode];
                var connectedResistors = currentNodeLogical.Connections.OfType<LogicalResistor>().ToList();

                if (connectedResistors.Count != 2) break;

                var prevR = connectedResistors.FirstOrDefault(x => x != chainResistors.First!.Value);
                if (prevR == null || consumedComponents.Contains(prevR)) break;

                chainResistors.AddFirst(prevR);
                consumedComponents.Add(prevR);
                currentBackwardNode = prevR.NodeA == currentNodeLogical.Id ? prevR.NodeB : prevR.NodeA;
                chainNodes.AddFirst(currentBackwardNode);
            }

            if (chainResistors.Count > 1)
            {
                // Merge chain
                double totalR = chainResistors.Sum(x => x.Resistance);
                var startNode = chainNodes.First!.Value;
                var endNode = chainNodes.Last!.Value;

                var mergedR = new LogicalResistor(new ResistorId(-1), startNode, endNode, totalR);
                optimizedComponents.Add(mergedR);
                AddToAdj(startNode, mergedR);
                AddToAdj(endNode, mergedR);

                // Mark original resistors as optimized
                foreach (var cr in chainResistors)
                {
                    _optimizedResistors.Add(cr.Id);
                }

                // Interpolation Map
                double currentR = 0;
                var nodeNode = chainNodes.First;
                var resNode = chainResistors.First;

                while (resNode != null)
                {
                    currentR += resNode.Value.Resistance;
                    var intermediateNode = nodeNode!.Next!.Value;
                    if (intermediateNode != endNode)
                    {
                        double ratio = currentR / totalR;
                        _interpolationMap[intermediateNode] = new InterpolationInfo(startNode, endNode, ratio);
                    }

                    resNode = resNode.Next;
                    nodeNode = nodeNode.Next;
                }
            }
            else
            {
                // Single resistor, no merge
                optimizedComponents.Add(r);
                AddToAdj(r.NodeA, r);
                AddToAdj(r.NodeB, r);
            }
        }

        return (optimizedComponents, optimizedAdjacency);
    }

    private bool IsLineNode(LogicalNode node)
    {
        if (node.Connections.Count != 2) return false;
        return node.Connections.All(c => c is LogicalResistor);
    }

    private void BuildPartition(HashSet<NodeId> nodes, List<ILogicalComponent> allComponents)
    {
        var circuit = new Circuit();
        _partitions.Add(circuit);

        if (nodes.Contains(Ground))
        {
            _physicalNodes[Ground] = circuit.Ground;
        }

        foreach (var nodeId in nodes)
        {
            if (nodeId.Value == 0) continue;
            var physNode = circuit.AddNode();
            _physicalNodes[nodeId] = physNode;
        }

        foreach (var component in allComponents)
        {
            if (IsComponentInPartition(component, nodes))
            {
                AddComponentToCircuit(circuit, component);
            }
        }

        circuit.BuildSystem();
    }

    private bool IsComponentInPartition(ILogicalComponent component, HashSet<NodeId> nodes)
    {
        return component switch
        {
            LogicalResistor r => nodes.Contains(r.NodeA) && nodes.Contains(r.NodeB),
            LogicalVoltageSource v => nodes.Contains(v.NodePos) && nodes.Contains(v.NodeNeg),
            LogicalCurrentSource c => nodes.Contains(c.NodeIn) && nodes.Contains(c.NodeOut),
            LogicalCapacitor cap => nodes.Contains(cap.NodeA) && nodes.Contains(cap.NodeB),
            LogicalInductor ind => nodes.Contains(ind.NodeA) && nodes.Contains(ind.NodeB),
            LogicalDiode d => nodes.Contains(d.Anode) && nodes.Contains(d.Cathode),
            LogicalTransformer t => nodes.Contains(t.P1) && nodes.Contains(t.P2) && nodes.Contains(t.S1) && nodes.Contains(t.S2),
            _ => false
        };
    }

    private void AddComponentToCircuit(Circuit circuit, ILogicalComponent component)
    {
        switch (component)
        {
            case LogicalResistor r:
                var phyR = new Resistor(GetPhysNode(r.NodeA, circuit), GetPhysNode(r.NodeB, circuit), r.Resistance);
                circuit.AddComponent(phyR);
                if (r.Id.Value >= 0) _physicalResistors[r.Id] = phyR;
                break;
            case LogicalVoltageSource v:
                var phyV = new VoltageSource(GetPhysNode(v.NodePos, circuit), GetPhysNode(v.NodeNeg, circuit), v.Voltage);
                circuit.AddComponent(phyV);
                _physicalVoltageSources[v.Id] = phyV;
                break;
            case LogicalCurrentSource c:
                var phyC = new CurrentSource(GetPhysNode(c.NodeIn, circuit), GetPhysNode(c.NodeOut, circuit), c.Current);
                circuit.AddComponent(phyC);
                _physicalCurrentSources[c.Id] = phyC;
                break;
            case LogicalCapacitor cap:
                var phyCap = new Capacitor(GetPhysNode(cap.NodeA, circuit), GetPhysNode(cap.NodeB, circuit), cap.Capacitance);
                phyCap.VoltageAcross = cap.VoltageAcross;  // Restore state
                circuit.AddComponent(phyCap);
                _physicalCapacitors[cap.Id] = phyCap;
                break;
            case LogicalInductor ind:
                var phyInd = new Inductor(GetPhysNode(ind.NodeA, circuit), GetPhysNode(ind.NodeB, circuit), ind.Inductance);
                phyInd.CurrentThrough = ind.CurrentThrough;  // Restore state
                circuit.AddComponent(phyInd);
                _physicalInductors[ind.Id] = phyInd;
                break;
            case LogicalDiode d:
                var phyD = new Diode(GetPhysNode(d.Anode, circuit), GetPhysNode(d.Cathode, circuit));
                circuit.AddComponent(phyD);
                _physicalDiodes[d.Id] = phyD;
                break;
            case LogicalTransformer t:
                var phyT = new Transformer(
                    GetPhysNode(t.P1, circuit), GetPhysNode(t.P2, circuit),
                    GetPhysNode(t.S1, circuit), GetPhysNode(t.S2, circuit), t.Ratio);
                circuit.AddComponent(phyT);
                _physicalTransformers[t.Id] = phyT;
                break;
        }
    }

    private Node GetPhysNode(NodeId id, Circuit circuit)
    {
        if (id.Value == 0) return circuit.Ground;
        return _physicalNodes[id];
    }

    #endregion
}
