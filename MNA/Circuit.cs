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

        // Track last known configuration to support reuse
        private double _lastDt = double.NaN;
        private int _stampVersion = 0;
        private int _lastStampVersion = -1;
        private bool _requiresPerStepRestamp;

        // Public counters for diagnostics/benchmarks
        public int LastIterations { get; private set; }

        private bool _dirty = true;
        private bool _requiresIteration;

        private const double DefaultTolerance = 1e-6;
        private const int DefaultMaxIterations = 50;

        public double ConvergenceTolerance { get; set; } = DefaultTolerance;
        public int MaxIterations { get; set; } = DefaultMaxIterations;

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
            if (component.RequiresIteration) _requiresIteration = true;
            if (component.RequiresPerStepRestamp) _requiresPerStepRestamp = true;
        }

        public void BuildSystem()
        {
            int nodeCount = Nodes.Count;
            int extraEqCount = 0;
            _requiresIteration = false;
            _requiresPerStepRestamp = false;

            // Assign indices for extra equations
            foreach (var component in Components)
            {
                if (component.HasExtraEquation)
                {
                    component.MatrixIndex = nodeCount + extraEqCount;
                    extraEqCount++;
                }

                if (component.RequiresIteration) _requiresIteration = true;
                if (component.RequiresPerStepRestamp) _requiresPerStepRestamp = true;
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

             // Anchor ground so the matrix is non-singular and ground stays at 0V
            AnchorGround();

            _dirty = false;
            _stampVersion++;
        }

        public void Solve(double dt)
        {
            if (_dirty) BuildSystem();

            // Fast path: nothing changed and static linear circuit
            if (!_requiresIteration && !_requiresPerStepRestamp && _matrixA != null && _vectorZ != null &&
                _vectorX != null && _lastStampVersion == _stampVersion && dt.Equals(_lastDt))
            {
                // Ensure node voltages reflect latest solution
                for (int i = 0; i < Nodes.Count; i++)
                {
                    Nodes[i].Voltage = _vectorX[i];
                }
                LastIterations = 0;
                return;
            }

            // Newton-Raphson Iteration for Non-Linear Components
            int maxIterations = _requiresIteration ? MaxIterations : 1;
            double tolerance = ConvergenceTolerance;

            // Allocate xPrev once if needed, or reuse a buffer if we want to be super optimized.
            // For now, just outside the loop is better than inside.
            Vector<double>? xPrev = null;
            if (_vectorX != null && _requiresIteration)
            {
                xPrev = Vector<double>.Build.Dense(_vectorX.Count);
            }

            bool converged = false;

            double lastResidual = double.NaN;
            double lastStep = double.NaN;

            int iterCount = 0;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                iterCount = iter + 1;
                // 1. Clear Z vector (sources need to re-stamp)
                if (_vectorZ != null) _vectorZ.Clear();

                // 2. Stamp components (update A and Z)
                _matrixA?.Clear();

                // Keep the ground row/col pinned so the matrix is well-conditioned
                AnchorGround();
                ApplyGmin();

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
                    double stepNorm = double.PositiveInfinity;
                    if (iter > 0 && xPrev != null)
                    {
                        stepNorm = (_vectorX - xPrev).InfinityNorm();
                    }

                    double residualNorm = double.PositiveInfinity;
                    if (_matrixA != null && _vectorZ != null)
                    {
                        residualNorm = (_matrixA * _vectorX - _vectorZ).InfinityNorm();
                    }

                    double scaledStepTol = tolerance * (1.0 + _vectorX.InfinityNorm());
                    double scaledResidualTol = _vectorZ != null
                        ? tolerance * (1.0 + _vectorZ.InfinityNorm())
                        : tolerance;

                    lastStep = stepNorm;
                    lastResidual = residualNorm;

                    if (!_requiresIteration)
                    {
                        converged = true;
                    }
                    else if (iter > 0 && stepNorm < scaledStepTol && residualNorm < scaledResidualTol)
                    {
                        converged = true;
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

            LastIterations = iterCount;
            if (!converged)
            {
                throw new InvalidOperationException($"Circuit solve did not converge within the maximum iterations. Residual={lastResidual}, Step={lastStep}");
            }

            // 5. Finalize Step: Update component states for next time step
            if (_vectorX != null)
            {
                foreach (var component in Components)
                {
                    component.UpdateState(_vectorX, dt);
                }
            }

            _lastDt = dt;
            _lastStampVersion = _stampVersion;
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

        private void AnchorGround()
        {
            if (_matrixA == null || _vectorZ == null) return;

            _matrixA[0, 0] = 1.0;
            _vectorZ[0] = 0.0;
        }

        private void ApplyGmin()
        {
            // Add a tiny shunt to ground on every non-ground node to avoid singular matrices
            if (_matrixA == null) return;

            const double gmin = 1e-12;
            for (int i = 1; i < Nodes.Count; i++)
            {
                _matrixA[i, i] += gmin;
            }
        }
    }
}
