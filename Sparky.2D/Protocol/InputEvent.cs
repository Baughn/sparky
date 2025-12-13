using System.Text.Json.Serialization;

namespace Sparky.TwoD.Protocol;

/// <summary>
/// Events sent from client to server for input handling.
/// </summary>
[JsonDerivedType(typeof(PlaceComponent), "PlaceComponent")]
[JsonDerivedType(typeof(RemoveComponent), "RemoveComponent")]
[JsonDerivedType(typeof(RequestFullState), "RequestFullState")]
[JsonDerivedType(typeof(SetComponentValue), "SetComponentValue")]
[JsonDerivedType(typeof(ToggleSwitchInput), "ToggleSwitchInput")]
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

/// <summary>
/// Sets a component's value (voltage for battery, resistance for resistor).
/// </summary>
public sealed record SetComponentValue(GridPos Pos, double Value) : InputEvent;

/// <summary>
/// Toggles a switch's open/closed state.
/// </summary>
public sealed record ToggleSwitchInput(GridPos Pos) : InputEvent;
