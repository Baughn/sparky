using CSparse.Storage;

namespace Sparky.MNA.Core {
    public class Inductor : Component {
        public double Inductance { get; set; }

        // State for transient analysis - exposed for state preservation during rebuilds
        public double CurrentThrough { get; set; } = 0;

        public override bool RequiresPerStepRestamp => true;

        public Inductor(Node node1, Node node2, double inductance)
            : base(node1, node2) {
            Inductance = inductance;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt) {
            int n1 = Node1.Id;
            int n2 = Node2.Id;

            if (dt == 0) {
                // DC Analysis: Inductor acts as a short circuit.
                // To avoid singular matrix (infinite conductance), we model it as a very small resistor.
                double rMin = 1e-9;
                double gMin = 1.0 / rMin;

                if (n1 != 0) {
                    A.At(n1, n1, gMin);
                    if (n2 != 0)
                        A.At(n1, n2, -gMin);
                }
                if (n2 != 0) {
                    A.At(n2, n2, gMin);
                    if (n1 != 0)
                        A.At(n2, n1, -gMin);
                }
                return;
            } else if (dt < 0) {
                return;
            }

            double gEq = dt / Inductance;
            double iEq = CurrentThrough;

            // Stamp Conductance G_eq
            if (n1 != 0) {
                A.At(n1, n1, gEq);
                if (n2 != 0)
                    A.At(n1, n2, -gEq);
                Z[n1] -= iEq;
            }

            if (n2 != 0) {
                A.At(n2, n2, gEq);
                if (n1 != 0)
                    A.At(n2, n1, -gEq);
                Z[n2] += iEq;
            }
        }

        public override void UpdateState(double[] x, double dt) {
            double v1 = (Node1.Id == 0) ? 0 : x[Node1.Id];
            double v2 = (Node2.Id == 0) ? 0 : x[Node2.Id];
            double v = v1 - v2;

            if (dt <= 0) {
                // DC Analysis: Inductor is modeled as tiny resistor (rMin = 1e-9)
                // Current = V / rMin
                const double rMin = 1e-9;
                CurrentThrough = v / rMin;
                return;
            }

            // Backward Euler: I_n = (dt/L)*V_n + I_prev
            double gEq = dt / Inductance;
            CurrentThrough = gEq * v + CurrentThrough;
        }

        public override void AccumulateEnergy(double[] x, double dt) {
            // P = V × I (positive = absorbing/storing, negative = releasing)
            double v1 = (Node1.Id == 0) ? 0 : x[Node1.Id];
            double v2 = (Node2.Id == 0) ? 0 : x[Node2.Id];
            double voltage = v1 - v2;
            double power = voltage * CurrentThrough;
            EnergyDelta = power * dt;
        }
    }
}
