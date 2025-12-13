using Sparky.Game.Core;
using Sparky.Game.Core.ComponentTypes;
using Sparky.MNA.Api;
using Sparky.TwoD.Protocol;

// Disambiguate types shared with Sparky.Game.Core
using CellType = Sparky.TwoD.Protocol.CellType;
using CellVisualState = Sparky.TwoD.Protocol.CellVisualState;

namespace Sparky.TwoD.Server;

/// <summary>
/// Game server that manages the circuit grid and simulation.
/// </summary>
/// <remarks>
/// The 2D grid maps to the XZ plane at Y=0 in voxel space.
/// Components are placed on the grid and connected via conductor voxels.
/// </remarks>
public class GameServer : IGameServer
{
    private readonly int _width;
    private readonly int _height;

    // Voxel grid for conductor connectivity
    private readonly VoxelGrid _voxelGrid = new();

    // MNA simulation
    private readonly SimulationManager _simulation = new();
    private readonly TopologyBuilder _topologyBuilder = new();

    // Cell data by position
    private readonly Dictionary<GridPos, CellData> _cells = new();

    // Components (managed separately from voxels)
    private readonly List<Component> _components = new();

    // Regions from last topology build
    private Dictionary<VoxelPos, TopologyBuilder.ConductorRegion> _regions = new();

    // Dirty tracking
    private bool _topologyDirty = false;
    private readonly HashSet<GridPos> _dirtyCells = new();

    public int Width => _width;
    public int Height => _height;

    public GameServer(int width = 32, int height = 32)
    {
        _width = width;
        _height = height;
    }

    public void HandleInput(InputEvent input)
    {
        switch (input)
        {
            case PlaceComponent place:
                PlaceCell(place.Pos, place.Type, place.Rotation);
                break;

            case RemoveComponent remove:
                RemoveCell(remove.Pos);
                break;

            case RequestFullState:
                // Handled by GetFullState()
                break;

            case ToggleSwitchInput toggle:
                ToggleSwitch(toggle.Pos);
                break;

            case SetComponentValue setValue:
                SetComponentValue(setValue.Pos, setValue.Value);
                break;
        }
    }

    private void ToggleSwitch(GridPos pos)
    {
        if (!_cells.TryGetValue(pos, out var cell))
            return;

        if (cell.Component is SwitchComponent sw)
        {
            sw.Toggle(_simulation);
            // Mark all cells dirty - switch toggle affects entire circuit's currents
            MarkAllCellsDirty();
        }
    }

    /// <summary>
    /// Marks all cells as dirty for visual update.
    /// Used when simulation state changes affect the entire circuit (e.g., switch toggle).
    /// </summary>
    private void MarkAllCellsDirty()
    {
        foreach (var pos in _cells.Keys)
        {
            _dirtyCells.Add(pos);
        }
    }

    private void SetComponentValue(GridPos pos, double value)
    {
        if (!_cells.TryGetValue(pos, out var cell))
            return;

        // Handle non-origin cells by redirecting to origin
        if (cell.OriginCell.HasValue)
        {
            SetComponentValue(cell.OriginCell.Value, value);
            return;
        }

        switch (cell.Component)
        {
            case BatteryComponent battery:
                battery.Voltage = value;
                battery.UpdateMnaValue(_simulation);
                // Mark all cells dirty - voltage change affects entire circuit
                MarkAllCellsDirty();
                break;

            case ResistorComponent resistor:
                resistor.Resistance = value;
                resistor.UpdateMnaValue(_simulation);
                // Mark all cells dirty - resistance change affects entire circuit
                MarkAllCellsDirty();
                break;
        }
    }

