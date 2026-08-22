// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Fornax;

/// <summary>
/// The firebox is the controller for the whole kiln: it owns the multiblock definition, the fuel,
/// and the fire. Everything else in the structure is inert scenery that the multiblock check reads.
/// </summary>
public class BlockUpdraftFirebox : Block, IIgnitable
{
    private WorldInteraction[] interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api.Side != EnumAppSide.Client) return;

        interactions = ObjectCacheUtil.GetOrCreate(api, "updraftFireboxInteractions", () =>
        {
            var fuelStacks = new List<ItemStack>();

            foreach (var obj in api.World.Collectibles)
            {
                if (obj.Code == null) continue;

                var props = obj.CombustibleProps;
                if (props == null || props.BurnDuration <= 0) continue;
                if (props.BurnTemperature < FornaxModSystem.Config.MinFuelBurnTemperature) continue;

                fuelStacks.Add(new ItemStack(obj));
            }

            var igniteStacks = BlockBehaviorCanIgnite.CanIgniteStacks(api, true);

            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "fornax:blockhelp-fornax-addfuel",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = fuelStacks.ToArray()
                },
                new WorldInteraction
                {
                    ActionLangCode = "fornax:blockhelp-fornax-addfuel-bulk",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "ctrl",
                    Itemstacks = fuelStacks.ToArray()
                },
                new WorldInteraction
                {
                    ActionLangCode = "fornax:blockhelp-fornax-takefuel",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "shift"
                },
                new WorldInteraction
                {
                    ActionLangCode = "fornax:blockhelp-fornax-guide",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "ctrl"
                },
                new WorldInteraction
                {
                    ActionLangCode = "fornax:blockhelp-fornax-ignite",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "shift",
                    Itemstacks = igniteStacks.ToArray()
                }
            };
        });
    }

    /// <summary>
    /// HorizontalOrientable points "side" away from whoever places the block. For a chest that
    /// is fine; for this it means the firebox mouth - and with it the whole kiln's front, its
    /// loading entrance and everything you interact with - ends up facing away from you.
    ///
    /// So placement is taken over here and the mouth is pointed at the player instead. The
    /// behaviour is left on the block for its rotation helpers, which schematics use.
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        if (!CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        BlockFacing mouth = FacingTowardsPlayer(byPlayer, blockSel.Position);
        Block oriented = world.BlockAccessor.GetBlock(CodeWithVariant("side", mouth.Code)) ?? this;
        oriented.DoPlaceBlock(world, byPlayer, blockSel, itemstack);

        return true;
    }

    private static BlockFacing FacingTowardsPlayer(IPlayer byPlayer, BlockPos pos)
    {
        var eye = byPlayer.Entity.Pos.XYZ.Add(byPlayer.Entity.LocalEyePos);
        return FacingTowards(eye.X, eye.Z, pos);
    }

    /// <summary>
    /// Which face of the block at <paramref name="pos"/> an observer at the given world x/z is
    /// looking at. Separated out so every quadrant can be checked directly - driving four real
    /// placements through the input system is at the mercy of where the aim ray happens to land.
    /// </summary>
    public static BlockFacing FacingTowards(double eyeX, double eyeZ, BlockPos pos)
    {
        double dx = eyeX - (pos.X + 0.5);
        double dz = eyeZ - (pos.Z + 0.5);

        if (Math.Abs(dx) > Math.Abs(dz)) return dx > 0 ? BlockFacing.EAST : BlockFacing.WEST;
        return dz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityUpdraftFirebox be)
        {
            return be.OnPlayerInteract(byPlayer);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return (interactions ?? System.Array.Empty<WorldInteraction>())
            .Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        var be = byEntity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityUpdraftFirebox;

        if (be == null || !be.CanIgnite)
        {
            // ItemFirestarter probes at secondsIgniting 0 and gives up unless it gets back
            // exactly Ignitable, so this is the only chance to tell the player anything.
            if (secondsIgniting <= 0) be?.ExplainWhyItWontLight(byEntity);
            return EnumIgniteState.NotIgnitablePreventDefault;
        }

        return secondsIgniting >= 1.5f ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (secondsIgniting < 1.45f) return;

        handling = EnumHandling.PreventDefault;

        if (byEntity.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityUpdraftFirebox be)
        {
            be.TryIgnite(byEntity as EntityPlayer);
        }
    }

    /// <summary>
    /// HorizontalOrientable hands back whatever variant is in the world, which after a firing
    /// is the lit one - a block that emits light and sheds embers sitting in your inventory.
    /// </summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return new[] { new ItemStack(ColdVariant(world)) };
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return new ItemStack(ColdVariant(world));
    }

    private Block ColdVariant(IWorldAccessor world)
    {
        return world.GetBlock(CodeWithVariants(new System.Collections.Generic.Dictionary<string, string>
        {
            { "state", "cold" }, { "side", "north" }
        })) ?? this;
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting)
    {
        // Lighting a torch off a burning firebox.
        var be = byEntity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityUpdraftFirebox;
        if (be == null || !be.Lit) return EnumIgniteState.NotIgnitable;

        return secondsIgniting > 2 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }
}
