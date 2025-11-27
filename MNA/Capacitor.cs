using CSparse.Storage;

namespace Sparky.MNA
{
    public class Capacitor : Component
    {
        public double Capacitance { get; }

        // State for transient analysis
        private double _voltageAcross = 0;

        public override bool RequiresPerStepRestamp => true;

        public Capacitor(Node node1, Node node2, double capacitance) : base(node1, node2)
        {
            Capacitance = capacitance;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt)
        {
            // DC Steady State: Capacitor is an open circuit (G = 0, I = 0)
            if (dt <= 0) return;

            // Transient Model (Backward Euler):
            // Capacitor C is modeled as a conductance G_eq in parallel with a current source I_eq.
            // I_n = C * (V_n - V_n-1) / dt
            // I_n = (C/dt) * V_n - (C/dt) * V_n-1
            // Let G_eq = C / dt
            // I_n = G_eq * V_n - I_eq
            // where I_eq = G_eq * V_n-1 (current source value based on previous voltage)

            // Current Source I_eq flows FROM Node 1 TO Node 2 (if V_n1 > V_n2 previously)
            // Actually, let's be precise.
            // I_branch = G_eq * (V1 - V2) - I_eq
            // where I_eq = G_eq * (V1_old - V2_old)

            // MNA Stamp for Resistor G_eq:
            // [n1, n1] += G_eq
            // [n2, n2] += G_eq
            // [n1, n2] -= G_eq
            // [n2, n1] -= G_eq

            // MNA Stamp for Current Source I_eq:
            // It's a source pushing current I_eq from n2 to n1?
            // Wait. I_branch is current LEAVING n1.
            // KCL at n1: ... + I_branch = 0
            // ... + G_eq(V1-V2) - I_eq = 0
            // ... + G_eq(V1-V2) = I_eq
            // So I_eq is added to the RHS (Z) at n1, and subtracted at n2.

            double gEq = Capacitance / dt;
            double iEq = gEq * _voltageAcross;

            int n1 = Node1.Id;
            int n2 = Node2.Id;

            // Stamp Conductance
            if (n1 != 0)
            {
                A.At(n1, n1, gEq);
                if (n2 != 0) A.At(n1, n2, -gEq);

                // Stamp Source (RHS)
                Z[n1] += iEq;
            }

            if (n2 != 0)
            {
                A.At(n2, n2, gEq);
                if (n1 != 0) A.At(n2, n1, -gEq);

                // Stamp Source (RHS)
                Z[n2] -= iEq;
            }
        }

        // We need a way to update state after solve
        // But Component doesn't have an Update method yet.
        // We should add one or handle it in Stamp? 
        // Stamp is called BEFORE solve. We need to update AFTER solve.
        // But wait, for the NEXT step, we use the voltage from THIS step.
        // So we can update the state at the beginning of Stamp?
        // No, Stamp uses the OLD state.
        // We need a separate UpdateState(vectorX) method.

        // Update state after solve
        public override void UpdateState(double[] x, double dt)
        {
            double v1 = (Node1.Id == 0) ? 0 : x[Node1.Id];
            double v2 = (Node2.Id == 0) ? 0 : x[Node2.Id];
            _voltageAcross = v1 - v2;
        }
    }
}
