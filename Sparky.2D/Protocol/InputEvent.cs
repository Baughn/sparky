namespace Sparky.TwoD.Protocol;

/// <summary>
/// Events sent from client to server for input handling.
/// </summary>
public abstract record InputEvent;

/// <summary>
/// Places or updates a component at a grid position.
/// </summary>
public sealed record PlaceComponent(
    GridPos Pos,
    CellType Type,
    int Rotation = 0
) : InputEvent;

/// <summary>
/// Removes the component at a grid position.
/// </summary>
public sealed record RemoveComponent(GridPos Pos) : InputEvent;

/// <summary>
/// Client requests current grid state (sent on connection).
/// </summary>
public sealed record RequestFullState : InputEvent;
