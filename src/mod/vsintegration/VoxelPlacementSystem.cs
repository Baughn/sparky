using System.Collections.Generic;
using System.Linq;
using Sparky.VSIntegration.Preview;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using Material = Sparky.Game.Core.Material;

namespace Sparky.VSIntegration;

/// <summary>
/// ModSystem handling server-side voxel placement requests.
/// Separated from VoxelPreviewSystem for single responsibility.
/// </summary>
public class VoxelPlacementSystem : ModSystem {
    /// <summary>
    /// Shared channel name for voxel operations (preview + placement).
    /// </summary>
    public const string ChannelName = "sparky-voxels";

    private ICoreServerAPI? _sapi;
    private IServerNetworkChannel? _serverChannel;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api) {
        _sapi = api;

        // Note: MaterialRegistry is loaded in SparkyModSystem.AssetsFinalize

        _serverChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<PreviewState>()
            .RegisterMessageType<PreviewUpdateRequest>()
            .RegisterMessageType<VoxelPlacementRequest>()
            .SetMessageHandler<VoxelPlacementRequest>(OnVoxelPlacementRequest);

        api.Logger.Debug("[Sparky] VoxelPlacementSystem server initialized");
    }

    private void OnVoxelPlacementRequest(IServerPlayer player, VoxelPlacementRequest request) {
        if (_sapi == null || request.Voxels.Count == 0)
            return;

        // Basic distance validation: check if player is within ~10 blocks of any target voxel
        var playerPos = player.Entity?.Pos?.XYZ;
        if (playerPos == null)
            return;

        // Check first voxel's distance (reasonable approximation for the whole cable)
        var firstVoxel = request.Voxels[0];
        var voxelBlockPos = new Vec3d(firstVoxel.X / 16.0, firstVoxel.Y / 16.0, firstVoxel.Z / 16.0);
        var distance = playerPos.DistanceTo(voxelBlockPos);
        if (distance > 15) // ~15 blocks max
        {
            _sapi.Logger.Warning($"[Sparky] Player {player.PlayerName} voxel placement rejected: too far ({distance:F1} blocks)");
            return;
        }

        _sapi.Logger.Debug($"[Sparky] OnVoxelPlacementRequest from {player.PlayerName}: {request.Voxels.Count} voxels, isRemoval={request.IsRemoval}");

        // Group voxels by block position
        var voxelsByBlock = new Dictionary<(int X, int Y, int Z), List<(int LocalX, int LocalY, int LocalZ, Material Material)>>();

        foreach (var voxel in request.Voxels) {
            // Handle negative coordinates properly for block position
            var blockX = voxel.X >= 0 ? voxel.X / 16 : (voxel.X - 15) / 16;
            var blockY = voxel.Y >= 0 ? voxel.Y / 16 : (voxel.Y - 15) / 16;
            var blockZ = voxel.Z >= 0 ? voxel.Z / 16 : (voxel.Z - 15) / 16;
            var localX = ((voxel.X % 16) + 16) % 16;
            var localY = ((voxel.Y % 16) + 16) % 16;
            var localZ = ((voxel.Z % 16) + 16) % 16;

            var material = MaterialRegistry.GetMaterialByIndex(voxel.MaterialIndex);

            var blockKey = (blockX, blockY, blockZ);
            if (!voxelsByBlock.TryGetValue(blockKey, out var list)) {
                list = new List<(int, int, int, Material)>();
                voxelsByBlock[blockKey] = list;
            }
            list.Add((localX, localY, localZ, material));
        }

        // Process each block
        foreach (var (blockKey, voxels) in voxelsByBlock) {
            var blockPos = new BlockPos(blockKey.X, blockKey.Y, blockKey.Z);
            var behavior = BEBehaviorCircuit.GetOrCreateAt(_sapi.World, blockPos);
            if (behavior == null) {
                var block = _sapi.World.BlockAccessor.GetBlock(blockPos);
                _sapi.Logger.Warning($"[Sparky] VoxelPlacement: GetOrCreateAt failed for block {block?.Code} at {blockPos}");
                continue;
            }

            if (request.IsRemoval) {
                // Remove voxels
                foreach (var (localX, localY, localZ, _) in voxels) {
                    behavior.RemoveVoxel(localX, localY, localZ);
                }

                // If block is now empty and is a circuit block, remove it
                if (behavior.ConductorCuboids.Count == 0) {
                    var block = _sapi.World.BlockAccessor.GetBlock(blockPos);
                    if (block is BlockCircuit) {
                        _sapi.World.BlockAccessor.SetBlock(0, blockPos);
                    }
                }
            } else {
                // Place voxels using batch method for efficiency
                _sapi.Logger.Debug($"[Sparky] VoxelPlacement: placing {voxels.Count} voxels at {blockPos}");
                behavior.SetConductorVoxelsBatch(voxels.Select(v => (v.LocalX, v.LocalY, v.LocalZ, v.Material)));
                _sapi.Logger.Debug($"[Sparky] VoxelPlacement: block now has {behavior.ConductorCuboids.Count} cuboids");
            }
        }
    }
}
