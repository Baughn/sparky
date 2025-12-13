# 2D Game Architecture

Last updated: 2025-12-13

This document describes the architecture of the 2D tablet circuit game in `Sparky.2D/`.

## Overview

The 2D game is a client-server architecture designed to eventually run inside Vintage Story as a tablet UI. For standalone development, both client and server run in-process.

## Key Files

```
Sparky.2D/
├── IGameServer.cs          # Server interface
├── IGameClient.cs          # Client interface
├── Program.cs              # Main loop
├── Protocol/
│   ├── GridPos.cs          # 2D grid coordinate
│   ├── CellType.cs         # Cell types + visual state
│   ├── InputEvent.cs       # Client → Server messages
│   ├── RenderCommand.cs    # Server → Client messages
│   └── ComponentTemplates.cs # Shared component layouts
├── Server/
│   └── GameServer.cs       # Simulation and state
└── Client/
    └── StandaloneClient.cs # GTK-based renderer
```

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                      StandaloneClient                        │
│  - GTK window with Cairo drawing                            │
│  - Input handling (mouse, keyboard)                         │
│  - Cell rendering with voltage coloring                     │
│  - Ghost preview and tooltips                               │
├────────────────────────────────┬────────────────────────────┤
│  InputEvent (place/remove)     │   RenderCommand (set/clear)│
│           ↓                    │           ↑                │
├────────────────────────────────┴────────────────────────────┤
│                        GameServer                           │
│  - Grid state (cells, components)                           │
│  - VoxelGrid for conductor connectivity                     │
│  - TopologyBuilder for region detection                     │
│  - SimulationManager (MNA solver)                           │
└─────────────────────────────────────────────────────────────┘
```

## Coordinate Systems

Two coordinate systems are used:

| System | Type | Usage |
|--------|------|-------|
| GridPos(X, Y) | 2D grid | UI placement, cell indexing |
| VoxelPos(X, Y, Z) | 3D voxel | Conductor connectivity |

**Mapping**: `GridPos(x, y)` → `VoxelPos(x, 0, y)` (XZ plane at Y=0)

## Client-Server Protocol

### Input Events (Client → Server)

```csharp
abstract record InputEvent;
record PlaceComponent(GridPos Pos, CellType Type, int Rotation) : InputEvent;
record RemoveComponent(GridPos Pos) : InputEvent;
record RequestFullState() : InputEvent;
```

### Render Commands (Server → Client)

```csharp
abstract record RenderCommand;
record SetCell(GridPos Pos, CellType Type, int Rotation, CellVisualState State) : RenderCommand;
record ClearCell(GridPos Pos) : RenderCommand;
record SetGridSize(int Width, int Height) : RenderCommand;
```

## Cell Types

```csharp
enum CellType
{
    Empty,              // Air
    Wire,               // Conductor
    Ground,             // 0V reference

    // Battery (3-cell component)
    Battery,            // Origin: negative terminal
    BatteryBody,        // Insulator
    BatteryPositive,    // Positive terminal

    // Resistor (3-cell component)
    Resistor,           // Origin: terminal A
    ResistorBody,       // Insulator
    ResistorTerminalB   // Terminal B
}
```

## 3-Cell Component Layout

Multi-cell components use **terminal - body - terminal** pattern:

```
Rotation 0 (+X):   [origin] → [body] → [far terminal]
Rotation 1 (+Y):   [origin] ↓ [body] ↓ [far terminal]
Rotation 2 (-X):   [far terminal] ← [body] ← [origin]
Rotation 3 (-Y):   [far terminal] ↑ [body] ↑ [origin]
```

The body cell is an **insulator** that prevents shorts between terminals.

**ComponentTemplates.cs** provides shared layout definitions:
```csharp
ComponentTemplates.GetCells(CellType.Battery, rotation)
// Returns: [(0,0, Battery), (offset, BatteryBody), (offset*2, BatteryPositive)]
```

## GameServer Internals

### Data Structures

```csharp
VoxelGrid _voxelGrid;           // Conductor/insulator voxels
SimulationManager _simulation;   // MNA solver
TopologyBuilder _topologyBuilder;
Dictionary<GridPos, CellData> _cells;
List<Component> _components;
Dictionary<VoxelPos, ConductorRegion> _regions;
```

### Tick Flow

1. If topology dirty:
   - Rebuild regions via `TopologyBuilder.BuildTopology()`
   - Run `_simulation.Step(dt)`
   - Mark all cells dirty for visual update
2. Generate render commands for dirty cells
3. Compute visual state (voltage, current, power)

### Component Lifecycle

**Placement**:
1. Validate bounds
2. Remove existing cells at target positions
3. Place conductor voxels at terminal positions
4. Create Component instance
5. Mark topology dirty

**Removal**:
1. If non-origin cell, redirect to origin
2. Remove MNA components via `component.RemoveMnaComponents()`
3. Clear voxels
4. Remove all cell entries (origin + body + far terminal)

## StandaloneClient Features

### Rendering
- Cairo drawing on GTK DrawingArea
- Voltage coloring: blue (-5V) → green (0V) → red (+5V)
- Component symbols (+ for positive, - for negative)
- Toolbar with tool selection and rotation

### Editor Features
- **Hover tooltip**: Shows voltage, current, power
- **Ghost preview**: Translucent preview with validity coloring
- **L-shaped wire routing**: Click-drag for efficient wire placement

### Drag State

```csharp
bool _isDragging;
GridPos? _dragStart;
List<GridPos> _dragPath;
bool? _dragHorizontalFirst;  // Direction lock
```

## Visual State

```csharp
record struct CellVisualState(
    float VoltageNormalized,   // For coloring
    float CurrentNormalized,   // For flow animation
    float PowerNormalized      // For heat glow
);
```

Normalization: voltage is divided by reference (5V or 10V) for color mapping.

## Integration with MNA

The 2D game uses:
- `SimulationManager` for circuit solving
- `TopologyBuilder` for conductor region detection
- `VoxelGrid` for conductor/insulator storage

Each Component type has:
- `AddMnaComponents(ISimulation, regions)` - Register with solver
- `RemoveMnaComponents(ISimulation)` - Deregister
- `ComputeVisualState(ISimulation)` - Get current/power for display
