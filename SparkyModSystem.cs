using Sparky.VSIntegration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

using Material = Sparky.Game.Core.Material;

namespace Sparky;

/// <summary>
/// Sparky mod - electrical circuit simulation for Vintage Story
/// </summary>
public class SparkyModSystem : ModSystem
{
    private const string CHANNEL_NAME = "sparky";

    /// <summary>
    /// The circuit network manager (server-side only).
    /// </summary>
    public CircuitNetworkManager? NetworkManager { get; private set; }

    private IServerNetworkChannel? _serverChannel;
    private IClientNetworkChannel? _clientChannel;

    /// <summary>
    /// Called on both client and server during initialization.
    /// </summary>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // Register block class
        api.RegisterBlockClass("BlockCircuit", typeof(BlockCircuit));

        // Register block entity class
        api.RegisterBlockEntityClass("BlockEntityCircuit", typeof(BlockEntityCircuit));

        // Register item class
        api.RegisterItemClass("ItemWireTool", typeof(ItemWireTool));

        api.Logger.Notification("[Sparky] Mod classes registered");
    }

    /// <summary>
    /// Called after all assets are loaded. Register conductor blocks here.
    /// </summary>
    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);

        // Register conductor blocks as Sparky materials
        RegisterConductorBlocks(api);
    }

    /// <summary>
    /// Registers conductor blocks with the circuit simulation system.
    /// </summary>
    private void RegisterConductorBlocks(ICoreAPI api)
    {
        // Clear any previous registrations
        BlockEntityCircuit.ClearConductorRegistrations();

        // Map of conductor block codes to materials
        var conductorMap = new (string Code, Material Material)[]
        {
            ("sparky:conductor-copper", Material.Copper),
            ("sparky:conductor-gold", Material.Gold),
            ("sparky:conductor-lead", Material.Lead),
            ("sparky:conductor-iron", Material.Iron)
        };

        foreach (var (code, material) in conductorMap)
        {
            var block = api.World.GetBlock(new AssetLocation(code));
            if (block != null)
            {
                BlockEntityCircuit.RegisterConductor(block.BlockId, material);
                api.Logger.Debug($"[Sparky] Registered conductor: {code} -> {material.Name}");
            }
            else
            {
                api.Logger.Warning($"[Sparky] Conductor block not found: {code}");
            }
        }

        api.Logger.Notification("[Sparky] Conductor blocks registered");
    }

    /// <summary>
    /// Called on the server during initialization.
    /// </summary>
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        // Initialize network manager
        NetworkManager = new CircuitNetworkManager();
        NetworkManager.Initialize(api);

        // Register network channel for future use
        _serverChannel = api.Network.RegisterChannel(CHANNEL_NAME);

        api.Logger.Notification("[Sparky] Server-side initialization complete");
    }

    /// <summary>
    /// Called on the client during initialization.
    /// </summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        // Register network channel for future use
        _clientChannel = api.Network.RegisterChannel(CHANNEL_NAME);

        api.Logger.Notification("[Sparky] Client-side initialization complete");
    }

    /// <summary>
    /// Called when the mod is being unloaded.
    /// </summary>
    public override void Dispose()
    {
        NetworkManager?.Shutdown();
        NetworkManager = null;

        base.Dispose();
    }
}
