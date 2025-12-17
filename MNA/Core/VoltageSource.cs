using CSparse.Storage;

namespace Sparky.MNA.Core {
    public class VoltageSource : Component {
        public double Voltage { get; set; }

        /// <summary>
        /// The current flowing through the voltage source (from positive to negative terminal).
        /// Populated by Circuit.Solve() after each solution.
        /// </summary>
        public double Current { get; internal set; }

        // Voltage sources require an extra equation (row) in the matrix
        // to solve for the current flowing through them.
        public override bool HasExtraEquation => true;
        public override bool RequiresPerStepRestamp => true;

        // The index of the extra equation in the matrix
        // private int _matrixIndex = -1; // Unused, using MatrixIndex from base

        public VoltageSource(Node node1, Node node2, double voltage)
            : base(node1, node2) {
            Voltage = voltage;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0) {
            if (MatrixIndex == -1)
                return; // Should not happen if BuildSystem called

            int index = MatrixIndex;
            int n1 = Node1.Id;
            int n2 = Node2.Id;

            // MNA Stamp for Voltage Source:
            // [ n1, index ] += 1
            // [ n2, index ] -= 1
            // [ index, n1 ] += 1
            // [ index, n2 ] -= 1
            // Z[index] = Voltage

            if (n1 != 0) {
                A.At(n1, index, 1);
                A.At(index, n1, 1);
            }

            if (n2 != 0) {
                A.At(n2, index, -1);
                A.At(index, n2, -1);
            }

            Z[index] = Voltage;
        }

        public override void AccumulateEnergy(double[] x, double dt) {
            // P = V × I (positive = delivering power to circuit)
            // Current flows from positive to negative terminal (Node1 to Node2)
            // Convention: positive Current means current leaving Node1
            // Power delivered = V × I (when current flows in reference direction)
            double power = Voltage * (-Current); // Negative because Current is defined into positive terminal
            EnergyDelta = power * dt;
        }
    }
}
