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
