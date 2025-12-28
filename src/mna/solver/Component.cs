using CSparse.Storage;

namespace Sparky.Mna.Solver;

public abstract class Component {
    public Node Node1 { get; }
    public Node Node2 { get; }

    // If true, this component adds an extra row/column to the matrix (e.g. Voltage Source, Inductor)
    public virtual bool HasExtraEquation => false;

    // Number of extra equations this component adds (usually 1 if HasExtraEquation, 0 otherwise)
    // Override for components like CCVS that need multiple auxiliary rows.
    public virtual int ExtraEquationCount => HasExtraEquation ? 1 : 0;

    // If true, this component is non-linear and participates in Newton iteration
    public virtual bool IsNonLinear => false;

    // If true, this component requires iterative solving (defaults to non-linear)
    public virtual bool RequiresIteration => IsNonLinear;

    // If true, this component must be re-stamped every Solve call even when dt/topology are unchanged
    public virtual bool RequiresPerStepRestamp => false;

    // Assigned index in the matrix (if HasExtraEquation is true)
    public int MatrixIndex { get; set; } = -1;

    protected Component(Node node1, Node node2) {
        Node1 = node1;
        Node2 = node2;
    }

    // Stamp the component into the matrix A and vector Z
    // dt is the time step in seconds
    public abstract void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0);

    // Update internal state after solve (for transient analysis)
    public virtual void UpdateState(double[] x, double dt) { }

    // Update operating point during Newton-Raphson iteration (for non-linear components)
    public virtual void UpdateOperatingPoint(double[] x) { }

    /// <summary>
    /// Energy transferred through this component during the last time step (Joules).
    /// Positive = energy delivered (sources) or dissipated (resistors/diodes).
    /// Computed by AccumulateEnergy() after solve convergence.
    /// </summary>
    public double EnergyDelta { get; protected set; }

    /// <summary>
    /// Compute the energy transferred through this component for a time step.
    /// Called after solve convergence. Sets EnergyDelta = P × dt.
    /// </summary>
    /// <param name="x">Solution vector containing node voltages and branch currents</param>
    /// <param name="dt">Time step in seconds</param>
    public virtual void AccumulateEnergy(double[] x, double dt) {
        // Default: no energy tracking (e.g., wires, VCVS, etc.)
        EnergyDelta = 0;
    }
}

