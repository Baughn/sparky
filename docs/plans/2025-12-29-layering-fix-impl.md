# Layering Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix inverted MNA/Voxel dependencies and introduce VoxelSimulation as the unified spatial simulation facade.

**Architecture:** VoxelSimulation owns VoxelGrid, ISimulation (MNA), and MnaTopologyBuilder. Consumers query spatial positions, not MNA node IDs. MNA becomes a pure math library with no spatial concepts.

**Tech Stack:** C# .NET 8.0, NUnit for tests

---

## Task 1: Create VoxelSimulation Scaffold

**Files:**
- Create: `src/voxel/VoxelSimulation.cs`
- Test: `tests/voxel/VoxelSimulationTests.cs`

**Step 1: Write the failing test**

```csharp
// tests/voxel/VoxelSimulationTests.cs
using NUnit.Framework;
using Sparky.Voxel;

namespace Sparky.Tests.Voxel;

[TestFixture]
public class VoxelSimulationTests {
    [Test]
    public void Step_WithEmptyGrid_DoesNotThrow() {
        var sim = new VoxelSimulation();
        Assert.DoesNotThrow(() => sim.Step(0.001));
    }

    [Test]
    public void Grid_ReturnsVoxelGrid() {
        var sim = new VoxelSimulation();
        Assert.That(sim.Grid, Is.Not.Null);
        Assert.That(sim.Grid, Is.InstanceOf<VoxelGrid>());
    }

    [Test]
    public void ElectricalEnabled_DefaultsToTrue() {
        var sim = new VoxelSimulation();
        Assert.That(sim.ElectricalEnabled, Is.True);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~VoxelSimulationTests"`
Expected: FAIL with "VoxelSimulation not found"

**Step 3: Write minimal implementation**

```csharp
// src/voxel/VoxelSimulation.cs
using Sparky.Mna.Api;

namespace Sparky.Voxel;

/// <summary>
/// Unified facade for spatial simulation combining voxel storage with domain solvers.
/// </summary>
/// <remarks>
/// VoxelSimulation owns:
/// - VoxelGrid (spatial state)
/// - ISimulation (MNA electrical solver)
/// - Topology builders (convert voxels to solver inputs)
///
/// Consumers query spatial positions, not solver-internal IDs.
/// </remarks>
public class VoxelSimulation {
    private readonly VoxelGrid _grid = new();
    private readonly SimulationManager _mnaSimulation = new();

    /// <summary>
    /// The voxel grid containing spatial state.
    /// </summary>
    public VoxelGrid Grid => _grid;

    /// <summary>
    /// Whether electrical simulation is enabled.
    /// </summary>
    public bool ElectricalEnabled { get; set; } = true;

    /// <summary>
    /// Advances all enabled simulations by dt seconds.
    /// </summary>
    public void Step(double dt) {
        if (ElectricalEnabled) {
            _mnaSimulation.Step(dt);
        }
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~VoxelSimulationTests"`
Expected: PASS

**Step 5: Commit**

```bash
jj describe -m "Add VoxelSimulation scaffold with Grid and Step"
```

---

## Task 2: Move MnaTopology to Voxel

**Files:**
- Move: `src/mna/topology/` → `src/voxel/MnaTopology/`
- Modify: `src/voxel/Sparky.Voxel.csproj`
- Modify: `src/mna/Sparky.Mna.csproj`

**Step 1: Move the directory**

```bash
mv src/mna/topology src/voxel/MnaTopology
```

**Step 2: Update namespaces in all moved files**

Replace in all files under `src/voxel/MnaTopology/`:
- `namespace Sparky.Mna.Topology` → `namespace Sparky.Voxel.MnaTopology`
- `using Sparky.Mna.Topology` → `using Sparky.Voxel.MnaTopology`

