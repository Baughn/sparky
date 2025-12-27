using System.Collections.Generic;

namespace Sparky.Voxel;

/// <summary>
/// The voxel data type stored in the octree: type + optional material.
/// </summary>
public readonly record struct VoxelData(VoxelType Type, Material? Material) {
    /// <summary>Air voxel (default value for octree).</summary>
    public static readonly VoxelData Air = new(VoxelType.Air, null);
}

/// <summary>
/// Builds and maintains prisms incrementally from an SVO-backed voxel storage.
/// </summary>
/// <remarks>
/// Key optimizations:
/// - O(log n) voxel get/set using sparse voxel octree
/// - Per-block prism caching with lazy rebuild
/// - Only affected blocks are invalidated on voxel changes
///
/// This replaces the expand-modify-rebuild cycle that allocated 4096-element
/// arrays on every SetVoxel call.
/// </remarks>
public class IncrementalPrismBuilder {
    private readonly SparseVoxelOctree<VoxelData> _svo = new(VoxelData.Air);
    private readonly Dictionary<BlockPos, List<Prism>> _prismCache = new();
    private readonly HashSet<BlockPos> _dirtyBlocks = new();
    private long _version;

    /// <summary>
    /// Gets the total number of voxels stored.
    /// </summary>
    public int VoxelCount => _svo.VoxelCount;

    /// <summary>
    /// Gets the set of blocks that have been modified since the last rebuild.
    /// </summary>
    /// <remarks>
    /// Use this to detect which blocks need topology updates.
    /// The dirty set is cleared when prisms are accessed via GetAllPrisms() or RebuildDirtyBlocks().
    /// </remarks>
    public IReadOnlySet<BlockPos> DirtyBlocks => _dirtyBlocks;

    /// <summary>
    /// Returns true if any blocks are dirty (need prism rebuild).
    /// </summary>
    public bool HasDirtyBlocks => _dirtyBlocks.Count > 0;

    /// <summary>
    /// A version number that increments every time a voxel is modified.
    /// Use this to detect if the grid changed since the last topology build.
    /// </summary>
    public long Version => _version;

    /// <summary>
    /// Sets a voxel at the given position.
    /// </summary>
    public void SetVoxel(VoxelPos pos, VoxelType type, Material? material) {
        var oldData = _svo.Get(pos);

        // Skip if no change
        if (oldData.Type == type && (type == VoxelType.Air ||
            ReferenceEquals(oldData.Material, material))) {
            return;
        }

        _svo.Set(pos, new VoxelData(type, material));
        _version++;

        // Mark this block's prism cache as dirty
        InvalidateBlock(pos.Block);
    }

    /// <summary>
    /// Sets multiple voxels in a batch.
    /// </summary>
    public void SetVoxels(IEnumerable<(VoxelPos Pos, VoxelType Type, Material? Material)> voxels) {
        foreach (var (pos, type, material) in voxels) {
            _svo.Set(pos, new VoxelData(type, material));
            _version++;
            InvalidateBlock(pos.Block);
        }
    }

    /// <summary>
    /// Gets the voxel data at the given position.
    /// </summary>
    public (VoxelType Type, Material? Material) GetVoxel(VoxelPos pos) {
        var data = _svo.Get(pos);
        return (data.Type, data.Material);
    }

    /// <summary>
    /// Gets the voxel type at the given position.
    /// </summary>
    public VoxelType GetVoxelType(VoxelPos pos) {
        return _svo.Get(pos).Type;
    }

    /// <summary>
    /// Gets the material at the given position.
    /// </summary>
    public Material? GetMaterial(VoxelPos pos) {
        return _svo.Get(pos).Material;
    }

    /// <summary>
    /// Returns all prisms in all blocks.
    /// </summary>
    public IEnumerable<(BlockPos Block, Prism Prism)> GetAllPrisms() {
        // Rebuild any dirty blocks
        RebuildDirtyBlocks();

        foreach (var (block, prisms) in _prismCache) {
            foreach (var prism in prisms) {
                yield return (block, prism);
            }
        }
    }

    /// <summary>
    /// Gets prisms in a specific block.
    /// </summary>
    public IEnumerable<Prism> GetPrismsInBlock(BlockPos block) {
        // Rebuild if dirty
        if (_dirtyBlocks.Contains(block)) {
            RebuildBlock(block);
            _dirtyBlocks.Remove(block);
        }

        if (_prismCache.TryGetValue(block, out var prisms)) {
            return prisms;
        }
        return [];
    }

    /// <summary>
    /// Gets cached prisms for a block WITHOUT triggering rebuild.
    /// Returns the OLD prisms if the block is dirty.
    /// </summary>
    /// <remarks>
    /// Use this for incremental topology updates to get the old state before rebuild.
    /// Returns empty if block has no cached prisms (either empty or never built).
    /// </remarks>
    public IReadOnlyList<Prism> GetCachedPrisms(BlockPos block) {
        if (_prismCache.TryGetValue(block, out var prisms)) {
            return prisms;
        }
        return [];
    }

    /// <summary>
    /// Rebuilds a single dirty block and returns both old and new prisms.
    /// </summary>
    /// <param name="block">The block to rebuild.</param>
    /// <returns>Tuple of (old prisms, new prisms). Old may be empty if block was new.</returns>
    /// <remarks>
    /// Use this for incremental topology updates. The block is removed from dirty set.
    /// If the block was not dirty, returns (current prisms, current prisms).
    /// </remarks>
    public (IReadOnlyList<Prism> OldPrisms, IReadOnlyList<Prism> NewPrisms) RebuildBlockIncremental(BlockPos block) {
        // Get old prisms (may be empty)
        var oldPrisms = GetCachedPrisms(block);

        if (!_dirtyBlocks.Contains(block)) {
            // Not dirty - return current state as both old and new
            return (oldPrisms, oldPrisms);
        }

        // Rebuild
        RebuildBlock(block);
        _dirtyBlocks.Remove(block);

        // Get new prisms
        var newPrisms = GetCachedPrisms(block);

        return (oldPrisms, newPrisms);
    }

