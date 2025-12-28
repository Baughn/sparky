using CSparse.Storage;

namespace Sparky.Mna.Solver;


/// <summary>
/// Current-Controlled Voltage Source (CCVS).
/// Output voltage is proportional to input current:
///   V_out = Transresistance × I_in
///
/// The input is a short circuit (zero voltage) that meters current.
/// The output is an ideal voltage source.
/// Uses two auxiliary equations:
///   - Row k1: current sensing (zero-volt constraint at input)
///   - Row k2: output voltage constraint
/// </summary>
public class CCVS : Component {
public Node ControlPos { get; }
public Node ControlNeg { get; }

/// <summary>
/// Transresistance in V/A (Ohms).
/// V_out = Transresistance × I_in
/// </summary>
public double Transresistance { get; set; }

/// <summary>
/// Sensed input current (flowing from ctrlPos to ctrlNeg).
/// Populated by Circuit.Solve() after each solution.
/// </summary>
public double InputCurrent { get; internal set; }

/// <summary>
/// Output current (flowing from outPos to outNeg).
/// Populated by Circuit.Solve() after each solution.
/// </summary>
public double OutputCurrent { get; internal set; }

public override bool HasExtraEquation => true;
public override int ExtraEquationCount => 2; // k1 for current sensing, k2 for output voltage
public override bool RequiresPerStepRestamp => true;

/// <summary>
/// Creates a CCVS.
/// </summary>
/// <param name="ctrlPos">Positive control (input) node - current enters here</param>
/// <param name="ctrlNeg">Negative control (input) node - current exits here</param>
/// <param name="outPos">Positive output node</param>
/// <param name="outNeg">Negative output node</param>
/// <param name="transresistance">Transresistance in V/A (Ohms)</param>
public CCVS(Node ctrlPos, Node ctrlNeg, Node outPos, Node outNeg, double transresistance)
    : base(outPos, outNeg) // Node1/Node2 are output nodes
{
    ControlPos = ctrlPos;
    ControlNeg = ctrlNeg;
    Transresistance = transresistance;
}

public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0) {
    if (MatrixIndex == -1)
        return;

    int k1 = MatrixIndex; // Current sensing auxiliary row
    int k2 = MatrixIndex + 1; // Output voltage auxiliary row
    int ctrlP = ControlPos.Id;
    int ctrlN = ControlNeg.Id;
    int outP = Node1.Id; // Output positive
    int outN = Node2.Id; // Output negative

    double rm = Transresistance;

    // Row k1: Zero-voltage constraint at input (senses current)
    // V_ctrlP - V_ctrlN = 0
    // x[k1] = I_in (current flowing ctrlP -> ctrlN)
    //   A[k1, ctrlP] += 1
    //   A[k1, ctrlN] -= 1
    //   z[k1] = 0

    if (ctrlP != 0)
        A.At(k1, ctrlP, 1);
    if (ctrlN != 0)
        A.At(k1, ctrlN, -1);

    // KCL for input current:
    //   A[ctrlP, k1] += 1   (I_in leaves ctrlP)
    //   A[ctrlN, k1] -= 1   (I_in enters ctrlN)

    if (ctrlP != 0)
        A.At(ctrlP, k1, 1);
    if (ctrlN != 0)
        A.At(ctrlN, k1, -1);

    // Row k2: Output voltage constraint
    // V_outP - V_outN = rm × I_in = rm × x[k1]
    // Rearranged: V_outP - V_outN - rm×x[k1] = 0
    //   A[k2, outP] += 1
    //   A[k2, outN] -= 1
    //   A[k2, k1] -= rm
    //   z[k2] = 0

    if (outP != 0)
        A.At(k2, outP, 1);
    if (outN != 0)
        A.At(k2, outN, -1);
    A.At(k2, k1, -rm);

    // KCL for output current:
    // x[k2] = I_out (current flowing outP -> outN)
    //   A[outP, k2] += 1   (I_out leaves outP)
    //   A[outN, k2] -= 1   (I_out enters outN)

    if (outP != 0)
        A.At(outP, k2, 1);
    if (outN != 0)
        A.At(outN, k2, -1);

    // RHS is zero for both constraints
    Z[k1] = 0;
    Z[k2] = 0;
}
}
