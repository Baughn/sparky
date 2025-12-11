using CSparse.Storage;

namespace Sparky.MNA.Core
{
    public class Resistor : Component
    {
        private double _resistance;
        public double Resistance
        {
            get => _resistance;
            set
            {
                _resistance = value;
                Conductance = 1.0 / _resistance;
            }
        }
        public double Conductance { get; private set; }

        public Resistor(Node node1, Node node2, double resistance)
            : base(node1, node2)
        {
            Resistance = resistance;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0)
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
                A.At(n1, n1, Conductance);
                if (n2 != 0)
                    A.At(n1, n2, -Conductance);
            }

            if (n2 != 0)
            {
                A.At(n2, n2, Conductance);
                if (n1 != 0)
                    A.At(n2, n1, -Conductance);
            }
        }

        public override void AccumulateEnergy(double[] x, double dt)
        {
            // P = I²R = V²/R (always positive - dissipated as heat)
            double v1 = (Node1.Id == 0) ? 0 : x[Node1.Id];
            double v2 = (Node2.Id == 0) ? 0 : x[Node2.Id];
            double v = v1 - v2;
            double power = v * v * Conductance; // V²/R = V² × G
            EnergyDelta = power * dt;
        }
    }
}
