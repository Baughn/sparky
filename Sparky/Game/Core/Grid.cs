using System;
using System.Collections.Generic;
using System.Linq;
using Sparky.MNA.Api;

namespace Sparky.Game.Core;

/// <summary>
/// Edge between two adjacent cell positions on the same face plane.
/// Used as key for node sharing between adjacent cells.
/// </summary>
/// <remarks>
/// Two cells share an edge when:
/// 1. They are on the same plane (same block face or adjacent block faces that are coplanar)
/// 2. They are adjacent within that plane
/// 3. Both have ports facing the shared edge
/// </remarks>
/// <summary>
/// Edge between two adjacent cell positions on the same face plane.
/// Used as key for node sharing between adjacent cells.
/// </summary>
/// <remarks>
/// Made public for testing. In production, only used internally by Grid.
/// </remarks>
public readonly record struct CellEdge
{
    /// <summary>First cell position (normalized to be "smaller" by hash code).</summary>
    public CellPos PosA { get; }

    /// <summary>Direction from PosA toward PosB.</summary>
    public FaceDirection Direction { get; }

    private CellEdge(CellPos posA, FaceDirection direction)
    {
        PosA = posA;
        Direction = direction;
    }

    /// <summary>
    /// Creates an edge between two adjacent positions.
    /// Normalizes the order so (A→B) and (B→A) produce the same edge.
    /// </summary>
    public static CellEdge Create(CellPos a, FaceDirection dirFromA)
    {
        // The edge is identified by the "lower" position and direction toward "higher"
        // For simplicity, we store the position with lower hash code
        var b = GetNeighbor(a, dirFromA);
        if (a.GetHashCode() <= b.GetHashCode())
            return new CellEdge(a, dirFromA);
        else
            return new CellEdge(b, dirFromA.Opposite());
    }

    /// <summary>
    /// Gets the neighbor position in the given direction.
    /// Public for testing.
    /// </summary>
    public static CellPos GetNeighbor(CellPos pos, FaceDirection dir)
    {
        // Move within the sub-grid
        var newSub = pos.Sub.Neighbor(dir);
        if (newSub.IsValid)
            return new CellPos(pos.Block, pos.Face, newSub);

        // TODO: Cross block boundary — for now, clamp
        return new CellPos(pos.Block, pos.Face, newSub.Clamp());
    }
}

/// <summary>
/// 2D/3D grid that manages cells and their electrical topology.
/// Uses sparse storage (Dictionary) to avoid allocating arrays for empty space.
/// </summary>
public class Grid
{
    private readonly Dictionary<CellPos, Cell> _cells = new();
    private readonly Dictionary<CellEdge, NodeId> _edgeNodes = new();
    private readonly HashSet<NodeId> _allocatedNodes = new();

    private int _nextCellId = 1;
    private bool _isDirty;
    private ISimulation? _simulation;

    /// <summary>
    /// Event raised when grid topology changes (cells added/removed).
    /// </summary>
    public event Action? TopologyChanged;

    /// <summary>
    /// Gets the number of cells in the grid.
    /// </summary>
    public int CellCount => _cells.Count;

    /// <summary>
    /// Returns true if topology needs rebuilding.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Binds this grid to an electrical simulation.
    /// </summary>
    public void BindSimulation(ISimulation simulation)
    {
        _simulation = simulation;
        _isDirty = true;
    }

    /// <summary>
    /// Gets the bound simulation, or throws if not bound.
    /// </summary>
    public ISimulation Simulation =>
        _simulation ?? throw new InvalidOperationException("Grid is not bound to a simulation.");

    /// <summary>
    /// Places a cell at the given position.
    /// </summary>
    /// <exception cref="ArgumentException">Position already occupied.</exception>
    public CellId PlaceCell(Cell cell, CellPos position, int rotation = 0)
    {
        if (!position.IsValid)
            throw new ArgumentException(
                $"Position {position} has invalid sub-position.",
                nameof(position)
            );

        if (_cells.ContainsKey(position))
            throw new ArgumentException(
                $"Position {position} is already occupied.",
                nameof(position)
            );

        cell.Id = new CellId(_nextCellId++);
        cell.Position = position;
        cell.Rotation = rotation;

        _cells[position] = cell;
        _isDirty = true;

        return cell.Id;
    }

    /// <summary>
    /// Removes the cell at the given position.
    /// </summary>
    /// <returns>True if cell was removed, false if position was empty.</returns>
    public bool RemoveCell(CellPos position)
    {
        if (!_cells.TryGetValue(position, out var cell))
            return false;

        // Remove electrical components if bound
        if (_simulation != null && cell.AsElectrical() is { } elec)
        {
            elec.RemoveComponents(_simulation);
        }

        _cells.Remove(position);
        _isDirty = true;
        return true;
    }

    /// <summary>
    /// Gets the cell at a position, or null if empty.
    /// </summary>
    public Cell? GetCell(CellPos position)
    {
        _cells.TryGetValue(position, out var cell);
        return cell;
    }

    /// <summary>
    /// Returns true if a cell exists at the position.
    /// </summary>
    public bool HasCell(CellPos position) => _cells.ContainsKey(position);

    /// <summary>
    /// Gets all cells in the grid.
    /// </summary>
    public IEnumerable<Cell> GetAllCells() => _cells.Values;

    /// <summary>
    /// Gets the cell with the given ID, or null if not found.
    /// </summary>
    public Cell? GetCellById(CellId id) => _cells.Values.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// Marks the grid as needing topology rebuild.
    /// </summary>
    public void MarkDirty() => _isDirty = true;

