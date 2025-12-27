# Repository Reorganization Design

*Created: 2025-12-27*

## Goals

1. Eliminate confusing nested `core/` directories
2. Organize source by topic at top level
3. Mirror test structure to source structure
4. Align namespaces with folder paths
5. Consolidate build outputs to `build/`

## Top-Level Source Layout

```
src/
├── voxel/      # Shared voxel infrastructure
├── mna/        # Electrical circuit solver (MNA)
├── mod/        # Vintage Story integration
└── handbook/   # Interactive handbook (client-server)
```

Future solvers (thermal, kinetic, etc.) become peers of `mna/`.

## Directory Details

### src/voxel/

VS-agnostic voxel infrastructure shared by all solvers:

```
src/voxel/
├── SparseVoxelOctree.cs
├── VoxelGrid.cs
├── IncrementalPrismBuilder.cs
├── Prism.cs
├── SpatialHash.cs
├── VoxelPos.cs
├── BlockPos.cs
├── VoxelPositionHelper.cs
├── Material.cs             # Typed accessor for JSON material properties
└── VoxelType.cs
```

### src/mna/

Electrical circuit solver with MNA-specific topology extraction:

```
src/mna/
├── api/                    # High-level interface for game integration
│   ├── ISimulation.cs
│   ├── SimulationManager.cs
│   ├── Ids.cs
│   └── Exceptions.cs
│
├── solver/                 # Low-level matrix solver
│   ├── Circuit.cs
│   ├── Component.cs
│   ├── Node.cs
│   ├── Resistor.cs
│   ├── VoltageSource.cs
│   ├── Capacitor.cs
│   ├── Diode.cs
│   └── ...
│
├── topology/               # Voxel → MNA circuit extraction
│   ├── TopologyBuilder.cs
│   ├── ConductorRegion.cs
│   └── CableLaying/
│       ├── CablePathfinder.cs
│       ├── CrossSection.cs
│       └── ...
│
└── utilities/
    ├── AcVoltageSource.cs
    ├── PwmVoltageSource.cs
    └── ...
```

### src/mod/

Vintage Story integration (structure mostly unchanged):

```
src/mod/
├── SparkyModSystem.cs
└── vsintegration/
    ├── BEBehaviorCircuit.cs
    ├── ItemWireTool.cs
    ├── CircuitNetworkManager.cs
    ├── Preview/
    ├── CableLaying/
    └── ...
```

### src/handbook/

Interactive handbook with X11-style client-server architecture:

```
src/handbook/
├── server/             # Simulation server
├── protocol/           # Wire protocol (shared by all clients)
└── client/
    └── standalone/     # Cairo standalone app
    # Future: ingame/   # In-game manual client
```

## Test Structure

Mirrors source structure:

```
tests/
├── voxel/
│   ├── SparseVoxelOctreeTests.cs
│   ├── VoxelGridTests.cs
│   ├── IncrementalPrismBuilderTests.cs
│   └── ...
│
├── mna/
│   ├── api/
│   │   ├── ApiTests.cs
│   │   ├── ApiSwitchTests.cs
│   │   └── ...
│   ├── solver/
│   │   ├── CircuitTests.cs
│   │   ├── ComponentTests.cs
│   │   ├── DiodeTests.cs
│   │   └── ...
│   └── topology/
│       ├── TopologyBuilderTests.cs
│       └── CableLaying/
│           └── ...
│
├── mod/
│   └── ...
│
├── handbook/
│   └── ...
│
├── regression/
│   └── ...
│
└── testhelpers/
    ├── CircuitBuilder.cs
    ├── CircuitPatterns.cs
    └── Tolerances.cs
```

## Namespace Mapping

| Folder | Namespace |
|--------|-----------|
| `src/voxel/` | `Sparky.Voxel` |
| `src/mna/api/` | `Sparky.Mna.Api` |
| `src/mna/solver/` | `Sparky.Mna.Solver` |
| `src/mna/topology/` | `Sparky.Mna.Topology` |
| `src/mna/utilities/` | `Sparky.Mna.Utilities` |
| `src/mod/vsintegration/` | `Sparky.Mod.VsIntegration` |
| `src/handbook/server/` | `Sparky.Handbook.Server` |
| `src/handbook/protocol/` | `Sparky.Handbook.Protocol` |
| `src/handbook/client/standalone/` | `Sparky.Handbook.Client.Standalone` |

## Project Files

```
sparky/
├── src/
│   ├── voxel/
│   │   └── Sparky.Voxel.csproj
│   ├── mna/
│   │   └── Sparky.Mna.csproj
│   ├── mod/
│   │   └── Sparky.Mod.csproj        # References Voxel, Mna
│   └── handbook/
│       └── Sparky.Handbook.csproj   # References Voxel, Mna
│
├── tests/
│   └── Sparky.Tests.csproj
│
├── benchmarks/
│   └── Sparky.Benchmarks.csproj
│
├── build/                           # All build outputs
│   ├── Debug/
│   ├── Release/
│   └── Sparky.zip                   # Mod package
│
├── Directory.Build.props
└── Sparky.sln
```

## Build Output Consolidation

`Directory.Build.props` redirects all output to `build/`:

```xml
<Project>
  <PropertyGroup>
    <BaseOutputPath>$(MSBuildThisFileDirectory)build/</BaseOutputPath>
    <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)build/obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
  </PropertyGroup>
</Project>
```

## Migration Summary

| From | To |
|------|----|
| `src/core/mna/core/` | `src/mna/solver/` |
| `src/core/mna/api/` | `src/mna/api/` |
| `src/core/mna/utilities/` | `src/mna/utilities/` |
| `src/core/game/core/` (SVO, VoxelGrid, Prisms) | `src/voxel/` |
| `src/core/game/core/` (TopologyBuilder, CableLaying) | `src/mna/topology/` |
| `src/2d/` | `src/handbook/` |
| `tests/*.cs` (root MNA tests) | `tests/mna/solver/` |
| `tests/mna/` | `tests/mna/api/` |
| `tests/game/` | `tests/voxel/` + `tests/mna/topology/` |
| `tests/2d/` | `tests/handbook/` |
