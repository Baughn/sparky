using MathNet.Numerics.LinearAlgebra;

namespace Sparky.MNA
{
    public abstract class Component
    {
        public Node Node1 { get; }
        public Node Node2 { get; }

        // If true, this component adds an extra row/column to the matrix (e.g. Voltage Source, Inductor)
        public virtual bool HasExtraEquation => false;

        // If true, this component is non-linear and participates in Newton iteration
        public virtual bool IsNonLinear => false;

        // If true, this component requires iterative solving (defaults to non-linear)
        public virtual bool RequiresIteration => IsNonLinear;

        // Assigned index in the matrix (if HasExtraEquation is true)
        public int MatrixIndex { get; set; } = -1;

        protected Component(Node node1, Node node2)
        {
            Node1 = node1;
            Node2 = node2;
        }

        // Stamp the component into the matrix A and vector Z
        // dt is the time step in seconds
        public abstract void Stamp(Matrix<double> A, Vector<double> Z, double dt = 0);

        // Update internal state after solve (for transient analysis)
        public virtual void UpdateState(Vector<double> x, double dt) { }

        // Update operating point during Newton-Raphson iteration (for non-linear components)
        public virtual void UpdateOperatingPoint(Vector<double> x) { }
    }
}
