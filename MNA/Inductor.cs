using MathNet.Numerics.LinearAlgebra;

namespace Sparky.MNA
{
    public class Inductor : Component
    {
        public double Inductance { get; }

        // State for transient analysis
        private double _currentThrough = 0;

        public Inductor(Node node1, Node node2, double inductance) : base(node1, node2)
        {
            Inductance = inductance;
        }

        public override void Stamp(Matrix<double> A, Vector<double> Z, double dt)
        {
            // DC Steady State: Inductor is a short circuit (R = 0)
            // This requires an extra equation if we treat it as a voltage source V=0.
            // Or we can use a very small resistance? No, MNA handles V=0 fine.

            if (dt <= 0)
            {
                // Treat as Short Circuit (Voltage Source V=0)
                // We need an index for this.
                // But wait, Component base class has MatrixIndex support now.
                // We need to set HasExtraEquation = true for DC?
                // Actually, for Transient, we model it as a Resistor + Current Source.
                // G_eq = dt / L
                // I_eq = I_prev + (dt/L)*V_prev (Trapezoidal) or I_prev (Backward Euler?)

                // Backward Euler for Inductor:
                // V_n = L * (I_n - I_n-1) / dt
                // I_n - I_n-1 = (dt/L) * V_n
                // I_n = (dt/L) * V_n + I_n-1
                // I_n = G_eq * V_n + I_eq
                // where G_eq = dt / L
                // and I_eq = I_n-1 (Current source value)

                // So it's a resistor G_eq in parallel with a current source I_eq.
                // I_eq is current source in parallel.
                // I_branch = G_eq * (V1 - V2) + I_eq
                // Note the sign.

                // If dt > 0, we don't need an extra equation! It's just a resistor and current source.
                // BUT if dt = 0 (DC), it's a short.
                // So HasExtraEquation depends on simulation mode? That's annoying.
                // Let's assume we always run transient, or for DC we use a small resistance?
                // Or we can implement HasExtraEquation logic.

                // For now, let's assume dt > 0 for Inductor usage, or handle DC as short.
                // If dt=0, we should probably treat it as a Voltage Source with V=0.
                // TODO: Implement proper DC handling (Short Circuit)
                return;
            }

            double gEq = dt / Inductance;
            double iEq = _currentThrough;

            int n1 = Node1.Id;
            int n2 = Node2.Id;

            // Stamp Conductance G_eq
            if (n1 != 0)
            {
                A[n1, n1] += gEq;
                if (n2 != 0) A[n1, n2] -= gEq;

                // Current Source I_eq
                // I_branch = G_eq*V + I_eq
                // KCL: ... + I_branch = 0
                // ... + G_eq*V = -I_eq
                // So subtract I_eq from RHS at n1
                Z[n1] -= iEq;
            }

            if (n2 != 0)
            {
                A[n2, n2] += gEq;
                if (n1 != 0) A[n2, n1] -= gEq;

                // Add I_eq to RHS at n2
                Z[n2] += iEq;
            }
        }

        public override void UpdateState(Vector<double> x, double dt)
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