    private void PlaceCell(GridPos pos, CellType type, int rotation)
    {
        if (pos.X < 0 || pos.X >= _width || pos.Y < 0 || pos.Y >= _height)
            return;

        // Remove existing cell first
        if (_cells.ContainsKey(pos))
        {
            RemoveCell(pos);
        }

        if (type == CellType.Empty)
            return;

        // Map 2D grid to 3D voxel space (Y=0 plane)
        var voxelPos = GridToVoxel(pos);

        // Create cell data
        var cell = new CellData(type, rotation);
        _cells[pos] = cell;

        // Place voxels and component based on type
        switch (type)
        {
            case CellType.Wire:
                _voxelGrid.SetVoxel(voxelPos, VoxelType.ResistiveConductor);
                break;

            case CellType.Ground:
                _voxelGrid.SetVoxel(voxelPos, VoxelType.Conductor);
                var ground = new GroundComponent(voxelPos);
                _components.Add(ground);
                cell.Component = ground;
                break;

            case CellType.Switch:
                // Switch occupies 3 grid cells: terminal A - body - terminal B
                var bodyGridPosSw = GetTerminalGridPos(pos, rotation, 1);
                var terminalBGridPosSw = GetTerminalGridPos(pos, rotation, 2);

                // Check bounds and remove any existing cells at all 3 positions
                if (bodyGridPosSw.X < 0 || bodyGridPosSw.X >= _width || bodyGridPosSw.Y < 0 || bodyGridPosSw.Y >= _height ||
                    terminalBGridPosSw.X < 0 || terminalBGridPosSw.X >= _width || terminalBGridPosSw.Y < 0 || terminalBGridPosSw.Y >= _height)
                    return;

                if (_cells.ContainsKey(bodyGridPosSw)) RemoveCell(bodyGridPosSw);
                if (_cells.ContainsKey(terminalBGridPosSw)) RemoveCell(terminalBGridPosSw);

                // Voxel positions
                var terminalAVoxelSw = voxelPos;
                var terminalBVoxelSw = GetTerminalPos(voxelPos, rotation, 2);

                _voxelGrid.SetVoxel(terminalAVoxelSw, VoxelType.Conductor);
                // Body is insulator - don't set it as conductor
                _voxelGrid.SetVoxel(terminalBVoxelSw, VoxelType.Conductor);

                var sw = new SwitchComponent(terminalAVoxelSw, terminalBVoxelSw, false);
                _components.Add(sw);
                cell.Component = sw;

                // Create cell entries for body and terminal B
                _cells[bodyGridPosSw] = new CellData(CellType.SwitchBody, rotation, pos);
                _cells[terminalBGridPosSw] = new CellData(CellType.SwitchTerminalB, rotation, pos);
                _dirtyCells.Add(bodyGridPosSw);
                _dirtyCells.Add(terminalBGridPosSw);
                break;

            case CellType.Battery:
                // Battery occupies 3 grid cells: negative terminal - body - positive terminal
                var bodyGridPosB = GetTerminalGridPos(pos, rotation, 1);
                var positiveGridPos = GetTerminalGridPos(pos, rotation, 2);

                // Check bounds and remove any existing cells at all 3 positions
                if (bodyGridPosB.X < 0 || bodyGridPosB.X >= _width || bodyGridPosB.Y < 0 || bodyGridPosB.Y >= _height ||
                    positiveGridPos.X < 0 || positiveGridPos.X >= _width || positiveGridPos.Y < 0 || positiveGridPos.Y >= _height)
                    return;

                if (_cells.ContainsKey(bodyGridPosB)) RemoveCell(bodyGridPosB);
                if (_cells.ContainsKey(positiveGridPos)) RemoveCell(positiveGridPos);

                // Voxel positions
                var negativeVoxel = voxelPos;
                var positiveVoxel = GetTerminalPos(voxelPos, rotation, 2);

                _voxelGrid.SetVoxel(negativeVoxel, VoxelType.Conductor);
                // Body is insulator (or just air) - don't set it as conductor
                _voxelGrid.SetVoxel(positiveVoxel, VoxelType.Conductor);

                var battery = new BatteryComponent(negativeVoxel, positiveVoxel, 5.0);
                _components.Add(battery);
                cell.Component = battery;

                // Create cell entries for body and positive terminal
                _cells[bodyGridPosB] = new CellData(CellType.BatteryBody, rotation, pos);
                _cells[positiveGridPos] = new CellData(CellType.BatteryPositive, rotation, pos);
                _dirtyCells.Add(bodyGridPosB);
                _dirtyCells.Add(positiveGridPos);
                break;

            case CellType.Resistor:
                // Resistor occupies 3 grid cells: terminal A - body - terminal B
                var bodyGridPosR = GetTerminalGridPos(pos, rotation, 1);
                var terminalBGridPos = GetTerminalGridPos(pos, rotation, 2);

                // Check bounds and remove any existing cells at all 3 positions
                if (bodyGridPosR.X < 0 || bodyGridPosR.X >= _width || bodyGridPosR.Y < 0 || bodyGridPosR.Y >= _height ||
                    terminalBGridPos.X < 0 || terminalBGridPos.X >= _width || terminalBGridPos.Y < 0 || terminalBGridPos.Y >= _height)
                    return;

                if (_cells.ContainsKey(bodyGridPosR)) RemoveCell(bodyGridPosR);
                if (_cells.ContainsKey(terminalBGridPos)) RemoveCell(terminalBGridPos);

                // Voxel positions
                var terminalAVoxel = voxelPos;
                var terminalBVoxel = GetTerminalPos(voxelPos, rotation, 2);

                _voxelGrid.SetVoxel(terminalAVoxel, VoxelType.Conductor);
                // Body is insulator - don't set it as conductor
                _voxelGrid.SetVoxel(terminalBVoxel, VoxelType.Conductor);

                var resistor = new ResistorComponent(terminalAVoxel, terminalBVoxel, 1.0);
                _components.Add(resistor);
                cell.Component = resistor;

                // Create cell entries for body and terminal B
                _cells[bodyGridPosR] = new CellData(CellType.ResistorBody, rotation, pos);
                _cells[terminalBGridPos] = new CellData(CellType.ResistorTerminalB, rotation, pos);
                _dirtyCells.Add(bodyGridPosR);
                _dirtyCells.Add(terminalBGridPos);
                break;
        }

        _topologyDirty = true;
        _dirtyCells.Add(pos);
    }

