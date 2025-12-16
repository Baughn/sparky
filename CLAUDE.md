# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Critical design docs are in the root directory. Localized design docs (e.g. for the tablet game) are in subdirectories. Subsystem and cross-cutting docs are generally in context/.

## Project Overview

Sparky is a Vintage Story mod that implements an electrical circuit simulator using Modified Nodal Analysis (MNA). The solver handles DC, transient (capacitors/inductors), and nonlinear (diodes) circuits with automatic graph partitioning for performance.

## Build & Test Commands

```bash
# Enter dev environment (automatic via direnv, or manual)
nix develop

# Build
dotnet build                    # Debug build
dotnet build -c Release         # Release build (creates bin/Sparky.zip)

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

1. **Core Layer (`MNA/Core/`)**: Low-level solver handling matrix assembly and linear algebra
   - `Circuit.cs`: Main solver with Newton-Raphson iteration, dense/sparse path selection
   - `Component.cs`: Abstract base for all circuit elements
   - Component implementations: `Resistor`, `VoltageSource`, `CurrentSource`, `Capacitor`, `Inductor`, `Diode`, `Transformer`

2. **API Layer (`MNA/Api/`)**: High-level interface for game integration
   - `ISimulation.cs`: Public interface with strongly-typed IDs
   - `SimulationManager.cs`: Implementation with graph partitioning and line optimization
   - `Exceptions.cs`, `Ids.cs`: Type-safe ID wrappers and custom exceptions

3. **Game Layer (`Game/Core/`)**: Voxel-based world representation
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

- `MNA.md`: MNA theory, component stamps, solver architecture
- `MNA/API.md`: High-level API design, partitioning, line optimization
- `context/voxel-storage.md`: VoxelGrid SVO + incremental prism architecture
- `TEST-PLAN.md`: Comprehensive test coverage roadmap

When making changes, update the relevant design docs (*.md in root and MNA/) to stay aligned. Prefer adding detailed subsystem knowledge to context files rather than this file.

## Bugs

When fixing bugs, always make a regression test first.

## Code Style

- C# .NET 8.0, nullable enabled, LangVersion latest
- 4-space indents, braces on new lines
- PascalCase types/methods/properties, camelCase locals/parameters
- Keep solver math well-commented; align terminology with `MNA.md`

## Testing

Tests use NUnit in `Sparky.Tests/`. Name tests as scenario + expectation (e.g., `TestVoltageDivider`). Use tolerance checks like `Within(1e-6)` for floating-point comparisons.

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
