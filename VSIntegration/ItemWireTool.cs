using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Material = Sparky.Game.Core.Material;
using Sparky.Game.Core;

namespace Sparky.VSIntegration;

/// <summary>
/// Operating mode for the wire tool.
/// </summary>
public enum WireToolMode
{
    /// <summary>
    /// Default mode: place/remove single voxels.
    /// </summary>
    SingleVoxel,

    // Future modes for cable laying:
    // CableLaying - two-click routing with A* pathfinding
}

/// <summary>
/// Tool for placing and removing conductor voxels in circuit blocks.
/// </summary>
public class ItemWireTool : Item
{
    /// <summary>
    /// Current operating mode.
    /// </summary>
    public WireToolMode CurrentMode { get; private set; } = WireToolMode.SingleVoxel;
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

    /// <summary>
    /// Handle left-click (attack) - removes voxels from circuit blocks.
    /// </summary>
    public override void OnHeldAttackStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handling)
    {
        if (blockSel == null)
        {
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
            return;
        }

        var world = byEntity.World;
        var block = world.BlockAccessor.GetBlock(blockSel.Position);

        // If targeting a circuit block, remove voxel instead of breaking block
        if (block is BlockCircuit)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityCircuit;
            if (be != null)
            {
                var (localX, localY, localZ) = GetClickedVoxel(blockSel);
                be.RemoveVoxel(localX, localY, localZ);

                // If block is now empty, remove it
                if (be.VoxelCuboids == null || be.VoxelCuboids.Count == 0)
                {
                    world.BlockAccessor.SetBlock(0, blockSel.Position);
                }

                handling = EnumHandHandling.PreventDefault;
                return;
            }
        }

        base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
    }

    /// <summary>
    /// Handle right-click (interact) - places voxels.
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

        // If targeting a circuit block, place voxel
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
        var be = GetOrCreateCircuitBlock(world, adjacentPos);
        if (be != null)
        {
            var (localX, localY, localZ) = GetVoxelPositionOnFace(blockSel);
            be.SetConductorVoxel(localX, localY, localZ, _selectedMaterial);
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
    }

    /// <summary>
    /// Gets an existing circuit block entity, or creates a new circuit block if the position is replaceable.
    /// Returns null if the position is occupied by a non-circuit, non-replaceable block.
    /// </summary>
    private BlockEntityCircuit? GetOrCreateCircuitBlock(IWorldAccessor world, Vintagestory.API.MathTools.BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);

        if (block is BlockCircuit)
        {
            return world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCircuit;
        }

        if (block.Replaceable >= 6000)
        {
            var circuitBlock = world.GetBlock(new AssetLocation("sparky:circuitblock"));
            if (circuitBlock == null)
            {
                api?.Logger.Warning("Could not find sparky:circuitblock");
                return null;
            }

            world.BlockAccessor.SetBlock(circuitBlock.BlockId, pos);
            return world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCircuit;
        }

        return null;
    }

    /// <summary>
    /// Called when right-clicking an existing circuit block. Places a conductor voxel.
    /// </summary>
    public bool OnCircuitBlockInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // Check if adjacent voxel would be outside this block
        var (localX, localY, localZ, outsideBlock) = GetAdjacentVoxelWithOverflow(blockSel);

        // Determine target block position
        var targetPos = outsideBlock
            ? blockSel.Position.AddCopy(blockSel.Face)
            : blockSel.Position;

        var be = GetOrCreateCircuitBlock(world, targetPos);
        if (be == null) return false;

        be.SetConductorVoxel(localX, localY, localZ, _selectedMaterial);
        return true;
    }

    /// <summary>
    /// Places a new circuit block and sets the initial voxel.
    /// </summary>
    private void PlaceNewCircuitBlock(IWorldAccessor world, BlockSelection blockSel, EntityAgent byEntity)
    {
        var be = GetOrCreateCircuitBlock(world, blockSel.Position);
        if (be == null) return;

        // Place voxel adjacent to the clicked face (in front of it)
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;
        var (localX, localY, localZ) = VoxelPositionHelper.GetAdjacentVoxel(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
        be.SetConductorVoxel(localX, localY, localZ, _selectedMaterial);
    }

    /// <summary>
    /// Gets the voxel coordinates (0-15) of the voxel whose face was clicked.
    /// This is the voxel "behind" the clicked face.
    /// </summary>
    private (int X, int Y, int Z) GetClickedVoxel(BlockSelection blockSel)
    {
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;
        return VoxelPositionHelper.GetClickedVoxel(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
    }

    /// <summary>
    /// Gets the voxel coordinates for placing adjacent to a clicked face,
    /// and indicates if the position is outside the current block.
    /// If outside, coordinates are wrapped to the adjacent block's local space.
    /// </summary>
    private (int X, int Y, int Z, bool OutsideBlock) GetAdjacentVoxelWithOverflow(BlockSelection blockSel)
    {
        var hitPos = blockSel.HitPosition;
        var face = blockSel.Face;
        return VoxelPositionHelper.GetAdjacentVoxelWithOverflow(
            hitPos.X, hitPos.Y, hitPos.Z,
            face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
    }

    /// <summary>
    /// Gets the voxel position when clicking a solid block to place in an adjacent circuit block.
    /// The voxel should be on the face of the circuit block that touches the solid block.
    /// </summary>
    private (int X, int Y, int Z) GetVoxelPositionOnFace(BlockSelection solidBlockSel)
    {
        var face = solidBlockSel.Face;
        var hit = solidBlockSel.HitPosition;

        // The hit position is on the solid block's face.
        // We need to map this to the adjacent block's coordinate space.
        // The voxel should be on the face of the adjacent block that touches the solid block.
        // That's the OPPOSITE face from where we clicked.

        // Map hit coordinates to the adjacent block's space
        double adjX = face.Axis == EnumAxis.X ? (face.Normali.X > 0 ? 0.0 : 1.0) : hit.X;
        double adjY = face.Axis == EnumAxis.Y ? (face.Normali.Y > 0 ? 0.0 : 1.0) : hit.Y;
        double adjZ = face.Axis == EnumAxis.Z ? (face.Normali.Z > 0 ? 0.0 : 1.0) : hit.Z;

        // Use GetClickedVoxel with the opposite face normal to get the voxel ON the face
        // (not adjacent to it)
        return VoxelPositionHelper.GetClickedVoxel(
            adjX, adjY, adjZ,
            -face.Normalf.X, -face.Normalf.Y, -face.Normalf.Z);
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
        dsc.AppendLine("Right click: Place conductor voxel");
        dsc.AppendLine("Left click: Remove voxel");
    }
}
