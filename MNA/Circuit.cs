using System;
using System.Collections.Generic;
using CSparse;
using CSparse.Double;
using CSparse.Double.Factorization;
using CSparse.Storage;

namespace Sparky.MNA
{
    public class Circuit
    {
        public List<Node> Nodes { get; } = new List<Node>();
        public List<Component> Components { get; } = new List<Component>();

        // Sparse matrix A in Ax = z
        private CoordinateStorage<double>? _matrixA;
        private CompressedColumnStorage<double>? _compressedA;
        private SparseLU? _cachedLu;
        // Vector z in Ax = z
        private double[]? _vectorZ;
        // Vector x (unknowns)
        private double[]? _vectorX;
        private double[]? _workResidual;
        private double[]? _denseRhs;
        private double[,]? _denseMatrix;

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
        private const int DenseSizeThreshold = 96;
        private const double DenseDensityThreshold = 0.18;

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

            // Create sparse matrix (coordinate storage accumulates duplicate stamps)
            int estimatedNonZeros = Math.Max(size * 2, Components.Count * 6 + size);
            _matrixA = new CoordinateStorage<double>(size, size, estimatedNonZeros);
            _compressedA = null;
            _vectorZ = new double[size];
            _vectorX = new double[size];

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

            int solutionSize = _vectorZ?.Length ?? _vectorX?.Length ?? 0;
            double[]? xPrev = _requiresIteration && solutionSize > 0 ? new double[solutionSize] : null;

            bool converged = false;

            double lastResidual = double.NaN;
            double lastStep = double.NaN;

            int iterCount = 0;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                iterCount = iter + 1;
            // 1. Clear Z vector (sources need to re-stamp)
            if (_vectorZ != null) Array.Fill(_vectorZ, 0.0);

            // 2. Stamp components (update A and Z)
            _matrixA?.Clear();
            // Only invalidate cached structures when they are actually stale.
            if (_requiresIteration || _requiresPerStepRestamp)
            {
                _compressedA = null;
                _cachedLu = null;
            }

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
                    _vectorX = SolveLinearSystem(_matrixA, _vectorZ);
                }

                // 4. Check convergence and Update State
                if (_vectorX != null)
                {
                    double stepNorm = double.PositiveInfinity;
                    if (iter > 0 && xPrev != null)
                    {
                        stepNorm = InfinityNormDifference(_vectorX, xPrev);
                    }

                    double residualNorm = double.PositiveInfinity;
                    if (_compressedA != null && _vectorZ != null)
                    {
                        residualNorm = ComputeResidualInfinity(_compressedA, _vectorX, _vectorZ);
                    }

                    double scaledStepTol = tolerance * (1.0 + InfinityNorm(_vectorX));
                    double scaledResidualTol = _vectorZ != null
                        ? tolerance * (1.0 + InfinityNorm(_vectorZ))
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
                    if (xPrev != null) Array.Copy(_vectorX, xPrev, _vectorX.Length);

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

        private void AnchorGround()
        {
            if (_matrixA == null || _vectorZ == null) return;

            _matrixA.At(0, 0, 1.0);
            _vectorZ[0] = 0.0;
        }

        private void ApplyGmin()
        {
            // Add a tiny shunt to ground on every non-ground node to avoid singular matrices
            if (_matrixA == null) return;

            const double gmin = 1e-12;
            for (int i = 1; i < Nodes.Count; i++)
            {
                _matrixA.At(i, i, gmin);
            }
        }

        private double[] SolveLinearSystem(CoordinateStorage<double> matrixA, double[] vectorZ)
        {
            if (_compressedA == null)
            {
                _compressedA = ToCompressed(matrixA);
            }

            bool useDense = ShouldUseDense(matrixA);
            if (useDense)
            {
                return SolveDense(matrixA, vectorZ);
            }

            return SolveSparse(vectorZ);
        }

        private double[] SolveSparse(double[] vectorZ)
        {
            if (_compressedA == null)
            {
                throw new InvalidOperationException("Compressed matrix not available for sparse solve.");
            }

            var lu = _cachedLu;
            if (lu == null)
            {
                lu = SparseLU.Create(_compressedA, ColumnOrdering.Natural, 1.0);
                if (lu == null)
                {
                    throw new InvalidOperationException("Circuit solve failed: LU factorization did not succeed.");
                }

                // Cache factorization only for linear static circuits (no iteration, no per-step restamp).
                if (!_requiresIteration && !_requiresPerStepRestamp)
                {
                    _cachedLu = lu;
                }
            }

            if (_vectorX == null || _vectorX.Length != vectorZ.Length)
            {
                _vectorX = new double[vectorZ.Length];
            }

            lu.Solve(vectorZ, _vectorX);
            return _vectorX;
        }

        private double[] SolveDense(CoordinateStorage<double> matrixA, double[] vectorZ)
        {
            int n = matrixA.RowCount;
            double[,] dense = GetDenseBuffer(n);

            // Build dense matrix from coordinate storage (accumulate duplicates).
            Array.Clear(dense, 0, dense.Length);
            for (int k = 0; k < matrixA.NonZerosCount; k++)
            {
                int row = matrixA.RowIndices[k];
                int col = matrixA.ColumnIndices[k];
                dense[row, col] += matrixA.Values[k];
            }

            // Prepare RHS
            if (_vectorX == null || _vectorX.Length != vectorZ.Length)
            {
                _vectorX = new double[vectorZ.Length];
            }
            var rhs = GetDenseRhsBuffer(n);
            Array.Copy(vectorZ, rhs, n);

            // In-place LU with partial pivoting (Doolittle).
            for (int k = 0; k < n; k++)
            {
                // Pivot search
                int pivotRow = k;
                double pivotVal = Math.Abs(dense[k, k]);
                for (int i = k + 1; i < n; i++)
                {
                    double val = Math.Abs(dense[i, k]);
                    if (val > pivotVal)
                    {
                        pivotVal = val;
                        pivotRow = i;
                    }
                }

                if (pivotVal < 1e-15)
                {
                    throw new InvalidOperationException("Circuit solve failed: matrix is singular in dense solve.");
                }

                if (pivotRow != k)
                {
                    SwapRows(dense, k, pivotRow);
                    (rhs[k], rhs[pivotRow]) = (rhs[pivotRow], rhs[k]);
                }

                double akk = dense[k, k];
                for (int i = k + 1; i < n; i++)
                {
                    double lik = dense[i, k] / akk;
                    dense[i, k] = lik;
                    for (int j = k + 1; j < n; j++)
                    {
                        dense[i, j] -= lik * dense[k, j];
                    }
                }
            }

            // Forward substitution Ly = b
            for (int i = 0; i < n; i++)
            {
                double sum = rhs[i];
                for (int j = 0; j < i; j++)
                {
                    sum -= dense[i, j] * rhs[j];
                }
                rhs[i] = sum;
            }

            // Back substitution Ux = y
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = rhs[i];
                for (int j = i + 1; j < n; j++)
                {
                    sum -= dense[i, j] * _vectorX[j];
                }
                _vectorX[i] = sum / dense[i, i];
            }

            return _vectorX;
        }

