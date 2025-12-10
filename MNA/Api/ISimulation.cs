using System;

namespace Sparky.MNA.Api;

/// <summary>
/// Statistics about the simulation state.
/// </summary>
public readonly record struct SimulationStats(
    int TotalIterations,
    int PartitionCount,
    int PhysicalNodeCount,
    int OptimizedNodeCount
);

/// <summary>
/// The primary interface for circuit simulation.
/// Thread safety: All methods must be called from a single thread,
/// except Step() which may be called from a worker thread after all modifications are complete.
/// </summary>
public interface ISimulation
{
    // Ground
    /// <summary>The ground node (always NodeId(0), voltage = 0V).</summary>
    NodeId Ground { get; }

    // Node Management
    /// <summary>Creates a new node and returns its ID.</summary>
    NodeId CreateNode();

    /// <summary>Removes a node. Throws if the node has connected components.</summary>
    void RemoveNode(NodeId id);

    /// <summary>Returns true if the node exists.</summary>
    bool NodeExists(NodeId id);

    // Resistors
    /// <summary>Adds a resistor between two nodes. Variable resistors skip line optimization.</summary>
    ResistorId AddResistor(NodeId nodeA, NodeId nodeB, double resistance, bool isVariable = false);

    /// <summary>Updates the resistance value. May be done in-place if not optimized.</summary>
    void UpdateResistor(ResistorId id, double resistance);

    /// <summary>Removes a resistor.</summary>
    void RemoveResistor(ResistorId id);

    /// <summary>Returns true if the resistor exists.</summary>
    bool ResistorExists(ResistorId id);

    /// <summary>Gets the resistance value.</summary>
    double GetResistance(ResistorId id);

    /// <summary>Gets the current flowing through the resistor (from nodeA to nodeB).</summary>
    double GetResistorCurrent(ResistorId id);

    /// <summary>Gets the power dissipated by the resistor (I^2 * R).</summary>
    double GetResistorPower(ResistorId id);

    // Voltage Sources
    /// <summary>Adds a voltage source (positive terminal at nodePos).</summary>
    VoltageSourceId AddVoltageSource(NodeId nodePos, NodeId nodeNeg, double voltage);

    /// <summary>Updates the voltage value.</summary>
    void UpdateVoltageSource(VoltageSourceId id, double voltage);

    /// <summary>Removes a voltage source.</summary>
    void RemoveVoltageSource(VoltageSourceId id);

    /// <summary>Returns true if the voltage source exists.</summary>
    bool VoltageSourceExists(VoltageSourceId id);

    /// <summary>Gets the voltage value.</summary>
    double GetVoltageSourceValue(VoltageSourceId id);

    /// <summary>Gets the current flowing through the voltage source.</summary>
    double GetVoltageSourceCurrent(VoltageSourceId id);

    // Current Sources
    /// <summary>Adds a current source (current flows from nodeIn to nodeOut).</summary>
    CurrentSourceId AddCurrentSource(NodeId nodeIn, NodeId nodeOut, double current);

    /// <summary>Updates the current value.</summary>
    void UpdateCurrentSource(CurrentSourceId id, double current);

    /// <summary>Removes a current source.</summary>
    void RemoveCurrentSource(CurrentSourceId id);

    /// <summary>Returns true if the current source exists.</summary>
    bool CurrentSourceExists(CurrentSourceId id);

    /// <summary>Gets the current value.</summary>
    double GetCurrentSourceValue(CurrentSourceId id);

    // Capacitors
    /// <summary>Adds a capacitor between two nodes.</summary>
    CapacitorId AddCapacitor(NodeId nodeA, NodeId nodeB, double capacitance);

    /// <summary>Updates the capacitance value.</summary>
    void UpdateCapacitor(CapacitorId id, double capacitance);

    /// <summary>Removes a capacitor.</summary>
    void RemoveCapacitor(CapacitorId id);

    /// <summary>Returns true if the capacitor exists.</summary>
    bool CapacitorExists(CapacitorId id);

    /// <summary>Gets the capacitance value.</summary>
    double GetCapacitance(CapacitorId id);

    /// <summary>Gets the voltage across the capacitor.</summary>
    double GetCapacitorVoltage(CapacitorId id);

    /// <summary>Gets the current through the capacitor.</summary>
    double GetCapacitorCurrent(CapacitorId id);

    /// <summary>Sets the voltage across the capacitor (nodeA - nodeB). Use for initial conditions.</summary>
    void SetCapacitorVoltage(CapacitorId id, double voltage);

