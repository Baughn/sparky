using System.Collections.Generic;

namespace Sparky.Game.Core;

/// <summary>
/// Stores voxel data for a single VS block (16³ voxels) as coalesced prisms.
/// </summary>
/// <remarks>
/// Instead of storing up to 4096 individual voxels, we store a small number
/// of axis-aligned prisms (~10-20 typical). This provides ~1000x memory
/// compression for typical cable geometry.
/// </remarks>
public class BlockVoxelData {
    private readonly List<Prism> _prisms = new();

    /// <summary>
    /// Gets all prisms in this block.
    /// </summary>
    public IReadOnlyList<Prism> Prisms => _prisms;

    /// <summary>
    /// Gets the number of prisms in this block.
    /// </summary>
    public int PrismCount => _prisms.Count;

    /// <summary>
    /// Returns true if this block has no prisms (all air).
    /// </summary>
    public bool IsEmpty => _prisms.Count == 0;

    /// <summary>
    /// Finds the prism containing the given local position, or null if air.
    /// </summary>
    public Prism? FindPrism(int localX, int localY, int localZ) {
        foreach (var prism in _prisms) {
            if (prism.Contains(localX, localY, localZ))
                return prism;
        }
        return null;
    }

    /// <summary>
    /// Gets the voxel type at the given local position.
    /// </summary>
    public VoxelType GetVoxelType(int localX, int localY, int localZ) {
        var prism = FindPrism(localX, localY, localZ);
        return prism?.Type ?? VoxelType.Air;
    }

    /// <summary>
    /// Gets the material at the given local position, or null if not a conductor.
    /// </summary>
    public Material? GetMaterial(int localX, int localY, int localZ) {
        var prism = FindPrism(localX, localY, localZ);
        return prism?.Material;
    }

    /// <summary>
    /// Rebuilds prisms from a 16³ voxel array.
    /// </summary>
    /// <param name="voxels">
    /// Flat array of 4096 elements indexed as [x + y*16 + z*256].
    /// Each element is (VoxelType, Material?).
    /// </param>
    public void RebuildFromVoxels((VoxelType Type, Material? Material)[] voxels) {
        _prisms.Clear();

        // Track which voxels have been claimed by a prism
        var claimed = new bool[4096];

        // Process each voxel type separately to keep them in distinct prisms
        ExtractPrisms(voxels, claimed, VoxelType.Conductor);
        ExtractPrisms(voxels, claimed, VoxelType.ResistiveConductor);
        ExtractPrisms(voxels, claimed, VoxelType.Insulator);
    }

    private void ExtractPrisms((VoxelType Type, Material? Material)[] voxels, bool[] claimed, VoxelType targetType) {
        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 16; y++) {
                for (int x = 0; x < 16; x++) {
                    int idx = x + y * 16 + z * 256;
                    if (claimed[idx])
                        continue;

                    var (type, material) = voxels[idx];
                    if (type != targetType)
                        continue;

                    // Start a new prism from this seed voxel
                    var prism = GrowPrism(voxels, claimed, x, y, z, type, material);
                    _prisms.Add(prism);
                }
            }
        }
    }

    /// <summary>
    /// Grows a prism from a seed voxel using greedy expansion.
    /// Splits at material boundaries.
    /// </summary>
    private Prism GrowPrism(
        (VoxelType Type, Material? Material)[] voxels,
        bool[] claimed,
        int startX, int startY, int startZ,
        VoxelType type, Material? material) {
        // Grow in +X direction first
        int endX = startX + 1;
        while (endX < 16 && CanExtendX(voxels, claimed, startX, endX, startY, startY + 1, startZ, startZ + 1, type, material)) {
            endX++;
        }

        // Grow in +Y direction
        int endY = startY + 1;
        while (endY < 16 && CanExtendY(voxels, claimed, startX, endX, startY, endY, startZ, startZ + 1, type, material)) {
            endY++;
        }

        // Grow in +Z direction
        int endZ = startZ + 1;
        while (endZ < 16 && CanExtendZ(voxels, claimed, startX, endX, startY, endY, startZ, endZ, type, material)) {
            endZ++;
        }

        // Mark all voxels in this prism as claimed
        for (int z = startZ; z < endZ; z++) {
            for (int y = startY; y < endY; y++) {
                for (int x = startX; x < endX; x++) {
                    claimed[x + y * 16 + z * 256] = true;
                }
            }
        }

        return new Prism(
            (byte)startX, (byte)startY, (byte)startZ,
            (byte)(endX - startX), (byte)(endY - startY), (byte)(endZ - startZ),
            type, material);
    }

    /// <summary>
    /// Checks if we can extend the prism by one in the +X direction.
    /// Returns false if any new voxel has wrong type/material or is already claimed.
    /// </summary>
    private bool CanExtendX(
        (VoxelType Type, Material? Material)[] voxels, bool[] claimed,
        int startX, int newX, int startY, int endY, int startZ, int endZ,
        VoxelType type, Material? material) {
        for (int z = startZ; z < endZ; z++) {
            for (int y = startY; y < endY; y++) {
                int idx = newX + y * 16 + z * 256;
                if (claimed[idx])
                    return false;

                var (vType, vMat) = voxels[idx];
                if (vType != type || !MaterialEquals(vMat, material))
                    return false;
            }
        }
        return true;
    }

    private bool CanExtendY(
        (VoxelType Type, Material? Material)[] voxels, bool[] claimed,
        int startX, int endX, int startY, int newY, int startZ, int endZ,
        VoxelType type, Material? material) {
        for (int z = startZ; z < endZ; z++) {
            for (int x = startX; x < endX; x++) {
                int idx = x + newY * 16 + z * 256;
                if (claimed[idx])
                    return false;

                var (vType, vMat) = voxels[idx];
                if (vType != type || !MaterialEquals(vMat, material))
                    return false;
            }
        }
        return true;
    }

    private bool CanExtendZ(
        (VoxelType Type, Material? Material)[] voxels, bool[] claimed,
        int startX, int endX, int startY, int endY, int startZ, int newZ,
        VoxelType type, Material? material) {
        for (int y = startY; y < endY; y++) {
            for (int x = startX; x < endX; x++) {
                int idx = x + y * 16 + newZ * 256;
                if (claimed[idx])
                    return false;

                var (vType, vMat) = voxels[idx];
                if (vType != type || !MaterialEquals(vMat, material))
                    return false;
            }
        }
        return true;
    }

    private static bool MaterialEquals(Material? a, Material? b) {
        // Reference equality for singleton materials
        return ReferenceEquals(a, b);
    }

    /// <summary>
    /// Expands all prisms back into a 16³ voxel array.
    /// </summary>
    public (VoxelType Type, Material? Material)[] ExpandToVoxels() {
        var voxels = new (VoxelType Type, Material? Material)[4096];

        foreach (var prism in _prisms) {
            var end = prism.End;
            for (int z = prism.LocalZ; z < end.Z; z++) {
                for (int y = prism.LocalY; y < end.Y; y++) {
                    for (int x = prism.LocalX; x < end.X; x++) {
                        voxels[x + y * 16 + z * 256] = (prism.Type, prism.Material);
                    }
                }
            }
        }

        return voxels;
    }
}
