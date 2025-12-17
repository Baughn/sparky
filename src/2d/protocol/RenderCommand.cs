namespace Sparky.TwoD.Protocol;

/// <summary>
/// Commands sent from server to client for rendering.
/// </summary>
public abstract record RenderCommand;

/// <summary>
/// Sets or updates a cell on the grid.
/// </summary>
public sealed record SetCell(
    GridPos Pos,
    CellType Type,
    int Rotation,
    CellVisualState State
) : RenderCommand;

/// <summary>
/// Removes a cell from the grid (sets to empty).
/// </summary>
public sealed record ClearCell(GridPos Pos) : RenderCommand;

/// <summary>
/// Sets the grid dimensions (sent on connection).
/// </summary>
public sealed record SetGridSize(int Width, int Height) : RenderCommand;

/// <summary>
/// Batch of render commands for efficient updates.
/// </summary>
public sealed record RenderBatch(IReadOnlyList<RenderCommand> Commands) : RenderCommand;
