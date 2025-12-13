using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using VoxelType = Sparky.Game.Core.VoxelType;
using Material = Sparky.Game.Core.Material;

namespace Sparky.VSIntegration;

/// <summary>
/// Tool for placing and removing conductor voxels in circuit blocks.
/// </summary>
public class ItemWireTool : Item
{
    /// <summary>
    /// Currently selected material for placement.
    /// </summary>
    private Material _selectedMaterial = Material.Copper;

    /// <summary>
    /// Material selection cycle order.
    /// </summary>
    private static readonly Material[] Materials =
    {
        Material.Copper,
        Material.Gold,
        Material.Lead,
        Material.Iron
    };

    private int _materialIndex = 0;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
    }

    /// <summary>
    /// Handle interaction with blocks.
    /// </summary>
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (blockSel == null)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        var world = byEntity.World;
        var pos = blockSel.Position;
        var block = world.BlockAccessor.GetBlock(pos);

        // If targeting a circuit block, place/remove voxel
        if (block is BlockCircuit)
        {
            var player = (byEntity as EntityPlayer)?.Player;
            if (player != null)
            {
                OnCircuitBlockInteract(world, player, blockSel);
                handling = EnumHandHandling.PreventDefault;
                return;
            }
        }

        // If targeting a replaceable block (air, grass, etc), place new circuit block
        if (block.Replaceable >= 6000)
        {
            PlaceNewCircuitBlock(world, blockSel, byEntity);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        // If targeting a solid block, try to place on the adjacent face
        var adjacentPos = blockSel.Position.AddCopy(blockSel.Face);
        var adjacentBlock = world.BlockAccessor.GetBlock(adjacentPos);

        if (adjacentBlock.Replaceable >= 6000)
        {
            // Create a new block selection for the adjacent position
            var newSel = new BlockSelection
            {
                Position = adjacentPos,
                Face = blockSel.Face.Opposite,
                HitPosition = GetOppositeHitPosition(blockSel)
            };
            PlaceNewCircuitBlock(world, newSel, byEntity);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
    }

    /// <summary>
    /// Called when interacting with an existing circuit block.
    /// </summary>
    public bool OnCircuitBlockInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityCircuit;
        if (be == null) return false;

        var (localX, localY, localZ) = GetVoxelPosition(blockSel);
        bool sneaking = byPlayer.Entity.Controls.Sneak;

        if (sneaking)
        {
            // Remove voxel
            be.SetVoxel(localX, localY, localZ, VoxelType.Air, null);

            // If block is now empty, remove it
            if (be.VoxelCount == 0)
            {
                world.BlockAccessor.SetBlock(0, blockSel.Position);
            }
        }
        else
        {
            // Place voxel
            be.SetVoxel(localX, localY, localZ, VoxelType.Conductor, _selectedMaterial);
        }

        return true;
    }

    /// <summary>
    /// Places a new circuit block and sets the initial voxel.
    /// </summary>
    private void PlaceNewCircuitBlock(IWorldAccessor world, BlockSelection blockSel, EntityAgent byEntity)
    {
        var circuitBlock = world.GetBlock(new AssetLocation("sparky:circuitblock"));
        if (circuitBlock == null)
        {
            api?.Logger.Warning("Could not find sparky:circuitblock");
            return;
        }

        world.BlockAccessor.SetBlock(circuitBlock.BlockId, blockSel.Position);

        // Get the newly created block entity
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityCircuit;
        if (be != null)
        {
            var (localX, localY, localZ) = GetVoxelPosition(blockSel);
            be.SetVoxel(localX, localY, localZ, VoxelType.Conductor, _selectedMaterial);
        }
    }

    /// <summary>
    /// Converts hit position to local voxel coordinates (0-15).
    /// </summary>
    private (int X, int Y, int Z) GetVoxelPosition(BlockSelection blockSel)
    {
        var hitPos = blockSel.HitPosition;

        // Convert 0-1 hit position to 0-15 voxel coordinates
        int x = Math.Clamp((int)(hitPos.X * 16), 0, 15);
        int y = Math.Clamp((int)(hitPos.Y * 16), 0, 15);
        int z = Math.Clamp((int)(hitPos.Z * 16), 0, 15);

        return (x, y, z);
    }

    /// <summary>
    /// Gets the opposite hit position when placing on adjacent block.
    /// </summary>
    private Vec3d GetOppositeHitPosition(BlockSelection blockSel)
    {
        var face = blockSel.Face;
        var hit = blockSel.HitPosition;

        // Map hit position to the opposite face
        return new Vec3d(
            face.Axis == EnumAxis.X ? (face.Normali.X > 0 ? 0.0 : 1.0) : hit.X,
            face.Axis == EnumAxis.Y ? (face.Normali.Y > 0 ? 0.0 : 1.0) : hit.Y,
            face.Axis == EnumAxis.Z ? (face.Normali.Z > 0 ? 0.0 : 1.0) : hit.Z
        );
    }

    /// <summary>
    /// Cycle through materials with scroll wheel or key.
    /// </summary>
    public override void OnHeldInteractStop(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel)
    {
        base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
    }

    /// <summary>
    /// Cycles to the next material.
    /// </summary>
    public void CycleNextMaterial()
    {
        _materialIndex = (_materialIndex + 1) % Materials.Length;
        _selectedMaterial = Materials[_materialIndex];
    }

    /// <summary>
    /// Cycles to the previous material.
    /// </summary>
    public void CyclePreviousMaterial()
    {
        _materialIndex = (_materialIndex - 1 + Materials.Length) % Materials.Length;
        _selectedMaterial = Materials[_materialIndex];
    }

    /// <summary>
    /// Sets the selected material directly.
    /// </summary>
    public void SetMaterial(Material material)
    {
        _selectedMaterial = material;
        _materialIndex = Array.IndexOf(Materials, material);
        if (_materialIndex < 0) _materialIndex = 0;
    }

    /// <summary>
    /// Gets the currently selected material.
    /// </summary>
    public Material GetSelectedMaterial() => _selectedMaterial;

    /// <summary>
    /// Returns info text shown in the hotbar.
    /// </summary>
    public override string GetHeldItemName(ItemStack itemStack)
    {
        return $"Wire Tool ({_selectedMaterial.Name})";
    }

    /// <summary>
    /// Returns detailed info for the handbook.
    /// </summary>
    public override void GetHeldItemInfo(
        ItemSlot inSlot,
        StringBuilder dsc,
        IWorldAccessor world,
        bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine($"Selected material: {_selectedMaterial.Name}");
        dsc.AppendLine("Left click: Place conductor voxel");
        dsc.AppendLine("Sneak + Left click: Remove voxel");
    }
}
