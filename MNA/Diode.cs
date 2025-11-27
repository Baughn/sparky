using MathNet.Numerics.LinearAlgebra;
using System;

namespace Sparky.MNA
{
    public class Diode : Component
    {
        // Shockley diode equation parameters
        // I = Is * (exp(Vd / (n * Vt)) - 1)
        private const double Is = 1e-12; // Saturation current
        private const double Vt = 0.026; // Thermal voltage (approx at room temp)
        private const double n = 1.0;    // Emission coefficient

        // Operating point (Voltage across diode from previous iteration)
        private double _vd = 0.6; // Start with a guess (forward biased)

        public Diode(Node node1, Node node2) : base(node1, node2)
        {
        }

        public override void Stamp(Matrix<double> A, Vector<double> Z, double dt = 0)
        {
            // Linearize around _vd
            // I = Is * (exp(_vd / (n*Vt)) - 1)
            // G_eq = dI/dV = (Is / (n*Vt)) * exp(_vd / (n*Vt))
            // I_eq = I - G_eq * _vd

            // TODO: Limit _vd to avoid overflow?
            // In SPICE, there are limiting algorithms (pnjlim).
            // For now, simple clamping or just let it fly (Newton-Raphson usually converges if guess is okay).

            double vdLimited = Math.Max(-5.0, Math.Min(_vd, 0.9)); // keep within safe exponential range
            double expArg = vdLimited / (n * Vt);
            double exp = Math.Exp(Math.Min(expArg, 40.0)); // avoid overflow
            double gEq = (Is / (n * Vt)) * exp;
            double iDiode = Is * (exp - 1);
            double iEq = iDiode - gEq * vdLimited;

            // Diode is modeled as Resistor G_eq in parallel with Current Source I_eq
            // Current flows from Node 1 to Node 2.
            // I_branch = G_eq * (V1 - V2) + I_eq
            // Wait, I_eq is the offset current.
            // Linearized: I ~ I(_vd) + G_eq * (V - _vd)
            // I = I(_vd) - G_eq*_vd + G_eq*V
            // I = I_eq + G_eq*V
            // where I_eq = I(_vd) - G_eq*_vd

            // MNA Stamp:
            // Conductance G_eq between n1 and n2.
            // Current Source I_eq flowing from n1 to n2?
            // KCL at n1: ... + I_branch = 0 -> ... + G_eq*V + I_eq = 0
            // ... + G_eq*V = -I_eq
            // So subtract I_eq from RHS at n1.

            int n1 = Node1.Id;
            int n2 = Node2.Id;

            // Stamp Conductance
            if (n1 != 0)
            {
                A[n1, n1] += gEq;
                if (n2 != 0) A[n1, n2] -= gEq;
                Z[n1] -= iEq;
            }

            if (n2 != 0)
            {
                A[n2, n2] += gEq;
                if (n1 != 0) A[n2, n1] -= gEq;
                Z[n2] += iEq;
            }
        }

        public override void UpdateOperatingPoint(Vector<double> x)
        {
            double v1 = (Node1.Id == 0) ? 0 : x[Node1.Id];
            double v2 = (Node2.Id == 0) ? 0 : x[Node2.Id];
            double newVd = v1 - v2;

            // Simple damping/limiting:
            // Clamp _vd to prevent overflow in exp() and aid convergence.
            // Max safe exponent is ~700. 
            // _vd / Vt < 700 => _vd < 700 * 0.026 = 18.2V
            // We clamp to 5.0V which is sufficient for most electronics (diode drop rarely exceeds 2-3V even at high currents).
            if (newVd > 0.9) newVd = 0.9;
            if (newVd < -5.0) newVd = -5.0;

            _vd = newVd;
        }
    }
}