    private void RemoveCell(GridPos pos)
    {
        if (!_cells.TryGetValue(pos, out var cell))
            return;

        // If this is a non-origin cell, redirect to remove the origin instead
        if (cell.OriginCell.HasValue)
        {
            RemoveCell(cell.OriginCell.Value);
            return;
        }

        var voxelPos = GridToVoxel(pos);

        // Remove component
        if (cell.Component != null)
        {
            cell.Component.RemoveMnaComponents(_simulation);
            _components.Remove(cell.Component);
        }

        // Remove voxels and cells based on type
        switch (cell.Type)
        {
            case CellType.Wire:
            case CellType.Ground:
                _voxelGrid.SetVoxel(voxelPos, VoxelType.Air);
                break;

            case CellType.Battery:
            case CellType.Resistor:
            case CellType.Switch:
                // 3-cell components: clear both terminals and remove all 3 cell entries
                _voxelGrid.SetVoxel(voxelPos, VoxelType.Air);
                var farTerminalVoxel = GetTerminalPos(voxelPos, cell.Rotation, 2);
                _voxelGrid.SetVoxel(farTerminalVoxel, VoxelType.Air);

                // Remove body and far terminal cell entries
                var bodyGridPos = GetTerminalGridPos(pos, cell.Rotation, 1);
                var farTerminalGridPos = GetTerminalGridPos(pos, cell.Rotation, 2);

                if (_cells.ContainsKey(bodyGridPos))
                {
                    _cells.Remove(bodyGridPos);
                    _dirtyCells.Add(bodyGridPos);
                }
                if (_cells.ContainsKey(farTerminalGridPos))
                {
                    _cells.Remove(farTerminalGridPos);
                    _dirtyCells.Add(farTerminalGridPos);
                }
                break;
        }

        _cells.Remove(pos);
        _topologyDirty = true;
        _dirtyCells.Add(pos);
    }

    public IEnumerable<RenderCommand> Tick(float dt)
    {
        // Rebuild topology if dirty
        if (_topologyDirty)
        {
            _regions = _topologyBuilder.BuildTopology(_voxelGrid, _components, _simulation);
            _topologyDirty = false;

            // Mark all cells dirty for visual update
            foreach (var pos in _cells.Keys)
            {
                _dirtyCells.Add(pos);
            }
        }

        // Always step simulation to recalculate voltages/currents
        // (e.g., after switch toggle or component value change)
        _simulation.Step(dt);

        // Generate render commands for dirty cells
        var commands = new List<RenderCommand>();
        foreach (var pos in _dirtyCells)
        {
            if (_cells.TryGetValue(pos, out var cell))
            {
                var state = ComputeVisualState(pos, cell);
                commands.Add(new SetCell(pos, cell.Type, cell.Rotation, state));
            }
            else
            {
                commands.Add(new ClearCell(pos));
            }
        }

        _dirtyCells.Clear();
        return commands;
    }

    public IEnumerable<RenderCommand> GetFullState()
    {
        yield return new SetGridSize(_width, _height);

        foreach (var (pos, cell) in _cells)
        {
            var state = ComputeVisualState(pos, cell);
            yield return new SetCell(pos, cell.Type, cell.Rotation, state);
        }
    }

