// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Fornax;

/// <summary>
/// Brain of the updraft kiln. Owns the fuel, the fire, the multiblock check and the wares.
///
/// Geometry, in the block's own frame: <see cref="Orientation"/> points from the firebox into the
/// structure, so the chamber centre column sits two blocks that way. Relative to that column:
///   y+0  combustion chamber (8 cells around a mud brick pillar)
///   y+1  nine grate tiles
///   y+2  the ware level, where ground storage piles sit
///   y+3  flue
///   y+4  roof, with the draft vent at the centre
/// </summary>
public class BlockEntityUpdraftFirebox : BlockEntityContainer, IHeatSource
{
    private static FornaxConfig Cfg => FornaxModSystem.Config;

    private const int WareLevel = 2;
    private const int VentLevel = 5;

    private readonly InventoryGeneric inventory;
    public override InventoryBase Inventory => inventory;
    public override string InventoryClassName => "updraftfirebox";

    /// <summary>Points from the firebox towards the chamber, i.e. the opposite of the mouth.</summary>
    public BlockFacing Orientation { get; private set; } = BlockFacing.NORTH;

    private MultiblockStructure structure;
    private BlockPos centerPos;
    private bool highlighting;

    // --- persisted state -------------------------------------------------
    public bool Lit;
    public bool StructureComplete;

    /// <summary>
    /// A batch has finished and is still sitting on the grate waiting to be taken out. Set the
    /// moment the firing ends, and cleared when the kiln is sealed up again for the next one.
    /// </summary>
    public bool BatchFired;

    /// <summary>Fuel energy already burned towards <see cref="FornaxConfig.FiringEnergyHours"/>.</summary>
    public double FiredEnergyHours;

    /// <summary>Energy already credited to the firing but not yet paid for by removing fuel items.</summary>
    private double burnCredit;

    public float ChamberTemperature;
    private double totalHoursLastUpdate;

    // --- transient -------------------------------------------------------
    private int tickCounter;

    public BlockEntityUpdraftFirebox()
    {
        inventory = new InventoryGeneric(Math.Max(1, Cfg.FuelSlots), null, null);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        structure = Block?.Attributes?["multiblockStructure"]?.AsObject<MultiblockStructure>();
        InitOrientation();

        if (totalHoursLastUpdate <= 0) totalHoursLastUpdate = api.World.Calendar.TotalHours;

        if (api.Side == EnumAppSide.Server)
        {
            RegisterGameTickListener(OnServerTick, 1000);
        }
    }

    private void InitOrientation()
    {
        string side = Block?.Variant?["side"];
        var facing = side == null ? BlockFacing.SOUTH : BlockFacing.FromCode(side) ?? BlockFacing.SOUTH;

        // "side" is the face the block presents to whoever placed it, so the kiln is behind it.
        Orientation = facing.Opposite;

        int rotYDeg = Orientation.Code switch
        {
            "east" => 270,
            "west" => 90,
            "south" => 180,
            _ => 0
        };

        structure?.InitForUse(rotYDeg);

        centerPos = Pos.AddCopy(Orientation.Normali.X * 2, 0, Orientation.Normali.Z * 2);
    }

    // =====================================================================
    //  Ticking
    // =====================================================================

    private void OnServerTick(float dt)
    {
        if (Lit && StructureComplete) BurnNearbyEntities();

        if (++tickCounter % 3 == 0) OnServerTick3s();
    }