    /// <summary>
    /// Rebuilds the electrical topology.
    /// Called automatically during Tick() if dirty, or manually.
    /// </summary>
    public void RebuildTopology()
    {
        if (_simulation == null)
            return;

        using var _ = _simulation.BeginBulkUpdate();

        // Step 1: Remove all existing components
        foreach (var cell in _cells.Values)
        {
            cell.AsElectrical()?.RemoveComponents(_simulation);
        }

        // Step 2: Clear edge node map, reclaim nodes
        foreach (var nodeId in _allocatedNodes)
        {
            if (nodeId.Value != 0) // Don't try to remove ground
                _simulation.RemoveNode(nodeId);
        }
        _edgeNodes.Clear();
        _allocatedNodes.Clear();

        // Step 3a: First pass - identify all ground edges
        // This ensures ground nodes are established before other cells try to use them
        foreach (var (pos, cell) in _cells)
        {
            if (cell.Type != CellType.Ground)
                continue;
            if (cell.AsElectrical() is not { } elec)
                continue;

            foreach (var localDir in elec.GetLocalPortDirections())
            {
                var worldDir = cell.LocalToWorld(localDir);
                var edge = CellEdge.Create(pos, worldDir);
                _edgeNodes[edge] = _simulation.Ground;
            }
        }

        // Step 3b: Process Wire cells first (they merge all edges into one node)
        foreach (var (pos, cell) in _cells)
        {
            if (cell.Type != CellType.Wire)
                continue;
            if (cell.AsElectrical() is not { } elec)
                continue;

            // Collect all edges for this wire
            var localDirs = elec.GetLocalPortDirections();
            var edges = new List<CellEdge>();
            NodeId? sharedNode = null;

            // First pass: find any existing node among all edges
            foreach (var localDir in localDirs)
            {
                var worldDir = cell.LocalToWorld(localDir);
                var edge = CellEdge.Create(pos, worldDir);
                edges.Add(edge);

                if (_edgeNodes.TryGetValue(edge, out var existingNode))
                {
                    // Prefer ground over other nodes (in case of conflict)
                    if (existingNode.Value == 0 || !sharedNode.HasValue)
                    {
                        sharedNode = existingNode;
                    }
                }
            }

            // If no existing node found, create one
            if (!sharedNode.HasValue)
            {
                sharedNode = _simulation.CreateNode();
                _allocatedNodes.Add(sharedNode.Value);
            }

            // Register this node for ALL edges of the wire
            foreach (var edge in edges)
            {
                if (!_edgeNodes.ContainsKey(edge))
                {
                    _edgeNodes[edge] = sharedNode.Value;
                }
            }

            // Build ports dictionary and create components
            var ports = new Dictionary<FaceDirection, NodeId>();
            foreach (var localDir in localDirs)
            {
                var worldDir = cell.LocalToWorld(localDir);
                ports[worldDir] = sharedNode.Value;
            }
            elec.CreateComponents(_simulation, ports);
        }

        // Step 3c: Process non-Wire cells
        foreach (var (pos, cell) in _cells)
        {
            if (cell.Type == CellType.Wire)
                continue; // Already processed
            if (cell.AsElectrical() is not { } elec)
                continue;

            var ports = new Dictionary<FaceDirection, NodeId>();
            var localDirs = elec.GetLocalPortDirections();

            foreach (var localDir in localDirs)
            {
                var worldDir = cell.LocalToWorld(localDir);
                var edge = CellEdge.Create(pos, worldDir);

                // Ground edges were already registered in step 3a, Wire edges in 3b
                if (_edgeNodes.TryGetValue(edge, out var existingNode))
                {
                    ports[worldDir] = existingNode;
                }
                else
                {
                    // Check if neighbor at this edge is a ground cell (redundant but safe)
                    var neighborPos = GetNeighborPos(pos, worldDir);
                    if (
                        neighborPos.HasValue
                        && _cells.TryGetValue(neighborPos.Value, out var neighbor)
                        && neighbor.Type == CellType.Ground
                    )
                    {
                        ports[worldDir] = _simulation.Ground;
                        _edgeNodes[edge] = _simulation.Ground;
                    }
                    else
                    {
                        var newNode = _simulation.CreateNode();
                        _allocatedNodes.Add(newNode);
                        _edgeNodes[edge] = newNode;
                        ports[worldDir] = newNode;
                    }
                }
            }

            // Step 4: Create components with the resolved ports
            elec.CreateComponents(_simulation, ports);
        }

        _isDirty = false;
        TopologyChanged?.Invoke();
    }

    /// <summary>
    /// Computes visual states for all cells.
    /// Call after simulation step.
    /// </summary>
    public Dictionary<CellId, CellVisualState> ComputeVisualStates()
    {
        var states = new Dictionary<CellId, CellVisualState>();

        if (_simulation == null)
            return states;

        foreach (var cell in _cells.Values)
        {
            var visualState =
                cell.AsElectrical()?.ComputeVisualState(_simulation) ?? CellVisualState.Default;
            states[cell.Id] = visualState;
        }

        return states;
    }

    /// <summary>
    /// Gets the neighboring cell position in the given direction.
    /// Returns null if the position would be invalid.
    /// </summary>
    private static CellPos? GetNeighborPos(CellPos pos, FaceDirection dir)
    {
        var newSub = pos.Sub.Neighbor(dir);
        if (newSub.IsValid)
            return new CellPos(pos.Block, pos.Face, newSub);

        // TODO: Cross-block neighbor calculation
        return null;
    }
}