    private CellVisualState ComputeVisualState(GridPos pos, CellData cell)
    {
        // Body cells are insulators - no voltage display
        if (cell.Type == CellType.BatteryBody || cell.Type == CellType.ResistorBody || cell.Type == CellType.SwitchBody)
        {
            return CellVisualState.Default;
        }

        var voxelPos = GridToVoxel(pos);

        // Get voltage at this cell's position
        float voltage = 0f;
        if (_regions.TryGetValue(voxelPos, out var region))
        {
            voltage = (float)(_simulation.GetVoltage(region.NodeId) / 10.0); // Normalize to 10V
        }

        // Get component - either directly on this cell or via OriginCell for far terminals
        Component? component = cell.Component;
        if (component == null && cell.OriginCell.HasValue)
        {
            // Far terminal cell - look up the origin's component
            if (_cells.TryGetValue(cell.OriginCell.Value, out var originCell))
            {
                component = originCell.Component;
            }
        }

        if (component != null)
        {
            var compState = component.ComputeVisualState(_simulation);

            // Special handling for switch to include closed state
            bool switchClosed = component is SwitchComponent sw && sw.IsClosed;

            return new CellVisualState(
                voltage,
                compState.CurrentNormalized,
                compState.PowerNormalized,
                switchClosed
            );
        }

        // Wire cells - get current from adjacent resistors or nearby components
        if (cell.Type == CellType.Wire)
        {
            float current = 0f;

            // Try to get current from inter-wire resistors first
            if (_regions.TryGetValue(voxelPos, out var wireRegion) && wireRegion.AdjacentResistors.Count > 0)
            {
                // Use max current (all should be ~equal in series, but handles end-of-chain case)
                double maxCurrent = 0;
                foreach (var rid in wireRegion.AdjacentResistors)
                {
                    maxCurrent = Math.Max(maxCurrent, Math.Abs(_simulation.GetResistorCurrent(rid)));
                }
                current = (float)maxCurrent;
            }
            else
            {
                // No inter-wire resistors - wire is merged with a terminal
                // Look at adjacent cells for a component to get current from
                current = GetCurrentFromAdjacentComponent(pos);
            }
            return new CellVisualState(voltage, current, 0, false);
        }

        // Other cells without components - just show voltage
        return new CellVisualState(voltage, 0, 0, false);
    }

    /// <summary>
    /// Gets current from an adjacent component terminal when a wire has no inter-wire resistors.
    /// This handles wires that are merged with terminal regions.
    /// </summary>
    private float GetCurrentFromAdjacentComponent(GridPos wirePos)
    {
        // Check all 4 adjacent cells for components
        var adjacentOffsets = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (dx, dy) in adjacentOffsets)
        {
            var adjPos = new GridPos(wirePos.X + dx, wirePos.Y + dy);
            if (!_cells.TryGetValue(adjPos, out var adjCell))
                continue;

            // Get component - either directly on this cell or via OriginCell for far terminals
            Component? component = adjCell.Component;
            if (component == null && adjCell.OriginCell.HasValue)
            {
                if (_cells.TryGetValue(adjCell.OriginCell.Value, out var originCell))
                {
                    component = originCell.Component;
                }
            }

            if (component != null)
            {
                var compState = component.ComputeVisualState(_simulation);
                if (compState.CurrentNormalized != 0)
                    return compState.CurrentNormalized;
            }
        }
        return 0f;
    }

    /// <summary>
    /// Converts 2D grid position to 3D voxel position (XZ plane at Y=0).
    /// </summary>
    private static VoxelPos GridToVoxel(GridPos pos)
    {
        return new VoxelPos(pos.X, 0, pos.Y);
    }

    /// <summary>
    /// Gets a terminal position at a given distance based on rotation.
    /// Rotation: 0=+X, 1=+Z, 2=-X, 3=-Z
    /// </summary>
    /// <param name="origin">Starting position.</param>
    /// <param name="rotation">Rotation (0-3).</param>
    /// <param name="distance">Distance in cells (default 1).</param>
    private static VoxelPos GetTerminalPos(VoxelPos origin, int rotation, int distance = 1)
    {
        return (rotation % 4) switch
        {
            0 => new VoxelPos(origin.X + distance, origin.Y, origin.Z),
            1 => new VoxelPos(origin.X, origin.Y, origin.Z + distance),
            2 => new VoxelPos(origin.X - distance, origin.Y, origin.Z),
            3 => new VoxelPos(origin.X, origin.Y, origin.Z - distance),
            _ => origin
        };
    }

    /// <summary>
    /// Gets a grid position at a given distance based on rotation.
    /// Rotation: 0=+X, 1=+Y(grid), 2=-X, 3=-Y(grid)
    /// </summary>
    private static GridPos GetTerminalGridPos(GridPos origin, int rotation, int distance = 1)
    {
        return (rotation % 4) switch
        {
            0 => new GridPos(origin.X + distance, origin.Y),
            1 => new GridPos(origin.X, origin.Y + distance),
            2 => new GridPos(origin.X - distance, origin.Y),
            3 => new GridPos(origin.X, origin.Y - distance),
            _ => origin
        };
    }

    /// <summary>
    /// Internal cell data storage.
    /// </summary>
    private class CellData
    {
        public CellType Type { get; }
        public int Rotation { get; }
        public Component? Component { get; set; }

        /// <summary>
        /// For non-origin cells of multi-cell components, points to the origin cell.
        /// Null for origin cells and single-cell components.
        /// </summary>
        public GridPos? OriginCell { get; }

        public CellData(CellType type, int rotation, GridPos? originCell = null)
        {
            Type = type;
            Rotation = rotation;
            OriginCell = originCell;
        }
    }
}
