using CSparse.Storage;

namespace Sparky.MNA.Core
{
    /// <summary>
    /// Current-Controlled Current Source (CCCS).
    /// Output current is proportional to input current:
    ///   I_out = Gain × I_in
    ///
    /// The input is a short circuit (zero voltage) that meters current.
    /// The output is an ideal current source.
    /// Uses one auxiliary equation to sense input current.
    /// </summary>
    public class CCCS : Component
    {
        public Node ControlPos { get; }
        public Node ControlNeg { get; }

        /// <summary>
        /// Current gain (dimensionless).
        /// I_out = Gain × I_in
        /// </summary>
        public double Gain { get; set; }

        /// <summary>
        /// Sensed input current (flowing from ctrlPos to ctrlNeg).
        /// Populated by Circuit.Solve() after each solution.
        /// </summary>
        public double InputCurrent { get; internal set; }

        public override bool HasExtraEquation => true;
        public override bool RequiresPerStepRestamp => true;

        /// <summary>
        /// Creates a CCCS.
        /// </summary>
        /// <param name="ctrlPos">Positive control (input) node - current enters here</param>
        /// <param name="ctrlNeg">Negative control (input) node - current exits here</param>
        /// <param name="outPos">Positive output node - current exits here</param>
        /// <param name="outNeg">Negative output node - current enters here</param>
        /// <param name="gain">Current gain (dimensionless)</param>
        public CCCS(Node ctrlPos, Node ctrlNeg, Node outPos, Node outNeg, double gain)
            : base(outPos, outNeg)  // Node1/Node2 are output nodes
        {
            ControlPos = ctrlPos;
            ControlNeg = ctrlNeg;
            Gain = gain;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0)
        {
            if (MatrixIndex == -1) return;

            int k = MatrixIndex;
            int ctrlP = ControlPos.Id;
            int ctrlN = ControlNeg.Id;
            int outP = Node1.Id;  // Output positive
            int outN = Node2.Id;  // Output negative

            // The input is a zero-voltage source (short circuit) that senses current.
            // Auxiliary variable x[k] = I_in (current flowing ctrlP -> ctrlN)
            //
            // Constraint equation (row k): V_ctrlP - V_ctrlN = 0
            //   A[k, ctrlP] += 1
            //   A[k, ctrlN] -= 1
            //   z[k] = 0

            if (ctrlP != 0) A.At(k, ctrlP, 1);
            if (ctrlN != 0) A.At(k, ctrlN, -1);

            // KCL for input current sensing:
            // I_in leaves ctrlP, enters ctrlN
            //   A[ctrlP, k] += 1
            //   A[ctrlN, k] -= 1

            if (ctrlP != 0) A.At(ctrlP, k, 1);
            if (ctrlN != 0) A.At(ctrlN, k, -1);

            // Output current: I_out = Gain × I_in = Gain × x[k]
            // I_out flows INTO outP and out of outN (like a current source injecting into outP).
            // KCL at outP: ... - I_out = 0  (current enters)  =>  A[outP, k] -= Gain
            // KCL at outN: ... + I_out = 0  (current leaves)  =>  A[outN, k] += Gain

            if (outP != 0) A.At(outP, k, -Gain);
            if (outN != 0) A.At(outN, k, Gain);

            // RHS is zero (short circuit constraint)
            Z[k] = 0;
        }
    }
}
