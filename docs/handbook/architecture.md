# Handbook Architecture

The Handbook is a standalone 2D circuit editor for testing and visualization. It uses a client-server architecture where both components run in-process, communicating via a message protocol. The server owns all simulation state (voxel grid, MNA solver, topology) while the client handles rendering and user input via GTK+Cairo.

## Key Files

```
src/handbook/
├── Program.cs                           # Main entry point and game loop
├── IGameServer.cs                       # Server interface
├── IGameClient.cs                       # Client interface
├── server/
│   └── GameServer.cs                    # Simulation state and input handling
├── client/
│   └── standalone/
│       └── StandaloneClient.cs          # GTK+Cairo renderer
└── protocol/
    ├── GridPos.cs                       # 2D grid coordinate
    ├── CellType.cs                      # Cell types and visual state
    ├── InputEvent.cs                    # Client-to-server messages
    ├── RenderCommand.cs                 # Server-to-client messages
    └── ComponentTemplates.cs            # Multi-cell component layouts
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    StandaloneClient                         │
│  - GTK window with Cairo drawing                            │
│  - Keyboard/mouse input handling                            │
│  - Cell rendering with voltage coloring                     │
│  - Ghost preview and hover tooltips                         │
├────────────────────────────────┬────────────────────────────┤
│  InputEvent (place/remove)     │   RenderCommand (set/clear)│
│           ↓                    │           ↑                │
├────────────────────────────────┴────────────────────────────┤
│                        GameServer                           │
│  - Grid state (Dictionary<GridPos, CellData>)               │
│  - VoxelSimulation (voxel grid + MNA solver + topology)     │
│  - Dirty tracking for incremental updates                   │
└─────────────────────────────────────────────────────────────┘
```

## Main Loop

The `Program.cs` entry point runs a simple game loop at approximately 60 FPS:

1. Poll client for input events
2. Forward each input event to server
3. Call `server.Tick(dt)` to advance simulation
4. Send resulting render commands to client
5. Render the frame
6. Sleep for frame rate limiting

All input events are logged to stdout as JSON for debugging.

## Client-Server Protocol

### Input Events (Client to Server)

| Event | Purpose |
|-------|---------|
| `PlaceComponent(pos, type, rotation)` | Place a component at a grid position |
| `RemoveComponent(pos)` | Remove the component at a position |
| `RequestFullState` | Request complete grid state (on connection) |
| `SetComponentValue(pos, value)` | Change battery voltage or resistor value |
| `ToggleSwitchInput(pos)` | Toggle a switch open/closed |

### Render Commands (Server to Client)

| Command | Purpose |
|---------|---------|
| `SetCell(pos, type, rotation, state)` | Create or update a cell |
| `ClearCell(pos)` | Remove a cell (set to empty) |
| `SetGridSize(width, height)` | Set grid dimensions |
| `RenderBatch(commands)` | Batch of render commands |

## Coordinate Systems

| System | Type | Usage |
|--------|------|-------|
| `GridPos(X, Y)` | 2D grid | UI placement, cell indexing |
| `VoxelPos(X, Y, Z)` | 3D voxel | Conductor connectivity, MNA topology |

**Mapping**: `GridPos(x, y)` maps to `VoxelPos(x, 0, y)` (XZ plane at Y=0).

## Cell Types

Single-cell components:
- `Wire` - Conductor connecting adjacent cells
- `Ground` - 0V reference point

Three-cell components (terminal-body-terminal pattern):
- `Battery` / `BatteryBody` / `BatteryPositive` - Voltage source (default 5V)
- `Resistor` / `ResistorBody` / `ResistorTerminalB` - Resistance (default 1 ohm)
- `Switch` / `SwitchBody` / `SwitchTerminalB` - Toggleable connection

The body cell is an insulator that prevents shorts between terminals. Clicking any cell of a multi-cell component affects the whole component.

## Server Internals

The `GameServer` owns:
- `VoxelSimulation` - Unified facade combining `VoxelGrid`, `SimulationManager` (MNA), and `TopologyBuilder`
- `Dictionary<GridPos, CellData>` - Cell state by position
- Dirty tracking sets for topology and visual updates

### Tick Flow

1. If topology dirty: rebuild via `VoxelSimulation.RebuildTopology()`, mark all cells dirty
2. Step simulation with `VoxelSimulation.Step(dt)`
3. Generate `SetCell`/`ClearCell` commands for dirty cells
4. Clear dirty sets

### Visual State Computation

Each cell's visual state includes:
- `VoltageNormalized` - Voltage at position (normalized to 10V scale)
- `CurrentNormalized` - Current flow (for animation)
- `PowerNormalized` - Power dissipation (for heat glow)
- `SwitchClosed` - Switch state (for switches only)

For wires, current is obtained from adjacent inter-region resistors or from nearby component terminals.

## Client Features

### Rendering

- Cairo drawing on GTK `DrawingArea`
- Voltage coloring: blue (-5V) to green (0V) to red (+5V)
- Component symbols: +/- for battery, box for resistor, line/angle for switch
- Heat glow for resistor power dissipation
- Grid lines with dark background

### Editor Tools

Tools selected via keyboard (1-7):
1. Wire - L-shaped drag routing
2. Battery - 3-cell voltage source
3. Resistor - 3-cell resistance
4. Switch - 3-cell toggleable connection
5. Ground - Single-cell 0V reference
6. Eraser - Remove components
7. Debug - Click to print cell state to stdout

### Input Handling

- Left click: Place component or erase (depending on tool)
- Right click: Edit component value (battery voltage, resistor ohms) or toggle switch
- R key: Rotate placement direction (0/90/180/270 degrees)
- Wire drag: Click-drag creates L-shaped wire paths with direction locking

### Ghost Preview

When hovering, translucent preview shows:
- Green tint for valid placement
- Red tint for invalid (out of bounds or occupied)
- Component shape via `ComponentTemplates.GetCells()`

### Hover Tooltip

Shows cell details when hovering over existing cells:
- Position, type, rotation
- Voltage, current, power (actual values, not normalized)

## Integration with MNA

The handbook uses the voxel layer's `VoxelSimulation` facade, which internally manages:
- `VoxelGrid` for conductor/insulator storage
- `TopologyBuilder` for extracting conductor regions via union-find
- `SimulationManager` (MNA solver) for circuit analysis

Component types (`BatteryComponent`, `ResistorComponent`, `SwitchComponent`, `GroundComponent`) from `Sparky.Voxel.MnaTopology.ComponentTypes` handle their own MNA registration and visual state computation.
