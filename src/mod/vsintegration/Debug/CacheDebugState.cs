using System;
using System.Collections.Generic;
using Sparky.Voxel.MnaTopology.CableLaying;
using Sparky.Voxel;
using Sparky.VSIntegration.CableLaying;
using Sparky.VSIntegration.Preview;
using Vintagestory.API.Common;
using VSBlockPos = Vintagestory.API.MathTools.BlockPos;

namespace Sparky.VSIntegration.Debug;

/// <summary>
/// Stores accumulated cache debug preview data for a player.
/// </summary>
public class CacheDebugState {
    /// <summary>
    /// Optional logging callback for debugging. Set to enable detailed logging.
    /// </summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Block positions that have been added to preview.</summary>
    private readonly HashSet<(int X, int Y, int Z)> _previewedBlocks = new();

    /// <summary>Accumulated preview voxels.</summary>
    private readonly List<PreviewVoxel> _previewVoxels = new();

    /// <summary>Y offset in voxels (3 blocks = 48 voxels).</summary>
    private const int YOffset = 48;

    /// <summary>Gets current preview voxels.</summary>
    public IReadOnlyList<PreviewVoxel> PreviewVoxels => _previewVoxels;

    /// <summary>
    /// Adds a block's voxel cache state to the preview.
    /// </summary>
    public void AddBlock(VSBlockPos blockPos, IBlockAccessor blockAccessor) {
        var key = (blockPos.X, blockPos.Y, blockPos.Z);
        if (_previewedBlocks.Contains(key))
            return;

        _previewedBlocks.Add(key);

        Log?.Invoke($"[CacheDebugState] AddBlock({blockPos}), blockAccessor type={blockAccessor.GetType().Name}");

        // Forward logging to WorldVoxelCache
        WorldVoxelCache.Log = Log;

        // Create WorldVoxelCache with radius=0 for single block
        var cache = new WorldVoxelCache(blockAccessor, blockPos, radius: 0);

        // Iterate all 16x16x16 voxels in the block
        int baseX = blockPos.X * 16;
        int baseY = blockPos.Y * 16;
        int baseZ = blockPos.Z * 16;

        // Count voxels by state for logging
        int emptyCount = 0, insulationCount = 0, conductorCount = 0, cableCount = 0, unroutableCount = 0;

        for (int z = 0; z < 16; z++) {
            for (int y = 0; y < 16; y++) {
                for (int x = 0; x < 16; x++) {
                    var voxelPos = new VoxelPos(baseX + x, baseY + y, baseZ + z);
                    var state = cache.GetState(voxelPos);

                    // Count by state
                    switch (state.Value) {
                        case CacheVoxelStateValue.Empty: emptyCount++; break;
                        case CacheVoxelStateValue.Insulation: insulationCount++; break;
                        case CacheVoxelStateValue.PreExistingConductor: conductorCount++; break;
                        case CacheVoxelStateValue.CableConductor: cableCount++; break;
                        case CacheVoxelStateValue.Unroutable: unroutableCount++; break;
                    }

                    // Skip Empty voxels
                    if (state == CacheVoxelState.Empty)
                        continue;

                    // Get color for state
                    int color = GetColorForState(state);

                    // Add with Y offset (+48 voxels = +3 blocks)
                    _previewVoxels.Add(new PreviewVoxel(
                        baseX + x,
                        baseY + y + YOffset,
                        baseZ + z,
                        color));
                }
            }
        }

        Log?.Invoke($"[CacheDebugState] Block {blockPos} voxel counts: " +
            $"Empty={emptyCount}, Insulation={insulationCount}, " +
            $"PreExistingConductor={conductorCount}, CableConductor={cableCount}, " +
            $"Unroutable={unroutableCount}, TotalPreviewVoxels={_previewVoxels.Count}");
    }

    /// <summary>
    /// Clears all previewed blocks.
    /// </summary>
    public void Clear() {
        _previewedBlocks.Clear();
        _previewVoxels.Clear();
    }

    /// <summary>
    /// Gets ARGB color for a cache voxel state.
    /// </summary>
    private static int GetColorForState(CacheVoxelState state) {
        const byte alpha = 160;

        return state.Value switch {
            // Insulation: Blue/gray
            CacheVoxelStateValue.Insulation => (alpha << 24) | (0x60 << 16) | (0x8C << 8) | 0xC0,

            // PreExistingConductor: Orange/copper
            CacheVoxelStateValue.PreExistingConductor => (alpha << 24) | (0xD0 << 16) | (0x70 << 8) | 0x30,

            // CableConductor: Green
            CacheVoxelStateValue.CableConductor => (alpha << 24) | (0x30 << 16) | (0xC0 << 8) | 0x30,

            // Unroutable: Red
            CacheVoxelStateValue.Unroutable => (alpha << 24) | (0xE0 << 16) | (0x30 << 8) | 0x30,

            // Fallback (shouldn't happen since Empty is skipped)
            _ => (alpha << 24) | (0xFF << 16) | (0xFF << 8) | 0xFF
        };
    }
}