    private void OnServerTick3s()
    {
        if (structure == null || centerPos == null) return;
        if (!AreaLoaded()) return;   // don't advance the clock, so nothing is lost

        double now = Api.World.Calendar.TotalHours;
        double hoursPassed = Math.Max(0, now - totalHoursLastUpdate);
        totalHoursLastUpdate = now;

        TendChamber();

        bool wasComplete = StructureComplete;
        StructureComplete = structure.InCompleteBlockCount(Api.World, Pos) == 0;

        bool dirty = wasComplete != StructureComplete;

        // Sealed up again: the batch has been dealt with, whatever the player did with it.
        if (StructureComplete && BatchFired)
        {
            BatchFired = false;
            dirty = true;
        }

        if (!Lit)
        {
            if (CoolChamber(hoursPassed)) dirty = true;
            if (dirty) MarkDirty();
            return;
        }

        // Someone opened the kiln mid-firing.
        if (wasComplete && !StructureComplete)
        {
            OnBreached();
            dirty = true;
        }

        if (!StructureComplete)
        {
            // Paused: the fuel stops being drawn on too, so nothing is wasted while it stands open.
            if (CoolChamber(hoursPassed)) dirty = true;
            if (dirty) MarkDirty();
            return;
        }

        SurveyFuel(out double fuelEnergy, out int hottestFuel);
        double available = fuelEnergy - burnCredit;

        if (available <= 0)
        {
            Lit = false;
            ApplyLitAppearance();
            CoolChamber(hoursPassed);
            MarkDirty();
            return;
        }

        double rate = Cfg.NominalBurnRate * FuelRateMultiplier(hottestFuel);
        double energy = Math.Min(available, hoursPassed * rate);

        if (energy > 0)
        {
            burnCredit += energy;
            FiredEnergyHours += energy;
            PayForBurnedFuel();
            dirty = true;
        }

        float target = Math.Min(hottestFuel + Cfg.DraftTemperatureBonus, Cfg.ChamberMaxTemperature);
        if (HeatChamber(hoursPassed, target)) dirty = true;

        // warming -> firing as it crosses the threshold. SwapVariant no-ops when the
        // variant already matches, so this is cheap to call on every tick.
        ApplyLitAppearance();

        ApplyChamberTemperatureToWares();

        if (FiredEnergyHours >= Cfg.FiringEnergyHours)
        {
            FinishFiring();
            dirty = true;
        }

        if (dirty) MarkDirty();
    }

