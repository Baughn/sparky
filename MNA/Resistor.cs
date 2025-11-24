using MathNet.Numerics.LinearAlgebra;

namespace Sparky.MNA
{
    public class Resistor : Component
    {
        public double Resistance { get; }
        public double Conductance { get; }

        public Resistor(Node node1, Node node2, double resistance) : base(node1, node2)
        {
            Resistance = resistance;
            Conductance = 1.0 / resistance;
        }

        public override void Stamp(Matrix<double> A, Vector<double> Z, double dt = 0)
        {
            int n1 = Node1.Id;
            int n2 = Node2.Id;

            // G adds to diagonal, subtracts from off-diagonal
            // [ n1, n1 ] += G
            // [ n2, n2 ] += G
            // [ n1, n2 ] -= G
            // [ n2, n1 ] -= G

            // Skip ground (index 0)
            if (n1 != 0)
            {
                A[n1, n1] += Conductance;
                if (n2 != 0) A[n1, n2] -= Conductance;
            }

            if (n2 != 0)
            {
                A[n2, n2] += Conductance;
                if (n1 != 0) A[n2, n1] -= Conductance;
            }
        }
    }
}
