using CSparse.Storage;

namespace Sparky.MNA.Core
{
    /// <summary>
    /// Voltage-Controlled Current Source (VCCS).
    /// Output current is proportional to input voltage:
    ///   I_out = Transconductance × (V_ctrlPos - V_ctrlNeg)
    ///
    /// The input draws no current (infinite input impedance).
    /// The output is an ideal current source.
    /// </summary>
    public class VCCS : Component
    {
        public Node ControlPos { get; }
        public Node ControlNeg { get; }

        /// <summary>
        /// Transconductance in A/V (Siemens).
        /// I_out = Transconductance × V_in
        /// </summary>
        public double Transconductance { get; set; }

        public override bool RequiresPerStepRestamp => true;

        /// <summary>
        /// Creates a VCCS.
        /// </summary>
        /// <param name="ctrlPos">Positive control (input) node</param>
        /// <param name="ctrlNeg">Negative control (input) node</param>
        /// <param name="outPos">Positive output node (current exits here)</param>
        /// <param name="outNeg">Negative output node (current enters here)</param>
        /// <param name="transconductance">Gain in A/V</param>
        public VCCS(Node ctrlPos, Node ctrlNeg, Node outPos, Node outNeg, double transconductance)
            : base(outPos, outNeg) // Node1/Node2 are output nodes
        {
            ControlPos = ctrlPos;
            ControlNeg = ctrlNeg;
            Transconductance = transconductance;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0)
        {
            int ctrlP = ControlPos.Id;
            int ctrlN = ControlNeg.Id;
            int outP = Node1.Id; // Output positive
            int outN = Node2.Id; // Output negative

            double gm = Transconductance;

            // VCCS creates cross-coupling conductance terms.
            // Current I_out = gm × (V_ctrlP - V_ctrlN) flows into outP and out of outN.
            // This is like a current source injecting current into outP.
            //
            // KCL at outP: ... - I_out = 0  (current enters, so negative in "leaving" sum)
            // KCL at outN: ... + I_out = 0  (current leaves)
            //
            // Matrix stamps (opposite sign from "current exits outP"):
            //   A[outP, ctrlP] -= gm
            //   A[outP, ctrlN] += gm
            //   A[outN, ctrlP] += gm
            //   A[outN, ctrlN] -= gm

            if (outP != 0)
            {
                if (ctrlP != 0)
                    A.At(outP, ctrlP, -gm);
                if (ctrlN != 0)
                    A.At(outP, ctrlN, gm);
            }

            if (outN != 0)
            {
                if (ctrlP != 0)
                    A.At(outN, ctrlP, gm);
                if (ctrlN != 0)
                    A.At(outN, ctrlN, -gm);
            }
        }
    }
}
