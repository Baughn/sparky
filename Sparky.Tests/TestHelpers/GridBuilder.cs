using Sparky.Game.Core;
using Sparky.Game.Core.CellTypes;
using Sparky.Game.Simulation;

namespace Sparky.Tests.TestHelpers;

/// <summary>
/// Fluent builder for creating test grids with cells.
/// Defaults to Y=0, BlockFacing.Up for 2D tablet game convenience.
/// </summary>
/// <remarks>
/// Usage patterns:
/// <code>
/// // Simple 2D circuit (horizontal surface at Y=0)
/// var builder = new GridBuilder()
///     .Battery(0, 0, voltage: 5.0)
///     .Wire(1, 0)
///     .Resistor(2, 0, 100)
///     .Ground(3, 0, rotation: 270)
///     .Tick();
///
/// // Get visual state
/// var state = builder.GetVisualState(1, 0);
///
/// // Access underlying objects
/// var voltage = builder.Sim.Electrical.GetVoltage(nodeId);
/// </code>
/// </remarks>
public class GridBuilder
{
    private readonly Grid _grid;
    private readonly GameSimulation _sim;
    private readonly BlockFacing _defaultFace;
    private readonly int _defaultY;
    private readonly Dictionary<(int x, int z), Cell> _cellsByPosition = new();

    /// <summary>
    /// Gets the underlying Grid.
    /// </summary>
    public Grid Grid => _grid;

    /// <summary>
    /// Gets the underlying GameSimulation.
    /// </summary>
    public GameSimulation Sim => _sim;

    /// <summary>
    /// Creates a GridBuilder with default settings (Y=0, Face=Up).
    /// </summary>
    public GridBuilder() : this(BlockFacing.Up, 0)
    {
    }

    /// <summary>
    /// Creates a GridBuilder with custom face and Y position.
    /// </summary>
    /// <param name="face">Default block face for cell placement.</param>
    /// <param name="y">Default Y coordinate for blocks.</param>
    public GridBuilder(BlockFacing face, int y = 0)
    {
        _defaultFace = face;
        _defaultY = y;
        _grid = new Grid();
        _sim = new GameSimulation(_grid);
    }

    /// <summary>
    /// Places a wire at the given 2D position.
    /// </summary>
    public GridBuilder Wire(int x, int z, int rotation = 0)
    {
        var cell = new WireCell();
        PlaceCell(cell, x, z, rotation);
        return this;
    }

    /// <summary>
    /// Places a battery at the given 2D position.
    /// </summary>
    /// <param name="x">X coordinate (block position).</param>
    /// <param name="z">Z coordinate (block position).</param>
    /// <param name="voltage">Voltage in volts (default 5V).</param>
    /// <param name="rotation">Rotation in degrees (0, 90, 180, 270).</param>
    public GridBuilder Battery(int x, int z, double voltage = 5.0, int rotation = 0)
    {
        var cell = new BatteryCell { Voltage = voltage };
        PlaceCell(cell, x, z, rotation);
        return this;
    }

    /// <summary>
    /// Places a resistor at the given 2D position.
    /// </summary>
    /// <param name="x">X coordinate (block position).</param>
    /// <param name="z">Z coordinate (block position).</param>
    /// <param name="resistance">Resistance in ohms (default 100Ω).</param>
    /// <param name="rotation">Rotation in degrees (0, 90, 180, 270).</param>
    public GridBuilder Resistor(int x, int z, double resistance = 100.0, int rotation = 0)
    {
        var cell = new ResistorCell { Resistance = resistance };
        PlaceCell(cell, x, z, rotation);
        return this;
    }

    /// <summary>
    /// Places a ground cell at the given 2D position.
    /// </summary>
    /// <param name="x">X coordinate (block position).</param>
    /// <param name="z">Z coordinate (block position).</param>
    /// <param name="rotation">Rotation in degrees (default 270 = port facing Left).</param>
    public GridBuilder Ground(int x, int z, int rotation = 270)
    {
        var cell = new GroundCell();
        PlaceCell(cell, x, z, rotation);
        return this;
    }

    /// <summary>
    /// Runs a simulation tick.
    /// </summary>
    /// <param name="dt">Time step in seconds (default 1ms).</param>
    public GridBuilder Tick(double dt = 0.001)
    {
        _sim.Tick(dt);
        return this;
    }

    /// <summary>
    /// Runs multiple simulation ticks.
    /// </summary>
    /// <param name="count">Number of ticks.</param>
    /// <param name="dt">Time step in seconds (default 1ms).</param>
    public GridBuilder TickN(int count, double dt = 0.001)
    {
        for (int i = 0; i < count; i++)
            _sim.Tick(dt);
        return this;
    }

    /// <summary>
    /// Gets the visual state for the cell at the given 2D position.
    /// </summary>
    public CellVisualState GetVisualState(int x, int z)
    {
        var states = _sim.GetVisualStates();
        if (_cellsByPosition.TryGetValue((x, z), out var cell))
        {
            if (states.TryGetValue(cell.Id, out var state))
                return state;
        }
        throw new InvalidOperationException($"No cell at position ({x}, {z})");
    }

    /// <summary>
    /// Gets the cell at the given 2D position.
    /// </summary>
    public Cell? GetCell(int x, int z)
    {
        return _cellsByPosition.TryGetValue((x, z), out var cell) ? cell : null;
    }

    /// <summary>
    /// Gets a typed cell at the given 2D position.
    /// </summary>
    public T? GetCell<T>(int x, int z) where T : Cell
    {
        return GetCell(x, z) as T;
    }

    private void PlaceCell(Cell cell, int x, int z, int rotation)
    {
        // For 2D tablet mode, x and z map to SubPos.U and SubPos.V within a single block
        // This allows simple linear circuits to work with adjacency within one block face
        var pos = new CellPos(
            new BlockPos(0, _defaultY, 0),
            _defaultFace,
            new SubPos(x, z));
        _grid.PlaceCell(cell, pos, rotation);
        _cellsByPosition[(x, z)] = cell;
    }
}
