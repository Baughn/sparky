# Cell Graph Design for Vintage Story

This captures how the “cell / sub-solver” model maps onto Vintage Story’s API for electrical/thermal/mechanical simulation blocks (generators, turbines, motors, ovens, lights, wires with multiple lines per block, diodes/resistors, etc.).

## API Fit (VS hooks)
- Blocks that participate in simulation get a `BlockEntity`; reuse common logic via `BlockEntityBehavior` where possible.
- Lifecycle: `BlockEntity.Initialize` (chunk load/placement) to register; `OnBlockBroken`/`OnBlockRemoved` to unregister; `OnBlockPlaced`/orientation changes trigger neighbor graph updates. Persist per-block state in `ToTreeAttributes`/`FromTreeAttributes`.
- Ticking: drive the simulation from server-side `IEventAPI.RegisterGameTickListener` (20 tps). Keep heavy work server-only; clients receive state via `MarkDirty`/packet sync for visuals.
- Persistence/caching: small state on the BE; chunk-scoped caches (net IDs, adjacency) can live in `IWorldChunk.LiveModData` mirrored into chunk moddata on unload; global managers live in a `ModSystem`.
- Threading: VS API/world access is not thread-safe—only pure simulation data runs on worker threads; all world reads/writes and BE mutations happen on the main/server thread.

## Proposed Implementation
- **Data model:**  
  - Each BE implements an `ISimCell` that exposes domain-specific ports: `(BlockPos pos, BlockFacing face, int portId, Domain domain)` describing connectors. Wires with multiple lines per block expose multiple ports; devices expose one or more ports per domain (electrical, thermal, kinetic).  
  - A `SimulationSystem` (ModSystem) tracks cells by chunk and domain, assigns stable cell IDs, and builds per-domain graphs.
- **Suggested shapes (server-side):**
  ```csharp
  public enum SimDomain { Electrical, Thermal, Kinetic }

  public readonly record struct SimPort(BlockPos Pos, BlockFacing Face, int PortId, SimDomain Domain);

  public interface ISimCell
  {
      IEnumerable<SimPort> GetPorts();                      // stable across ticks/rotations
      bool CanConnect(SimPort local, SimPort remote);       // port-compatibility filter
      void PreSim(float dt, SimDomain domain);              // read world state -> sim state
      void PostSim(float dt, SimDomain domain, object sim); // apply results -> BE
  }

  public interface ISimSubSolver
  {
      SimDomain Domain { get; }
      void AddCell(ISimCell cell, SimPort[] ports);
      void Solve(float dt); // pure, no world access
  }
  ```

- **Graph/build:**  
  - On placement/load, the cell registers; on removal/unload it deregisters. Neighbor changes trigger a local refresh (re-scan the 6 faces plus in-block port data).  
  - Graphs are built incrementally: flood-fill from dirty cells to form connected components (“sub-solvers”) per domain; cache network IDs in chunk `LiveModData` so reloads don’t rebuild the entire world.  
  - Port compatibility rules are owned by the block/behavior (e.g., face + size + portId) so filtering stays deterministic and chunk-boundary-safe.
- **Simulation loop (per tick):**  
  1. **Pre** (main thread): invoke per-cell prehooks to pull gameplay state into the sim (fuel/steam pressure, switch states, rotor speed/torque, oven temps).  
  2. **Solve** (worker threads): dispatch each sub-solver as a job. Electrical sub-solvers build the MNA circuit, run linear or NR solves; thermal/kinetic nets run their solvers. Jobs only touch copied state.  
  3. **Post** (main thread): commit outputs back to BEs (currents/voltages, torque, heat), trigger effects, and `MarkDirty` for client visuals.  
  - Batch tiny sub-solvers per tick to avoid thread-pool churn; cap work per tick and queue overflow to the next tick if needed.
- **Skeleton for the ModSystem tick:**
  ```csharp
  public override void StartServerSide(ICoreServerAPI api)
  {
      api.Event.RegisterGameTickListener(OnTick, 50); // 20 tps
  }

  private void OnTick(float dt)
  {
      // 1) collect dirty cells; rebuild affected sub-solvers
      // 2) pre: main thread
      foreach (var cell in cells) cell.PreSim(dt, SimDomain.Electrical);

      // 3) schedule jobs
      foreach (var sub in electricalSubSolvers)
          jobQueue.Enqueue(sub, dt);

      jobQueue.Drain(); // wait for worker completion

      // 4) post: main thread
      foreach (var cell in cells) cell.PostSim(dt, SimDomain.Electrical, /*sim*/ null);
  }
  ```

- **Persistence:**  
  - Per-block: ports/config/state via `TreeAttribute` serialization.  
  - Per-network: store lightweight IDs and maybe last-known solutions in chunk moddata if you want warm starts; rebuild topology from cells on load otherwise.  
  - Mod-level configs (e.g., tick budget, max net size) go through the usual mod config files.
- **Chunk loading / connectivity:**  
  - If any cell of a network is in a loaded chunk, walk its connectivity and request loads (“tickets”) for all chunks that contain connected cells so the full network is simulated—avoid partial nets that would misbehave.  
  - Release tickets when no loaded cells remain in that network. Apply a sanity cap on distance/size to prevent runaway loads from huge bases; beyond the cap, either pause the network or require a player-placed “anchor” block to opt-in to long-distance loading.

