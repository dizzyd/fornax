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
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Fornax;

/// <summary>
/// One of the two strapped panels that close the brick kiln's wicket, and the thing that
/// replaces the mud kiln's six seals.
///
/// The mud kiln is sealed by building a wall and unsealed by mining it out: twenty-four clay and
/// six soil a firing, and six blocks to break to get at the batch. A brick kiln does not work
/// that way and never did. Its wicket is a permanent panel on hinges, and what seals it is a
/// bead of clay luted round the joint before every firing - which the fire then burns out, so
/// the panels stand open on their own once the batch is done.
///
/// So the panel is permanent and the luting is the consumable: one clay each, two a firing,
/// against the mud kiln's twenty-four. That is what the copper bought. It is also why the state
/// lives in the block code rather than in a block entity - "luted" and "open" are the whole of
/// what a panel knows, the multiblock check reads them by code like every other position, and a
/// block that needs no entity should not have one.
///
/// Deliberately not a door in the engine's sense. A door swings freely and costs nothing to
/// work, which is what the beehive kiln already is; this one has to be paid for every time it
/// is closed, and that is the entire mechanic.
/// </summary>
public class BlockKilnDoor : Block
{
    /// <summary>First code part of both panel states, which is how they are recognised.</summary>
    public const string CodePart = "kilndoor";

    public const string OpenState = "open";
    public const string LutedState = "luted";

    private const string LuteHelp = "fornax:blockhelp-fornax-lute";
    private const string UnluteHelp = "fornax:blockhelp-fornax-unlute";

    private WorldInteraction[] interactions;

    public bool IsLuted => Variant["state"] == LutedState;

    /// <summary>Whether a block is a wicket panel in the given state, whichever way it faces.</summary>
    public static bool Is(Block block, string state) =>
        block?.Code?.Domain == "fornax"
        && block.Code.FirstCodePart() == CodePart
        && block.Variant["state"] == state;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api.Side != EnumAppSide.Client) return;

        interactions = ObjectCacheUtil.GetOrCreate(api, "fornaxKilnDoorInteractions", () =>
        {
            var clay = new List<ItemStack>();

            foreach (var obj in api.World.Collectibles)
            {
                if (IsClay(obj)) clay.Add(new ItemStack(obj));
            }

            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = LuteHelp,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = clay.ToArray()
                },
                new WorldInteraction
                {
                    ActionLangCode = UnluteHelp,
                    MouseButton = EnumMouseButton.Right
                }
            };
        });
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var held = byPlayer?.InventoryManager?.ActiveHotbarSlot;

        if (!IsLuted)
        {
            if (held == null || held.Empty || !IsClay(held.Itemstack?.Collectible))
            {
                // Held nothing, or nothing that will seal it. Said out loud rather than
                // swallowed: an open panel and a luted one are a texture apart, and a click that
                // has no effect and no message is indistinguishable from a broken block.
                if (world.Api is ICoreClientAPI capi)
                {
                    capi.TriggerIngameError(this, "needsclay", Lang.Get("fornax:wicket-needs-clay"));
                }

                return true;
            }

            return Swap(world, byPlayer, blockSel.Position, LutedState, take: held);
        }

        // Luted, and being opened by hand. The luting is clay packed into a joint - there is
        // nothing to recover, which is exactly why breaking into a hot kiln costs something.
        // The firebox notices the structure has opened on its next tick and applies the thermal
        // shock itself, by the same path as prising a mud seal out.
        return Swap(world, byPlayer, blockSel.Position, OpenState, take: null);
    }

    /// <summary>
    /// Swaps the panel's state and leaves everything else about it alone - the facing above all,
    /// since a panel that turned to face north every time it was luted would swing its way round
    /// the kiln over a few firings.
    /// </summary>
    private bool Swap(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, string toState, ItemSlot take)
    {
        var next = world.GetBlock(CodeWithVariant("state", toState));
        if (next == null || next.Id == Id) return false;

        if (take != null)
        {
            take.TakeOut(1);
            take.MarkDirty();
        }

        world.BlockAccessor.SetBlock(next.Id, pos);
        world.BlockAccessor.MarkBlockDirty(pos);

        world.PlaySoundAt(new AssetLocation(take != null ? "game:sounds/block/dirt1" : "game:sounds/block/ceramicplace"),
            pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, byPlayer, true, 12);

        return true;
    }

    /// <summary>
    /// Any of vanilla's clays. Which one makes no difference to a bead of luting, and refusing
    /// fire clay because it is the expensive one would only teach players to carry two kinds.
    /// </summary>
    private static bool IsClay(CollectibleObject obj) =>
        obj?.Code?.Domain == "game" && obj.Code.FirstCodePart() == "clay";

    /// <summary>
    /// The panel drops and picks as an open one whatever state it was in. Luting is packed into
    /// the joint on the kiln, not carried around in a pocket, so a luted panel taken off the
    /// wall is simply a panel - and one that came back as "luted" could never be luted again.
    /// </summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return new[] { new ItemStack(LooseVariant(world)) };
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return new ItemStack(LooseVariant(world));
    }

    private Block LooseVariant(IWorldAccessor world)
    {
        return world.GetBlock(CodeWithVariants(new Dictionary<string, string>
        {
            { "state", OpenState }, { "side", "north" }
        })) ?? this;
    }

    /// <summary>Only the one gesture that applies: luting an open panel, or opening a luted one.</summary>
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        string wanted = IsLuted ? UnluteHelp : LuteHelp;

        var own = interactions == null
            ? Array.Empty<WorldInteraction>()
            : Array.FindAll(interactions, w => w.ActionLangCode == wanted);

        return own.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
