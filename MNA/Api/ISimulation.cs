using System;
using Sparky.MNA.Api.Limits;

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

    // Controlled Sources

    // VCVS (Voltage-Controlled Voltage Source)
    /// <summary>Adds a VCVS. Output voltage = gain × input voltage.</summary>
    VcvsId AddVCVS(NodeId ctrlPos, NodeId ctrlNeg, NodeId outPos, NodeId outNeg, double gain);

    /// <summary>Updates the VCVS voltage gain.</summary>
    void UpdateVCVS(VcvsId id, double gain);

    /// <summary>Removes a VCVS.</summary>
    void RemoveVCVS(VcvsId id);

    /// <summary>Returns true if the VCVS exists.</summary>
    bool VCVSExists(VcvsId id);

    /// <summary>Gets the VCVS voltage gain.</summary>
    double GetVCVSGain(VcvsId id);

    /// <summary>Gets the output current flowing through the VCVS.</summary>
    double GetVCVSCurrent(VcvsId id);

    // VCCS (Voltage-Controlled Current Source)
    /// <summary>Adds a VCCS. Output current = transconductance × input voltage.</summary>
    VccsId AddVCCS(NodeId ctrlPos, NodeId ctrlNeg, NodeId outPos, NodeId outNeg, double transconductance);

    /// <summary>Updates the VCCS transconductance.</summary>
    void UpdateVCCS(VccsId id, double transconductance);

    /// <summary>Removes a VCCS.</summary>
    void RemoveVCCS(VccsId id);

    /// <summary>Returns true if the VCCS exists.</summary>
    bool VCCSExists(VccsId id);

    /// <summary>Gets the VCCS transconductance.</summary>
    double GetVCCSTransconductance(VccsId id);

    /// <summary>Gets the output current of the VCCS (= transconductance × input voltage).</summary>
    double GetVCCSCurrent(VccsId id);

    // CCVS (Current-Controlled Voltage Source)
    /// <summary>Adds a CCVS. Output voltage = transresistance × input current. Input is short-circuited.</summary>
    CcvsId AddCCVS(NodeId ctrlPos, NodeId ctrlNeg, NodeId outPos, NodeId outNeg, double transresistance);

    /// <summary>Updates the CCVS transresistance.</summary>
    void UpdateCCVS(CcvsId id, double transresistance);

    /// <summary>Removes a CCVS.</summary>
    void RemoveCCVS(CcvsId id);

    /// <summary>Returns true if the CCVS exists.</summary>
    bool CCVSExists(CcvsId id);

    /// <summary>Gets the CCVS transresistance.</summary>
    double GetCCVSTransresistance(CcvsId id);

    /// <summary>Gets the sensed input current of the CCVS.</summary>
    double GetCCVSInputCurrent(CcvsId id);

    /// <summary>Gets the output current of the CCVS.</summary>
    double GetCCVSOutputCurrent(CcvsId id);

    // CCCS (Current-Controlled Current Source)
    /// <summary>Adds a CCCS. Output current = gain × input current. Input is short-circuited.</summary>
    CccsId AddCCCS(NodeId ctrlPos, NodeId ctrlNeg, NodeId outPos, NodeId outNeg, double gain);

    /// <summary>Updates the CCCS current gain.</summary>
    void UpdateCCCS(CccsId id, double gain);

    /// <summary>Removes a CCCS.</summary>
    void RemoveCCCS(CccsId id);

    /// <summary>Returns true if the CCCS exists.</summary>
    bool CCCSExists(CccsId id);

    /// <summary>Gets the CCCS current gain.</summary>
    double GetCCCSGain(CccsId id);

    /// <summary>Gets the sensed input current of the CCCS.</summary>
    double GetCCCSInputCurrent(CccsId id);

    /// <summary>Gets the output current of the CCCS (= gain × input current).</summary>
    double GetCCCSOutputCurrent(CccsId id);

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

    // === Limit Management ===

    /// <summary>
    /// Registers a callback for limit events. Returns a disposable token for unregistration.
    /// </summary>
    IDisposable OnLimitEvent(LimitEventHandler handler);

    /// <summary>Gets the cumulative simulation time (sum of all dt values passed to Step).</summary>
    double SimulationTime { get; }

    // Resistor Limits
    /// <summary>Sets a limit on a resistor.</summary>
    void SetResistorLimit(ResistorId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a resistor.</summary>
    void ClearResistorLimit(ResistorId id, LimitKind kind);
    /// <summary>Gets a limit config from a resistor, or null if not set.</summary>
    LimitConfig? GetResistorLimit(ResistorId id, LimitKind kind);

    // Voltage Source Limits
    /// <summary>Sets a limit on a voltage source.</summary>
    void SetVoltageSourceLimit(VoltageSourceId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a voltage source.</summary>
    void ClearVoltageSourceLimit(VoltageSourceId id, LimitKind kind);
    /// <summary>Gets a limit config from a voltage source, or null if not set.</summary>
    LimitConfig? GetVoltageSourceLimit(VoltageSourceId id, LimitKind kind);

    // Current Source Limits
    /// <summary>Sets a limit on a current source.</summary>
    void SetCurrentSourceLimit(CurrentSourceId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a current source.</summary>
    void ClearCurrentSourceLimit(CurrentSourceId id, LimitKind kind);
    /// <summary>Gets a limit config from a current source, or null if not set.</summary>
    LimitConfig? GetCurrentSourceLimit(CurrentSourceId id, LimitKind kind);

    // Capacitor Limits
    /// <summary>Sets a limit on a capacitor.</summary>
    void SetCapacitorLimit(CapacitorId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a capacitor.</summary>
    void ClearCapacitorLimit(CapacitorId id, LimitKind kind);
    /// <summary>Gets a limit config from a capacitor, or null if not set.</summary>
    LimitConfig? GetCapacitorLimit(CapacitorId id, LimitKind kind);

    // Inductor Limits
    /// <summary>Sets a limit on an inductor.</summary>
    void SetInductorLimit(InductorId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from an inductor.</summary>
    void ClearInductorLimit(InductorId id, LimitKind kind);
    /// <summary>Gets a limit config from an inductor, or null if not set.</summary>
    LimitConfig? GetInductorLimit(InductorId id, LimitKind kind);

    // Diode Limits
    /// <summary>Sets a limit on a diode.</summary>
    void SetDiodeLimit(DiodeId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a diode.</summary>
    void ClearDiodeLimit(DiodeId id, LimitKind kind);
    /// <summary>Gets a limit config from a diode, or null if not set.</summary>
    LimitConfig? GetDiodeLimit(DiodeId id, LimitKind kind);

    // Transformer Limits
    /// <summary>Sets a limit on a transformer.</summary>
    void SetTransformerLimit(TransformerId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a transformer.</summary>
    void ClearTransformerLimit(TransformerId id, LimitKind kind);
    /// <summary>Gets a limit config from a transformer, or null if not set.</summary>
    LimitConfig? GetTransformerLimit(TransformerId id, LimitKind kind);

    // Switch Limits
    /// <summary>Sets a limit on a switch.</summary>
    void SetSwitchLimit(SwitchId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a switch.</summary>
    void ClearSwitchLimit(SwitchId id, LimitKind kind);
    /// <summary>Gets a limit config from a switch, or null if not set.</summary>
    LimitConfig? GetSwitchLimit(SwitchId id, LimitKind kind);

    // VCVS Limits
    /// <summary>Sets a limit on a VCVS.</summary>
    void SetVCVSLimit(VcvsId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a VCVS.</summary>
    void ClearVCVSLimit(VcvsId id, LimitKind kind);
    /// <summary>Gets a limit config from a VCVS, or null if not set.</summary>
    LimitConfig? GetVCVSLimit(VcvsId id, LimitKind kind);

    // VCCS Limits
    /// <summary>Sets a limit on a VCCS.</summary>
    void SetVCCSLimit(VccsId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a VCCS.</summary>
    void ClearVCCSLimit(VccsId id, LimitKind kind);
    /// <summary>Gets a limit config from a VCCS, or null if not set.</summary>
    LimitConfig? GetVCCSLimit(VccsId id, LimitKind kind);

    // CCVS Limits
    /// <summary>Sets a limit on a CCVS.</summary>
    void SetCCVSLimit(CcvsId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a CCVS.</summary>
    void ClearCCVSLimit(CcvsId id, LimitKind kind);
    /// <summary>Gets a limit config from a CCVS, or null if not set.</summary>
    LimitConfig? GetCCVSLimit(CcvsId id, LimitKind kind);

    // CCCS Limits
    /// <summary>Sets a limit on a CCCS.</summary>
    void SetCCCSLimit(CccsId id, LimitKind kind, LimitConfig config);
    /// <summary>Clears a limit from a CCCS.</summary>
    void ClearCCCSLimit(CccsId id, LimitKind kind);
    /// <summary>Gets a limit config from a CCCS, or null if not set.</summary>
    LimitConfig? GetCCCSLimit(CccsId id, LimitKind kind);

    // === Energy Accounting ===

    /// <summary>Gets cumulative energy delivered by a voltage source (Joules). Positive = delivering power.</summary>
    double GetVoltageSourceEnergy(VoltageSourceId id);

    /// <summary>Gets cumulative energy delivered by a current source (Joules). Positive = delivering power.</summary>
    double GetCurrentSourceEnergy(CurrentSourceId id);

    /// <summary>Gets cumulative energy dissipated by a resistor (Joules). Always positive.</summary>
    double GetResistorEnergy(ResistorId id);

    /// <summary>Gets cumulative energy dissipated by a diode (Joules). Always positive.</summary>
    double GetDiodeEnergy(DiodeId id);

    /// <summary>Gets cumulative net energy absorbed by a capacitor (Joules). Positive = charging, negative = discharging.</summary>
    double GetCapacitorEnergy(CapacitorId id);

    /// <summary>Gets cumulative net energy absorbed by an inductor (Joules). Positive = storing, negative = releasing.</summary>
    double GetInductorEnergy(InductorId id);

    /// <summary>Resets all energy counters to zero.</summary>
    void ResetEnergyCounters();

    /// <summary>Resets the energy counter for a specific resistor.</summary>
    void ResetEnergyCounter(ResistorId id);

    /// <summary>Resets the energy counter for a specific voltage source.</summary>
    void ResetEnergyCounter(VoltageSourceId id);

    /// <summary>Resets the energy counter for a specific current source.</summary>
    void ResetEnergyCounter(CurrentSourceId id);

    /// <summary>Resets the energy counter for a specific capacitor.</summary>
    void ResetEnergyCounter(CapacitorId id);

    /// <summary>Resets the energy counter for a specific inductor.</summary>
    void ResetEnergyCounter(InductorId id);

    /// <summary>Resets the energy counter for a specific diode.</summary>
    void ResetEnergyCounter(DiodeId id);
}
