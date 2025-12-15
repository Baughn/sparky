using System.Collections.Generic;
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
        api.Event.RegisterRenderer(_renderer, EnumRenderStage.OIT, "sparky-voxel-preview");

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
        {
            _renderer.ClearPlayerPreview(state.PlayerUid);
        }
        else
        {
            _renderer.SetPlayerPreview(state.PlayerUid, state.Voxels);
        }
    }

    private void OnClientTick(float dt)
    {
        if (_capi == null || _clientChannel == null) return;

        var player = _capi.World.Player;
        if (player == null) return;

        // Check if player is holding wire tool
        var heldItem = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Item;
        if (heldItem is not ItemWireTool wireTool)
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
        var targetVoxel = CalculateTargetVoxel(blockSel, wireTool);
        if (targetVoxel == null)
        {
            SendPreviewUpdate(new List<PreviewVoxel>());
            return;
        }

        // Build preview with material color
        var material = wireTool.GetSelectedMaterial();
        var color = VoxelPreviewMesh.GetMaterialColor(material, 128);

        var voxels = new List<PreviewVoxel>
        {
            new PreviewVoxel(targetVoxel.Value.X, targetVoxel.Value.Y, targetVoxel.Value.Z, color)
        };

        SendPreviewUpdate(voxels);
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

    private (int X, int Y, int Z)? CalculateTargetVoxel(BlockSelection blockSel, ItemWireTool wireTool)
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
