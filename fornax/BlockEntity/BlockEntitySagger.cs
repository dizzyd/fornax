// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Fornax;

/// <summary>
/// A lidded fireclay box that flint is calcined in.
///
/// Flint already knows what it becomes - vanilla gives it calcined flint at 1000 degrees - but
/// neither kiln will take it: it is not "fire" work and its product is an item, the same two
/// reasons lime was turned away. Lime was let in with a tag. Flint is let in only inside one of
/// these, which leaves vanilla flint exactly as it was: not ground storable, and not kiln work
/// anywhere else - on a kiln shelf, in a pile, or in somebody's chest left on the grate.
///
/// The lid is what makes it a sagger and not a bowl. Only a lidded sagger fires, so the lid is
/// the step that says "this is ready", and in return it keeps its contents through a breach:
/// thermal shock cracking flint is what calcining is for, so there is nothing for it to ruin.
///
/// The lid is the block's "state" variant rather than a field here, so a lidded sagger looks
/// lidded with no renderer of its own, and the swap keeps this entity and its contents.
/// </summary>
public class BlockEntitySagger : BlockEntityContainer
{
    public const string ClassName = "Sagger";

    private static readonly AssetLocation FlintCode = new("game", "flint");

    private readonly InventoryGeneric inventory;

    public BlockEntitySagger()
    {
        inventory = new InventoryGeneric(1, null, null, (_, self) => new ItemSlotSagger(self, this));
    }

    public override InventoryBase Inventory => inventory;

    public override string InventoryClassName => "fornax-sagger";

    public bool Lidded => (Block as BlockSagger)?.IsLidded == true;

    public ItemSlot Slot => inventory[0];

    /// <summary>Flint inside and the lid off: loaded, but not ready to fire.</summary>
    public bool NeedsLid => !Lidded && Takes(Slot.Itemstack);

    /// <summary>
    /// Whether more flint would go in. Not just space: flint does not stack onto calcined flint,
    /// so a sagger that has fired takes nothing until it is emptied.
    /// </summary>
    public bool HasRoomForFlint =>
        Slot.Empty || Takes(Slot.Itemstack) && Slot.GetRemainingSlotSpace(Slot.Itemstack) > 0;

    /// <summary>Whether a sagger takes this. Flint, and nothing else.</summary>
    public static bool Takes(ItemStack stack) => stack?.Collectible?.Code?.Equals(FlintCode) == true;

    /// <summary>
    /// Whether a firing calcines what is in this slot: flint, in a sagger, with the lid on.
    /// The kiln asks this of every slot inside a sagger instead of its usual question.
    /// </summary>
    public static bool Calcines(ItemSlot slot) =>
        slot is ItemSlotSagger sagger && sagger.Owner.Lidded && Takes(slot.Itemstack);

    /// <summary>
    /// Sneak with an empty hand works the lid; otherwise an open sagger takes flint from the
    /// hand, one at a time or the whole stack with Ctrl, and gives its contents back to an
    /// empty one. Worked on the server only; the client says why a click did nothing.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var held = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;

        if (controls.ShiftKey && (held == null || held.Empty))
        {
            SetLid(!Lidded);
            return true;
        }

        if (Lidded)
        {
            TriggerError("lidded", "fornax:sagger-lidded");
            return true;
        }

        if (held != null && !held.Empty)
        {
            if (!Takes(held.Itemstack))
            {
                TriggerError("flintonly", "fornax:sagger-flint-only");
                return true;
            }

            if (!Slot.Empty && !Takes(Slot.Itemstack))
            {
                TriggerError("emptyfirst", "fornax:sagger-empty-it-first");
                return true;
            }

            if (!HasRoomForFlint)
            {
                TriggerError("full", "fornax:sagger-full");
                return true;
            }

            if (Api.Side != EnumAppSide.Server) return true;

            int wanted = controls.CtrlKey ? held.StackSize : 1;
            if (held.TryPutInto(Api.World, Slot, wanted) > 0)
            {
                held.MarkDirty();
                MarkDirty(true);
                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/loosestone2"), Pos, 0);
            }

            return true;
        }

        if (Slot.Empty) return false;
        if (Api.Side != EnumAppSide.Server) return true;

        var stack = Slot.TakeOutWhole();
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
        {
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        Slot.MarkDirty();
        MarkDirty(true);
        return true;
    }

    private void SetLid(bool lidded)
    {
        if (Api.Side != EnumAppSide.Server) return;

        var next = Api.World.GetBlock(Block.CodeWithVariant("state", lidded ? BlockSagger.LiddedState : BlockSagger.OpenState));
        if (next == null || next.Id == Block.Id) return;

        Api.World.BlockAccessor.ExchangeBlock(next.Id, Pos);
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/ceramicplace"), Pos, 0);
    }

    private void TriggerError(string code, string langKey)
    {
        if (Api is ICoreClientAPI capi) capi.TriggerIngameError(this, code, Lang.Get(langKey));
    }

    /// <summary>
    /// Says what is inside even with the lid on: the lid hides it from sight, not from the
    /// player who put it there and wants to know whether it has fired yet.
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (Slot.Empty) dsc.AppendLine(Lang.Get("fornax:sagger-empty"));
        else dsc.AppendLine(Lang.Get("fornax:sagger-contents", Slot.StackSize, Slot.Itemstack.GetName()));

        if (NeedsLid) dsc.AppendLine(Lang.Get("fornax:sagger-needs-lid"));
    }
}

/// <summary>
/// The one slot in a sagger. Takes flint up to <see cref="FornaxConfig.SaggerCapacity"/>, and
/// nothing goes in or out while the lid is on - which holds against hoppers and chutes as well
/// as hands. The kiln writes the calcined flint straight into it, past all of this.
/// </summary>
public class ItemSlotSagger : ItemSlot
{
    public readonly BlockEntitySagger Owner;

    public ItemSlotSagger(InventoryBase inventory, BlockEntitySagger owner) : base(inventory)
    {
        Owner = owner;
    }

    public override int MaxSlotStackSize
    {
        get => FornaxModSystem.Config.SaggerCapacity;
        set { }
    }

    public override bool CanHold(ItemSlot sourceSlot) =>
        !Owner.Lidded && BlockEntitySagger.Takes(sourceSlot?.Itemstack) && base.CanHold(sourceSlot);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) =>
        !Owner.Lidded && BlockEntitySagger.Takes(sourceSlot?.Itemstack) && base.CanTakeFrom(sourceSlot, priority);

    public override bool CanTake() => !Owner.Lidded && base.CanTake();
}
