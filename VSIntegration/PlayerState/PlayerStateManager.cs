using System.Collections.Generic;

namespace Sparky.VSIntegration.PlayerState;

/// <summary>
/// Manages per-player state that needs to be synced from client to server.
/// Uses Dictionary&lt;PlayerStateKey, object&gt; for extensibility.
/// </summary>
public class PlayerStateManager {
    private readonly Dictionary<string, Dictionary<PlayerStateKey, object>> _playerState = new();

    public T? GetState<T>(string playerUid, PlayerStateKey key) where T : struct {
        if (_playerState.TryGetValue(playerUid, out var states) &&
            states.TryGetValue(key, out var value)) {
            return (T)value;
        }
        return null;
    }

    public T GetStateOrDefault<T>(string playerUid, PlayerStateKey key, T defaultValue) where T : struct {
        return GetState<T>(playerUid, key) ?? defaultValue;
    }

    public void SetState<T>(string playerUid, PlayerStateKey key, T value) where T : struct {
        if (!_playerState.TryGetValue(playerUid, out var states)) {
            states = new Dictionary<PlayerStateKey, object>();
            _playerState[playerUid] = states;
        }
        states[key] = value;
    }

    public void ClearPlayer(string playerUid) {
        _playerState.Remove(playerUid);
    }
}
