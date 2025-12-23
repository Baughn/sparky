# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Design documentation is consolidated in `context/`. Project source code lives in `src/` (core, mod, 2d), with tests in `tests/` and benchmarks in `benchmarks/`.

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

### Three-Layer Design

1. **Core Layer (`src/core/mna/core/`)**: Low-level solver handling matrix assembly and linear algebra
   - `Circuit.cs`: Main solver with Newton-Raphson iteration, dense/sparse path selection
   - `Component.cs`: Abstract base for all circuit elements
   - Component implementations: `Resistor`, `VoltageSource`, `CurrentSource`, `Capacitor`, `Inductor`, `Diode`, `Transformer`

2. **API Layer (`src/core/mna/api/`)**: High-level interface for game integration
   - `ISimulation.cs`: Public interface with strongly-typed IDs
   - `SimulationManager.cs`: Implementation with graph partitioning and line optimization
   - `Exceptions.cs`, `Ids.cs`: Type-safe ID wrappers and custom exceptions

3. **Game Layer (`src/core/game/core/`)**: Voxel-based world representation
   - `VoxelGrid.cs`: Sparse voxel storage with O(log n) access via SVO
   - `TopologyBuilder.cs`: Converts voxel geometry to MNA circuit topology
   - `SparseVoxelOctree.cs`, `IncrementalPrismBuilder.cs`: Optimized storage internals
   - See `context/voxel-storage.md` for detailed architecture

### Key Algorithms

- **Solver selection**: Dense LU for ≤96 nodes or high density (≥0.18); CSparse for larger sparse systems
- **Graph partitioning**: Disconnected sub-circuits solve independently in parallel
- **Line optimization**: Chains of series resistors merge into single equivalent resistors; intermediate node voltages interpolated
- **Newton-Raphson**: Iterative linearization for nonlinear components (diodes)
- **Backward Euler**: Time integration for capacitors/inductors

## Design Documentation

All design docs are in `context/`:
- `context/mna-theory.md`: MNA theory, component stamps, solver architecture
- `context/mna-api.md`: High-level API design, partitioning, line optimization
- `context/voxel-storage.md`: VoxelGrid SVO + incremental prism architecture
- `context/vsintegration.md`: Vintage Story mod integration layer

API documentation is in apidocs/. This includes a complete copy of the Vintage Story documentation, but not the wiki.

When making changes, update the relevant design docs in `context/` to stay aligned.

## Bugs

When fixing bugs, always make a regression test first.

## Code Style

- C# .NET 8.0, nullable enabled, LangVersion latest
- 4-space indents, braces on same line (K&R style)
- PascalCase types/methods/properties, camelCase locals/parameters
- Keep solver math well-commented; align terminology with `context/mna-theory.md`
- Functions should *usually* have only a single purpose.
- Important: When possible, make errors impossible through construction, not checks. Parse inputs. Avoid nullable types. Use newtypes and other type-system features to avoid any form of type confusion.
  When it comes to API design, minimize the number of ways they can be used; ideally, any misuse should fail to type-check.
- Important: When a condition should be impossible, prefer crashes to logging.

## Testing

Tests use NUnit in `tests/`. Name tests as scenario + expectation (e.g., `TestVoltageDivider`). Use tolerance checks like `Within(1e-6)` for floating-point comparisons.

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
