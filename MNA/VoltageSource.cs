using MathNet.Numerics.LinearAlgebra;

namespace Sparky.MNA
{
    public class VoltageSource : Component
    {
        public double Voltage { get; set; }

        // Voltage sources require an extra equation (row) in the matrix
        // to solve for the current flowing through them.
        public override bool HasExtraEquation => true;

        // The index of the extra equation in the matrix
        // private int _matrixIndex = -1; // Unused, using MatrixIndex from base

        public VoltageSource(Node node1, Node node2, double voltage) : base(node1, node2)
        {
            Voltage = voltage;
        }

        public override void Stamp(Matrix<double> A, Vector<double> Z, double dt = 0)
        {
            if (MatrixIndex == -1) return; // Should not happen if BuildSystem called

            int index = MatrixIndex;
            int n1 = Node1.Id;
            int n2 = Node2.Id;

            // MNA Stamp for Voltage Source:
            // [ n1, index ] += 1
            // [ n2, index ] -= 1
            // [ index, n1 ] += 1
            // [ index, n2 ] -= 1
            // Z[index] = Voltage

            if (n1 != 0)
            {
                A[n1, index] += 1;
                A[index, n1] += 1;
            }

            if (n2 != 0)
            {
                A[n2, index] -= 1;
                A[index, n2] -= 1;
            }

            Z[index] = Voltage;
        }
    }
}
