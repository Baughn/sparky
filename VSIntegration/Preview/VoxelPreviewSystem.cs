using System;
using System.Collections.Generic;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;
using Sparky.VSIntegration.CableLaying;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

using Material = Sparky.Game.Core.Material;

namespace Sparky.VSIntegration.Preview;

/// <summary>
/// ModSystem managing preview state synchronization between server and all clients.
/// Server tracks all players' previews and broadcasts to clients.
/// </summary>
public class VoxelPreviewSystem : ModSystem
{
    private const string ChannelName = "sparky-preview";

    // Server state
    private ICoreServerAPI? _sapi;
    private IServerNetworkChannel? _serverChannel;
    private readonly Dictionary<string, PreviewState> _playerPreviews = new();

    // Client state
    private ICoreClientAPI? _capi;
    private IClientNetworkChannel? _clientChannel;
    private VoxelPreviewRenderer? _renderer;
    private long _clientTickListenerId;
    private PreviewState? _lastSentState;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;

        _serverChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<PreviewState>()
            .RegisterMessageType<PreviewUpdateRequest>()
            .SetMessageHandler<PreviewUpdateRequest>(OnClientPreviewUpdate);

        // Broadcast all previews periodically (20Hz)
        api.Event.RegisterGameTickListener(OnServerTick, 50);

        // Clean up when players disconnect
        api.Event.PlayerDisconnect += OnPlayerDisconnect;

        api.Logger.Debug("[Sparky] VoxelPreviewSystem server initialized");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;

        _renderer = new VoxelPreviewRenderer(api);
        api.Event.RegisterRenderer(_renderer, EnumRenderStage.Opaque, "sparky-voxel-preview");

        _clientChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<PreviewState>()
            .RegisterMessageType<PreviewUpdateRequest>()
            .SetMessageHandler<PreviewState>(OnPreviewReceived);

        // Check local player's tool state (50fps)
        _clientTickListenerId = api.Event.RegisterGameTickListener(OnClientTick, 20);