Files to update:
- `src/voxel/MnaTopology/TopologyBuilder.cs`
- `src/voxel/MnaTopology/Component.cs`
- `src/voxel/MnaTopology/TerminalRegion.cs`
- `src/voxel/MnaTopology/ComponentTypes/BatteryComponent.cs`
- `src/voxel/MnaTopology/ComponentTypes/GroundComponent.cs`
- `src/voxel/MnaTopology/ComponentTypes/ResistorComponent.cs`
- `src/voxel/MnaTopology/ComponentTypes/SwitchComponent.cs`
- `src/voxel/MnaTopology/CableLaying/CablePathfinder.cs`
- `src/voxel/MnaTopology/CableLaying/CableValidator.cs`
- `src/voxel/MnaTopology/CableLaying/CrossSection.cs`
- `src/voxel/MnaTopology/CableLaying/IWorldVoxelCache.cs`
- `src/voxel/MnaTopology/CableLaying/PathResult.cs`
- `src/voxel/MnaTopology/CableLaying/SnapPositionFinder.cs`

**Step 3: Update project references**

In `src/voxel/Sparky.Voxel.csproj`, add:
```xml
<ItemGroup>
  <ProjectReference Include="../mna/Sparky.Mna.csproj" />
</ItemGroup>
```

In `src/mna/Sparky.Mna.csproj`, remove:
```xml
<ProjectReference Include="../voxel/Sparky.Voxel.csproj" />
```

**Step 4: Run build to verify**

Run: `dotnet build`
Expected: Build succeeds (may have warnings about unused usings)

**Step 5: Run tests**

Run: `dotnet test`
Expected: Tests fail due to namespace changes in consumers

**Step 6: Commit**

```bash
jj describe -m "Move topology to src/voxel/MnaTopology, flip Voxel/MNA dependency"
```

---

## Task 3: Update Handbook namespace references

**Files:**
- Modify: `src/handbook/server/GameServer.cs`

**Step 1: Update using statements**

Replace:
```csharp
using Sparky.Mna.Topology;
using Sparky.Mna.Topology.ComponentTypes;
```

With:
```csharp
using Sparky.Voxel.MnaTopology;
using Sparky.Voxel.MnaTopology.ComponentTypes;
```

**Step 2: Run build**

Run: `dotnet build src/handbook/Sparky.Handbook.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
jj describe -m "Update Handbook to use Sparky.Voxel.MnaTopology namespace"
```

---

## Task 4: Update Mod namespace references

**Files:**
- Modify: `src/mod/SparkyModSystem.cs`
- Modify: `src/mod/vsintegration/Debug/CacheDebugState.cs`
- Modify: `src/mod/vsintegration/CableLaying/CableLayingState.cs`
- Modify: `src/mod/vsintegration/CableLaying/WorldVoxelCache.cs`
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewSystem.cs`
- Modify: `src/mod/vsintegration/ItemWireTool.cs`
- Modify: `src/mod/vsintegration/CircuitNetworkManager.cs`

**Step 1: Update using statements in all files**

Replace in each file:
```csharp
using Sparky.Mna.Topology;
using Sparky.Mna.Topology.CableLaying;
using Sparky.Mna.Topology.ComponentTypes;
```

With:
```csharp
using Sparky.Voxel.MnaTopology;
using Sparky.Voxel.MnaTopology.CableLaying;
using Sparky.Voxel.MnaTopology.ComponentTypes;
```

Also update type aliases like:
```csharp
using TopologyBuilder = Sparky.Mna.Topology.TopologyBuilder;
using Component = Sparky.Mna.Topology.Component;
```

To:
```csharp
using TopologyBuilder = Sparky.Voxel.MnaTopology.TopologyBuilder;
using Component = Sparky.Voxel.MnaTopology.Component;
```

**Step 2: Run build**

Run: `dotnet build src/mod/mod.csproj`
Expected: Build succeeds

**Step 3: Run all tests**

Run: `dotnet test`
Expected: All 671 tests pass

**Step 4: Commit**

```bash
jj describe -m "Update Mod to use Sparky.Voxel.MnaTopology namespace"
```

---

## Task 5: Add spatial query methods to VoxelSimulation

**Files:**
- Modify: `src/voxel/VoxelSimulation.cs`
- Modify: `tests/voxel/VoxelSimulationTests.cs`

**Step 1: Write failing tests for spatial queries**

Add to `tests/voxel/VoxelSimulationTests.cs`:

```csharp
[Test]
public void GetVoltageAt_WithNoCircuit_ReturnsZero() {
    var sim = new VoxelSimulation();
    var voltage = sim.GetVoltageAt(new VoxelPos(0, 0, 0));
    Assert.That(voltage, Is.EqualTo(0.0));
}

