using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Sparky.MNA
{
    public class Circuit
    {
        public List<Node> Nodes { get; } = new List<Node>();
        public List<Component> Components { get; } = new List<Component>();

        // Sparse matrix A in Ax = z
        private Matrix<double>? _matrixA;
        // Vector z in Ax = z
        private Vector<double>? _vectorZ;
        // Vector x (unknowns)
        private Vector<double>? _vectorX;

        private bool _dirty = true;

        public Circuit()
        {
            // Ground node is always index 0
            Nodes.Add(new Node(0));
        }

        public Node Ground => Nodes[0];

        public Node AddNode()
        {
            var node = new Node(Nodes.Count);
            Nodes.Add(node);
            return node;
        }

        public void AddComponent(Component component)
        {
            Components.Add(component);
            _dirty = true;
        }

        public void BuildSystem()
        {
            int nodeCount = Nodes.Count;
            int extraEqCount = 0;

            // Assign indices for extra equations
            foreach (var component in Components)
            {
                if (component.HasExtraEquation)
                {
                    component.MatrixIndex = nodeCount + extraEqCount;
                    extraEqCount++;
                }
            }

            int size = nodeCount + extraEqCount;

            // Create sparse matrix
            _matrixA = Matrix<double>.Build.Sparse(size, size);
            _vectorZ = Vector<double>.Build.Dense(size);
            _vectorX = Vector<double>.Build.Dense(size);

            foreach (var component in Components)
            {
                component.Stamp(_matrixA, _vectorZ);
            }

            _dirty = false;
        }

        public void Solve(double dt)
        {
            if (_dirty) BuildSystem();

            // Newton-Raphson Iteration for Non-Linear Components
            int maxIterations = 50; // Increased from 10 to handle stiff non-linearities
            double tolerance = 1e-6;

            // Allocate xPrev once if needed, or reuse a buffer if we want to be super optimized.
            // For now, just outside the loop is better than inside.
            Vector<double>? xPrev = null;
            if (_vectorX != null)
            {
                xPrev = Vector<double>.Build.Dense(_vectorX.Count);
            }

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // 1. Clear Z vector (sources need to re-stamp)
                _vectorZ?.Clear();

                // 2. Stamp components (update A and Z)
                // Note: For linear DC, A is constant. For transient/non-linear, A changes.
                // TODO: Optimize this later. For now, clear and restamp.
                _matrixA?.Clear();

                if (_matrixA != null && _vectorZ != null)
                {
                    foreach (var component in Components)
                    {
                        component.Stamp(_matrixA, _vectorZ, dt);
                    }
                }

                // 3. Solve Ax = z
                if (_matrixA != null && _vectorZ != null)
                {
                    _vectorX = _matrixA.Solve(_vectorZ);
                }

                // 4. Check convergence and Update State
                if (_vectorX != null)
                {
                    bool converged = false;
                    if (iter > 0) // Can't check convergence on first step (xPrev is stale or empty)
                    {
                        // Check infinity norm of (x - xPrev)
                        var diff = _vectorX - xPrev;
                        if (diff.InfinityNorm() < tolerance)
                        {
                            converged = true;
                        }
                    }

                    // Copy current X to xPrev for next iteration check
                    if (xPrev != null) _vectorX.CopyTo(xPrev);

                    // Update nodes with new voltages
                    for (int i = 0; i < Nodes.Count; i++)
                    {
                        Nodes[i].Voltage = _vectorX[i];
                    }

                    // Update component operating points (for non-linear convergence)
                    foreach (var component in Components)
                    {
                        component.UpdateOperatingPoint(_vectorX);
                    }

                    if (converged) break;
                }
            }

            // 5. Finalize Step: Update component states for next time step
            if (_vectorX != null)
            {
                foreach (var component in Components)
                {
                    component.UpdateState(_vectorX, dt);
                }
            }
        }

        private int GetExtraEquationCount()
        {
            int count = 0;
            foreach (var c in Components)
            {
                if (c.HasExtraEquation) count++;
            }
            return count;
        }
    }
}
