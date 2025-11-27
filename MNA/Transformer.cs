using CSparse.Storage;

namespace Sparky.MNA
{
    /// <summary>
    /// Ideal Transformer.
    /// Relations:
    /// Vp / Vs = Np / Ns = 1 / n
    /// Ip / Is = -Ns / Np = -n
    /// where n = Ns / Np (turns ratio)
    /// 
    /// Equations:
    /// V1 - V2 - (1/n)*(V3 - V4) = 0
    /// Ip = I_aux
    /// Is = -(1/n) * I_aux
    /// </summary>
    public class Transformer : Component
    {
        public Node Node3 { get; }
        public Node Node4 { get; }
        public double Ratio { get; } // n = Ns / Np

        public override bool HasExtraEquation => true;

        public Transformer(Node node1, Node node2, Node node3, Node node4, double ratio)
            : base(node1, node2)
        {
            Node3 = node3;
            Node4 = node4;
            Ratio = ratio;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0)
        {
            if (MatrixIndex == -1) return;

            int k = MatrixIndex;
            int n1 = Node1.Id;
            int n2 = Node2.Id;
            int n3 = Node3.Id;
            int n4 = Node4.Id;

            double invRatio = 1.0 / Ratio;

            // Equation: (V1 - V2) - (1/n)*(V3 - V4) = 0
            // Row k (Aux equation):
            if (n1 != 0) A.At(k, n1, 1);
            if (n2 != 0) A.At(k, n2, -1);
            if (n3 != 0) A.At(k, n3, -invRatio);
            if (n4 != 0) A.At(k, n4, invRatio);

            // Current contributions (KCL):
            // Primary flows: I_aux leaves n1, enters n2
            if (n1 != 0) A.At(n1, k, 1);
            if (n2 != 0) A.At(n2, k, -1);

            // Secondary flows: -(1/n)*I_aux leaves n3, enters n4
            // So we add -(1/n) to column k for n3, and +(1/n) for n4
            if (n3 != 0) A.At(n3, k, -invRatio);
            if (n4 != 0) A.At(n4, k, invRatio);
        }
    }
}