[Test]
public void GetVoltageAt_WithSimpleCircuit_ReturnsCorrectVoltage() {
    var sim = new VoxelSimulation();

    // Place a ground at origin
    sim.Grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);

    // Place a wire from (1,0,0) to (3,0,0)
    sim.Grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.Conductor);
    sim.Grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);
    sim.Grid.SetVoxel(new VoxelPos(3, 0, 0), VoxelType.Conductor);

    // Add ground component at origin
    sim.AddGround(new VoxelPos(0, 0, 0));

    // Add voltage source: positive at (3,0,0), negative at (0,0,0), 5V
    sim.AddVoltageSource(new VoxelPos(3, 0, 0), new VoxelPos(0, 0, 0), 5.0);

    sim.RebuildTopology();
    sim.Step(0.001);

    // All connected conductors should be at 5V (relative to ground at 0V)
    Assert.That(sim.GetVoltageAt(new VoxelPos(3, 0, 0)), Is.EqualTo(5.0).Within(1e-6));
    Assert.That(sim.GetVoltageAt(new VoxelPos(0, 0, 0)), Is.EqualTo(0.0).Within(1e-6));
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~VoxelSimulationTests"`
Expected: FAIL - methods don't exist

**Step 3: Implement spatial query infrastructure**

Update `src/voxel/VoxelSimulation.cs`:

```csharp
using Sparky.Mna.Api;
using Sparky.Voxel.MnaTopology;

namespace Sparky.Voxel;

public class VoxelSimulation {
    private readonly VoxelGrid _grid = new();
    private readonly SimulationManager _mnaSimulation = new();
    private readonly TopologyBuilder _topologyBuilder = new();
    private readonly List<Component> _components = new();

    private Dictionary<VoxelPos, TopologyBuilder.ConductorRegion> _regions = new();
    private bool _topologyDirty = true;

    public VoxelGrid Grid => _grid;
    public bool ElectricalEnabled { get; set; } = true;

    /// <summary>
    /// Marks topology as needing rebuild (called when voxels change).
    /// </summary>
    public void MarkDirty() {
        _topologyDirty = true;
    }

    /// <summary>
    /// Rebuilds electrical topology from current voxel state.
    /// </summary>
    public void RebuildTopology() {
        _regions = _topologyBuilder.BuildTopology(_grid, _components, _mnaSimulation);
        _topologyDirty = false;
    }

    /// <summary>
    /// Advances all enabled simulations by dt seconds.
    /// </summary>
    public void Step(double dt) {
        if (_topologyDirty) {
            RebuildTopology();
        }

        if (ElectricalEnabled) {
            _mnaSimulation.Step(dt);
        }
    }

    /// <summary>
    /// Gets the voltage at a voxel position.
    /// Returns 0.0 if no conductor exists at this position.
    /// </summary>
    public double GetVoltageAt(VoxelPos pos) {
        if (_regions.TryGetValue(pos, out var region)) {
            return _mnaSimulation.GetVoltage(region.NodeId);
        }
        return 0.0;
    }

    /// <summary>
    /// Adds a ground reference at the specified voxel position.
    /// </summary>
    public void AddGround(VoxelPos pos) {
        var ground = new GroundComponent(pos);
        _components.Add(ground);
        _topologyDirty = true;
    }

    /// <summary>
    /// Adds a voltage source between two voxel positions.
    /// </summary>
    public void AddVoltageSource(VoxelPos positive, VoxelPos negative, double voltage) {
        var battery = new BatteryComponent(negative, positive, voltage);
        _components.Add(battery);
        _topologyDirty = true;
    }
}
```

**Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~VoxelSimulationTests"`
Expected: PASS

**Step 5: Commit**

```bash
jj describe -m "Add spatial voltage query to VoxelSimulation"
```

---

## Task 6: Add current query to VoxelSimulation

**Files:**
- Modify: `src/voxel/VoxelSimulation.cs`
- Modify: `tests/voxel/VoxelSimulationTests.cs`

**Step 1: Write failing test**

Add to `tests/voxel/VoxelSimulationTests.cs`:

