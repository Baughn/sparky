using CSparse.Storage;

namespace Sparky.Mna.Solver;

/// <summary>
/// Voltage-Controlled Voltage Source (VCVS).
/// Output voltage is proportional to input voltage:
///   V_out = Gain × (V_ctrlPos - V_ctrlNeg)
///
/// The input draws no current (infinite input impedance).
/// The output is an ideal voltage source.
/// Uses one auxiliary equation to track output current.
/// </summary>
public class VCVS : Component {
    public Node ControlPos { get; }
    public Node ControlNeg { get; }

    /// <summary>
    /// Voltage gain (dimensionless).
    /// V_out = Gain × V_in
    /// </summary>
    public double Gain { get; set; }

    /// <summary>
    /// Current flowing through the output (from outPos to outNeg).
    /// Populated by Circuit.Solve() after each solution.
    /// </summary>
    public double Current { get; internal set; }

    public override bool HasExtraEquation => true;
    public override bool RequiresPerStepRestamp => true;

    /// <summary>
    /// Creates a VCVS.
    /// </summary>
    /// <param name="ctrlPos">Positive control (input) node</param>
    /// <param name="ctrlNeg">Negative control (input) node</param>
    /// <param name="outPos">Positive output node</param>
    /// <param name="outNeg">Negative output node</param>
    /// <param name="gain">Voltage gain (dimensionless)</param>
    public VCVS(Node ctrlPos, Node ctrlNeg, Node outPos, Node outNeg, double gain)
        : base(outPos, outNeg) // Node1/Node2 are output nodes
    {
        ControlPos = ctrlPos;
        ControlNeg = ctrlNeg;
        Gain = gain;
    }

    public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0) {
        if (MatrixIndex == -1)
            return;

        int k = MatrixIndex;
        int ctrlP = ControlPos.Id;
        int ctrlN = ControlNeg.Id;
        int outP = Node1.Id; // Output positive
        int outN = Node2.Id; // Output negative

        // VCVS constraint equation (row k):
        //   V_outP - V_outN = Gain × (V_ctrlP - V_ctrlN)
        // Rearranged:
        //   V_outP - V_outN - Gain×V_ctrlP + Gain×V_ctrlN = 0
        //
        // Matrix stamps for row k:
        //   A[k, outP] += 1
        //   A[k, outN] -= 1
        //   A[k, ctrlP] -= Gain
        //   A[k, ctrlN] += Gain
        //   z[k] = 0

        if (outP != 0)
            A.At(k, outP, 1);
        if (outN != 0)
            A.At(k, outN, -1);
        if (ctrlP != 0)
            A.At(k, ctrlP, -Gain);
        if (ctrlN != 0)
            A.At(k, ctrlN, Gain);

        // Current tracking (KCL contributions):
        // The auxiliary variable x[k] represents current flowing from outP to outN.
        //   A[outP, k] += 1   (current leaves outP)
        //   A[outN, k] -= 1   (current enters outN)

        if (outP != 0)
            A.At(outP, k, 1);
        if (outN != 0)
            A.At(outN, k, -1);

        // RHS is zero (constraint equation)
        Z[k] = 0;
    }
}