        private static bool ShouldUseDense(CoordinateStorage<double> matrixA)
        {
            int n = matrixA.RowCount;
            if (n <= DenseSizeThreshold) return true;

            double density = matrixA.NonZerosCount / (double)(n * n);
            return density >= DenseDensityThreshold;
        }

        private static CompressedColumnStorage<double> ToCompressed(CoordinateStorage<double> storage) =>
            SparseMatrix.OfIndexed(storage, false);

        private double[] GetDenseRhsBuffer(int size)
        {
            if (_denseRhs == null || _denseRhs.Length != size)
            {
                _denseRhs = new double[size];
            }

            return _denseRhs;
        }

        private double[,] GetDenseBuffer(int size)
        {
            if (_denseMatrix == null || _denseMatrix.GetLength(0) != size || _denseMatrix.GetLength(1) != size)
            {
                _denseMatrix = new double[size, size];
            }

            return _denseMatrix;
        }

        private static void SwapRows(double[,] matrix, int rowA, int rowB)
        {
            if (rowA == rowB) return;
            int n = matrix.GetLength(1);
            for (int j = 0; j < n; j++)
            {
                (matrix[rowA, j], matrix[rowB, j]) = (matrix[rowB, j], matrix[rowA, j]);
            }
        }

        private double ComputeResidualInfinity(CompressedColumnStorage<double> matrixA, double[] x, double[] z)
        {
            if (_workResidual == null || _workResidual.Length != z.Length)
            {
                _workResidual = new double[z.Length];
            }

            Multiply(matrixA, x, _workResidual);
            for (int i = 0; i < z.Length; i++)
            {
                _workResidual[i] -= z[i];
            }

            return InfinityNorm(_workResidual);
        }

        private static void Multiply(CompressedColumnStorage<double> matrixA, double[] x, double[] result)
        {
            Array.Fill(result, 0.0);
            for (int col = 0; col < matrixA.ColumnCount; col++)
            {
                double xj = x[col];
                if (xj == 0) continue;

                int start = matrixA.ColumnPointers[col];
                int end = matrixA.ColumnPointers[col + 1];
                for (int idx = start; idx < end; idx++)
                {
                    result[matrixA.RowIndices[idx]] += matrixA.Values[idx] * xj;
                }
            }
        }

        private static double InfinityNorm(double[] vector)
        {
            double max = 0.0;
            for (int i = 0; i < vector.Length; i++)
            {
                double val = Math.Abs(vector[i]);
                if (val > max) max = val;
            }

            return max;
        }

        private static double InfinityNormDifference(double[] current, double[] previous)
        {
            double max = 0.0;
            int len = Math.Min(current.Length, previous.Length);
            for (int i = 0; i < len; i++)
            {
                double val = Math.Abs(current[i] - previous[i]);
                if (val > max) max = val;
            }

            return max;
        }
    }
}