```csharp
[Test]
public void GetCurrentThrough_WithResistiveWire_ReturnsCorrectCurrent() {
    var sim = new VoxelSimulation();

    // Ground at origin
    sim.Grid.SetVoxel(new VoxelPos(0, 0, 0), VoxelType.Conductor);
    sim.AddGround(new VoxelPos(0, 0, 0));

    // Resistive wire from (1,0,0) to (2,0,0)
    sim.Grid.SetVoxel(new VoxelPos(1, 0, 0), VoxelType.ResistiveConductor);
    sim.Grid.SetVoxel(new VoxelPos(2, 0, 0), VoxelType.Conductor);

    // 5V source at (2,0,0)
    sim.AddVoltageSource(new VoxelPos(2, 0, 0), new VoxelPos(0, 0, 0), 5.0);

    sim.RebuildTopology();
    sim.Step(0.001);

    // Current through resistive voxel should be non-zero
    var current = sim.GetCurrentThrough(new VoxelPos(1, 0, 0));
    Assert.That(current, Is.Not.EqualTo(0.0));
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "GetCurrentThrough"`
Expected: FAIL - method doesn't exist

**Step 3: Implement GetCurrentThrough**

Add to `src/voxel/VoxelSimulation.cs`:

```csharp
/// <summary>
/// Gets the current flowing through a voxel position.
/// For resistive conductors, returns the current through adjacent resistors.
/// Returns 0.0 if no conductor exists or no current is flowing.
/// </summary>
public double GetCurrentThrough(VoxelPos pos) {
    if (!_regions.TryGetValue(pos, out var region)) {
        return 0.0;
    }

    // Get max current from adjacent resistors
    double maxCurrent = 0;
    foreach (var resistorId in region.AdjacentResistors) {
        var current = Math.Abs(_mnaSimulation.GetResistorCurrent(resistorId));
        maxCurrent = Math.Max(maxCurrent, current);
    }

    return maxCurrent;
}
```

**Step 4: Run test**

Run: `dotnet test --filter "GetCurrentThrough"`
Expected: PASS

**Step 5: Commit**

```bash
jj describe -m "Add spatial current query to VoxelSimulation"
```

---

## Task 7: Update Handbook to use VoxelSimulation

**Files:**
- Modify: `src/handbook/server/GameServer.cs`

**Step 1: Replace field declarations**

Replace:
```csharp
private readonly VoxelGrid _voxelGrid = new();
private readonly SimulationManager _simulation = new();
private readonly TopologyBuilder _topologyBuilder = new();
```

With:
```csharp
private readonly VoxelSimulation _simulation = new();
```

**Step 2: Update grid access**

Replace all occurrences of `_voxelGrid` with `_simulation.Grid`.

**Step 3: Update topology rebuild**

Replace:
```csharp
_regions = _topologyBuilder.BuildTopology(_voxelGrid, _components, _simulation);
```

With:
```csharp
_simulation.RebuildTopology();
// Note: regions are now internal to VoxelSimulation
```

**Step 4: Update voltage queries**

Replace:
```csharp
var voltage = _simulation.GetVoltage(region.NodeId);
```

With:
```csharp
var voltage = _simulation.GetVoltageAt(voxelPos);
```

**Step 5: This is a significant refactor - break into subtasks**

This task is complex because GameServer uses regions directly for visual state computation. Options:

A) Expose regions through VoxelSimulation (leak abstraction temporarily)
B) Add additional query methods to VoxelSimulation
C) Keep GameServer using lower-level APIs for now, migrate incrementally

**Recommended: Option A for this phase** - expose `_regions` via a property, plan to remove it later.

Add to `VoxelSimulation.cs`:
```csharp
/// <summary>
/// Gets the conductor regions map.
/// TEMPORARY: Exposed for migration, will be removed.
/// </summary>
[Obsolete("Use spatial query methods instead. Will be removed.")]
public IReadOnlyDictionary<VoxelPos, TopologyBuilder.ConductorRegion> Regions => _regions;

/// <summary>
/// Gets the underlying MNA simulation.
/// TEMPORARY: Exposed for migration, will be removed.
/// </summary>
[Obsolete("Use spatial query methods instead. Will be removed.")]
public ISimulation MnaSimulation => _mnaSimulation;
```

**Step 6: Update GameServer to use VoxelSimulation with temporary accessors**

