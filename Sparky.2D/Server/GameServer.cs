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
                _voxelGrid.SetVoxel(voxelPos, VoxelType.Conductor);
                break;

            case CellType.Ground:
                _voxelGrid.SetVoxel(voxelPos, VoxelType.Conductor);
                var ground = new GroundComponent(voxelPos);
                _components.Add(ground);
                cell.Component = ground;
                break;

            case CellType.Battery:
                // Battery occupies 2 cells - negative at pos, positive at pos+rotation
                var negativePos = voxelPos;
                var positivePos = GetTerminalPos(voxelPos, rotation);

                _voxelGrid.SetVoxel(negativePos, VoxelType.Conductor);
                _voxelGrid.SetVoxel(positivePos, VoxelType.Conductor);

                var battery = new BatteryComponent(negativePos, positivePos, 5.0);
                _components.Add(battery);
                cell.Component = battery;
                break;

            case CellType.Resistor:
                // Resistor occupies 2 cells - terminal A at pos, terminal B at pos+rotation
                var terminalA = voxelPos;
                var terminalB = GetTerminalPos(voxelPos, rotation);

                _voxelGrid.SetVoxel(terminalA, VoxelType.Conductor);
                _voxelGrid.SetVoxel(terminalB, VoxelType.Conductor);

                var resistor = new ResistorComponent(terminalA, terminalB, 100.0);
                _components.Add(resistor);
                cell.Component = resistor;
                break;
        }

        _topologyDirty = true;
        _dirtyCells.Add(pos);
    }

    private void RemoveCell(GridPos pos)
    {
        if (!_cells.TryGetValue(pos, out var cell))
            return;

        var voxelPos = GridToVoxel(pos);

        // Remove component
        if (cell.Component != null)
        {
            cell.Component.RemoveMnaComponents(_simulation);
            _components.Remove(cell.Component);
        }

        // Remove voxels based on type
        switch (cell.Type)
        {
            case CellType.Wire:
            case CellType.Ground:
                _voxelGrid.SetVoxel(voxelPos, VoxelType.Air);
                break;

            case CellType.Battery:
            case CellType.Resistor:
                _voxelGrid.SetVoxel(voxelPos, VoxelType.Air);
                var secondPos = GetTerminalPos(voxelPos, cell.Rotation);
                _voxelGrid.SetVoxel(secondPos, VoxelType.Air);
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
            _simulation.Step(dt);
            _topologyDirty = false;

            // Mark all cells dirty for visual update
            foreach (var pos in _cells.Keys)
            {
                _dirtyCells.Add(pos);
            }
        }

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
        var voxelPos = GridToVoxel(pos);

        // Get voltage at this cell's position
        float voltage = 0f;
        if (_regions.TryGetValue(voxelPos, out var region))
        {
            voltage = (float)(_simulation.GetVoltage(region.NodeId) / 10.0); // Normalize to 10V
        }

        // Get component state if this cell has one
        if (cell.Component != null)
        {
            var compState = cell.Component.ComputeVisualState(_simulation);
            return new CellVisualState(
                voltage,
                compState.CurrentNormalized,
                compState.PowerNormalized
            );
        }

        return new CellVisualState(voltage, 0, 0);
    }

    /// <summary>
    /// Converts 2D grid position to 3D voxel position (XZ plane at Y=0).
    /// </summary>
    private static VoxelPos GridToVoxel(GridPos pos)
    {
        return new VoxelPos(pos.X, 0, pos.Y);
    }

    /// <summary>
    /// Gets the second terminal position based on rotation.
    /// Rotation: 0=+X, 1=+Z, 2=-X, 3=-Z
    /// </summary>
    private static VoxelPos GetTerminalPos(VoxelPos origin, int rotation)
    {
        return (rotation % 4) switch
        {
            0 => new VoxelPos(origin.X + 1, origin.Y, origin.Z),
            1 => new VoxelPos(origin.X, origin.Y, origin.Z + 1),
            2 => new VoxelPos(origin.X - 1, origin.Y, origin.Z),
            3 => new VoxelPos(origin.X, origin.Y, origin.Z - 1),
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

        public CellData(CellType type, int rotation)
        {
            Type = type;
            Rotation = rotation;
        }
    }
}
