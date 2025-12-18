# Plan: Convert BlockEntityCircuit to Behavior-Based Architecture

## Goal

Convert the monolithic `BlockEntityCircuit` class into a `BEBehaviorCircuit` that can be:
1. Attached to any block with a compatible block entity (Generic, MicroBlock, etc.)
2. Self-contained with its own voxel storage
3. Route cables through existing blocks like fence posts

## Design Decisions

- **Storage:** Behavior uses its own voxel storage (not host's microblock storage)
- **Migration:** Disable `BlockEntityCircuit` early, keep for reference
- **Injection:** Filter by `EntityClass == "Generic"` or `null`

## Current Architecture

```
BlockEntityCircuit : BlockEntityMicroBlock
├── NetworkId (Guid)
├── Static conductor registry
├── SetConductorVoxel/RemoveVoxel (uses inherited SetVoxel)
├── ExportToVoxelGrid
└── Lifecycle hooks → CircuitNetworkManager
```

**Consumers:**
- `CircuitNetworkManager` - registers blocks, exports to VoxelGrid, sets NetworkId
- `VoxelPreviewSystem` - creates blocks, calls SetConductorVoxelsBatch/RemoveVoxel
- `WorldVoxelCache` - reads VoxelCuboids/BlockIds for pathfinding
- `BlockCircuit` - gets selection boxes
- `SparkyModSystem` - registers classes, calls static RegisterConductor

## Target Architecture

```
BEBehaviorCircuit : BlockEntityBehavior
├── Own voxel storage (ConductorCuboids, ConductorBlockIds)
├── NetworkId (Guid)
├── SetConductorVoxel/RemoveVoxel
├── ExportToVoxelGrid
├── Mesh generation (OnTesselation)
└── Lifecycle hooks → CircuitNetworkManager

Can attach to:
├── BlockEntityGeneric (uses own voxel storage)
├── BlockEntityMicroBlock (can use own OR host's storage)
└── Any BlockEntity subclass
```

## Key API Findings

From BlockEntityMicroBlock:
- `SetVoxel()` is **PUBLIC** - accessible from behaviors
- `VoxelCuboids`, `BlockIds` are **PUBLIC**
- `MarkMeshDirty()`, `CreateMesh()` are **PUBLIC**
- `IMicroblockBehavior` interface for mesh regeneration callbacks

## Phased Implementation

### Phase 1: Create BEBehaviorCircuit with Own Storage ✅ COMPLETE
**Goal:** Standalone behavior with self-contained voxel storage

**Created:** `src/mod/vsintegration/BEBehaviorCircuit.cs`

```csharp
public class BEBehaviorCircuit : BlockEntityBehavior
{
    // Own voxel storage
    public List<uint> ConductorCuboids = new();
    public int[] ConductorBlockIds = Array.Empty<int>();
    public Guid NetworkId { get; internal set; }

    // Voxel API
    public void SetConductorVoxel(int x, int y, int z, Material material);
    public void RemoveVoxel(int x, int y, int z);
    public Material? GetConductorAt(int x, int y, int z);
    public void ExportToVoxelGrid(VoxelGrid grid, SparkyBlockPos pos);

    // Rendering
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tess);

    // Serialization
    public override void ToTreeAttributes(ITreeAttribute tree);
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world);
}
```

**Completed:**
- ✅ Created `BEBehaviorCircuit.cs` with own voxel storage
- ✅ Duplicated static conductor registry (both classes have it during transition)
- ✅ Registered behavior class `"sparky:circuit"` in `SparkyModSystem.Start()`
- ✅ Conductors registered with both old entity and new behavior
- ✅ Network manager calls commented out with `// TODO Phase 2`

**Test:** Unit tests deferred - requires VS API mocking. Will verify via manual testing in Phase 2.

**Checkpoint:** ✅ Build succeeds, all 669 existing tests pass

---

### Phase 2: Disable BlockEntityCircuit, Update CircuitNetworkManager ✅ COMPLETE
**Goal:** Switch network manager to use behavior, disable old entity

**Completed:**
- ✅ `CircuitNetworkManager.cs`: Changed `RegisterBlock(BlockPos, BEBehaviorCircuit)`
- ✅ `CircuitNetworkManager.cs`: Updated `ProcessDirtyBlocks()` to use `GetBehavior<BEBehaviorCircuit>()`
- ✅ `SparkyModSystem.cs`: Commented out `RegisterBlockEntityClass("BlockEntityCircuit", ...)`
- ✅ `circuitblock.json`: Changed `entityClass` to `"Generic"`, added `entityBehaviors: [{ "name": "sparky:circuit" }]`
- ✅ `BlockEntityCircuit.cs`: Disabled network manager calls (kept for reference)
- ✅ `BEBehaviorCircuit.cs`: Enabled all network manager integration

**Test:** Manual - place circuit block, verify network registration works

**Checkpoint:** ✅ Build succeeds, all 669 tests pass

---

### Phase 3: Runtime Behavior Injection
**Goal:** Inject behavior into vanilla blocks

**Changes to `SparkyModSystem.AssetsFinalize()`:**
```csharp
foreach (Block block in api.World.Blocks) {
    if (block.Code == null || block.Id == 0) continue;
    if (!ShouldInjectCircuitBehavior(block)) continue;
    if (HasCircuitBehavior(block)) continue;
    block.BlockEntityBehaviors = block.BlockEntityBehaviors
        .Append(new BlockEntityBehaviorType { Name = "sparky:circuit" })
        .ToArray();
    if (block.EntityClass == null) block.EntityClass = "Generic";
}

bool ShouldInjectCircuitBehavior(Block block) =>
    block.EntityClass == "Generic" || block.EntityClass == null;

bool HasCircuitBehavior(Block block) =>
    block.BlockEntityBehaviors?.Any(b => b?.Name == "sparky:circuit") == true;
```

**Completed:**
- ✅ `SparkyModSystem.cs`: Injects `BEBehaviorCircuit` into Generic/null blocks, avoids duplicates

**Test:** Manual - verify fence posts, signs, etc. have the behavior attached

**Checkpoint:** Vanilla blocks have circuit behavior

---

### Phase 4: VoxelPreviewSystem Integration
**Goal:** Wire tool places voxels via behavior on any block

**Changes to `VoxelPreviewSystem.cs`:**
- Rename `GetOrCreateCircuitBlock()` → `GetOrCreateCircuitBehavior()`
- Return `BEBehaviorCircuit?` instead of `BlockEntityCircuit?`
- For blocks that already have behavior: return it
- For replaceable blocks: place `sparky:circuitblock`, get behavior
- Call `behavior.SetConductorVoxelsBatch()` / `behavior.RemoveVoxel()`
- Update `ItemWireTool` to treat `BEBehaviorCircuit` blocks as circuit hosts

**Completed:**
- ✅ `ItemWireTool.cs`: Single-voxel place/remove treats behavior blocks as circuit hosts

**Test:** Manual - use wire tool on fence post, see conductor voxels render

**Checkpoint:** Can place wires on fence posts

---

### Phase 5: WorldVoxelCache Integration
**Goal:** Cable pathfinding sees behavior's voxels

**Changes to `WorldVoxelCache.cs`:**
- In `ProcessBlock()`, check for `BEBehaviorCircuit` behavior
- Read `behavior.ConductorCuboids` and `behavior.ConductorBlockIds`
- Use `BEBehaviorCircuit.IsConductor()` (static method moved from old class)

**Completed:**
- ✅ `WorldVoxelCache.cs`: Uses `BEBehaviorCircuit` data to classify conductors/insulators
- ✅ Added regression test for behavior cuboid mapping

**Test:** Manual - cable pathfinding routes around existing conductors in fence posts

**Checkpoint:** Full cable routing works with behavior-based blocks

---

### Phase 6: Selection Boxes (If Needed)
**Goal:** Per-voxel selection on behavior's conductors

May need a `BlockBehaviorCircuit` that overrides `GetSelectionBoxes()` to include conductor voxels from the BE behavior. Evaluate after Phase 5.

---

### Phase 7: Cleanup
**Goal:** Remove deprecated code

- Delete `BlockEntityCircuit.cs` (or archive to `_deprecated/`)
- Remove commented registrations
- Update `context/vsintegration.md` documentation

**Test:** Full regression

## Files to Modify

| Phase | File | Change |
|-------|------|--------|
| 1 | `src/mod/vsintegration/BEBehaviorCircuit.cs` | **Create** |
| 1 | `src/mod/SparkyModSystem.cs` | Register behavior class |
| 1 | `tests/` | Add unit tests for behavior |
| 2 | `src/mod/vsintegration/CircuitNetworkManager.cs` | Use `BEBehaviorCircuit` |
| 2 | `src/mod/vsintegration/BlockEntityCircuit.cs` | Comment out registration |
| 2 | `assets/sparky/blocktypes/circuitblock.json` | Use Generic + behavior |
| 3 | `src/mod/SparkyModSystem.cs` | Add `AssetsFinalize` injection |
| 4 | `src/mod/vsintegration/Preview/VoxelPreviewSystem.cs` | Find behavior instead of entity |
| 5 | `src/mod/vsintegration/CableLaying/WorldVoxelCache.cs` | Read from behavior |
| 7 | `src/mod/vsintegration/BlockEntityCircuit.cs` | Delete or archive |
| 7 | `context/vsintegration.md` | Update documentation |

## Risk Mitigation

- **Each phase has a checkpoint** - don't proceed until tests pass
- **BlockEntityCircuit kept for reference** - can revert if needed
- **Behavior injection is additive** - doesn't break existing blocks without behaviors

---

## Background Research

### How Block Entity Behaviors Work in Vintage Story

**BlockEntityBehavior** is a composition pattern that attaches reusable functionality to any `BlockEntity` without requiring inheritance. This is distinct from `BlockBehavior` (which attaches to `Block` classes).

**Base class:** `Vintagestory.API.Common.BlockEntityBehavior`

**Key properties/methods:**
```csharp
public class BlockEntityBehavior
{
    public BlockEntity Blockentity;      // The host block entity
    public BlockPos Pos => Blockentity.Pos;
    public Block Block => Blockentity.Block;
    public ICoreAPI Api;
    public JsonObject properties;        // From JSON definition

    // Lifecycle (called by BlockEntity, which delegates to all behaviors)
    public virtual void Initialize(ICoreAPI api, JsonObject properties);
    public virtual void OnBlockRemoved();
    public virtual void OnBlockUnloaded();
    public virtual void OnBlockPlaced(ItemStack byItemStack = null);
    public virtual void OnBlockBroken(IPlayer byPlayer = null);

    // Serialization (behavior data stored in block entity's tree)
    public virtual void ToTreeAttributes(ITreeAttribute tree);
    public virtual void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world);

    // Rendering (can add meshes to chunk)
    public virtual bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tess);

    // Networking
    public virtual void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data);
    public virtual void OnReceivedServerPacket(int packetid, byte[] data);

    // Info display
    public virtual void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc);
}
```

**Source:** `apidocs/vsapi/Common/Collectible/Block/BlockEntityBehavior.cs`

### How Behaviors Are Registered and Attached

**1. Registration (in ModSystem.Start or StartServerSide):**
```csharp
api.RegisterBlockEntityBehaviorClass("sparky:circuit", typeof(BEBehaviorCircuit));
```

**2. JSON declaration (in block's JSON file):**
```json
{
    "entityClass": "Generic",
    "entityBehaviors": [
        { "name": "sparky:circuit", "properties": { "someOption": true } }
    ]
}
```

**3. Automatic instantiation:** When `BlockEntity.CreateBehaviors()` is called (during block entity creation), it iterates `block.BlockEntityBehaviors` and instantiates each registered behavior.

**Source:** `apidocs/vsapi/Common/Collectible/Block/BlockEntity.cs:102-120`

**Key insight from `apidocs/vsessentialsmod/Loading/BlockType.cs:473-476`:**
```csharp
// If a block has entity behaviors but no entity class, VS auto-assigns "Generic"
if (block.EntityClass == null && block.BlockEntityBehaviors != null && block.BlockEntityBehaviors.Length > 0)
{
    block.EntityClass = "Generic";
}
```

### Runtime Behavior Injection Pattern

Behaviors can be injected at runtime during `AssetsFinalize`, before blocks are used. The `ModSystemBlockReinforcement` in vssurvivalmod demonstrates this pattern:

**Source:** `apidocs/vssurvivalmod/Systems/BlockReinforcement.cs:506-518`
```csharp
public override void AssetsFinalize(ICoreAPI api)
{
    if (api.Side == EnumAppSide.Server)
    {
        addReinforcementBehavior();
    }
}

private void addReinforcementBehavior()
{
    foreach (Block block in api.World.Blocks)
    {
        if (block.Code == null || block.Id == 0) continue;

        if (IsReinforcable(block))
        {
            // For BlockBehaviors (not BE behaviors, but same pattern applies)
            block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorReinforcable(block));
        }
    }
}
```

For **BlockEntityBehaviors**, the equivalent is:
```csharp
var beh = new BlockEntityBehaviorType { Name = "sparky:circuit" };
block.BlockEntityBehaviors = block.BlockEntityBehaviors.Append(beh).ToArray();
```

### Example: BEBehaviorMicroblockSnowCover

This behavior demonstrates self-contained voxel storage on top of a microblock host:

**Source:** `apidocs/vssurvivalmod/Systems/Microblock/BEBehaviorMicroblockSnowCover.cs`

```csharp
public class BEBehaviorMicroblockSnowCover : BlockEntityBehavior, IRotatable, IMicroblockBehavior
{
    // Own storage - NOT using host's VoxelCuboids
    public List<uint> SnowCuboids = new List<uint>();
    public List<uint> GroundSnowCuboids = new List<uint>();
    public MeshData SnowMesh;

    BlockEntityMicroBlock beMicroBlock;

    public BEBehaviorMicroblockSnowCover(BlockEntity blockentity) : base(blockentity)
    {
        // Cast to access microblock-specific features if needed
        beMicroBlock = blockentity as BlockEntityMicroBlock;
    }

    // Mesh generation - adds snow on TOP of the block's normal mesh
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tess)
    {
        // ... generate SnowMesh from SnowCuboids ...
        mesher.AddMeshData(SnowMesh);
        return false; // Don't skip the block's own mesh
    }

    // Serialization - uses sparky_ style prefixes to avoid collisions
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        tree["snowcuboids"] = new IntArrayAttribute(SnowCuboids.ToArray());
        tree["groundSnowCuboids"] = new IntArrayAttribute(GroundSnowCuboids.ToArray());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        uint[] snowvalues = (tree["snowcuboids"] as IntArrayAttribute)?.AsUint;
        SnowCuboids = snowvalues != null ? new List<uint>(snowvalues) : new();
        // ...
    }

    // IMicroblockBehavior - callbacks when host rebuilds
    public void RebuildCuboidList(BoolArray16x16x16 voxels, byte[,,] voxelMaterial) { ... }
    public void RegenMesh() { ... }
}
```

**Key takeaways:**
- Behavior stores its own `SnowCuboids` list, separate from host's `VoxelCuboids`
- Uses `BlockEntityMicroBlock.CreateMesh()` (static, public) to generate mesh
- Serialization uses distinct attribute keys to avoid conflicts
- Implements `IMicroblockBehavior` for mesh regeneration callbacks

### BlockEntityMicroBlock Public API

The following are all **PUBLIC** and accessible from behaviors:

| Member | Type | Description |
|--------|------|-------------|
| `VoxelCuboids` | `List<uint>` | Packed cuboid data (4 bits per coord + 8 bits material) |
| `BlockIds` | `int[]` | Material palette mapping index → VS block ID |
| `SetVoxel(Vec3i, bool, byte, int)` | method | Add/remove voxel at position |
| `MarkMeshDirty()` | method | Invalidate mesh for regeneration |
| `CreateMesh(...)` | static method | Generate mesh from cuboid list |
| `ToUint(...)` / `FromUint(...)` | static methods | Pack/unpack cuboid data |
| `RegenSelectionBoxes(...)` | method | Rebuild selection boxes after voxel changes |

**Source:** `apidocs/vssurvivalmod/Systems/Microblock/BEMicroBlock.cs`

### Why Own Storage Instead of Host's MicroBlock Storage

**Problem:** We want to route cables through blocks like fence posts that use `BlockEntityGeneric`, not `BlockEntityMicroBlock`. These blocks have no voxel storage.

**Solution:** The behavior carries its own `ConductorCuboids` storage, just like `BEBehaviorMicroblockSnowCover` carries `SnowCuboids`.

**Benefits:**
1. Works with ANY block entity class (Generic, MicroBlock, containers, etc.)
2. Conductor voxels render as overlay via `OnTesselation`
3. Self-contained serialization
4. No dependency on host's voxel system

**Trade-off:** For blocks that ARE microblocks, we could theoretically share storage. We chose not to for simplicity - the behavior is always self-contained.

### Coupling Analysis: What Uses BlockEntityCircuit

**CircuitNetworkManager.cs:**
- `RegisterBlock(BlockPos, BlockEntityCircuit)` - receives instance on Initialize
- `ProcessDirtyBlocks()` - casts `GetBlockEntity()` result, calls `ExportToVoxelGrid()`
- Sets `be.NetworkId` after topology rebuild

**VoxelPreviewSystem.cs:**
- `GetOrCreateCircuitBlock()` - creates circuit blocks, returns `BlockEntityCircuit`
- Calls `be.SetConductorVoxelsBatch()`, `be.RemoveVoxel()`
- Checks `be.VoxelCuboids.Count` to detect empty blocks

**WorldVoxelCache.cs:**
- `ProcessBlock()` - checks `if (be is BlockEntityCircuit circuit)`
- Reads `circuit.VoxelCuboids`, `circuit.BlockIds`
- Calls static `BlockEntityCircuit.IsConductor()`

**BlockCircuit.cs:**
- `GetSelectionBoxes()` - casts to `BlockEntityCircuit`, calls `GetSelectionBoxes()`
- Accesses `VoxelCuboids`, `BlockIds` for collision

**SparkyModSystem.cs:**
- `RegisterBlockEntityClass("BlockEntityCircuit", typeof(BlockEntityCircuit))`
- Calls static `BlockEntityCircuit.RegisterConductor()`

### Static Conductor Registry

The mapping from VS block IDs to Sparky `Material` types is currently stored in `BlockEntityCircuit`:

```csharp
private static readonly Dictionary<int, Material> BlockIdToMaterial = new();

public static void RegisterConductor(int blockId, Material material);
public static bool IsConductor(int blockId);
public static Material? GetConductorMaterial(int blockId);
```

This will move to `BEBehaviorCircuit` unchanged - it's static/global, not instance-specific.