Replace field usage patterns:
- `_voxelGrid` → `_simulation.Grid`
- `_topologyBuilder.BuildTopology(...)` → `_simulation.RebuildTopology(); _regions = _simulation.Regions`
- `_simulation.GetVoltage(nodeId)` → `_simulation.MnaSimulation.GetVoltage(nodeId)` (temporary)
- `_simulation.Step(dt)` → `_simulation.Step(dt)`

**Step 7: Run tests**

Run: `dotnet test`
Expected: All tests pass

**Step 8: Commit**

```bash
jj describe -m "Migrate Handbook to VoxelSimulation (with temporary accessors)"
```

---

## Task 8: Update Mod to use VoxelSimulation

**Files:**
- Modify: `src/mod/vsintegration/CircuitNetworkManager.cs`

**Step 1: Update NetworkState class**

Replace:
```csharp
public class NetworkState {
    public Guid Id { get; init; }
    public VoxelGrid Voxels { get; } = new();
    public TopologyBuilder Topology { get; } = new();
    public ISimulation Simulation { get; init; } = null!;
    // ...
}
```

With:
```csharp
public class NetworkState {
    public Guid Id { get; init; }
    public VoxelSimulation Simulation { get; } = new();
    // ...
}
```

**Step 2: Update voxel grid access**

Replace `network.Voxels` with `network.Simulation.Grid`.

**Step 3: Update topology rebuild**

Replace:
```csharp
var regions = network.Topology.BuildTopology(
    network.Voxels,
    Enumerable.Empty<Component>(),
    network.Simulation);
```

With:
```csharp
network.Simulation.RebuildTopology();
```

**Step 4: Update simulation stepping**

Replace:
```csharp
network.Simulation.Step(TimeStep);
```

With:
```csharp
network.Simulation.Step(TimeStep);
```
(Same call - VoxelSimulation.Step forwards to MNA)

**Step 5: Run tests**

Run: `dotnet test`
Expected: All tests pass

**Step 6: Commit**

```bash
jj describe -m "Migrate Mod to VoxelSimulation"
```

---

## Task 9: Update context documentation

**Files:**
- Modify: `context/voxel-storage.md`
- Modify: `context/mna-api.md`

**Step 1: Update voxel-storage.md**

Add VoxelSimulation section:
```markdown
## VoxelSimulation

`VoxelSimulation` is the unified facade for spatial simulation:

- Owns `VoxelGrid` (spatial state)
- Owns MNA simulation (electrical solver)
- Owns `MnaTopologyBuilder` (voxels → circuit)
- Provides spatial queries: `GetVoltageAt(pos)`, `GetCurrentThrough(pos)`

Consumers should not interact with MNA node IDs directly. Query spatial positions instead.

### Domain Toggles

- `ElectricalEnabled` - enables/disables MNA stepping
- Future: `ThermalEnabled`, `KineticEnabled`
```

**Step 2: Update mna-api.md**

Note the new layer boundary:
```markdown
## Layer Boundary

MNA is a pure circuit math library with no spatial concepts. The Voxel layer provides:
- `VoxelSimulation` - unified facade
- `MnaTopologyBuilder` - extracts circuits from voxels

Direct use of `ISimulation` for creating/stepping simulations is allowed.
Querying voltage/current should go through `VoxelSimulation` spatial methods.
```

**Step 3: Commit**

```bash
jj describe -m "Update context docs for VoxelSimulation architecture"
```

---

## Task 10: Run full test suite and cleanup

**Step 1: Run all tests**

Run: `dotnet test`
Expected: All 671+ tests pass

**Step 2: Run build in Release mode**

Run: `dotnet build -c Release`
Expected: Build succeeds, mod zip created

**Step 3: Check for any remaining old namespace references**

Run: `grep -r "Sparky.Mna.Topology" src/`
Expected: No matches (all migrated to Sparky.Voxel.MnaTopology)

**Step 4: Final commit**

```bash
jj describe -m "Complete layering fix: VoxelSimulation facade with proper MNA/Voxel separation"
```

---

## Summary

After completing all tasks:

1. **MNA** (`src/mna/`) is pure circuit math with no spatial dependencies
2. **Voxel** (`src/voxel/`) owns:
   - `VoxelGrid` - spatial storage
   - `MnaTopology/` - voxel → circuit conversion
   - `VoxelSimulation` - unified facade
3. **Handbook/Mod** use `VoxelSimulation` for spatial queries
4. Layer violations are fixed - no upward dependencies