    // Inductors
    /// <summary>Adds an inductor between two nodes.</summary>
    InductorId AddInductor(NodeId nodeA, NodeId nodeB, double inductance);

    /// <summary>Updates the inductance value.</summary>
    void UpdateInductor(InductorId id, double inductance);

    /// <summary>Removes an inductor.</summary>
    void RemoveInductor(InductorId id);

    /// <summary>Returns true if the inductor exists.</summary>
    bool InductorExists(InductorId id);

    /// <summary>Gets the inductance value.</summary>
    double GetInductance(InductorId id);

    /// <summary>Gets the current through the inductor.</summary>
    double GetInductorCurrent(InductorId id);

    /// <summary>Sets the current through the inductor (from nodeA to nodeB). Use for initial conditions.</summary>
    void SetInductorCurrent(InductorId id, double current);

    // Diodes
    /// <summary>Adds a diode (current flows from anode to cathode when forward biased).</summary>
    DiodeId AddDiode(NodeId anode, NodeId cathode);

    /// <summary>Removes a diode.</summary>
    void RemoveDiode(DiodeId id);

    /// <summary>Returns true if the diode exists.</summary>
    bool DiodeExists(DiodeId id);

    /// <summary>Gets the current through the diode.</summary>
    double GetDiodeCurrent(DiodeId id);

    /// <summary>Gets the voltage across the diode (anode - cathode).</summary>
    double GetDiodeVoltage(DiodeId id);

    // Transformers
    /// <summary>Adds an ideal transformer. Primary: p1-p2, Secondary: s1-s2, ratio = Ns/Np.</summary>
    TransformerId AddTransformer(NodeId p1, NodeId p2, NodeId s1, NodeId s2, double ratio);

    /// <summary>Updates the transformer turns ratio.</summary>
    void UpdateTransformer(TransformerId id, double ratio);

    /// <summary>Removes a transformer.</summary>
    void RemoveTransformer(TransformerId id);

    /// <summary>Returns true if the transformer exists.</summary>
    bool TransformerExists(TransformerId id);

    /// <summary>Gets the transformer turns ratio.</summary>
    double GetTransformerRatio(TransformerId id);

    /// <summary>Gets the primary and secondary currents.</summary>
    (double Primary, double Secondary) GetTransformerCurrents(TransformerId id);

    // Switches
    /// <summary>Adds a switch between two nodes. Initial state defaults to open.</summary>
    SwitchId AddSwitch(NodeId nodeA, NodeId nodeB, bool initiallyClosed = false);

    /// <summary>Sets the switch state (true = closed/conducting, false = open).</summary>
    void SetSwitchState(SwitchId id, bool closed);

    /// <summary>Toggles the switch state (open becomes closed, closed becomes open).</summary>
    void ToggleSwitch(SwitchId id);

    /// <summary>Removes a switch.</summary>
    void RemoveSwitch(SwitchId id);

    /// <summary>Returns true if the switch exists.</summary>
    bool SwitchExists(SwitchId id);

    /// <summary>Gets the current switch state (true = closed/conducting).</summary>
    bool GetSwitchState(SwitchId id);

    /// <summary>Gets the current flowing through the switch (from nodeA to nodeB).</summary>
    double GetSwitchCurrent(SwitchId id);

    // Simulation Control
    /// <summary>Advances the simulation by dt seconds.</summary>
    void Step(double dt);

    /// <summary>Clears the entire simulation.</summary>
    void Clear();

    /// <summary>
    /// Begins a bulk update scope. Circuit rebuilding is deferred until the scope is disposed.
    /// Step() cannot be called during a bulk update.
    /// </summary>
    IDisposable BeginBulkUpdate();

    // State Readout
    /// <summary>
    /// Gets the voltage at a logical node.
    /// If the node was optimized away, returns an interpolated value.
    /// </summary>
    double GetVoltage(NodeId nodeId);

    // Diagnostics
    /// <summary>Gets the number of independent circuit partitions.</summary>
    int PartitionCount { get; }

    /// <summary>Returns true if the node was optimized away (part of a merged resistor chain).</summary>
    bool IsNodeOptimized(NodeId id);

    /// <summary>Gets simulation statistics.</summary>
    SimulationStats GetStats();

    // Optimization Control
    /// <summary>
    /// When true, series resistor chains are merged to reduce matrix size.
    /// Intermediate node voltages are computed via interpolation.
    /// </summary>
    bool EnableLineOptimization { get; set; }
}
