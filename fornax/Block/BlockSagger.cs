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
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Fornax;

/// <summary>
/// The sagger block. Everything it does is in <see cref="BlockEntitySagger"/>; this hands it the
/// click and keeps a lidded sagger from coming back lidded when it is picked up.
/// </summary>
public class BlockSagger : Block
{
    private const string AddHelp = "fornax:blockhelp-fornax-sagger-add";
    private const string TakeHelp = "fornax:blockhelp-fornax-sagger-take";
    private const string LidOnHelp = "fornax:blockhelp-fornax-sagger-lid-on";
    private const string LidOffHelp = "fornax:blockhelp-fornax-sagger-lid-off";

    public const string OpenState = "open";
    public const string LiddedState = "closed";

    private WorldInteraction[] interactions;

    public bool IsLidded => Variant["state"] == LiddedState;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api.Side != EnumAppSide.Client) return;

        interactions = ObjectCacheUtil.GetOrCreate(api, "fornaxSaggerInteractions", () =>
        {
            var flint = api.World.GetItem(new AssetLocation("game", "flint"));

            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = AddHelp,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = flint == null ? null : new[] { new ItemStack(flint) }
                },
                new WorldInteraction { ActionLangCode = TakeHelp, MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = LidOnHelp, MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" },
                new WorldInteraction { ActionLangCode = LidOffHelp, MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" }
            };
        });
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntitySagger sagger)
        {
            return sagger.OnInteract(byPlayer);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    /// <summary>A lid is not a separate thing to carry, so a sagger always comes back open.</summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return new[] { new ItemStack(OpenVariant(world)) };
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return new ItemStack(OpenVariant(world));
    }

    /// <summary>
    /// Its own name rather than the picked stack's, which is always the open one - and whether
    /// the lid is on is the one thing worth reading off a sagger at a glance.
    /// </summary>
    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        GetHeldItemName(new ItemStack(this));

    private Block OpenVariant(IWorldAccessor world) =>
        world.GetBlock(CodeWithVariant("state", OpenState)) ?? this;

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        if (interactions == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

        var sagger = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntitySagger;
        var wanted = new List<string>();

        if (IsLidded)
        {
            wanted.Add(LidOffHelp);
        }
        else
        {
            if (sagger == null || sagger.HasRoomForFlint) wanted.Add(AddHelp);
            if (sagger != null && !sagger.Slot.Empty) wanted.Add(TakeHelp);
            wanted.Add(LidOnHelp);
        }

        var own = Array.FindAll(interactions, w => wanted.Contains(w.ActionLangCode));
        return own.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
