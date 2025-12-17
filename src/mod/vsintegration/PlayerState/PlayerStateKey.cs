namespace Sparky.VSIntegration.PlayerState;

/// <summary>
/// Keys for per-player server-synced state.
/// Add new members as more state types are needed.
/// </summary>
public enum PlayerStateKey {
    WireToolMode = 0,
    // Future: SelectedMaterial, CableLayingPhase, etc.
}
