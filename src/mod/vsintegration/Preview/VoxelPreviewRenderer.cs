using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using ColorUtil = Vintagestory.API.MathTools.ColorUtil;

namespace Sparky.VSIntegration.Preview;

/// <summary>
/// Renders preview voxels for all players as transparent ghost blocks.
/// </summary>
public class VoxelPreviewRenderer : IRenderer {
    /// <summary>
    /// Render after terrain (0.37) but before particles (0.6).
    /// </summary>
    public double RenderOrder => 0.5;

    /// <summary>
    /// Not currently used by VS.
    /// </summary>
    public int RenderRange => 100;

    private readonly ICoreClientAPI _capi;
    private TextureAtlasPosition? _copperTexPos;
    private int _blockAtlasTextureId;

    // Per-player preview state
    private readonly Dictionary<string, PlayerPreviewState> _playerStates = new();

    private class PlayerPreviewState {
        public List<PreviewVoxel> Voxels = new();
        public MeshRef? MeshRef;
        public Vec3d MeshOrigin = new();
        public bool IsDirty;
    }

    public VoxelPreviewRenderer(ICoreClientAPI capi) {
        _capi = capi;
        // Texture lookup deferred to first render - atlas may not be ready during init
    }

    private void EnsureTextureLoaded() {
        if (_copperTexPos != null)
            return;

        // Get copper conductor block's texture from atlas
        var copperBlock = _capi.World.GetBlock(new AssetLocation("sparky:circuitblock"));

        _copperTexPos = _capi.BlockTextureAtlas.GetPosition(copperBlock, "copper", true);
        _blockAtlasTextureId = _copperTexPos.atlasTextureId;

        if (_blockAtlasTextureId == 0) {
            _capi.Logger.Warning("[Sparky] Got texture ID 0 for preview - texture resolution may have failed");
        }
    }

    /// <summary>
    /// Sets the preview voxels for a player. Empty list clears the preview.
    /// </summary>
    public void SetPlayerPreview(string playerUid, List<PreviewVoxel> voxels) {
        if (!_playerStates.TryGetValue(playerUid, out var state)) {
            state = new PlayerPreviewState();
            _playerStates[playerUid] = state;
        }

        state.Voxels = voxels;
        state.IsDirty = true;
    }

    /// <summary>
    /// Clears the preview for a player.
    /// </summary>
    public void ClearPlayerPreview(string playerUid) {
        if (_playerStates.TryGetValue(playerUid, out var state)) {
            state.MeshRef?.Dispose();
            _playerStates.Remove(playerUid);
        }
    }

    /// <summary>
    /// Clears all previews.
    /// </summary>
    public void ClearAllPreviews() {
        foreach (var state in _playerStates.Values) {
            state.MeshRef?.Dispose();
        }
        _playerStates.Clear();
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
        if (stage != EnumRenderStage.Opaque)
            return;
        if (_playerStates.Count == 0)
            return;

        var rapi = _capi.Render;
        var player = _capi.World.Player;
        if (player?.Entity == null)
            return;
        var camPos = player.Entity.CameraPos;

        // Rebuild any dirty meshes
        foreach (var kvp in _playerStates) {
            var state = kvp.Value;
            if (state.IsDirty) {
                RebuildMesh(state);
                state.IsDirty = false;
            }
        }

        // Set up rendering state for transparent meshes
        rapi.GlToggleBlend(true, EnumBlendMode.Standard);
        rapi.GlDisableCullFace();

        // Use standard shader like ToolMoldRenderer does
        var prog = rapi.StandardShader;
        prog.Use();
        prog.RgbaAmbientIn = rapi.AmbientColor;
        prog.RgbaFogIn = rapi.FogColor;
        prog.FogMinIn = rapi.FogMin;
        prog.FogDensityIn = rapi.FogDensity;
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.DontWarpVertices = 0;
        prog.AddRenderFlags = 0;
        prog.ExtraGodray = 0;
        prog.OverlayOpacity = 0;
        prog.NormalShaded = 0;
        prog.AlphaTest = 0.01f;

        // Bind texture like ToolMoldRenderer does
        rapi.BindTexture2d(_blockAtlasTextureId);

        prog.ViewMatrix = rapi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;

        foreach (var kvp in _playerStates) {
            var state = kvp.Value;
            if (state.MeshRef == null || state.Voxels.Count == 0)
                continue;

            // Update model matrix for camera-relative positioning
            prog.ModelMatrix = new Matrixf()
                .Identity()
                .Translate(
                    (float)(state.MeshOrigin.X - camPos.X),
                    (float)(state.MeshOrigin.Y - camPos.Y),
                    (float)(state.MeshOrigin.Z - camPos.Z))
                .Values;

            // Get lighting at mesh origin
            var blockPos = new BlockPos(
                (int)state.MeshOrigin.X,
                (int)state.MeshOrigin.Y,
                (int)state.MeshOrigin.Z,
                0);
            prog.RgbaLightIn = _capi.World.BlockAccessor.GetLightRGBs(blockPos);

            rapi.RenderMesh(state.MeshRef);
        }

        prog.Stop();
        rapi.GlEnableCullFace();
    }

    private void RebuildMesh(PlayerPreviewState state) {
        // Dispose old mesh
        state.MeshRef?.Dispose();
        state.MeshRef = null;

        if (state.Voxels.Count == 0)
            return;

        // Ensure texture is loaded (deferred from init)
        EnsureTextureLoaded();
        if (_copperTexPos == null)
            return;

        // Set mesh origin at minimum voxel position
        state.MeshOrigin = VoxelPreviewMesh.ComputeMeshOrigin(state.Voxels);

        // Build multi-voxel mesh with face culling
        var meshData = VoxelPreviewMesh.BuildVoxelMesh(state.Voxels);
        if (meshData == null)
            return;

        // Map UVs from 0-1 to texture atlas position
        meshData.SetTexPos(_copperTexPos);

        // Upload to GPU
        state.MeshRef = _capi.Render.UploadMesh(meshData);
    }

    public void Dispose() {
        ClearAllPreviews();
    }
}