    /// <summary>The whole 5x5 footprint must be loaded before we advance anything.</summary>
    private bool AreaLoaded()
    {
        if (Api.Side == EnumAppSide.Client) return true;

        var probe = new BlockPos(Pos.dimension);
        for (int dx = -2; dx <= 2; dx += 4)
        {
            for (int dz = -2; dz <= 2; dz += 4)
            {
                probe.Set(centerPos.X + dx, centerPos.Y + WareLevel, centerPos.Z + dz);
                if (Api.World.BlockAccessor.GetChunkAtBlockPos(probe) == null) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Keeps the combustion chamber empty, and eventually stops it filling itself back up.
    ///
    /// The chamber has to be air, and a kiln built on grass or dirt has soil sitting directly
    /// under those cells. Soil sprouts tall grass into whatever air is above it - a lit firebox
    /// even helps, since the light it throws is what soil looks for before it grows - so a kiln
    /// that was complete yesterday quietly stops being complete, mid-firing, with the offending
    /// block hidden under the grate where nobody would think to look.
    ///
    /// So two things happen here, before the structure is judged. Whatever the ground has grown
    /// into the chamber is cleared, so it never counts against the check; and once the chamber
    /// reaches firing heat the soil under it bakes to packed dirt, which is not soil and grows
    /// nothing, so a kiln that has been fired once is done with this for good. Only plants and
    /// soil are touched - a floor someone laid deliberately stays exactly as they laid it.
    /// </summary>
    private void TendChamber()
    {
        // The temperature the firebox starts looking like it is firing at, reused: if it looks
        // like a kiln from outside, the ground inside has had a kiln's worth of heat on it.
        bool baking = ChamberTemperature >= Cfg.FiringGlowTemperature;
        Block packedDirt = baking ? Api.World.GetBlock(new AssetLocation("game", "packeddirt")) : null;

        var pos = new BlockPos(Pos.dimension);

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;   // the mud brick pillar the grate rests on

                pos.Set(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);

                var block = Api.World.BlockAccessor.GetBlock(pos);

                // No drops: they would fall into a sealed chamber and burn there anyway.
                if (block.Id != 0 && block.BlockMaterial == EnumBlockMaterial.Plant)
                {
                    Api.World.BlockAccessor.SetBlock(0, pos);
                }

                if (packedDirt == null) continue;

                // BlockSoil is the class that does the growing, and the only one worth baking.
                // Mud brick and cob are EnumBlockMaterial.Soil too, so material is no guide here.
                pos.Down();
                if (Api.World.BlockAccessor.GetBlock(pos) is BlockSoil)
                {
                    Api.World.BlockAccessor.SetBlock(packedDirt.Id, pos);
                }
            }
        }
    }

    private bool HeatChamber(double hoursPassed, float target)
    {
        if (ChamberTemperature >= target) return false;

        float before = ChamberTemperature;
        ChamberTemperature = Math.Min(target, ChamberTemperature + (float)(hoursPassed * Cfg.ChamberHeatingPerHour));
        return Math.Abs(ChamberTemperature - before) > 0.01f;
    }

    private bool CoolChamber(double hoursPassed)
    {
        if (ChamberTemperature <= Cfg.AmbientTemperature) return false;

        float before = ChamberTemperature;
        ChamberTemperature = Math.Max(Cfg.AmbientTemperature, ChamberTemperature - (float)(hoursPassed * Cfg.ChamberCoolingPerHour));
        return Math.Abs(ChamberTemperature - before) > 0.01f;
    }

    // =====================================================================
    //  Fuel
    // =====================================================================

    public bool IsValidFuel(ItemStack stack)
    {
        if (stack?.Collectible == null) return false;

        var props = stack.Collectible.CombustibleProps;
        return props != null
               && props.BurnDuration > 0
               && props.BurnTemperature >= Cfg.MinFuelBurnTemperature;
    }

    private double FuelEnergyPerItem(ItemStack stack)
    {
        var props = stack?.Collectible?.CombustibleProps;
        if (props == null || props.BurnDuration <= 0) return 0;

        return props.BurnDuration / Cfg.BurnDurationPerFuelHour;
    }

    private void SurveyFuel(out double totalEnergy, out int hottestBurnTemperature)
    {
        totalEnergy = 0;
        hottestBurnTemperature = 0;

        foreach (var slot in inventory)
        {
            if (slot.Empty || !IsValidFuel(slot.Itemstack)) continue;

            var props = slot.Itemstack.Collectible.CombustibleProps;
            totalEnergy += slot.Itemstack.StackSize * (props.BurnDuration / Cfg.BurnDurationPerFuelHour);
            hottestBurnTemperature = Math.Max(hottestBurnTemperature, props.BurnTemperature);
        }
    }

    private float FuelRateMultiplier(int burnTemperature)
    {
        if (burnTemperature >= Cfg.HotFuelTemperature) return Cfg.HotFuelRate;
        if (burnTemperature >= Cfg.WarmFuelTemperature) return Cfg.WarmFuelRate;
        return Cfg.CoolFuelRate;
    }

    /// <summary>Removes whole fuel items once enough energy has been credited to pay for them.</summary>
    private void PayForBurnedFuel()
    {
        while (burnCredit > 0)
        {
            ItemSlot slot = null;
            foreach (var candidate in inventory)
            {
                if (!candidate.Empty && IsValidFuel(candidate.Itemstack)) { slot = candidate; break; }
            }

            if (slot == null) { burnCredit = 0; return; }

            double perItem = FuelEnergyPerItem(slot.Itemstack);
            if (perItem <= 0 || burnCredit < perItem) return;

            slot.TakeOut(1);
            slot.MarkDirty();
            burnCredit -= perItem;
        }
    }

    public double FuelEnergyRemaining()
    {
        SurveyFuel(out double energy, out _);
        return Math.Max(0, energy - burnCredit);
    }

    public int FuelItemCount()
    {
        int count = 0;
        foreach (var slot in inventory)
        {
            if (!slot.Empty && IsValidFuel(slot.Itemstack)) count += slot.Itemstack.StackSize;
        }

        return count;
    }

    /// <summary>In-game hours the fuel currently loaded would burn for, at the rate it burns at.</summary>
    public double EstimatedBurnHours()
    {
        SurveyFuel(out double energy, out int hottest);
        double remaining = Math.Max(0, energy - burnCredit);
        double rate = Cfg.NominalBurnRate * FuelRateMultiplier(hottest);

        return rate <= 0 ? 0 : remaining / rate;
    }

    // =====================================================================
    //  Wares
    // =====================================================================

    private bool IsFireable(ItemStack stack)
    {
        if (stack?.Collectible == null) return false;

        var props = stack.Collectible.GetCombustibleProperties(Api.World, stack, null);
        if (props == null) return false;

        if (props.SmeltingType == EnumSmeltType.Fire) return true;

        return props.SmeltedStack?.ResolvedItemstack?.Block?.BlockMaterial == EnumBlockMaterial.Ceramic;
    }

    /// <summary>Every occupied ground storage slot on the grate, fireable or not.</summary>
    private void WalkGrate(Action<BlockEntityGroundStorage, ItemSlot> onSlot)
    {
        if (centerPos == null) return;

        var pos = new BlockPos(Pos.dimension);

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                pos.Set(centerPos.X + dx, centerPos.Y + WareLevel, centerPos.Z + dz);

                if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage storage) continue;

                for (int i = 0; i < storage.Inventory.Count; i++)
                {
                    var slot = storage.Inventory[i];
                    if (slot.Empty) continue;

                    onSlot(storage, slot);
                }
            }
        }
    }

    /// <summary>The green wares: the ones a firing still has work to do on.</summary>
    private void WalkWares(Action<BlockEntityGroundStorage, ItemSlot> onWare)
    {
        WalkGrate((storage, slot) =>
        {
            if (IsFireable(slot.Itemstack)) onWare(storage, slot);
        });
    }

    public int CountWares()
    {
        int total = 0;
        WalkWares((_, slot) => total += slot.Itemstack.StackSize);
        return total;
    }

    /// <summary>
    /// What is on the grate that a firing has nothing left to do with - which, on a kiln that
    /// has just finished one, is the batch itself. Fired pottery has no combustible properties
    /// any more, so it stops being counted as wares at the exact moment the player most wants
    /// to be told it is there.
    /// </summary>
    public int CountFinishedWares()
    {
        int total = 0;
        WalkGrate((_, slot) =>
        {
            if (!IsFireable(slot.Itemstack)) total += slot.Itemstack.StackSize;
        });

        return total;
    }

    private void ApplyChamberTemperatureToWares()
    {
        WalkWares((storage, slot) =>
        {
            slot.Itemstack.Collectible.SetTemperature(Api.World, slot.Itemstack, ChamberTemperature);
            slot.MarkDirty();
            storage.MarkDirty();
        });
    }

    private void FinishFiring()
    {
        WalkWares((storage, slot) =>
        {
            var raw = slot.Itemstack;
            var props = raw.Collectible.GetCombustibleProperties(Api.World, raw, null);
            var fired = props?.SmeltedStack?.ResolvedItemstack;
            if (fired == null) return;

            float temperature = raw.Collectible.GetTemperature(Api.World, raw);
            int ratio = Math.Max(1, props.SmeltedRatio);

            storage.forceStorageProps = true;
            slot.Itemstack = fired.Clone();
            slot.Itemstack.StackSize = Math.Max(1, raw.StackSize / ratio);
            slot.Itemstack.Collectible.SetTemperature(Api.World, slot.Itemstack, temperature);
            slot.MarkDirty();
            storage.MarkDirty(true);
        });

        Lit = false;
        FiredEnergyHours = 0;
        burnCredit = 0;
        BatchFired = true;
        ApplyLitAppearance();

        CrackSeals();

        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/ceramicplace"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, false, 24);
    }

    /// <summary>Every firing destroys the mud seals; the entrance must be re-sealed for the next batch.</summary>
    private void CrackSeals()
    {
        var cracked = Api.World.GetBlock(new AssetLocation("fornax", "kilnseal-cracked"));
        if (cracked == null) return;

        structure.WalkMatchingBlocks(Api.World, Pos, (block, pos) =>
        {
            if (block.Code?.Domain == "fornax" && block.Code.Path == "kilnseal-intact")
            {
                Api.World.BlockAccessor.SetBlock(cracked.Id, pos);
            }
        });

        StructureComplete = false;
    }

    /// <summary>
    /// Cold air hitting hot ceramics cracks them. Below the safe temperature nothing is lost at all;
    /// from there the per-ware chance ramps to <see cref="FornaxConfig.MaxShatterChance"/>.
    /// Whatever survives keeps the heat it has already taken, so re-sealing resumes the firing.
    /// </summary>
    private void OnBreached()
    {
        if (ChamberTemperature < Cfg.ShatterSafeTemperature) return;

        float span = Math.Max(1, Cfg.ShatterPeakTemperature - Cfg.ShatterSafeTemperature);
        float chance = GameMath.Clamp((ChamberTemperature - Cfg.ShatterSafeTemperature) / span, 0, 1) * Cfg.MaxShatterChance;
        if (chance <= 0) return;

        int shattered = 0;

        WalkWares((storage, slot) =>
        {
            int lost = 0;
            for (int i = 0; i < slot.Itemstack.StackSize; i++)
            {
                if (Api.World.Rand.NextDouble() < chance) lost++;
            }

            if (lost <= 0) return;

            shattered += lost;

            if (lost >= slot.Itemstack.StackSize) slot.Itemstack = null;
            else slot.Itemstack.StackSize -= lost;

            slot.MarkDirty();
            storage.MarkDirty(true);
        });

        if (shattered > 0)
        {
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/ceramicbreak"),
                centerPos.X + 0.5, centerPos.Y + WareLevel, centerPos.Z + 0.5, null, false, 24);
        }
    }

    // =====================================================================
    //  Interaction
    // =====================================================================

    public bool CanIgnite => !Lit && StructureComplete && FuelEnergyRemaining() > 0;

    /// <summary>
    /// Says why the kiln refused to light. Without this a firestarter on an incomplete or
    /// unfuelled kiln simply does nothing at all, with no message and no clue which of the
    /// 134 structure positions is wrong - which is indistinguishable from a broken mod.
    /// </summary>
    public void ExplainWhyItWontLight(EntityAgent byEntity)
    {
        if (Api is not ICoreClientAPI capi) return;

        if (Lit)
        {
            capi.TriggerIngameError(this, "alreadylit", Lang.Get("fornax:already-lit"));
            return;
        }

        int missing = structure?.InCompleteBlockCount(Api.World, Pos) ?? 0;
        if (missing > 0)
        {
            capi.TriggerIngameError(this, "incomplete", Lang.Get("fornax:cant-light-incomplete", missing));

            var player = (byEntity as EntityPlayer)?.Player;
            if (player != null && structure != null)
            {
                structure.HighlightIncompleteParts(Api.World, player, Pos);
                highlighting = true;
            }

            return;
        }

        if (FuelEnergyRemaining() <= 0)
        {
            capi.TriggerIngameError(this, "nofuel", Lang.Get("fornax:cant-light-nofuel"));
        }
    }

    public void TryIgnite(EntityPlayer byEntity)
    {
        if (!CanIgnite) return;

        Lit = true;
        totalHoursLastUpdate = Api.World.Calendar.TotalHours;
        ClearHighlights();
        ApplyLitAppearance();
        MarkDirty(true);
    }

    /// <summary>
    /// Points the firebox and the draft vent at their "lit" variants and back again.
    ///
    /// The flames live inside a sealed chamber where nothing can see them, so a lit kiln has
    /// to advertise itself from the outside: the lit firebox emits light, glows at the mouth
    /// and sheds embers, and the drafting vent carries the plume. All of that is declared on
    /// the blocktypes, so the engine spawns and culls it rather than a tick listener here.
    /// </summary>
    public void RefreshAppearance() => ApplyLitAppearance();

    private void ApplyLitAppearance()
    {
        if (Api?.Side != EnumAppSide.Server) return;

        // cold -> nothing in the box, ready -> fuelled and waiting,
        // warming -> lit but the chamber is still coming up, firing -> up to temperature
        string state;
        if (!Lit) state = FuelEnergyRemaining() > 0 ? "ready" : "cold";
        else state = ChamberTemperature >= Cfg.FiringGlowTemperature ? "firing" : "warming";
        SwapVariant(Pos, "state", state, exchange: true);

        if (centerPos != null)
        {
            SwapVariant(centerPos.UpCopy(VentLevel), "state", Lit ? "drafting" : "idle", exchange: false);
        }
    }

    private void SwapVariant(BlockPos pos, string group, string state, bool exchange)
    {
        var current = Api.World.BlockAccessor.GetBlock(pos);
        if (current?.Code == null || current.Variant[group] == state) return;
        if (current.Code.Domain != "fornax") return;

        var next = Api.World.GetBlock(current.CodeWithVariant(group, state));
        if (next == null || next.Id == current.Id) return;

        // ExchangeBlock keeps the block entity; SetBlock would destroy and rebuild it,
        // taking the fuel and the firing progress with it.
        if (exchange) Api.World.BlockAccessor.ExchangeBlock(next.Id, pos);
        else Api.World.BlockAccessor.SetBlock(next.Id, pos);

        Api.World.BlockAccessor.MarkBlockDirty(pos);
    }

    public bool OnPlayerInteract(IPlayer byPlayer)
    {
        var hotbar = byPlayer.InventoryManager.ActiveHotbarSlot;
        bool sneaking = byPlayer.WorldData.EntityControls.ShiftKey;

        // Ctrl + right-click with an empty hand: show or hide the build guide. Same gesture
        // the beehive kiln uses on its door, so it is one thing to learn rather than two.
        if (hotbar.Empty && byPlayer.Entity.Controls.CtrlKey)
        {
            ToggleBuildGuide(byPlayer);
            return true;
        }

        if (!hotbar.Empty && IsValidFuel(hotbar.Itemstack))
        {
            if (Lit)
            {
                // Sealed and burning: the firebox is not reachable until the batch is done.
                // Swallow the click rather than letting it fall through to something else.
                TriggerError("firing", "fornax:cant-refuel-while-firing");
                return true;
            }

            int wanted = byPlayer.Entity.Controls.CtrlKey ? hotbar.StackSize : 1;
            int moved = 0;

            foreach (var slot in inventory)
            {
                if (wanted - moved <= 0) break;
                moved += hotbar.TryPutInto(Api.World, slot, wanted - moved);
            }

            if (moved > 0)
            {
                hotbar.MarkDirty();
                ApplyLitAppearance();
                MarkDirty(true);
                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/dirt1"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 12);
                return true;
            }

            return false;
        }

        if (!hotbar.Empty) return false;   // let firestarters and torches through to the ignite path

        if (sneaking)
        {
            if (Lit)
            {
                TriggerError("firing", "fornax:cant-unload-while-firing");
                return true;
            }

            foreach (var slot in inventory)
            {
                if (slot.Empty) continue;

                if (!byPlayer.InventoryManager.TryGiveItemstack(slot.Itemstack, true))
                {
                    Api.World.SpawnItemEntity(slot.Itemstack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                slot.Itemstack = null;
                slot.MarkDirty();
                ApplyLitAppearance();
                MarkDirty(true);
                return true;
            }

            return false;
        }

        ShowStructureProblems(byPlayer);
        return true;
    }

    // =====================================================================
    //  Build guide
    // =====================================================================

    /// <summary>
    /// Every structure position that is not yet what it should be, with a colour keyed to the
    /// block that belongs there. Computed here rather than through
    /// MultiblockStructure.HighlightIncompleteParts so the positions can be asserted in a test
    /// and so obstructions read differently from things simply not built yet.
    /// </summary>
    public List<BlockPos> MissingStructurePositions(List<int> colors = null, List<Block> wantedBlocks = null)
    {
        var missing = new List<BlockPos>();
        if (structure?.TransformedOffsets == null) return missing;

        var codeByNumber = new Dictionary<int, AssetLocation>();
        foreach (var pair in structure.BlockNumbers) codeByNumber[pair.Value] = pair.Key;

        foreach (var offset in structure.TransformedOffsets)
        {
            var at = new BlockPos(Pos.X + offset.X, Pos.Y + offset.Y, Pos.Z + offset.Z, Pos.dimension);
            if (!codeByNumber.TryGetValue(offset.W, out var wanted)) continue;

            var have = Api.World.BlockAccessor.GetBlock(at);
            if (WildcardUtil.Match(wanted, have.Code)) continue;

            missing.Add(at);
            colors?.Add(GuideColor(wanted, have));
            wantedBlocks?.Add(ResolveWanted(wanted));
        }

        return missing;
    }

    /// <summary>
    /// How many structure positions are wrong, and how many of those are cracked mud seals.
    /// Every firing cracks the six seals across the loading entrance, so on a kiln that has
    /// just finished a batch those are the whole of what is "wrong" with it - which is worth
    /// telling apart from a kiln somebody has knocked a hole in.
    /// </summary>
    public int CountMissing(out int crackedSeals)
    {
        crackedSeals = 0;

        var missing = MissingStructurePositions();
        foreach (var at in missing)
        {
            var have = Api.World.BlockAccessor.GetBlock(at);
            if (have.Code?.Domain == "fornax" && have.Code.Path == "kilnseal-cracked") crackedSeals++;
        }

        return missing.Count;
    }

    /// <summary>
    /// A concrete block to draw for a wildcard requirement. The patterns accept several blocks
    /// - any mud brick, cob, any firebox facing - so the guide has to pick one to show, and it
    /// should be one that actually exists rather than the pattern itself.
    /// </summary>
    private Block ResolveWanted(AssetLocation wanted)
    {
        string path = wanted.Path;

        if (path.StartsWith("air") || path.Contains("groundstorage")) return null;
        if (path.Contains("mudbrick") || path.Contains("cob")) return Api.World.GetBlock(new AssetLocation("game", "mudbrick-dark"));
        if (path.Contains("kilnfirebox")) return Api.World.GetBlock(new AssetLocation("fornax", "kilnfirebox-cold-north"));
        if (path.Contains("kilnvent")) return Api.World.GetBlock(new AssetLocation("fornax", "kilnvent-idle"));
        if (path.Contains("kilnseal")) return Api.World.GetBlock(new AssetLocation("fornax", "kilnseal-intact"));

        // no wildcard: it names one block
        var exact = Api.World.GetBlock(wanted);
        if (exact != null) return exact;

        var found = Api.World.SearchBlocks(wanted);
        return found != null && found.Length > 0 ? found[0] : null;
    }

    private static int GuideColor(AssetLocation wanted, Block have)
    {
        string path = wanted.Path;

        // Kept deliberately faint: the ghost block underneath carries the texture, and this
        // is only a hint at which is which. An obstruction is the exception - that one needs
        // to shout, because it is the only case where you have to remove something.
        if ((path.StartsWith("air") || path.Contains("groundstorage")) && have.Id != 0)
            return ColorUtil.ColorFromRgba(215, 70, 70, 130);

        if (path.Contains("kilngrate")) return ColorUtil.ColorFromRgba(150, 200, 225, 55);
        if (path.Contains("kilnseal")) return ColorUtil.ColorFromRgba(90, 130, 210, 55);
        if (path.Contains("kilnvent")) return ColorUtil.ColorFromRgba(140, 140, 160, 55);
        if (path.Contains("kilnfirebox")) return ColorUtil.ColorFromRgba(240, 150, 60, 70);
        if (path.Contains("mudbrick") || path.Contains("cob")) return ColorUtil.ColorFromRgba(120, 210, 120, 45);

        return ColorUtil.ColorFromRgba(200, 200, 200, 45);
    }

    /// <summary>Ctrl + right-click puts a ghost of the whole kiln up, and takes it down again.</summary>
    public void ToggleBuildGuide(IPlayer byPlayer)
    {
        if (Api is not ICoreClientAPI capi) return;

        if (highlighting)
        {
            structure?.ClearHighlights(Api.World, byPlayer);
            FornaxModSystem.GhostRenderer?.Clear();
            highlighting = false;
            return;
        }

        var colors = new List<int>();
        var wanted = new List<Block>();
        var missing = MissingStructurePositions(colors, wanted);

        if (missing.Count == 0)
        {
            capi.TriggerIngameError(this, "complete", Lang.Get("fornax:guide-complete"));
            return;
        }

        // Two layers: the ghost blocks say WHICH block goes where, the tinted highlight makes
        // them easy to pick out at a glance and is the only thing that can flag an obstruction.
        FornaxModSystem.GhostRenderer?.ShowGhosts(Pos, missing, wanted);
        Api.World.HighlightBlocks(byPlayer, MultiblockStructure.HighlightSlotId, missing, colors);

        highlighting = true;
        capi.TriggerIngameError(this, "guide", Lang.Get("fornax:guide-shown", missing.Count));
    }

    private void TriggerError(string code, string langKey)
    {
        if (Api is ICoreClientAPI capi) capi.TriggerIngameError(this, code, Lang.Get(langKey));
    }

    private void ShowStructureProblems(IPlayer byPlayer)
    {
        if (Api is not ICoreClientAPI capi || structure == null) return;

        int missing = CountMissing(out int crackedSeals);

        if (missing > 0)
        {
            // Nothing wrong but the seals the firing cracked: that is a finished kiln, not a
            // broken one. Still highlight them, since they are what has to be broken to unload.
            capi.TriggerIngameError(this, BatchFired && missing == crackedSeals ? "done" : "incomplete",
                BatchFired && missing == crackedSeals
                    ? Lang.Get("fornax:firing-done")
                    : Lang.Get("fornax:structure-incomplete", missing));

            structure.HighlightIncompleteParts(Api.World, byPlayer, Pos);
            highlighting = true;
            return;
        }

        if (highlighting)
        {
            structure.ClearHighlights(Api.World, byPlayer);
            highlighting = false;
        }
    }

    private void BurnNearbyEntities()
    {
        var mouth = Pos.ToVec3d().Add(0.5, 0.5, 0.5)
            .Add(Orientation.Opposite.Normali.X * 0.6, 0, Orientation.Opposite.Normali.Z * 0.6);

        var entities = Api.World.GetEntitiesAround(mouth, (float)Cfg.FireboxBurnRadius, 2f, e => e.Alive && e is EntityAgent);

        foreach (var entity in entities)
        {
            entity.ReceiveDamage(new DamageSource
            {
                DamageTier = 1,
                SourcePos = mouth,
                SourceBlock = Block,
                Type = EnumDamageType.Fire
            }, Cfg.FireboxBurnDamage);
        }
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return Lit && StructureComplete ? Cfg.HeatSourceStrength : 0;
    }

    // =====================================================================
    //  Persistence & info
    // =====================================================================

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        Lit = tree.GetBool("lit");
        StructureComplete = tree.GetBool("structureComplete");
        BatchFired = tree.GetBool("batchFired");
        FiredEnergyHours = tree.GetDouble("firedEnergyHours");
        burnCredit = tree.GetDouble("burnCredit");
        ChamberTemperature = tree.GetFloat("chamberTemperature");
        totalHoursLastUpdate = tree.GetDouble("totalHoursLastUpdate");
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBool("lit", Lit);
        tree.SetBool("structureComplete", StructureComplete);
        tree.SetBool("batchFired", BatchFired);
        tree.SetDouble("firedEnergyHours", FiredEnergyHours);
        tree.SetDouble("burnCredit", burnCredit);
        tree.SetFloat("chamberTemperature", ChamberTemperature);
        tree.SetDouble("totalHoursLastUpdate", totalHoursLastUpdate);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (structure == null) return;

        int missing = CountMissing(out int crackedSeals);

        // A finished batch cracks every seal, so the kiln calls itself broken at the exact
        // moment it has done its job. Lead with what actually happened, and only add the raw
        // count when something other than the seals is wrong with it too.
        if (BatchFired)
        {
            dsc.AppendLine(Lang.Get("fornax:firing-done"));
            if (missing > crackedSeals) dsc.AppendLine(Lang.Get("fornax:structure-incomplete", missing));
        }
        else if (missing > 0)
        {
            dsc.AppendLine(Lang.Get("fornax:structure-incomplete", missing));
        }
        else
        {
            dsc.AppendLine(Lang.Get("fornax:structure-complete"));
        }

        int wares = CountWares();
        int finished = BatchFired ? CountFinishedWares() : 0;

        if (wares > 0) dsc.AppendLine(Lang.Get("fornax:wares-loaded", wares));
        if (finished > 0) dsc.AppendLine(Lang.Get("fornax:fired-wares", finished));
        if (wares == 0 && finished == 0) dsc.AppendLine(Lang.Get("fornax:no-wares"));

        int fuel = FuelItemCount();
        if (fuel > 0)
        {
            dsc.AppendLine(Lang.Get("fornax:fuel-remaining", fuel, EstimatedBurnHours()));

            // Only worth saying something when the fuel loaded will not see the batch out -
            // and not at all on a kiln still holding a finished one, where a shortfall for the
            // next firing reads as something having gone wrong with the last.
            double needed = Math.Max(0, Cfg.FiringEnergyHours - FiredEnergyHours);
            double have = FuelEnergyRemaining();
            if (have < needed && !BatchFired)
            {
                dsc.AppendLine(Lang.Get("fornax:fuel-short", (int)Math.Round(100 * have / Math.Max(0.001, needed))));
            }
        }
        else
        {
            dsc.AppendLine(Lang.Get("fornax:fuel-none"));
        }

        if (Lit)
        {
            dsc.AppendLine(Lang.Get("fornax:lit", FiredEnergyHours, Cfg.FiringEnergyHours));
            dsc.AppendLine(Lang.Get("fornax:chamber-temp", ChamberTemperature));

            if (ChamberTemperature >= Cfg.ShatterSafeTemperature)
            {
                dsc.AppendLine(Lang.Get("fornax:chamber-hot-warning"));
            }
        }
        else if (missing == 0 && fuel > 0)
        {
            dsc.AppendLine(Lang.Get("fornax:not-lit"));
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        ClearHighlights();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        ClearHighlights();
    }

    private void ClearHighlights()
    {
        if (highlighting && Api is ICoreClientAPI capi)
        {
            structure?.ClearHighlights(Api.World, capi.World.Player);
            FornaxModSystem.GhostRenderer?.Clear();
            highlighting = false;
        }
    }
}
