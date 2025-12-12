using Sparky.TwoD.Protocol;

namespace Sparky.TwoD;

/// <summary>
/// Client interface for rendering and input handling.
/// </summary>
/// <remarks>
/// The client receives render commands from the server and
/// sends input events based on user interaction.
/// </remarks>
public interface IGameClient
{
    /// <summary>
    /// Handles a render command from the server.
    /// </summary>
    void HandleCommand(RenderCommand command);

    /// <summary>
    /// Handles multiple render commands.
    /// </summary>
    void HandleCommands(IEnumerable<RenderCommand> commands)
    {
        foreach (var cmd in commands)
            HandleCommand(cmd);
    }

    /// <summary>
    /// Polls for pending input events.
    /// </summary>
    IEnumerable<InputEvent> PollInput();

    /// <summary>
    /// Renders the current state.
    /// </summary>
    void Render();

    /// <summary>
    /// Returns true if the client wants to close.
    /// </summary>
    bool ShouldClose { get; }
}
