using System.Collections.Generic;

namespace Sparky.MNA.Utilities;

/// <summary>
/// Helper to manage multiple time-varying sources and update them together.
/// </summary>
public class SourceUpdater {
    private readonly List<TimeVaryingSource> _sources = new();

    /// <summary>
    /// Registers a source for batch updates.
    /// </summary>
    public void Add(TimeVaryingSource source) {
        _sources.Add(source);
    }

    /// <summary>
    /// Unregisters a source from batch updates.
    /// Does not remove the source from the simulation.
    /// </summary>
    public void Remove(TimeVaryingSource source) {
        _sources.Remove(source);
    }

    /// <summary>
    /// Updates all registered sources. Call before sim.Step().
    /// </summary>
    public void UpdateAll() {
        foreach (var source in _sources) {
            if (source.Exists) {
                source.Update();
            }
        }
    }

    /// <summary>
    /// Gets the number of registered sources.
    /// </summary>
    public int Count => _sources.Count;

    /// <summary>
    /// Removes all registered sources from the simulation and clears the list.
    /// </summary>
    public void RemoveAll() {
        foreach (var source in _sources) {
            if (source.Exists) {
                source.Remove();
            }
        }
        _sources.Clear();
    }

    /// <summary>
    /// Clears the list without removing sources from the simulation.
    /// </summary>
    public void Clear() {
        _sources.Clear();
    }
}
