using System;

namespace Sparky.Game.Core.CableLaying;

/// <summary>
/// Voxel state in the WorldVoxelCache used for cable pathfinding.
/// </summary>
/// <remarks>
/// This is a wrapper struct around the enum to satisfy SparseVoxelOctree's IEquatable constraint.
/// </remarks>
public readonly struct CacheVoxelState : IEquatable<CacheVoxelState>
{
    /// <summary>The underlying state value.</summary>
    public CacheVoxelStateValue Value { get; }

    /// <summary>Creates a new cache voxel state.</summary>
    public CacheVoxelState(CacheVoxelStateValue value) => Value = value;

    // Pre-defined constants
    public static readonly CacheVoxelState Empty = new(CacheVoxelStateValue.Empty);
    public static readonly CacheVoxelState Insulation = new(CacheVoxelStateValue.Insulation);
    public static readonly CacheVoxelState PreExistingConductor = new(CacheVoxelStateValue.PreExistingConductor);
    public static readonly CacheVoxelState CableConductor = new(CacheVoxelStateValue.CableConductor);
    public static readonly CacheVoxelState Unroutable = new(CacheVoxelStateValue.Unroutable);

    public bool Equals(CacheVoxelState other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is CacheVoxelState other && Equals(other);
    public override int GetHashCode() => (int)Value;
    public static bool operator ==(CacheVoxelState left, CacheVoxelState right) => left.Equals(right);
    public static bool operator !=(CacheVoxelState left, CacheVoxelState right) => !left.Equals(right);
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Voxel state values for the WorldVoxelCache.
/// </summary>
public enum CacheVoxelStateValue : byte
{
    /// <summary>
    /// Air within a Circuit block or an air block.
    /// Cable can occupy this space.
    /// </summary>
    Empty = 0,

    /// <summary>
    /// Solid non-conductor surface.
    /// Cable cannot occupy but can be adjacent for support.
    /// </summary>
    Insulation = 1,

    /// <summary>
    /// Existing circuit conductor.
    /// Cable must NOT be adjacent (would cause electrical short).
    /// </summary>
    PreExistingConductor = 2,

    /// <summary>
    /// Temporary marker for path being placed during A* search.
    /// Used to track the cable path as it's constructed.
    /// </summary>
    CableConductor = 3,

    /// <summary>
    /// Non-air space inside a non-Circuit block (e.g., air gaps in stairs/fences).
    /// Cable cannot occupy AND cannot use for adjacency support.
    /// This prevents cables from "floating" through architectural gaps.
    /// </summary>
    Unroutable = 4
}
