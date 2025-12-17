using System.Collections.Generic;
using ProtoBuf;

namespace Sparky.VSIntegration.Preview;

/// <summary>
/// Network-serializable preview state for a single player.
/// Sent from server to all clients to sync preview visibility.
/// </summary>
[ProtoContract]
public class PreviewState {
    /// <summary>
    /// The player UID whose preview this represents.
    /// </summary>
    [ProtoMember(1)]
    public string PlayerUid { get; set; } = string.Empty;

    /// <summary>
    /// The preview voxels to render. Empty list = clear preview.
    /// </summary>
    [ProtoMember(2)]
    public List<PreviewVoxel> Voxels { get; set; } = new();
}

/// <summary>
/// A single voxel in a preview, with position and color.
/// </summary>
[ProtoContract]
public struct PreviewVoxel {
    /// <summary>
    /// Global voxel X coordinate.
    /// </summary>
    [ProtoMember(1)]
    public int X { get; set; }

    /// <summary>
    /// Global voxel Y coordinate.
    /// </summary>
    [ProtoMember(2)]
    public int Y { get; set; }

    /// <summary>
    /// Global voxel Z coordinate.
    /// </summary>
    [ProtoMember(3)]
    public int Z { get; set; }

    /// <summary>
    /// RGBA color (alpha in high byte for transparency).
    /// </summary>
    [ProtoMember(4)]
    public int Rgba { get; set; }

    public PreviewVoxel(int x, int y, int z, int rgba) {
        X = x;
        Y = y;
        Z = z;
        Rgba = rgba;
    }
}

/// <summary>
/// Message sent from client to server when preview changes.
/// </summary>
[ProtoContract]
public class PreviewUpdateRequest {
    /// <summary>
    /// The voxels to preview. Empty = clear.
    /// </summary>
    [ProtoMember(1)]
    public List<PreviewVoxel> Voxels { get; set; } = new();
}

/// <summary>
/// Message sent from client to server to place or remove voxels.
/// Used for both single voxel operations and cable paths.
/// </summary>
[ProtoContract]
public class VoxelPlacementRequest {
    /// <summary>
    /// The voxels to place or remove.
    /// </summary>
    [ProtoMember(1)]
    public List<VoxelPlacement> Voxels { get; set; } = new();

    /// <summary>
    /// True for voxel removal (left-click), false for placement (right-click).
    /// </summary>
    [ProtoMember(2)]
    public bool IsRemoval { get; set; }
}

/// <summary>
/// A single voxel to place, with position and material.
/// </summary>
[ProtoContract]
public struct VoxelPlacement {
    /// <summary>
    /// Global voxel X coordinate.
    /// </summary>
    [ProtoMember(1)]
    public int X { get; set; }

    /// <summary>
    /// Global voxel Y coordinate.
    /// </summary>
    [ProtoMember(2)]
    public int Y { get; set; }

    /// <summary>
    /// Global voxel Z coordinate.
    /// </summary>
    [ProtoMember(3)]
    public int Z { get; set; }

    /// <summary>
    /// Material index (cast from Material enum). Ignored for removal.
    /// </summary>
    [ProtoMember(4)]
    public int MaterialIndex { get; set; }

    public VoxelPlacement(int x, int y, int z, int materialIndex = 0) {
        X = x;
        Y = y;
        Z = z;
        MaterialIndex = materialIndex;
    }
}