        api.Logger.Debug("[Sparky] VoxelPreviewSystem client initialized");
    }

    #region Server Side

    private void OnClientPreviewUpdate(IServerPlayer player, PreviewUpdateRequest request)
    {
        var state = new PreviewState
        {
            PlayerUid = player.PlayerUID,
            Voxels = request.Voxels
        };

        _playerPreviews[player.PlayerUID] = state;
    }

    private void OnServerTick(float dt)
    {
        if (_sapi == null || _serverChannel == null) return;

        // Broadcast all preview states to all clients
        foreach (var state in _playerPreviews.Values)
        {
            _serverChannel.BroadcastPacket(state);
        }
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        if (_playerPreviews.Remove(player.PlayerUID))
        {
            // Broadcast empty state to clear preview on all clients
            _serverChannel?.BroadcastPacket(new PreviewState
            {
                PlayerUid = player.PlayerUID,
                Voxels = new()
            });
        }
    }

    #endregion

    #region Client Side

    private void OnPreviewReceived(PreviewState state)
    {
        if (_renderer == null) return;

        if (state.Voxels.Count == 0)
            _renderer.ClearPlayerPreview(state.PlayerUid);
        else
            _renderer.SetPlayerPreview(state.PlayerUid, state.Voxels);
    }

    private void OnClientTick(float dt)
    {
        if (_capi == null || _clientChannel == null) return;

        var player = _capi.World.Player;
        if (player == null) return;

        // Check if player is holding wire tool
        var slot = player.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack?.Item is not ItemWireTool wireTool)
        {
            SendPreviewUpdate(new List<PreviewVoxel>());
            return;
        }

        // Check if looking at a block
        var blockSel = player.CurrentBlockSelection;
        if (blockSel == null)
        {
            SendPreviewUpdate(new List<PreviewVoxel>());
            return;
        }

        // Calculate target voxel position
        var targetVoxel = CalculateTargetVoxel(blockSel);
        if (targetVoxel == null)
        {
            SendPreviewUpdate(new List<PreviewVoxel>());
            return;
        }

        // Get mode and material from ItemStack.Attributes
        var mode = wireTool.GetMode(slot);
        var material = wireTool.GetSelectedMaterial(slot);

        // Get face direction for cross-section orientation
        var faceDir = BlockFaceToVoxelDirection(blockSel.Face);

        List<PreviewVoxel> voxels;
        if (mode.IsCableMode())
        {
            // Get per-player cable state from ModSystem
            var modSystem = _capi.ModLoader.GetModSystem<SparkyModSystem>();
            var cableState = modSystem.GetCableState(player.PlayerUID);
            voxels = BuildCablePreview(cableState, mode, material, targetVoxel.Value, faceDir, _capi.World.BlockAccessor);
        }
        else
        {
            voxels = BuildSingleVoxelPreview(material, targetVoxel.Value);
        }

        SendPreviewUpdate(voxels);
    }

    private static VoxelDirection BlockFaceToVoxelDirection(Vintagestory.API.MathTools.BlockFacing face)
    {
        return face.Index switch
        {
            0 => VoxelDirection.ZNeg, // North
            1 => VoxelDirection.XPos, // East
            2 => VoxelDirection.ZPos, // South
            3 => VoxelDirection.XNeg, // West
            4 => VoxelDirection.YPos, // Up
            5 => VoxelDirection.YNeg, // Down
            _ => VoxelDirection.YPos
        };
    }

    private List<PreviewVoxel> BuildSingleVoxelPreview(Material material, (int X, int Y, int Z) target)
    {
        var color = VoxelPreviewMesh.GetMaterialColor(material, 128);

        return new List<PreviewVoxel>
        {
            new PreviewVoxel(target.X, target.Y, target.Z, color)
        };
    }

    private List<PreviewVoxel> BuildCablePreview(
        CableLayingState? cableState,
        WireToolMode mode,
        Material material,
        (int X, int Y, int Z) target,
        VoxelDirection faceDir,
        IBlockAccessor blockAccessor)
    {
        var targetPos = new VoxelPos(target.X, target.Y, target.Z);

        // If no cable state yet, show cross-section preview based on mode
        if (cableState == null)
        {
            var crossSection = mode.GetCrossSection();
            if (crossSection == null)
                return new List<PreviewVoxel>();
            return BuildCrossSectionPreview(crossSection.Value, targetPos, faceDir, material);
        }

        // Poll for completed pathfinding
        cableState.TryUpdatePath();

        // Spammy: _capi?.Logger.Debug($"[Sparky Preview] BuildCablePreview: phase={cableState.CurrentPhase}, hasPath={cableState.CurrentPath != null}");

        switch (cableState.CurrentPhase)
        {
            case CableLayingState.Phase.Idle:
                // Show cross-section preview at snapped position (where cable would actually start)
                var (snappedPos, _) = cableState.GetSnappedStartPosition(targetPos, blockAccessor);
                return BuildCrossSectionPreview(cableState.CrossSection, snappedPos, faceDir, material);

            case CableLayingState.Phase.StartSelected:
            case CableLayingState.Phase.PathReady:
                // Update goal position (triggers pathfinding if changed)
                cableState.UpdateGoal(targetPos);

                // Show current path or start position
                if (cableState.CurrentPath != null)
                    return BuildPathPreview(cableState.CurrentPath.Value, material);

                // No path yet - show start position
                if (cableState.StartPosition.HasValue)
                    return BuildCrossSectionPreview(cableState.CrossSection, cableState.StartPosition.Value, faceDir, material);

                return new List<PreviewVoxel>();

            default:
                return new List<PreviewVoxel>();
        }
    }

    private List<PreviewVoxel> BuildCrossSectionPreview(CrossSection crossSection, VoxelPos anchor, VoxelDirection faceDir, Material material)
    {
        var voxels = new List<PreviewVoxel>();
        var color = VoxelPreviewMesh.GetMaterialColor(material, 128);

        // The face direction is where the cable would go INTO the surface
        // We want the cross-section perpendicular to this direction
        foreach (var pos in crossSection.GetVoxelPositions(anchor, faceDir, CrossSectionOrientation.Flat))
        {
            voxels.Add(new PreviewVoxel(pos.X, pos.Y, pos.Z, color));
        }

        return voxels;
    }

    private List<PreviewVoxel> BuildPathPreview(PathResult path, Material material)
    {
        var voxels = new List<PreviewVoxel>();

        // Color based on path result type
        var color = path.Type switch
        {
            PathResultType.Complete => GetCompletePathColor(material),   // Green tint
            PathResultType.Partial => GetPartialPathColor(material),     // Yellow tint
            PathResultType.NoProgress => GetNoProgressColor(),           // Red
            _ => VoxelPreviewMesh.GetMaterialColor(material, 128)
        };

        foreach (var pos in path.Path)
        {
            voxels.Add(new PreviewVoxel(pos.X, pos.Y, pos.Z, color));
        }

        return voxels;
    }

    private static int GetCompletePathColor(Material material)
    {
        // Green tint - mix material color with green
        var baseColor = VoxelPreviewMesh.GetMaterialColor(material, 160);
        return TintColor(baseColor, 0.5f, 1.0f, 0.5f);
    }

    private static int GetPartialPathColor(Material material)
    {
        // Yellow tint - mix material color with yellow
        var baseColor = VoxelPreviewMesh.GetMaterialColor(material, 160);
        return TintColor(baseColor, 1.0f, 1.0f, 0.3f);
    }

    private static int GetNoProgressColor()
    {
        // Pure red
        return (160 << 24) | (255 << 16) | (50 << 8) | 50;
    }

    private static int TintColor(int argb, float rMult, float gMult, float bMult)
    {
        int a = (argb >> 24) & 0xFF;
        int r = Math.Min(255, (int)(((argb >> 16) & 0xFF) * rMult));
        int g = Math.Min(255, (int)(((argb >> 8) & 0xFF) * gMult));
        int b = Math.Min(255, (int)((argb & 0xFF) * bMult));
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private void SendPreviewUpdate(List<PreviewVoxel> voxels)
    {
        if (_clientChannel == null) return;

        // Only send if changed
        if (_lastSentState != null &&
            VoxelsEqual(_lastSentState.Voxels, voxels))
        {
            return;
        }

        var request = new PreviewUpdateRequest { Voxels = voxels };
        _clientChannel.SendPacket(request);

        _lastSentState = new PreviewState { Voxels = voxels };
    }

    private static bool VoxelsEqual(List<PreviewVoxel> a, List<PreviewVoxel> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z || a[i].Rgba != b[i].Rgba)
                return false;
        }
        return true;
    }

    private (int X, int Y, int Z)? CalculateTargetVoxel(BlockSelection blockSel)
    {
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;

        // Get adjacent voxel (where we'd place)
        var (localX, localY, localZ, outside) = Game.Core.VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);

        // Determine target block position
        var blockPos = outside
            ? blockSel.Position.AddCopy(blockSel.Face)
            : blockSel.Position;

        // Convert to global voxel coordinates
        int globalX = blockPos.X * 16 + localX;
        int globalY = blockPos.Y * 16 + localY;
        int globalZ = blockPos.Z * 16 + localZ;

        return (globalX, globalY, globalZ);
    }

    #endregion

    public override void Dispose()
    {
        if (_sapi != null)
        {
            _sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;
        }

        if (_capi != null)
        {
            _capi.Event.UnregisterGameTickListener(_clientTickListenerId);
            if (_renderer != null)
            {
                _capi.Event.UnregisterRenderer(_renderer, EnumRenderStage.OIT);
                _renderer.Dispose();
            }
        }

        base.Dispose();
    }
}
