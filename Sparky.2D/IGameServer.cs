using Sparky.TwoD.Protocol;

namespace Sparky.TwoD;

/// <summary>
/// Server interface for the circuit simulation.
/// </summary>
/// <remarks>
/// The server owns the grid state, components, and simulation.
/// It processes input events from clients and emits render commands.
/// </remarks>
public interface IGameServer
{
    /// <summary>
    /// Grid width in cells.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Grid height in cells.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Processes an input event from a client.
    /// </summary>
    void HandleInput(InputEvent input);

    /// <summary>
    /// Advances the simulation by dt seconds.
    /// </summary>
    /// <param name="dt">Time delta in seconds.</param>
    /// <returns>Render commands for any changed cells.</returns>
    IEnumerable<RenderCommand> Tick(float dt);

    /// <summary>
    /// Gets the full grid state as render commands.
    /// </summary>
    IEnumerable<RenderCommand> GetFullState();
}
