# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Design documentation is consolidated in `docs/`. Project source code lives in `src/` (mna, voxel, handbook, mod), with tests in `tests/` and benchmarks in `benchmarks/`.

## Project Overview

Sparky is a Vintage Story mod that implements an electrical circuit simulator using Modified Nodal Analysis (MNA). The solver handles DC, transient (capacitors/inductors), and nonlinear (diodes) circuits with automatic graph partitioning for performance.

## Build & Test Commands

```bash
# Enter dev environment (automatic via direnv, or manual)
nix develop

# Build
dotnet build                    # Debug build
dotnet build -c Release         # Release build (creates src/mod/bin/Sparky.zip)

# Test
dotnet test                     # Run all tests
dotnet test --filter "FullyQualifiedName~TestName"  # Run specific test

# Benchmarks
./benchmark.sh run              # Run benchmarks
./benchmark.sh compare base.csv new.csv  # Compare benchmark results
./benchmark.sh trailer          # Benchmark vs parent commit, add trailer
```

## Architecture

### Project Structure

1. **MNA Layer (`src/mna/`)**: Circuit solver with high-level API
   - `solver/`: Low-level solver handling matrix assembly and linear algebra
     - `Circuit.cs`: Main solver with Newton-Raphson iteration, dense/sparse path selection
     - `Component.cs`: Abstract base for all circuit elements
     - Component implementations: `Resistor`, `VoltageSource`, `CurrentSource`, `Capacitor`, `Inductor`, `Diode`, `Transformer`
   - `api/`: High-level interface for game integration
     - `ISimulation.cs`: Public interface with strongly-typed IDs
     - `SimulationManager.cs`: Implementation with graph partitioning and line optimization
     - `Exceptions.cs`, `Ids.cs`: Type-safe ID wrappers and custom exceptions
   - See `docs/mna/theory.md`, `docs/mna/solver.md`, `docs/mna/api.md` for detailed documentation

2. **Voxel Layer (`src/voxel/`)**: Voxel-based world representation
   - `VoxelGrid.cs`: Sparse voxel storage with O(log n) access via SVO
   - `SparseVoxelOctree.cs`, `IncrementalPrismBuilder.cs`: Optimized storage internals
   - `MnaTopology/`: Converts voxel geometry to MNA circuit topology
   - See `docs/voxel/storage.md`, `docs/voxel/topology.md` for detailed architecture

3. **Handbook Layer (`src/handbook/`)**: 2D visualization application
   - Standalone GUI for testing and documentation
   - See `docs/handbook/architecture.md`, `docs/handbook/design.md` for details

4. **Mod Layer (`src/mod/`)**: Vintage Story mod integration
   - See `docs/mod/integration.md`, `docs/mod/cable-layer.md` for details

### Key Algorithms

- **Solver selection**: Dense LU for ≤96 nodes or high density (≥0.18); CSparse for larger sparse systems
- **Graph partitioning**: Disconnected sub-circuits solve independently in parallel
- **Line optimization**: Chains of series resistors merge into single equivalent resistors; intermediate node voltages interpolated
- **Newton-Raphson**: Iterative linearization for nonlinear components (diodes)
- **Backward Euler**: Time integration for capacitors/inductors

## Design Documentation

All design docs are in `docs/`, organized by layer:
- `docs/mna/`: MNA theory, solver internals, high-level API
- `docs/voxel/`: VoxelGrid storage, topology extraction
- `docs/handbook/`: 2D circuit editor architecture and design
- `docs/mod/`: Vintage Story integration and cable laying system
- `docs/plans/`: Implementation plans for past and current work

API documentation is in `apidocs/`. This includes a complete copy of the Vintage Story documentation, but not the wiki.

When making changes, update the relevant design docs in `docs/` to stay aligned.

## Bugs

When fixing bugs, always make a regression test first.

## Code Style

- C# .NET 8.0, nullable enabled, LangVersion latest
- 4-space indents, braces on same line (K&R style)
- PascalCase types/methods/properties, camelCase locals/parameters
- Keep solver math well-commented; align terminology with `docs/mna/theory.md`
- Functions should *usually* have only a single purpose.
- Important: When possible, make errors impossible through construction, not checks. Parse inputs. Avoid nullable types. Use newtypes and other type-system features to avoid any form of type confusion.
  When it comes to API design, minimize the number of ways they can be used; ideally, any misuse should fail to type-check.
- Important: When a condition should be impossible, prefer crashes to logging.

## Testing

Tests use NUnit in `tests/`. Name tests as scenario + expectation (e.g., `TestVoltageDivider`). Use tolerance checks like `Within(1e-6)` for floating-point comparisons.

### Testing Philosophy

Tests should be **understandable, straightforward, and obviously correct**. Coverage matters, but not at the expense of readability. Prefer:

- **Property-based tests** where the property definition IS the specification (e.g., Kirchhoff's laws, Ohm's law). If a property is hard to express clearly, that may indicate the code needs refactoring.
- **Verifying relationships** over specific values. Arriving at the same result by two different methods (solver vs formula) is as reliable as checking hardcoded expected values.
- **Relative tolerance** for numeric properties across wide value ranges, not absolute tolerance.

### Property-Based Testing with CsCheck

Use [CsCheck](https://github.com/AnthonyLloyd/CsCheck) for property-based testing. It was chosen over FsCheck for:
- Cleaner numeric range syntax: `Gen.Double[1, 1_000_000]`
- Better shrinking for dependent variables (e.g., failures when r1 ≈ r2)
- More intuitive C# API

```csharp
// Example: Voltage divider property
Gen.Select(
    Gen.Double[1, 1000],      // voltage
    Gen.Double[1, 1_000_000], // r1
    Gen.Double[1, 1_000_000]  // r2
).Sample((voltage, r1, r2) => {
    var (vMid, _) = SolveVoltageDivider(voltage, r1, r2);
    var expected = voltage * r2 / (r1 + r2);
    var relErr = Math.Abs(vMid - expected) / expected;
    if (relErr >= 1e-6)
        throw new Exception($"vMid={vMid}, expected={expected}, relErr={relErr:e}");
});
```

See `tests/PropertyTestCsCheck.cs` for examples.

## Version Control

This project uses **Jujutsu (jj)** instead of git. Key commands:

```bash
jj status                    # Show working copy changes
jj log                       # Show commit history
jj diff                      # Show current changes
jj commit -m "message"       # Set commit message for current change, *and* create a new change; like git commit.
jj new                       # Create a new change on top of current
jj squash                    # Squash current change into parent
```