    /// <summary>
    /// Gets the total number of prisms across all blocks.
    /// </summary>
    public int PrismCount {
        get {
            RebuildDirtyBlocks();
            int count = 0;
            foreach (var prisms in _prismCache.Values) {
                count += prisms.Count;
            }
            return count;
        }
    }

    /// <summary>
    /// Gets the number of blocks with voxels.
    /// </summary>
    public int BlockCount {
        get {
            RebuildDirtyBlocks();
            return _prismCache.Count;
        }
    }

    /// <summary>
    /// Clears all voxels and cached prisms.
    /// </summary>
    public void Clear() {
        _svo.Clear();
        _prismCache.Clear();
        _dirtyBlocks.Clear();
    }

    /// <summary>
    /// Returns all non-Air voxels.
    /// </summary>
    public IEnumerable<(VoxelPos Pos, VoxelType Type, Material? Material)> GetAllVoxels() {
        foreach (var (pos, data) in _svo.GetAllVoxels()) {
            yield return (pos, data.Type, data.Material);
        }
    }

    private void InvalidateBlock(BlockPos block) {
        _dirtyBlocks.Add(block);
    }

    private void RebuildDirtyBlocks() {
        if (_dirtyBlocks.Count == 0)
            return;

        foreach (var block in _dirtyBlocks) {
            RebuildBlock(block);
        }
        _dirtyBlocks.Clear();
    }

    private void RebuildBlock(BlockPos block) {
        // Extract voxels for this block from the SVO
        var voxels = new (VoxelType Type, Material? Material)[4096];
        bool hasAnyVoxels = false;

        // Get voxels from SVO for this block
        int baseX = block.X * 16;
        int baseY = block.Y * 16;
        int baseZ = block.Z * 16;

        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 16; y++) {
                for (int x = 0; x < 16; x++) {
                    var pos = new VoxelPos(baseX + x, baseY + y, baseZ + z);
                    var data = _svo.Get(pos);
                    voxels[x + y * 16 + z * 256] = (data.Type, data.Material);
                    if (data.Type != VoxelType.Air)
                        hasAnyVoxels = true;
                }
            }
        }

        if (!hasAnyVoxels) {
            _prismCache.Remove(block);
            return;
        }

        // Build prisms using greedy meshing
        var prisms = BuildPrismsFromVoxels(voxels);
        _prismCache[block] = prisms;
    }

    /// <summary>
    /// Builds prisms from a 16x16x16 voxel array using greedy meshing.
    /// </summary>
    private static List<Prism> BuildPrismsFromVoxels((VoxelType Type, Material? Material)[] voxels) {
        var prisms = new List<Prism>();
        var claimed = new bool[4096];

        // Process each voxel type separately
        ExtractPrisms(voxels, claimed, prisms, VoxelType.Conductor);
        ExtractPrisms(voxels, claimed, prisms, VoxelType.ResistiveConductor);
        ExtractPrisms(voxels, claimed, prisms, VoxelType.Insulator);

        return prisms;
    }

    private static void ExtractPrisms(
        (VoxelType Type, Material? Material)[] voxels,
        bool[] claimed,
        List<Prism> prisms,
        VoxelType targetType) {
        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 16; y++) {
                for (int x = 0; x < 16; x++) {
                    int idx = x + y * 16 + z * 256;
                    if (claimed[idx])
                        continue;

                    var (type, material) = voxels[idx];
                    if (type != targetType)
                        continue;

                    var prism = GrowPrism(voxels, claimed, x, y, z, type, material);
                    prisms.Add(prism);
                }
            }
        }
    }

    private static Prism GrowPrism(
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

        // Mark claimed
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

    private static bool CanExtendX(
        (VoxelType Type, Material? Material)[] voxels, bool[] claimed,
        int startX, int newX, int startY, int endY, int startZ, int endZ,
        VoxelType type, Material? material) {
        for (int z = startZ; z < endZ; z++) {
            for (int y = startY; y < endY; y++) {
                int idx = newX + y * 16 + z * 256;
                if (claimed[idx])
                    return false;

                var (vType, vMat) = voxels[idx];
                if (vType != type || !ReferenceEquals(vMat, material))
                    return false;
            }
        }
        return true;
    }

    private static bool CanExtendY(
        (VoxelType Type, Material? Material)[] voxels, bool[] claimed,
        int startX, int endX, int startY, int newY, int startZ, int endZ,
        VoxelType type, Material? material) {
        for (int z = startZ; z < endZ; z++) {
            for (int x = startX; x < endX; x++) {
                int idx = x + newY * 16 + z * 256;
                if (claimed[idx])
                    return false;

                var (vType, vMat) = voxels[idx];
                if (vType != type || !ReferenceEquals(vMat, material))
                    return false;
            }
        }
        return true;
    }

    private static bool CanExtendZ(
        (VoxelType Type, Material? Material)[] voxels, bool[] claimed,
        int startX, int endX, int startY, int endY, int startZ, int newZ,
        VoxelType type, Material? material) {
        for (int y = startY; y < endY; y++) {
            for (int x = startX; x < endX; x++) {
                int idx = x + y * 16 + newZ * 256;
                if (claimed[idx])
                    return false;

                var (vType, vMat) = voxels[idx];
                if (vType != type || !ReferenceEquals(vMat, material))
                    return false;
            }
        }
        return true;
    }
}
