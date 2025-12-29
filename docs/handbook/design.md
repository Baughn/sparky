# Handbook: 2D Circuit Editor

A standalone 2D circuit editor built with GTK/Cairo that serves as both a testing tool and future in-game handbook for the Sparky mod. It uses the same voxel simulation backend as the Vintage Story integration, mapped to a 2D grid.

## Key Files

```
src/handbook/
├── Program.cs                      # Main loop, client-server initialization
├── IGameClient.cs                  # Client interface (render commands, input polling)
├── IGameServer.cs                  # Server interface (input handling, simulation tick)
├── client/standalone/
│   └── StandaloneClient.cs         # GTK+Cairo rendering implementation
├── protocol/
│   ├── CellType.cs                 # Cell type enum and CellVisualState
│   ├── ComponentTemplates.cs       # Multi-cell component layouts
│   ├── GridPos.cs                  # 2D grid coordinates
│   ├── InputEvent.cs               # Client-to-server events
│   └── RenderCommand.cs            # Server-to-client rendering commands
└── server/
    └── GameServer.cs               # Grid state, voxel simulation integration
```

## Architecture

The handbook uses a client-server architecture designed to support both standalone operation and future Vintage Story integration over a network.

### Client-Server Protocol

**Server to Client (RenderCommand):**
- `SetGridSize(width, height)` - Initialize grid dimensions
- `SetCell(pos, type, rotation, state)` - Place or update a cell with visual state
- `ClearCell(pos)` - Remove a cell
- `RenderBatch(commands)` - Batch multiple commands

**Client to Server (InputEvent):**
- `PlaceComponent(pos, type, rotation)` - Place a component
- `RemoveComponent(pos)` - Remove a component
- `SetComponentValue(pos, value)` - Set voltage or resistance
- `ToggleSwitchInput(pos)` - Toggle a switch
- `RequestFullState` - Request complete grid state

### Grid to Voxel Mapping

The 2D grid maps to the XZ plane at Y=0 in voxel space. Each grid cell corresponds to a single voxel, and the underlying `VoxelSimulation` handles topology building and MNA solving.

## Cell Types

| Type | Description | Cells | Has Component |
|------|-------------|-------|---------------|
| `Wire` | Conductor connecting adjacent cells | 1 | No |
| `Ground` | Reference node (0V) | 1 | Yes |
| `Battery` | Voltage source (default 5V) | 3 | Yes |
| `Resistor` | Resistance (default 1 ohm) | 3 | Yes |
| `Switch` | Toggleable conductor | 3 | Yes |

### Multi-Cell Components

Battery, Resistor, and Switch use a 3-cell layout:
```
[Origin Terminal] -- [Body] -- [Far Terminal]
```

- **Origin cell**: Placement point, holds the component reference
- **Body cell**: Insulator preventing shorts between terminals
- **Far terminal**: Second electrical connection point

Layout is defined in `ComponentTemplates.GetCells()` and varies by rotation (0-3, representing 0/90/180/270 degrees).

## Visual State

Each cell receives a `CellVisualState` from the server containing:

| Field | Range | Description |
|-------|-------|-------------|
| `VoltageNormalized` | [-1, 1] | Voltage / 10V, for color mapping |
| `CurrentNormalized` | [0, 1] | Current / 1A |
| `PowerNormalized` | [0, 1] | Power / 10W |
| `SwitchClosed` | bool | For switch cells only |

### Voltage Coloring

Cells are colored based on voltage:
- Blue: -5V (VoltageNormalized = -0.5)
- Green: 0V (VoltageNormalized = 0)
- Red: +5V (VoltageNormalized = +0.5)

Body cells display dark gray since they have no electrical data.

### Heat Visualization

Resistor terminals show heat-based coloring where `PowerNormalized` shifts the color from brown toward red.

## User Interaction

### Tool Selection

| Key | Tool |
|-----|------|
| 1 | Wire |
| 2 | Battery |
| 3 | Resistor |
| 4 | Switch |
| 5 | Ground |
| 6 | Eraser |
| 7 | Debug |
| R | Rotate (cycle 0-3) |
| Q/Esc | Quit |

### Mouse Controls

**Left click:**
- Place selected component at cursor position
- Wire tool: starts L-shaped drag routing
- Debug mode: outputs cell state to stdout as JSON

**Right click:**
- On Battery/Resistor: opens edit dialog for value
- On Switch: toggles open/closed state

### Wire Drag Routing

Clicking and dragging with the Wire tool creates an L-shaped path:
1. Initial drag direction (horizontal or vertical) is detected from first movement
2. Path extends in primary direction first, then secondary
3. Ghost preview shows valid (green) or invalid (red) placements
4. On release, all valid wire cells are placed

### Ghost Preview

Before placement, a translucent preview shows where cells will be placed:
- Green tint: valid (cells empty and within bounds)
- Red tint: invalid (occupied or out of bounds)

Multi-cell components preview all three cells with individual validity coloring.

### Hover Tooltip

Hovering over any cell displays a tooltip showing:
- Cell position and type
- Voltage (actual value and normalized)
- Current (A)
- Power (W)

### Edit Dialogs

Right-clicking Battery or Resistor opens a GTK dialog:
- Battery: linear slider for voltage (0.1V - 100V)
- Resistor: logarithmic slider for resistance (0.001 - 1e9 ohms)

## Simulation Integration

The `GameServer` maintains:
- A `VoxelSimulation` instance containing the voxel grid and MNA solver
- Cell data mapping grid positions to types, rotations, and components
- Dirty tracking for incremental rendering updates

### Tick Cycle

Each frame:
1. Process input events (place, remove, toggle, set value)
2. If topology changed, rebuild via `VoxelSimulation.RebuildTopology()`
3. Step simulation with `VoxelSimulation.Step(dt)`
4. Generate `SetCell` commands for dirty cells
5. Client renders updated cells

### Component Value Updates

Battery voltage and resistor resistance can be changed at runtime:
- Updates flow to the underlying MNA simulation
- All cells are marked dirty for visual refresh

## Debug Mode

Selecting debug mode (key 7) changes left-click to output cell state as JSON:

```json
{
  "debug": "cell",
  "pos": {"x": 5, "y": 3},
  "type": "Wire",
  "rotation": 0,
  "state": {
    "voltage": 0.5,
    "current": 0.1,
    "power": 0.05
  }
}
```

Input events are also logged to stdout in JSON format during normal operation.

## Standalone Client

The `StandaloneClient` renders using GTK3 with Cairo:
- Window with 800x600 default size
- Grid rendered as a 20x20 pixel cell grid
- Custom macOS handling for window activation (GTK's Present() is unreliable)
- 60 FPS frame rate cap via 16ms sleep

Cairo was chosen because Vintage Story uses Cairo for 2D GUI elements, ensuring the rendering code can be reused.
