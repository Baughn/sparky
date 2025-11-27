using CSparse.Storage;

namespace Sparky.MNA
{
    public class CurrentSource : Component
    {
        public double Current { get; set; }

        public override bool RequiresPerStepRestamp => true;

        public CurrentSource(Node node1, Node node2, double current) : base(node1, node2)
        {
            Current = current;
        }

        public override void Stamp(CoordinateStorage<double> A, double[] Z, double dt = 0)
        {
            int n1 = Node1.Id;
            int n2 = Node2.Id;

            // Current source adds to the RHS vector Z
            // Current flows from Node1 to Node2? 
            // Convention: Current leaves Node1, enters Node2?
            // Usually: Source from n1 to n2 means current flows n1 -> n2.
            // KCL at n1: ... + I_out = 0 -> ... = -I_out
            // KCL at n2: ... - I_in = 0 -> ... = I_in

            // If current flows n1 -> n2:
            // Node 1 loses current: Z[n1] -= Current
            // Node 2 gains current: Z[n2] += Current

            if (n1 != 0) Z[n1] -= Current;
            if (n2 != 0) Z[n2] += Current;
        }
    }
}
