# Repository Reorganization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reorganize the repository from nested `core/` structure to topic-based top-level directories with matching test structure.

**Architecture:** Split `src/core/` into `src/voxel/` (shared infrastructure) and `src/mna/` (electrical solver). Rename `src/2d/` to `src/handbook/`. Move tests to mirror source structure. Consolidate build outputs to `build/`.

**Tech Stack:** .NET 8, C#, jj (version control)

---

## Phase 1: Build Infrastructure

### Task 1: Create Directory.Build.props

**Files:**
- Create: `Directory.Build.props`

**Step 1: Create the build props file**

```xml
<Project>
  <PropertyGroup>
    <BaseOutputPath>$(MSBuildThisFileDirectory)build/</BaseOutputPath>
    <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)build/obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
  </PropertyGroup>
</Project>
```

**Step 2: Clean old build artifacts**

Run: `rm -rf src/*/bin src/*/obj tests/bin tests/obj benchmarks/bin benchmarks/obj`

**Step 3: Verify build still works**

Run: `dotnet build`
Expected: SUCCESS (outputs now in `build/`)

**Step 4: Commit**

```bash
jj describe -m "Add Directory.Build.props for centralized build output"
jj new
```

---

## Phase 2: Create New Project Structure

### Task 2: Create Sparky.Voxel project

**Files:**
- Create: `src/voxel/Sparky.Voxel.csproj`

**Step 1: Create directory and project file**

```bash
mkdir -p src/voxel
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Sparky.Voxel</AssemblyName>
    <RootNamespace>Sparky.Voxel</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Sparky.Tests" />
  </ItemGroup>
</Project>
```

**Step 2: Commit**

```bash
jj describe -m "Add Sparky.Voxel project skeleton"
jj new
```

---

### Task 3: Create Sparky.Mna project

**Files:**
- Create: `src/mna/Sparky.Mna.csproj`

**Step 1: Create directory structure and project file**

```bash
mkdir -p src/mna/{api,solver,topology,utilities}
mkdir -p src/mna/api/{Energy,Limits}
mkdir -p src/mna/topology/CableLaying
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Sparky.Mna</AssemblyName>
    <RootNamespace>Sparky.Mna</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CSparse" Version="4.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../voxel/Sparky.Voxel.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Sparky.Tests" />
  </ItemGroup>
</Project>
```

**Step 2: Commit**

```bash
jj describe -m "Add Sparky.Mna project skeleton"
jj new
```

---

### Task 4: Create Sparky.Handbook project

**Files:**
- Create: `src/handbook/Sparky.Handbook.csproj`

**Step 1: Create directory structure and project file**

