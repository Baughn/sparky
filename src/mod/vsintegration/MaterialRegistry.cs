using System.Collections.Generic;
using Vintagestory.API.Common;

using Material = Sparky.Voxel.Material;

namespace Sparky.VSIntegration;

/// <summary>
/// Conductor material definition loaded from materials.json.
/// </summary>
public record ConductorMaterial(
    string BlockCode,
    Material Material,
    double Resistivity,
    string PreviewColor,
    string DisplayName
);

/// <summary>
/// Registry for conductor materials loaded from JSON configuration.
/// </summary>
public static class MaterialRegistry {
    private static readonly List<ConductorMaterial> _conductors = new();
    private static readonly Dictionary<string, ConductorMaterial> _conductorsByBlockCode = new();
    private static bool _initialized;

    /// <summary>
    /// All registered conductor materials.
    /// </summary>
    public static IReadOnlyList<ConductorMaterial> Conductors => _conductors;

    /// <summary>
    /// Gets a material by its index (for wire tool material selection).
    /// </summary>
    public static Material GetMaterialByIndex(int index) {
        if (index >= 0 && index < _conductors.Count)
            return _conductors[index].Material;
        return Material.Copper; // Fallback
    }

    /// <summary>
    /// Gets the conductor material for a block code, or null if not registered.
    /// </summary>
    public static ConductorMaterial? GetByBlockCode(string blockCode) {
        _conductorsByBlockCode.TryGetValue(blockCode, out var material);
        return material;
    }

    /// <summary>
    /// Loads the material registry by scanning all blocks for sparky conductor attributes.
    /// Should be called during AssetsFinalize. This allows other mods to add conductors
    /// by simply adding attributes.sparky.conductor=true to their block definitions.
    /// </summary>
    public static void Load(ICoreAPI api) {
        if (_initialized) {
            _conductors.Clear();
            _conductorsByBlockCode.Clear();
        }

        foreach (var block in api.World.Blocks) {
            if (block?.Code == null) continue;

            var sparkyAttrs = block.Attributes?["sparky"];
            if (sparkyAttrs == null || !sparkyAttrs.Exists) continue;
            if (!sparkyAttrs["conductor"].AsBool(false)) continue;

            var blockCode = block.Code.ToString();
            var materialName = sparkyAttrs["material"].AsString("Copper");
            var resistivity = sparkyAttrs["resistivity"].AsDouble(0.001);
            var previewColor = sparkyAttrs["previewColor"].AsString("#B87333");
            var displayName = sparkyAttrs["displayName"].AsString(materialName + " Wire");

            // Map material name to Material singleton
            var material = materialName switch {
                "Copper" => Material.Copper,
                "Gold" => Material.Gold,
                "Lead" => Material.Lead,
                "Iron" => Material.Iron,
                _ => Material.Copper
            };

            var conductor = new ConductorMaterial(blockCode, material, resistivity, previewColor, displayName);
            _conductors.Add(conductor);
            _conductorsByBlockCode[blockCode] = conductor;

            api.Logger.Debug($"[Sparky] Found conductor block: {blockCode} -> {materialName}");
        }

        _initialized = true;
        api.Logger.Notification($"[Sparky] Loaded {_conductors.Count} conductor materials from block attributes");
    }
}
