namespace Sparky.Game.Core;

/// <summary>
/// An axis-aligned rectangular prism of voxels within a single VS block.
/// </summary>
/// <remarks>
/// Prisms are the coalesced representation of contiguous same-material voxels.
/// They are clipped at VS block boundaries (16³) and split at taps (side adjacencies)
/// to ensure correct resistance calculations.
///
/// Memory: ~14 bytes per prism vs ~50 bytes per voxel in dictionary storage.
/// </remarks>
public readonly record struct Prism
{
    /// <summary>
    /// Local X position within the block (0-15).
    /// </summary>
    public byte LocalX { get; init; }

    /// <summary>
    /// Local Y position within the block (0-15).
    /// </summary>
    public byte LocalY { get; init; }

    /// <summary>
    /// Local Z position within the block (0-15).
    /// </summary>
    public byte LocalZ { get; init; }

    /// <summary>
    /// Size in X dimension (1-16, stored as actual size).
    /// </summary>
    public byte SizeX { get; init; }

    /// <summary>
    /// Size in Y dimension (1-16, stored as actual size).
    /// </summary>
    public byte SizeY { get; init; }

    /// <summary>
    /// Size in Z dimension (1-16, stored as actual size).
    /// </summary>
    public byte SizeZ { get; init; }

    /// <summary>
    /// The voxel type (Conductor or Insulator).
    /// </summary>
    public VoxelType Type { get; init; }

    /// <summary>
    /// The conductor material (null for insulators).
    /// </summary>
    public Material? Material { get; init; }

    /// <summary>
    /// Creates a prism with the given properties.
    /// </summary>
    public Prism(byte localX, byte localY, byte localZ,
                 byte sizeX, byte sizeY, byte sizeZ,
                 VoxelType type, Material? material = null)
    {
        LocalX = localX;
        LocalY = localY;
        LocalZ = localZ;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        Type = type;
        Material = material;
    }

    /// <summary>
    /// Creates a single-voxel prism (1×1×1).
    /// </summary>
    public static Prism SingleVoxel(byte localX, byte localY, byte localZ,
                                     VoxelType type, Material? material = null)
    {
        return new Prism(localX, localY, localZ, 1, 1, 1, type, material);
    }

    /// <summary>
    /// Returns true if the given local position is within this prism.
    /// </summary>
    public bool Contains(int localX, int localY, int localZ)
    {
        return localX >= LocalX && localX < LocalX + SizeX
            && localY >= LocalY && localY < LocalY + SizeY
            && localZ >= LocalZ && localZ < LocalZ + SizeZ;
    }

    /// <summary>
    /// Returns true if the given local position tuple is within this prism.
    /// </summary>
    public bool Contains((int X, int Y, int Z) local)
    {
        return Contains(local.X, local.Y, local.Z);
    }

    /// <summary>
    /// Gets the volume (number of voxels) in this prism.
    /// </summary>
    public int Volume => SizeX * SizeY * SizeZ;

    /// <summary>
    /// Gets the end position (exclusive) in each dimension.
    /// </summary>
    public (int X, int Y, int Z) End => (LocalX + SizeX, LocalY + SizeY, LocalZ + SizeZ);
}
