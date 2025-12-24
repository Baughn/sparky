# Plan: JSON-Defined Composite Components + Terminal Blocks

## Goal

1. **Immediate**: Enable placement of "terminal blocks" (zero-resistance conductor blocks that serve as component connection points) in creative mode
2. **Short-term**: Create JSON schema for component blueprints that define componentClass + terminals
3. **Future**: Full component system where players place components that build internal MNA graphs

## Current State

- **2D Game**: Components hardcoded in `ComponentTemplates.cs` with 3-cell layouts
- **VS Mod**: Only conductors JSON-defined via `attributes.sparky.conductor`; no components
- **BEBehaviorCircuit**: Stores conductor voxels; no component instance tracking
- **TopologyBuilder**: Receives `Enumerable.Empty<Component>()` - component integration incomplete

## Design Decisions (Confirmed)

| Decision | Choice |
|----------|--------|
| Component sizes | JSON-defined per component (not uniform) |
| Config UI | Right-click with empty hand opens config dialog |
| Dual-layer (chisel) | Deferred to later implementation |
| Creative placement | New tool for terminal blocks (WireTool stays for conductors) |

## Architecture Vision

```
JSON Blueprint
├── componentClass: "Battery" (C# class implementing component logic)
├── terminals: named connection points (zero-resistance conductor blocks)
├── size: [4, 3, 3] (voxel dimensions)
└── parameters: { voltage: 5.0, configurable: true }

In-Game Flow:
1. Player places component (fills space with terminal blocks + body)
2. BEBehaviorCircuit tracks component instance
3. Component.Setup(terminals[]) receives conductor references
4. Component builds internal MNA graph (non-spatial: capacitors, diodes, etc.)
5. Component.Tick() called each simulation tick (e.g., battery voltage adjustment)
```

---

## Implementation Phases

> **Scope**: Implement Phases 1-2 now. Phases 3-4 are documented for future work.

### Phase 1: Terminal Block Type + Placement Tool [IMPLEMENT NOW]

**Goal**: Add a "terminal" voxel type that acts as zero-resistance conductor, placeable via new creative tool.

**JSON Definition** (`src/mod/assets/sparky/blocktypes/terminal.json`):
```json
{
  "code": "terminal",
  "attributes": {
    "sparky": {
      "conductor": true,
      "terminal": true,
      "resistivity": 0.0,
      "displayName": "Terminal"
    }
  }
}
```

**Files to create:**
- `src/mod/vsintegration/ItemTerminalTool.cs` - Creative tool for placing terminal voxels
- `src/mod/assets/sparky/itemtypes/terminaltool.json` - Tool definition

**Files to modify:**
- `src/mod/SparkyModSystem.cs` - Register new item class
- `src/mod/vsintegration/MaterialRegistry.cs` - Handle terminal flag
- `src/core/game/core/Material.cs` - Add `Material.Terminal` (resistivity = 0)

### Phase 2: Component Blueprint JSON Schema [IMPLEMENT NOW]

**Goal**: Define JSON schema for component blueprints. No placement yet - just the data model.

**Example Blueprint** (`src/mod/assets/sparky/components/battery-small.json`):
```json
{
  "code": "battery-small",
  "componentClass": "Battery",
  "size": [3, 1, 1],
  "terminals": {
    "negative": { "voxels": [[0, 0, 0]] },
    "positive": { "voxels": [[2, 0, 0]] }
  },
  "body": {
    "voxels": [[1, 0, 0]],
    "material": "insulator"
  },
  "parameters": {
    "voltage": { "default": 5.0, "min": 0.1, "max": 100.0 }
  },
  "displayName": "Small Battery"
}
```

**Files to create:**
- `src/core/game/core/ComponentBlueprint.cs` - Blueprint data structure (VS-independent)
- `src/mod/vsintegration/ComponentRegistry.cs` - Load blueprints from JSON

**Files to modify:**
- `src/mod/SparkyModSystem.cs` - Call ComponentRegistry.Load() in AssetsFinalize

### Phase 3: Component Instance Storage [FUTURE]

**Goal**: Enable BEBehaviorCircuit to track placed component instances.

**Data Model:**
```csharp
public class PlacedComponent {
    public string BlueprintCode { get; }
    public VoxelPos Origin { get; }
    public int Rotation { get; }
    public Dictionary<string, object> Parameters { get; }
}
```

**Files to modify:**
- `src/mod/vsintegration/BEBehaviorCircuit.cs`:
  - Add `List<PlacedComponent> Components`
  - Add `PlaceComponent(blueprint, origin, rotation)`
  - Add `RemoveComponent(origin)`
  - Serialize/deserialize components

### Phase 4: Component Integration with Simulation [FUTURE]

**Goal**: Wire up component instances to the MNA solver.

**Files to modify:**
- `src/core/game/core/TopologyBuilder.cs` - Accept component list, call Setup()
- `src/mod/vsintegration/CircuitNetworkManager.cs` - Pass components to BuildTopology
- `src/core/game/core/Component.cs` - Add Setup(terminals) and Tick() methods

---

## Future Work (Post-Plan)

These items are deferred but should be kept in mind:

### Survival Mode Component Placement
- Craftable component items (not tools)
- Components consume materials on placement
- Components drop items when broken

### Component Config UI
- Right-click with empty hand opens dialog
- Adjust voltage, resistance, etc.
- Visual feedback of current values

### 2D Game Unification
- Refactor `ComponentTemplates.cs` to use shared `ComponentBlueprint`
- Load same JSON definitions

### Dual-Layer System (Chisel Coexistence)
- Allow vanilla chisel on circuit blocks for aesthetics
- Keep circuit voxels and decorative voxels as separate layers
- Render both layers together

---

## Critical Files

### To Create
- `src/core/game/core/ComponentBlueprint.cs` - Blueprint data structure
- `src/mod/vsintegration/ComponentRegistry.cs` - Load blueprints from JSON
- `src/mod/vsintegration/ItemTerminalTool.cs` - Terminal placement tool
- `src/mod/assets/sparky/itemtypes/terminaltool.json` - Tool definition
- `src/mod/assets/sparky/blocktypes/terminal.json` - Terminal block definition
- `src/mod/assets/sparky/components/battery-small.json` - Example blueprint

### To Modify
- `src/core/game/core/Material.cs` - Add Material.Terminal
- `src/core/game/core/Component.cs` - Add Setup(terminals) and Tick() methods
- `src/core/game/core/TopologyBuilder.cs` - Accept component list
- `src/mod/SparkyModSystem.cs` - Register new classes, load registries
- `src/mod/vsintegration/MaterialRegistry.cs` - Handle terminal flag
- `src/mod/vsintegration/BEBehaviorCircuit.cs` - Component instance storage
- `src/mod/vsintegration/CircuitNetworkManager.cs` - Pass components to BuildTopology

### Reference (Read-Only)
- `src/mod/vsintegration/ItemWireTool.cs` - Pattern for tool implementation
- `src/core/game/core/ComponentTypes/BatteryComponent.cs` - Existing component implementation
- `src/2d/protocol/ComponentTemplates.cs` - Current 2D hardcoded templates
- `src/2d/server/GameServer.cs` - 2D component placement logic