```bash
mkdir -p src/handbook/{server,protocol,client/standalone}
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Sparky.Handbook</AssemblyName>
    <RootNamespace>Sparky.Handbook</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CairoSharp" Version="3.24.24.95" />
    <PackageReference Include="Silk.NET.Windowing" Version="2.21.0" />
    <PackageReference Include="Silk.NET.Input" Version="2.21.0" />
    <PackageReference Include="GtkSharp" Version="3.24.24.95" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../voxel/Sparky.Voxel.csproj" />
    <ProjectReference Include="../mna/Sparky.Mna.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Commit**

```bash
jj describe -m "Add Sparky.Handbook project skeleton"
jj new
```

---

## Phase 3: Move Voxel Infrastructure

### Task 5: Move voxel source files

**Files:**
- Move: `src/core/game/core/*.cs` (voxel files only) → `src/voxel/`

**Voxel files to move (NOT topology):**
- `BlockFacing.cs`
- `BlockPos.cs`
- `BlockVoxelData.cs`
- `IncrementalPrismBuilder.cs`
- `Material.cs`
- `Prism.cs`
- `SparseVoxelOctree.cs`
- `SpatialHash.cs`
- `VoxelGrid.cs`
- `VoxelPos.cs`
- `VoxelPositionHelper.cs`
- `VoxelType.cs`

**Step 1: Move files**

```bash
cd /Users/svein/dev/sparky
for f in BlockFacing BlockPos BlockVoxelData IncrementalPrismBuilder Material Prism SparseVoxelOctree SpatialHash VoxelGrid VoxelPos VoxelPositionHelper VoxelType; do
  mv src/core/game/core/${f}.cs src/voxel/
done
```

**Step 2: Update namespace in each file**

Change: `namespace Sparky.Game.Core;`
To: `namespace Sparky.Voxel;`

**Step 3: Verify voxel project builds**

Run: `dotnet build src/voxel/Sparky.Voxel.csproj`
Expected: SUCCESS

**Step 4: Commit**

```bash
jj describe -m "Move voxel infrastructure to src/voxel with Sparky.Voxel namespace"
jj new
```

---

## Phase 4: Move MNA Solver

### Task 6: Move MNA solver core files

**Files:**
- Move: `src/core/mna/core/*.cs` → `src/mna/solver/`

**Step 1: Move files**

```bash
mv src/core/mna/core/*.cs src/mna/solver/
```

**Step 2: Update namespace in each file**

Change: `namespace Sparky.MNA.Core {`
To: `namespace Sparky.Mna.Solver;`

(Also convert from brace style to file-scoped namespace)

**Step 3: Update using statements**

Files referencing `Sparky.MNA.Core` need to use `Sparky.Mna.Solver`

**Step 4: Commit**

```bash
jj describe -m "Move MNA solver to src/mna/solver with Sparky.Mna.Solver namespace"
jj new
```

---

### Task 7: Move MNA API files

**Files:**
- Move: `src/core/mna/api/*.cs` → `src/mna/api/`
- Move: `src/core/mna/api/Energy/*.cs` → `src/mna/api/Energy/`
- Move: `src/core/mna/api/Limits/*.cs` → `src/mna/api/Limits/`

**Step 1: Move files**

```bash
mv src/core/mna/api/*.cs src/mna/api/
mv src/core/mna/api/Energy/*.cs src/mna/api/Energy/
mv src/core/mna/api/Limits/*.cs src/mna/api/Limits/
```

**Step 2: Update namespaces**

- `Sparky.MNA.Api` → `Sparky.Mna.Api`
- `Sparky.MNA.Api.Energy` → `Sparky.Mna.Api.Energy`
- `Sparky.MNA.Api.Limits` → `Sparky.Mna.Api.Limits`

**Step 3: Update using statements**

Files referencing old namespaces need updating.

**Step 4: Commit**

```bash
jj describe -m "Move MNA API to src/mna/api with Sparky.Mna.Api namespace"
jj new
```

---

### Task 8: Move MNA utilities

**Files:**
- Move: `src/core/mna/utilities/*.cs` → `src/mna/utilities/`

**Step 1: Move files**

```bash
mv src/core/mna/utilities/*.cs src/mna/utilities/
```

**Step 2: Update namespace**

Change: `namespace Sparky.MNA.Utilities;`
To: `namespace Sparky.Mna.Utilities;`

**Step 3: Commit**

```bash
jj describe -m "Move MNA utilities to src/mna/utilities"
jj new
```

---

### Task 9: Move topology files to MNA

**Files:**
- Move: `src/core/game/core/TopologyBuilder.cs` → `src/mna/topology/`
- Move: `src/core/game/core/TerminalRegion.cs` → `src/mna/topology/`
- Move: `src/core/game/core/Component.cs` → `src/mna/topology/`
- Move: `src/core/game/core/ComponentTypes/*.cs` → `src/mna/topology/ComponentTypes/`
- Move: `src/core/game/core/CableLaying/*.cs` → `src/mna/topology/CableLaying/`

**Step 1: Create ComponentTypes directory and move files**

```bash
mkdir -p src/mna/topology/ComponentTypes
mv src/core/game/core/TopologyBuilder.cs src/mna/topology/
mv src/core/game/core/TerminalRegion.cs src/mna/topology/
mv src/core/game/core/Component.cs src/mna/topology/
mv src/core/game/core/ComponentTypes/*.cs src/mna/topology/ComponentTypes/
mv src/core/game/core/CableLaying/*.cs src/mna/topology/CableLaying/
```

**Step 2: Update namespaces**

- `Sparky.Game.Core` → `Sparky.Mna.Topology`
- `Sparky.Game.Core.ComponentTypes` → `Sparky.Mna.Topology.ComponentTypes`
- `Sparky.Game.Core.CableLaying` → `Sparky.Mna.Topology.CableLaying`

**Step 3: Add using for Sparky.Voxel where needed**

TopologyBuilder and CableLaying files use VoxelGrid, Prism, etc.

**Step 4: Verify MNA project builds**

Run: `dotnet build src/mna/Sparky.Mna.csproj`
Expected: SUCCESS

**Step 5: Commit**

```bash
jj describe -m "Move topology extraction to src/mna/topology"
jj new
```

---

## Phase 5: Move Handbook

### Task 10: Move handbook files

**Files:**
- Move: `src/2d/server/*.cs` → `src/handbook/server/`
- Move: `src/2d/protocol/*.cs` → `src/handbook/protocol/`
- Move: `src/2d/client/*.cs` → `src/handbook/client/standalone/`
- Move: `src/2d/Program.cs` → `src/handbook/`
- Move: `src/2d/IGameClient.cs`, `src/2d/IGameServer.cs` → `src/handbook/`

**Step 1: Move files**

```bash
mv src/2d/server/*.cs src/handbook/server/
mv src/2d/protocol/*.cs src/handbook/protocol/
mv src/2d/client/*.cs src/handbook/client/standalone/
mv src/2d/Program.cs src/handbook/
mv src/2d/IGameClient.cs src/2d/IGameServer.cs src/handbook/
```

**Step 2: Update namespaces**

- `Sparky.TwoD` → `Sparky.Handbook`
- `Sparky.TwoD.Server` → `Sparky.Handbook.Server`
- `Sparky.TwoD.Protocol` → `Sparky.Handbook.Protocol`
- `Sparky.TwoD.Client` → `Sparky.Handbook.Client.Standalone`

**Step 3: Update using statements for new MNA/Voxel namespaces**

**Step 4: Verify handbook builds**

Run: `dotnet build src/handbook/Sparky.Handbook.csproj`
Expected: SUCCESS

**Step 5: Commit**

```bash
jj describe -m "Move 2d to src/handbook with Sparky.Handbook namespace"
jj new
```

---

## Phase 6: Update Mod Project

### Task 11: Update mod project references

**Files:**
- Modify: `src/mod/mod.csproj`

**Step 1: Update project references**

Change:
```xml
<ProjectReference Include="../core/core.csproj" />
```

To:
```xml
<ProjectReference Include="../voxel/Sparky.Voxel.csproj" />
<ProjectReference Include="../mna/Sparky.Mna.csproj" />
```

**Step 2: Update using statements in mod source files**

- `using Sparky.Game.Core;` → `using Sparky.Voxel;` and/or `using Sparky.Mna.Topology;`
- `using Sparky.MNA.Api;` → `using Sparky.Mna.Api;`
- `using Sparky.MNA.Core;` → `using Sparky.Mna.Solver;`
- `using Sparky.Game.Core.CableLaying;` → `using Sparky.Mna.Topology.CableLaying;`

**Step 3: Verify mod builds**

Run: `dotnet build src/mod/mod.csproj`
Expected: SUCCESS (if VINTAGE_STORY is set)

**Step 4: Commit**

```bash
jj describe -m "Update mod project to use new Voxel and Mna projects"
jj new
```

---

## Phase 7: Move Tests

### Task 12: Create test directory structure

**Step 1: Create directories**

```bash
mkdir -p tests/voxel
mkdir -p tests/mna/{api,solver,topology/CableLaying}
mkdir -p tests/handbook
```

**Step 2: Commit**

```bash
jj describe -m "Create new test directory structure"
jj new
```

---

### Task 13: Move voxel tests

**Files:**
- Move: `tests/game/SparseVoxelOctreeTests.cs` → `tests/voxel/`
- Move: `tests/game/VoxelGridTests.cs` → `tests/voxel/`
- Move: `tests/game/IncrementalPrismBuilderTests.cs` → `tests/voxel/`
- Move: `tests/game/PrismTests.cs` → `tests/voxel/`
- Move: `tests/game/SpatialHashTests.cs` → `tests/voxel/`
- Move: `tests/game/BlockPosTests.cs` → `tests/voxel/`
- Move: `tests/game/VoxelPositionHelperTests.cs` → `tests/voxel/`
- Move: `tests/game/BlockFacingTests.cs` → `tests/voxel/`

**Step 1: Move files**

```bash
for f in SparseVoxelOctreeTests VoxelGridTests IncrementalPrismBuilderTests PrismTests SpatialHashTests BlockPosTests VoxelPositionHelperTests BlockFacingTests; do
  mv tests/game/${f}.cs tests/voxel/
done
```

**Step 2: Update using statements**

Change: `using Sparky.Game.Core;`
To: `using Sparky.Voxel;`

**Step 3: Commit**

```bash
jj describe -m "Move voxel tests to tests/voxel"
jj new
```

---

### Task 14: Move MNA solver tests (from root)

**Files:**
- Move: `tests/CircuitTests.cs` → `tests/mna/solver/`
- Move: `tests/ComponentTests.cs` → `tests/mna/solver/`
- Move: `tests/DiodeTests.cs` → `tests/mna/solver/`
- Move: `tests/TransformerTests.cs` → `tests/mna/solver/`
- Move: `tests/TransientTests.cs` → `tests/mna/solver/`
- Move: `tests/CurrentSourceTests.cs` → `tests/mna/solver/`
- Move: `tests/SolverPathTests.cs` → `tests/mna/solver/`
- Move: `tests/SolverRobustnessTests.cs` → `tests/mna/solver/`
- Move: `tests/EdgeCaseCircuitTests.cs` → `tests/mna/solver/`
- Move: `tests/ScenarioTests.cs` → `tests/mna/solver/`
- Move: `tests/AdvancedScenarioTests.cs` → `tests/mna/solver/`
- Move: `tests/PropertyTests.cs` → `tests/mna/solver/`
- Move: `tests/VerificationTests.cs` → `tests/mna/solver/`
- Move: `tests/ParallelPartitionTests.cs` → `tests/mna/solver/`
- Move: `tests/DiagnosticsTests.cs` → `tests/mna/solver/`

**Step 1: Move files**

```bash
for f in CircuitTests ComponentTests DiodeTests TransformerTests TransientTests CurrentSourceTests SolverPathTests SolverRobustnessTests EdgeCaseCircuitTests ScenarioTests AdvancedScenarioTests PropertyTests VerificationTests ParallelPartitionTests DiagnosticsTests; do
  mv tests/${f}.cs tests/mna/solver/
done
```

**Step 2: Update using statements**

Change: `using Sparky.MNA.Core;`
To: `using Sparky.Mna.Solver;`

**Step 3: Commit**

```bash
jj describe -m "Move MNA solver tests to tests/mna/solver"
jj new
```

---

### Task 15: Move MNA API tests

**Files:**
- Move: `tests/mna/*.cs` → `tests/mna/api/`

**Step 1: Move files**

```bash
mv tests/mna/*.cs tests/mna/api/
```

**Step 2: Update using statements**

Change: `using Sparky.MNA.Api;`
To: `using Sparky.Mna.Api;`

**Step 3: Commit**

```bash
jj describe -m "Move MNA API tests to tests/mna/api"
jj new
```

---

### Task 16: Move topology tests

**Files:**
- Move: `tests/game/TopologyBuilderTests.cs` → `tests/mna/topology/`
- Move: `tests/game/CableLaying/*.cs` → `tests/mna/topology/CableLaying/`

**Step 1: Move files**

```bash
mv tests/game/TopologyBuilderTests.cs tests/mna/topology/
mv tests/game/CableLaying/*.cs tests/mna/topology/CableLaying/
```

**Step 2: Update using statements**

- `using Sparky.Game.Core;` → `using Sparky.Mna.Topology;` (and `using Sparky.Voxel;` where needed)
- `using Sparky.Game.Core.CableLaying;` → `using Sparky.Mna.Topology.CableLaying;`

**Step 3: Commit**

```bash
jj describe -m "Move topology tests to tests/mna/topology"
jj new
```

---

### Task 17: Move handbook tests

**Files:**
- Move: `tests/2d/*.cs` → `tests/handbook/`

**Step 1: Move files**

```bash
mv tests/2d/*.cs tests/handbook/
```

**Step 2: Update using statements**

Change: `using Sparky.TwoD.*;`
To: `using Sparky.Handbook.*;`

**Step 3: Commit**

```bash
jj describe -m "Move handbook tests to tests/handbook"
jj new
```

---

### Task 18: Update test project file

**Files:**
- Modify: `tests/tests.csproj`

**Step 1: Update project references**

```xml
<ItemGroup>
  <ProjectReference Include="../src/voxel/Sparky.Voxel.csproj" />
  <ProjectReference Include="../src/mna/Sparky.Mna.csproj" />
  <ProjectReference Include="../src/handbook/Sparky.Handbook.csproj" />
</ItemGroup>

<ItemGroup Condition="'$(VINTAGE_STORY)' != ''">
  <ProjectReference Include="../src/mod/mod.csproj" />
  <Reference Include="VintagestoryAPI">
    <HintPath>$(VINTAGE_STORY)/VintagestoryAPI.dll</HintPath>
    <Private>true</Private>
  </Reference>
</ItemGroup>

<ItemGroup Condition="'$(VINTAGE_STORY)' == ''">
  <Compile Remove="mod/**" />
</ItemGroup>
```

**Step 2: Verify all tests pass**

Run: `dotnet test`
Expected: All tests pass

**Step 3: Commit**

```bash
jj describe -m "Update test project references"
jj new
```

---

## Phase 8: Update Benchmarks

### Task 19: Update benchmarks project

**Files:**
- Modify: `benchmarks/benchmarks.csproj`

**Step 1: Update project references**

```xml
<ItemGroup>
  <ProjectReference Include="../src/voxel/Sparky.Voxel.csproj" />
  <ProjectReference Include="../src/mna/Sparky.Mna.csproj" />
  <ProjectReference Include="../tests/tests.csproj" />
</ItemGroup>
```

**Step 2: Update using statements in benchmark files**

**Step 3: Verify benchmarks build**

Run: `dotnet build benchmarks/benchmarks.csproj`
Expected: SUCCESS

**Step 4: Commit**

```bash
jj describe -m "Update benchmarks project references"
jj new
```

---

## Phase 9: Update Solution

### Task 20: Update solution file

**Files:**
- Modify: `Sparky.sln`

**Step 1: Remove old projects, add new ones**

```bash
dotnet sln remove src/core/core.csproj
dotnet sln remove src/2d/2d.csproj
dotnet sln add src/voxel/Sparky.Voxel.csproj
dotnet sln add src/mna/Sparky.Mna.csproj
dotnet sln add src/handbook/Sparky.Handbook.csproj
```

**Step 2: Verify solution builds**

Run: `dotnet build`
Expected: SUCCESS

**Step 3: Commit**

```bash
jj describe -m "Update solution with new project structure"
jj new
```

---

## Phase 10: Cleanup

### Task 21: Remove old directories

**Step 1: Verify everything works**

Run: `dotnet build && dotnet test`
Expected: SUCCESS

**Step 2: Remove old directories**

```bash
rm -rf src/core src/2d
rm -rf tests/game tests/2d
rmdir tests/mna 2>/dev/null || true  # Remove if empty after moves
```

**Step 3: Commit**

```bash
jj describe -m "Remove old directory structure"
jj new
```

---

### Task 22: Update context documentation

**Files:**
- Modify: `context/mna-api.md` (update directory references)
- Modify: `context/voxel-storage.md` (update directory references)
- Modify: `context/vsintegration.md` (update directory references)
- Modify: `AGENTS.md` (update directory references)

**Step 1: Update file path references in documentation**

Replace references to old paths:
- `src/core/mna/` → `src/mna/`
- `src/core/game/core/` → `src/voxel/` or `src/mna/topology/`
- `src/2d/` → `src/handbook/`
- `Sparky.MNA.Core` → `Sparky.Mna.Solver`
- `Sparky.MNA.Api` → `Sparky.Mna.Api`
- `Sparky.Game.Core` → `Sparky.Voxel` or `Sparky.Mna.Topology`

**Step 2: Commit**

```bash
jj describe -m "Update documentation for new directory structure"
jj new
```

---

### Task 23: Final verification

**Step 1: Full build**

Run: `dotnet build -c Release`
Expected: SUCCESS, outputs in `build/Release/`

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass

**Step 3: Verify mod package**

Run: `ls build/Release/Sparky.zip` (or wherever the zip ends up)
Expected: Mod zip exists

**Step 4: Commit and squash if desired**

```bash
jj describe -m "Complete repository reorganization"
```

---

## Summary of Namespace Changes

| Old Namespace | New Namespace |
|--------------|---------------|
| `Sparky.MNA.Core` | `Sparky.Mna.Solver` |
| `Sparky.MNA.Api` | `Sparky.Mna.Api` |
| `Sparky.MNA.Api.Energy` | `Sparky.Mna.Api.Energy` |
| `Sparky.MNA.Api.Limits` | `Sparky.Mna.Api.Limits` |
| `Sparky.MNA.Utilities` | `Sparky.Mna.Utilities` |
| `Sparky.Game.Core` | `Sparky.Voxel` (for voxel files) |
| `Sparky.Game.Core` | `Sparky.Mna.Topology` (for topology files) |
| `Sparky.Game.Core.CableLaying` | `Sparky.Mna.Topology.CableLaying` |
| `Sparky.Game.Core.ComponentTypes` | `Sparky.Mna.Topology.ComponentTypes` |
| `Sparky.TwoD` | `Sparky.Handbook` |
| `Sparky.TwoD.Server` | `Sparky.Handbook.Server` |
| `Sparky.TwoD.Protocol` | `Sparky.Handbook.Protocol` |
| `Sparky.TwoD.Client` | `Sparky.Handbook.Client.Standalone` |

## File Movement Summary

| From | To |
|------|-----|
| `src/core/game/core/` (voxel files) | `src/voxel/` |
| `src/core/game/core/` (topology files) | `src/mna/topology/` |
| `src/core/mna/core/` | `src/mna/solver/` |
| `src/core/mna/api/` | `src/mna/api/` |
| `src/core/mna/utilities/` | `src/mna/utilities/` |
| `src/2d/` | `src/handbook/` |
| `tests/*.cs` (MNA solver tests) | `tests/mna/solver/` |
| `tests/mna/` | `tests/mna/api/` |
| `tests/game/` (voxel tests) | `tests/voxel/` |
| `tests/game/` (topology tests) | `tests/mna/topology/` |
| `tests/2d/` | `tests/handbook/` |
