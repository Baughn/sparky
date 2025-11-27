using CSparse.Storage;

namespace Sparky.MNA
{
    public class Inductor : Component
    {
        public double Inductance { get; }

        // State for transient analysis
        private double _currentThrough = 0;

        public override bool RequiresPerStepRestamp => true;

        public Inductor(Node node1, Node node2, double inductance) : base(node1, node2)
        {
            Inductance = inductance;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt)
        {
            int n1 = Node1.Id;
            int n2 = Node2.Id;

            if (dt == 0)
            {
                // DC Analysis: Inductor acts as a short circuit.
                // To avoid singular matrix (infinite conductance), we model it as a very small resistor.
                double rMin = 1e-9;
                double gMin = 1.0 / rMin;

                if (n1 != 0)
                {
                    A.At(n1, n1, gMin);
                    if (n2 != 0) A.At(n1, n2, -gMin);
                }
                if (n2 != 0)
                {
                    A.At(n2, n2, gMin);
                    if (n1 != 0) A.At(n2, n1, -gMin);
                }
                return;
            }
            else if (dt < 0)
            {
                return;
            }

            double gEq = dt / Inductance;
            double iEq = _currentThrough;

            // Stamp Conductance G_eq
            if (n1 != 0)
            {
                A.At(n1, n1, gEq);
                if (n2 != 0) A.At(n1, n2, -gEq);
                Z[n1] -= iEq;
            }

            if (n2 != 0)
            {
                A.At(n2, n2, gEq);
                if (n1 != 0) A.At(n2, n1, -gEq);
                Z[n2] += iEq;
            }
        }

        public override void UpdateState(double[] x, double dt)
        {
            if (dt <= 0) return;

            double v1 = (Node1.Id == 0) ? 0 : x[Node1.Id];
            double v2 = (Node2.Id == 0) ? 0 : x[Node2.Id];
            double v = v1 - v2;

            // Backward Euler: I_n = (dt/L)*V_n + I_prev
            double gEq = dt / Inductance;
            _currentThrough = gEq * v + _currentThrough;
        }
    }
}