## Downsides / Risks
- Chunk churn: heavy edits or large bases can cause many local rebuilds; mitigate with localized flood-fills, rate limiting, and caching network IDs per chunk.
- Multi-wire blocks increase adjacency complexity; port IDs must be stable across rotations/variants to avoid reconnect bugs.
- Thread safety: any accidental world/BE access off-thread will crash or corrupt state; enforce a strict data-copy boundary for jobs.
- Sync/IO: overusing `MarkDirty` or sending large state each tick will hurt bandwidth; keep client payloads minimal and derive visuals where possible.
- Reflection/attributes are handy for registration, but avoid using reflection in hot tick paths—prefer static registries or source-gen if the boilerplate grows.

---

## Phase 2: Voxel-Based Connectivity Model

> **Note**: This section describes the new voxel-based model that replaces the explicit port-based connectivity from Phase 1.

### Overview

Instead of explicit ports with direction declarations, connectivity is now **implicit via adjacency**. Each VS block contains a 16×16×16 voxel grid. Adjacent conductor voxels automatically connect - no port declarations needed.

### Voxel Coordinates

```csharp
// Absolute voxel position in world space
public readonly record struct VoxelPos(int X, int Y, int Z)
{
    // 16 voxels per VS block axis
    public const int VoxelsPerBlock = 16;

    // Which VS block contains this voxel
    public BlockPos Block => new(
        X >= 0 ? X / VoxelsPerBlock : (X - VoxelsPerBlock + 1) / VoxelsPerBlock,
        Y >= 0 ? Y / VoxelsPerBlock : (Y - VoxelsPerBlock + 1) / VoxelsPerBlock,
        Z >= 0 ? Z / VoxelsPerBlock : (Z - VoxelsPerBlock + 1) / VoxelsPerBlock
    );
}
```

### Voxel Types

```csharp
public enum VoxelType
{
    Air,        // Empty space - no connectivity
    Conductor,  // Connects to adjacent conductors
    Insulator   // Blocks connectivity (used in component bodies)
}
```

### Implicit Connectivity

Two conductor voxels connect if they are adjacent in any of the 6 cardinal directions. This means:
- **4 parallel traces** require 8 voxels (4 conductors + 3 gaps)
- **Wire crossings** are built as 3D structures (physical separation)
- **Diodes** use insulating voxels between anode and cathode terminals

### Multi-Voxel Components

Components are no longer single cells. A battery might be 14×14×8 voxels:
- **Terminal regions**: Conductor voxels at + and - ends that interface with external wiring
- **Body**: Insulating voxels (or just not part of voxel grid) - doesn't participate in connectivity
- **Behavior**: MNA component (voltage source) connects between terminal regions

```csharp
public abstract class Component
{
    public VoxelPos Origin { get; }
    public abstract IReadOnlyList<TerminalRegion> Terminals { get; }

    // Called during topology rebuild
    public abstract void CreateMnaComponents(ISimulation sim,
        IReadOnlyDictionary<string, NodeId> terminalNodes);
}

public class TerminalRegion
{
    public string Name { get; }  // "positive", "negative", "anode", etc.
    public IReadOnlySet<VoxelPos> Voxels { get; }  // Conductor voxels in this terminal
}
```

### Topology Building

The topology is built by flood-filling connected conductor regions:

1. **Find conductor regions**: Flood-fill from each conductor voxel to find connected components. Each region = one MNA node.
2. **Map terminals to nodes**: For each component, determine which MNA node each terminal region touches.
3. **Create MNA components**: Connect voltage sources, resistors, etc. between the appropriate nodes.

```csharp
public class TopologyBuilder
{
    void RebuildTopology(VoxelGrid voxels, IEnumerable<Component> components, ISimulation sim)
    {
        // 1. Flood-fill to find connected conductor regions
        var regions = FindConductorRegions(voxels);  // Each region → one NodeId

        // 2. Map component terminals to nodes
        foreach (var component in components)
        {
            var terminalNodes = new Dictionary<string, NodeId>();
            foreach (var terminal in component.Terminals)
            {
                // Find which region this terminal touches
                var node = FindNodeForTerminal(terminal, regions);
                terminalNodes[terminal.Name] = node;
            }

            // 3. Create MNA components
            component.CreateMnaComponents(sim, terminalNodes);
        }
    }
}
```

### Wire is Just Conductor Voxels

There is no special "WireCell" type. Wire is simply conductor voxels placed by the player. Adjacent conductor voxels naturally form connected regions that share an MNA node.

### Ground Component

Ground is a component with one terminal region that forces its connected conductor region to MNA node 0 (ground).

### Materials (Phase 2.5 - IMPLEMENTED)

Each conductor voxel has an associated `Material` that defines its resistivity:

```csharp
public sealed class Material
{
    public string Name { get; }
    public double Resistivity { get; }  // Ω/voxel (game-scaled)

    // Predefined materials
    public static Material Copper { get; }  // 0.001 Ω/voxel - baseline
    public static Material Lead { get; }    // 0.01 Ω/voxel - 10x copper, for fuses
    public static Material Iron { get; }    // 0.005 Ω/voxel - 5x copper
    public static Material Gold { get; }    // 0.0015 Ω/voxel - 1.5x copper
}
```

**Game-scaled resistivity**: Values are in Ω/voxel for easy mental math:
- 100 copper voxels = 0.1Ω
- 100 lead voxels = 1Ω (heats up faster → fuse material)

**API Usage**:
```csharp
// Default to copper
grid.SetVoxel(pos, VoxelType.Conductor);

// Specify material
grid.SetVoxel(pos, Material.Lead);

// Query material
Material? mat = grid.GetMaterial(pos);  // null for Air/Insulator
```

### Prism Coalescing (Phase 3 - Future)

> **Deferred to Phase 3**: Large regions of same-material conductors will coalesce into "prisms" for efficient representation as single resistors. Resistance calculation: R = ρ × L / A where L is length and A is cross-section area.
