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

    /// <summary>The structure's block numbers the other way round, which InitForUse keeps private.</summary>
    private Dictionary<int, AssetLocation> codeByNumber;

    private BlockPos centerPos;

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

    /// <summary>
    /// Who struck the firestarter, so the batch can be credited to them when it comes out hours
    /// later - see <see cref="XSkillsPottery"/>.
    ///
    /// Persisted, because a firing outlives the session that started it. Kept as a UID rather
    /// than a player: resolving it at ignition would hold a reference to someone who may well
    /// have logged out by the time the kiln finishes, and PlayerByUid returning null then is
    /// exactly the answer wanted - nobody is here to be told.
    ///
    /// The vanilla kilns have no ignition of their own to hang this on, so XSkills stamps its
    /// owner on whoever last touched them. This kiln is asked to be lit, by somebody, which is
    /// a better answer than the one it would have had to guess.
    /// </summary>
    private string litByUid;

    // --- transient -------------------------------------------------------
    private int tickCounter;

    public BlockEntityUpdraftFirebox()
    {
        inventory = new InventoryGeneric(Math.Max(1, Cfg.FuelSlots), null, null);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

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

        structure = ResolveStructure(rotYDeg);

        codeByNumber = new Dictionary<int, AssetLocation>();
        if (structure != null)
        {
            foreach (var pair in structure.BlockNumbers) codeByNumber[pair.Value] = pair.Key;
        }

        centerPos = Pos.AddCopy(Orientation.Normali.X * 2, 0, Orientation.Normali.Z * 2);
    }

    /// <summary>
    /// The multiblock definition for one orientation, shared by every kiln that faces that way.
    ///
    /// It is 110 offsets of JSON and takes tens of KB and a couple of hundred microseconds to
    /// deserialize - paid once per firebox per chunk load if each block entity builds its own,
    /// which for a player walking past a pottery yard is a steady drip of garbage for no reason.
    /// Nothing mutates it after InitForUse, so one instance per rotation serves the lot. The cache
    /// lives on the API object, so it is per side and goes when the world does.
    /// </summary>
    private MultiblockStructure ResolveStructure(int rotYDeg)
    {
        var attributes = Block?.Attributes?["multiblockStructure"];
        if (attributes == null || !attributes.Exists) return null;

        return ObjectCacheUtil.GetOrCreate(Api, $"fornax:multiblock-{Block.FirstCodePart()}-{rotYDeg}", () =>
        {
            var shared = attributes.AsObject<MultiblockStructure>();
            shared.InitForUse(rotYDeg);
            return shared;
        });
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
        StructureComplete = CountStructureProblems() == 0;

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

    /// <summary>
    /// Cools the chamber towards ambient, and charges for letting a firing go out.
    ///
    /// The banked hours are hours of heat already in the ware, not credit in a ledger. Most of
    /// what a firing costs is the climb to temperature, and a kiln that has gone stone cold has
    /// to make that climb over again - so reaching ambient with a batch unfinished forfeits
    /// <see cref="FornaxConfig.ColdRestartPenaltyHours"/> of it. Relighting while the chamber
    /// still holds heat costs nothing at all, which is the whole distinction: a pause is free,
    /// walking away is not.
    ///
    /// Charged on the tick that reaches ambient and no other, since every later call to this
    /// leaves at the guard above.
    /// </summary>
    private bool CoolChamber(double hoursPassed)
    {
        if (ChamberTemperature <= Cfg.AmbientTemperature) return false;

        float before = ChamberTemperature;
        ChamberTemperature = Math.Max(Cfg.AmbientTemperature, ChamberTemperature - (float)(hoursPassed * Cfg.ChamberCoolingPerHour));

        if (ChamberTemperature <= Cfg.AmbientTemperature && FiredEnergyHours > 0)
        {
            double lost = Math.Min(FiredEnergyHours, Math.Max(0, Cfg.ColdRestartPenaltyHours));
            FiredEnergyHours -= lost;
            Api.World.Logger.VerboseDebug("[fornax] kiln at {0} went cold, forfeiting {1:0.#} of its firing", Pos, lost);
        }

        return Math.Abs(ChamberTemperature - before) > 0.01f;
    }

    // =====================================================================
    //  Fuel
    // =====================================================================

    public bool IsValidFuel(ItemStack stack)
    {
        if (stack?.Collectible == null) return false;
        if (IsIgnitionTool(stack)) return false;

        var props = stack.Collectible.CombustibleProps;
        return props != null
               && props.BurnDuration > 0
               && props.BurnTemperature >= Cfg.MinFuelBurnTemperature;
    }

    /// <summary>
    /// Something whose job at this firebox is to light it, whatever else it may be.
    ///
    /// A firestarter burns at 600 and a lit torch at 600/8, so at the default threshold both are
    /// refused as fuel by temperature alone and fall through to the ignite path. Lower
    /// <see cref="FornaxConfig.MinFuelBurnTemperature"/> to 600 - which is exactly what a world
    /// running BTRO-Fuels wants, since that mod puts firewood, brushwood, bamboo, sticks and
    /// dried peat all at 600 - and they become valid fuel instead. The fuel branch in
    /// <see cref="OnPlayerInteract"/> runs before the fall-through, and OnBlockInteractStart is
    /// asked before the held item's own handler, so the click would post the player's firestarter
    /// into the firebox and the kiln could never be lit at all.
    ///
    /// So this is not about temperature. Vanilla marks both: the firestarter by its class, a lit
    /// torch by the CanIgnite behaviour its lit variants carry and its unlit ones do not, which
    /// is the same pair of marks a mod's own lighter would use.
    /// </summary>
    private static bool IsIgnitionTool(ItemStack stack) =>
        stack.Collectible is ItemFirestarter
        || stack.Block?.GetBehavior<BlockBehaviorCanIgnite>() != null;

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

        if (HasKilnTag(stack)) return true;

        var props = stack.Collectible.GetCombustibleProperties(Api.World, stack, null);
        if (props == null) return false;

        if (props.SmeltingType == EnumSmeltType.Fire) return true;

        return props.SmeltedStack?.ResolvedItemstack?.Block?.BlockMaterial == EnumBlockMaterial.Ceramic;
    }

    /// <summary>
    /// Whether something is tagged as kiln work.
    ///
    /// The two clauses above are a copy of what BlockEntityBeeHiveKiln accepts - minus its
    /// third, which takes an item on the strength of a "beehivekiln" attribute alone. That is
    /// the ecosystem's opt-in: a mod tags its item and vanilla fires it. Leaving it out left
    /// this kiln with no way to be extended at all, so everything that wanted in had to patch
    /// the multiblock instead. "fornaxkiln" is the same idea aimed at this kiln.
    ///
    /// Deliberately cheap - no stack resolution - because every ware is asked this on every
    /// tick of a firing. <see cref="FiredResult"/> does the resolving, once, at the end.
    /// </summary>
    private static bool HasKilnTag(ItemStack stack)
    {
        var attributes = stack.Collectible.Attributes;
        if (attributes == null) return false;

        if (attributes["fornaxkiln"].Exists) return true;

        // A beehivekiln tag is somebody else saying "this is kiln work", which is good enough
        // for this kiln too - unless this mod is the one that wrote it, in which case it is
        // about the other kiln and says nothing here. See FornaxModSystem.OurBeehiveTags.
        return attributes["beehivekiln"].Exists
            && !FornaxModSystem.OurBeehiveTags.Contains(stack.Collectible.Code);
    }

    /// <summary>
    /// The topmost chamber course a firing reaches. Normally just the grate; with containers
    /// enabled, the headspace above it too, since a stacked kiln shelf is what puts wares there.
    /// </summary>
    private int TopWareLevel => Cfg.FireContainersInChamber ? WareLevel + 1 : WareLevel;

    /// <summary>
    /// Whether the firing reads wares out of this.
    ///
    /// Plain ground storage always, and "plain" is meant exactly. Stackable Kiln Shelves'
    /// block entity <em>derives</em> from BlockEntityGroundStorage, so an "is" test here reads
    /// a shelf as an ordinary pile and hands this kiln two courses of shelving whatever the
    /// config says - which is how that mod worked in here by accident before anyone decided it
    /// should. A subclass is a mod doing something more than a heap on the floor, and that is
    /// precisely the thing <see cref="FornaxConfig.FireContainersInChamber"/> exists to decide.
    /// </summary>
    private bool IsWareHolder(BlockEntity be) =>
        be is BlockEntityContainer && (Cfg.FireContainersInChamber || IsPlainGroundStorage(Api, be));

    /// <summary>The class name every ordinary ground storage pile is built from.</summary>
    public const string GroundStorageClass = "GroundStorage";

    /// <summary>
    /// Ground storage itself, and nothing derived from it. See <see cref="IsWareHolder"/>.
    ///
    /// The type to compare against is asked of the class registry rather than written down as
    /// typeof(BlockEntityGroundStorage), because "ordinary ground storage" is a registration, not
    /// a class. A mod is free to call RegisterBlockEntityClass("GroundStorage", ...) with a
    /// subclass of its own, and that replaces what every pile in the world is built from -
    /// Dense Ground Storage does exactly this, unconditionally, in its Start. Against a literal
    /// typeof that reads as "no plain ground storage exists anywhere", and the kiln quietly
    /// refuses every ware in the game until the player finds this config switch.
    ///
    /// Asking the registry keeps the distinction the switch is actually about. Stackable Kiln
    /// Shelves registers its BlockEntityKilnShelf under its own name, "BEKilnShelf", so a shelf
    /// is still not a pile - which is the whole point, since it derives from
    /// BlockEntityGroundStorage and an "is" test cannot tell them apart.
    /// </summary>
    public static bool IsPlainGroundStorage(ICoreAPI api, BlockEntity be) =>
        be != null
        && be.GetType() == (api?.ClassRegistry?.GetBlockEntity(GroundStorageClass)
                            ?? typeof(BlockEntityGroundStorage));

    /// <summary>Every occupied slot in the chamber a firing can reach, fireable or not.</summary>
    private void WalkGrate(Action<BlockEntity, ItemSlot> onSlot)
    {
        if (centerPos == null) return;

        var pos = new BlockPos(Pos.dimension);

        for (int y = WareLevel; y <= TopWareLevel; y++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    pos.Set(centerPos.X + dx, centerPos.Y + y, centerPos.Z + dz);

                    var be = Api.World.BlockAccessor.GetBlockEntity(pos);
                    if (!IsWareHolder(be)) continue;

                    var inventory = ((BlockEntityContainer)be).Inventory;

                    for (int i = 0; i < inventory.Count; i++)
                    {
                        var slot = inventory[i];
                        if (slot.Empty) continue;

                        onSlot(be, slot);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Something sitting on the grate that a firing will not touch, named so it can be said out
    /// loud, or null if the grate holds nothing but wares.
    ///
    /// Two ways to end up here. A block in a ware position that is not ground storage - a modded
    /// kiln shelf, a chest, a lime pile - is legal structurally, since the chamber only refuses
    /// solid cubes, but <see cref="WalkGrate"/> reads ground storage and nothing else, so its
    /// contents are simply not part of the batch. And an item with no fire-smelting path is
    /// ground-stored quite happily and then ignored. Both used to be silent.
    /// </summary>
    public string FirstUnfireableOnGrate()
    {
        if (centerPos == null) return null;

        var pos = new BlockPos(Pos.dimension);

        for (int y = WareLevel; y <= TopWareLevel; y++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    pos.Set(centerPos.X + dx, centerPos.Y + y, centerPos.Z + dz);

                    var block = Api.World.BlockAccessor.GetBlock(pos);
                    if (block.Id == 0) continue;

                    // A solid cube here is an obstruction, which the structure survey already
                    // reports. Saying it twice, in two different registers, helps nobody.
                    if (!IsChamberClear(block)) continue;

                    var be = Api.World.BlockAccessor.GetBlockEntity(pos);
                    if (!IsWareHolder(be)) return BlockName(block, pos);

                    var inventory = ((BlockEntityContainer)be).Inventory;

                    for (int i = 0; i < inventory.Count; i++)
                    {
                        var slot = inventory[i];
                        if (slot.Empty || IsFireable(slot.Itemstack)) continue;

                        // A finished batch is unfireable too, and saying so would be nonsense.
                        if (BatchFired) continue;

                        return slot.Itemstack.GetName();
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// How many of a ware the kiln will take in one slot, or no limit if it does not say.
    ///
    /// maxFireable is vanilla's own per-ware cap - raw brick is 12 - and ground storage enforces
    /// it when you light a pit kiln, with "Can only fire up to {0} at once". Ground storage will
    /// hold twice that: a brick pile stacks to 24. Reading the cap is what keeps this kiln to
    /// 108 raw bricks a firing rather than 216, against 12 in a pit kiln.
    /// </summary>
    private static int MaxFireable(ItemStack stack) =>
        stack.Collectible.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps?.MaxFireable
        ?? int.MaxValue;

    private bool IsOverfull(ItemStack stack) =>
        Cfg.RespectMaxFireable && stack.StackSize > MaxFireable(stack);

    /// <summary>
    /// The temperature the chamber will settle at with the fuel now in the firebox.
    ///
    /// An empty firebox is judged against the best this kiln can do rather than against nothing:
    /// a kiln you have not fuelled yet has no business calling its load unfireable.
    /// </summary>
    private float TargetChamberTemperature()
    {
        SurveyFuel(out _, out int hottestFuel);
        if (hottestFuel <= 0) return Cfg.ChamberMaxTemperature;

        return Math.Min(hottestFuel + Cfg.DraftTemperatureBonus, Cfg.ChamberMaxTemperature);
    }

    private static int MeltingPointOf(ItemStack stack, IWorldAccessor world) =>
        stack.Collectible.GetCombustibleProperties(world, stack, null)?.MeltingPoint ?? 0;

    /// <summary>
    /// Whether the chamber will never get hot enough to do anything with this.
    ///
    /// No vanilla ware can trigger it. The hottest that this kiln fires melts at 850 - raw
    /// brick, refractory brick, shingle - and the coolest fuel the firebox will even accept
    /// still drives the chamber to 900. But the kiln takes anything carrying a kiln tag now,
    /// and a mod is free to tag something that wants more heat than this makes; firing it
    /// anyway would be the kiln lying about what it is.
    /// </summary>
    private bool IsTooCold(ItemStack stack, float target) => MeltingPointOf(stack, Api.World) > target;

    /// <summary>The green wares: the ones a firing still has work to do on.</summary>
    private void WalkWares(Action<BlockEntity, ItemSlot> onWare)
    {
        // Once, not once per slot: SurveyFuel walks the firebox and this runs every tick.
        float target = TargetChamberTemperature();

        WalkGrate((storage, slot) =>
        {
            var stack = slot.Itemstack;
            if (IsFireable(stack) && !IsOverfull(stack) && !IsTooCold(stack, target)) onWare(storage, slot);
        });
    }

    /// <summary>
    /// A ware on the grate that wants more heat than the loaded fuel will give, or null.
    ///
    /// Said before the kiln is lit, which is the whole point: finding out afterwards costs a
    /// firing's worth of fuel and tells you nothing about why.
    /// </summary>
    public ItemStack FirstTooColdOnGrate()
    {
        float target = TargetChamberTemperature();
        ItemStack found = null;

        WalkGrate((_, slot) =>
        {
            if (found != null) return;

            var stack = slot.Itemstack;
            if (IsFireable(stack) && !IsOverfull(stack) && IsTooCold(stack, target)) found = stack;
        });

        return found;
    }

    /// <summary>The chamber temperature and the melting point, for a message about the two.</summary>
    public void ChamberVersus(ItemStack stack, out int reaches, out int needs)
    {
        reaches = (int)TargetChamberTemperature();
        needs = MeltingPointOf(stack, Api.World);
    }

    /// <summary>
    /// A pile on the grate with more in it than the kiln will fire at once, or null.
    ///
    /// Reported rather than quietly skipped: an overfull pile looks exactly like a loaded one,
    /// and finding out by burning a firing's worth of fuel over it is not a fair way to learn.
    /// </summary>
    public ItemStack FirstOverfullOnGrate()
    {
        ItemStack found = null;

        WalkGrate((_, slot) =>
        {
            if (found != null) return;
            if (IsFireable(slot.Itemstack) && IsOverfull(slot.Itemstack)) found = slot.Itemstack;
        });

        return found;
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
        // Resolved once for the whole batch rather than per ware, and null whenever nobody is
        // owed anything: no XSkills, the setting off, or whoever lit it has since logged out.
        var creditTo = Cfg.GrantXSkillsExperience && XSkillsPottery.Bound && litByUid != null
            ? Api.World.PlayerByUid(litByUid)
            : null;

        // What actually fired, kept with the pile it fired in: how many wares share a pile is
        // part of what the batch is worth. Collected rather than credited on the spot because
        // XSkills reads the finished stacks and may swap them, which has no business happening
        // half way through deciding what they are.
        var credited = creditTo == null ? null : new List<(BlockEntity Holder, ItemSlot Slot)>();

        WalkWares((storage, slot) =>
        {
            var raw = slot.Itemstack;
            var props = raw.Collectible.GetCombustibleProperties(Api.World, raw, null);
            var fired = FiredResult(raw, props);
            if (fired == null) return;

            float temperature = raw.Collectible.GetTemperature(Api.World, raw);

            // props can be absent entirely on something that is here only by its kiln tag.
            int ratio = Math.Max(1, props?.SmeltedRatio ?? 1);

            // Ground storage renders a pile from its contents' own storage props, and a fired
            // ware's differ from a green one's. Nothing else in the chamber works that way.
            if (storage is BlockEntityGroundStorage groundStorage) groundStorage.forceStorageProps = true;

            slot.Itemstack = fired.Clone();
            slot.Itemstack.StackSize = Math.Max(1, raw.StackSize / ratio);
            slot.Itemstack.Collectible.SetTemperature(Api.World, slot.Itemstack, temperature);

            credited?.Add((storage, slot));

            slot.MarkDirty();
            storage.MarkDirty(true);
        });

        if (credited != null) XSkillsPottery.GrantFiring(Api, creditTo, credited);

        Lit = false;
        FiredEnergyHours = 0;
        burnCredit = 0;
        BatchFired = true;
        ApplyLitAppearance();

        CrackSeals();

        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/ceramicplace"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, false, 24);
    }

    /// <summary>
    /// What a ware comes out as, in the order the three sources are trusted.
    ///
    /// "fornaxkiln" first, because it is a statement about this kiln specifically: whoever set
    /// it meant it. Then combustibleProps, which is what the ware already said it becomes.
    ///
    /// "beehivekiln" last, and only as a fallback for a ware that has nothing else - which is
    /// the whole reason this ordering is not the obvious one. That attribute is keyed 0-3 by how
    /// many of that kiln's doors stand open, since that is what decides how much air reaches the
    /// wares, and key "0" is the fully reducing firing that a mud-sealed chamber physically is.
    /// Taking it whenever it exists would be the truer simulation, but it would also quietly
    /// hand this kiln the tans and creams that are a beehive kiln's to give - a red raw brick
    /// would fire to tan here rather than red. Reading the tag only when combustibleProps is
    /// silent keeps every existing ware firing to exactly what it fires to today, and still lets
    /// a tag-only ware convert instead of sitting on the grate forever.
    /// </summary>
    private ItemStack FiredResult(ItemStack raw, CombustibleProperties props)
    {
        var own = raw.Collectible.Attributes?["fornaxkiln"];
        if (own?.Exists == true && ResolveTagged(own) is ItemStack named) return named;

        if (props?.SmeltedStack?.ResolvedItemstack != null) return props.SmeltedStack.ResolvedItemstack;

        var beehive = raw.Collectible.Attributes?["beehivekiln"];
        return beehive?.Exists == true ? ResolveTagged(beehive["0"]) : null;
    }

    private ItemStack ResolveTagged(JsonObject json)
    {
        if (json == null || !json.Exists) return null;

        var stack = json.AsObject<JsonItemStack>();
        return stack?.Resolve(Api.World, "fornax firing result", false) == true ? stack.ResolvedItemstack : null;
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

        var survey = Survey();
        if (survey.Total > 0)
        {
            // "You left a stone slab in the chamber" and "you have 40 walls still to build" are
            // the same number to the structure check and nothing alike to whoever is holding the
            // firestarter, so they do not get the same sentence.
            if (survey.OnlyObstructions)
            {
                capi.TriggerIngameError(this, "obstructed",
                    Lang.Get("fornax:cant-light-obstructed", survey.ObstructionName));
            }
            else
            {
                capi.TriggerIngameError(this, "incomplete", Lang.Get("fornax:cant-light-incomplete", survey.Total));
            }

            HighlightProblems((byEntity as EntityPlayer)?.Player);
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
        litByUid = byEntity?.PlayerUID;
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

        if (!hotbar.Empty)
        {
            // Held something that burns, but not hot enough. Said out loud rather than swallowed:
            // the click otherwise does nothing whatsoever, which is indistinguishable from a
            // broken block, and there is no other way to learn that a threshold exists - let
            // alone that it is a config setting. A world running BTRO-Fuels lands here for its
            // firewood, brushwood, bamboo, sticks and dried peat, all of which that mod puts at
            // 600 against a floor of 650.
            ExplainColdFuel(hotbar.Itemstack);

            return false;   // let firestarters and torches through to the ignite path
        }

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
    //  Structure check & build guide
    // =====================================================================

    /// <summary>
    /// The structure's stand-in code for "this position is inside the kiln". Positions carrying
    /// it are judged by <see cref="IsChamberClear"/> instead of by block code.
    /// </summary>
    private static bool IsChamber(AssetLocation wanted) =>
        wanted != null && wanted.Domain == "fornax" && wanted.Path == "chamber";

    /// <summary>
    /// Whether a chamber position is acceptable. Anything that is not a solid cube passes: wares
    /// in ground storage, a lime pile, a modded kiln shelf, a torch somebody dropped in there.
    /// Only something that would genuinely wall the chamber off counts as an obstruction.
    ///
    /// The beehive kiln matches these positions by block code - "@(air|groundstorage)" - and so
    /// did this, which makes every mod that invents a new way to hold wares an incompatibility
    /// until somebody ships a JSON patch naming it. Both BulkQuicklime and Stackable Kiln Shelves
    /// ship exactly that patch against the beehive kiln, and clobber each other doing it. Judging
    /// the space rather than the name costs nothing and needs no such list.
    /// </summary>
    private static bool IsChamberClear(Block have) => have.Id == 0 || !have.SideSolid.All;

    /// <summary>
    /// What to call a block in a message. Blocks that assemble their placed name out of what
    /// they are holding - a coal pile, ground storage - hand back an empty string when they are
    /// holding nothing, which would put "the kiln can't fire ." in front of the player.
    /// </summary>
    private string BlockName(Block block, BlockPos at)
    {
        string name = block.GetPlacedBlockName(Api.World, at);
        if (Readable(name)) return name;

        name = block.GetHeldItemName(new ItemStack(block));
        return Readable(name) ? name : block.Code.ToShortString();
    }

    /// <summary>
    /// Lang.Get hands back the key it was given when nothing translates it, so a mod that ships
    /// no lang file - BulkQuicklime is one - would otherwise put "bulkquicklime:block-limepile"
    /// in front of the player. The block's own code reads better than that.
    /// </summary>
    private static bool Readable(string name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains(":block-") && !name.Contains(":item-");

    /// <summary>The one rule for whether a structure position is what it should be.</summary>
    private static bool Matches(AssetLocation wanted, Block have) =>
        IsChamber(wanted) ? IsChamberClear(have) : WildcardUtil.Match(wanted, have.Code);

    /// <summary>
    /// Every structure position that is not yet what it should be, with a colour keyed to the
    /// block that belongs there. Computed here rather than through
    /// MultiblockStructure.HighlightIncompleteParts so the positions can be asserted in a test,
    /// so obstructions read differently from things simply not built yet, and so the chamber can
    /// be judged by <see cref="IsChamberClear"/> rather than by code.
    /// </summary>
    public List<BlockPos> MissingStructurePositions(List<int> colors = null, List<Block> wantedBlocks = null)
    {
        var missing = new List<BlockPos>();
        if (structure?.TransformedOffsets == null || codeByNumber == null) return missing;

        foreach (var offset in structure.TransformedOffsets)
        {
            if (!codeByNumber.TryGetValue(offset.W, out var wanted)) continue;

            var at = new BlockPos(Pos.X + offset.X, Pos.Y + offset.Y, Pos.Z + offset.Z, Pos.dimension);
            var have = Api.World.BlockAccessor.GetBlock(at);
            if (Matches(wanted, have)) continue;

            missing.Add(at);
            colors?.Add(GuideColor(wanted, have));
            wantedBlocks?.Add(ResolveWanted(wanted));
        }

        return missing;
    }

    /// <summary>
    /// The same walk as <see cref="MissingStructurePositions"/> without the allocations, for the
    /// three-second tick, which asks this of every loaded kiln and almost always gets nothing.
    /// </summary>
    private int CountStructureProblems()
    {
        if (structure?.TransformedOffsets == null || codeByNumber == null) return 0;

        var at = new BlockPos(Pos.dimension);
        var offsets = structure.TransformedOffsets;
        int wrong = 0;

        for (int i = 0; i < offsets.Count; i++)
        {
            var offset = offsets[i];
            if (!codeByNumber.TryGetValue(offset.W, out var wanted)) continue;

            at.Set(Pos.X + offset.X, Pos.Y + offset.Y, Pos.Z + offset.Z);
            if (!Matches(wanted, Api.World.BlockAccessor.GetBlock(at))) wrong++;
        }

        return wrong;
    }

    /// <summary>
    /// What is wrong with the kiln, split by the kind of problem, because the three read
    /// completely differently to whoever is standing in front of it: a wall that was never
    /// built, something left in the chamber that has to come out, and the six mud seals that
    /// every firing cracks on purpose.
    /// </summary>
    public StructureSurvey Survey()
    {
        var survey = new StructureSurvey();

        foreach (var at in MissingStructurePositions())
        {
            var have = Api.World.BlockAccessor.GetBlock(at);

            if (have.Code?.Domain == "fornax" && have.Code.Path == "kilnseal-cracked")
            {
                survey.CrackedSeals++;
            }
            else if (have.Id != 0)
            {
                survey.Obstructed++;
                survey.FirstObstruction ??= at;
                survey.ObstructionName ??= BlockName(have, at);
            }
            else
            {
                survey.Unbuilt++;
            }

            survey.Total++;
        }

        return survey;
    }

    /// <summary>See <see cref="Survey"/>.</summary>
    public class StructureSurvey
    {
        /// <summary>Positions that are not what they should be, of any kind.</summary>
        public int Total;

        /// <summary>Positions where the block that belongs there simply is not there yet.</summary>
        public int Unbuilt;

        /// <summary>Positions inside the kiln filled by something solid.</summary>
        public int Obstructed;

        /// <summary>Seals the last firing cracked. Not damage - that is how a kiln is opened.</summary>
        public int CrackedSeals;

        public BlockPos FirstObstruction;
        public string ObstructionName;

        /// <summary>Nothing wrong but the seals a firing cracked: a finished kiln, not a broken one.</summary>
        public bool OnlyCrackedSeals => Total > 0 && Total == CrackedSeals;

        /// <summary>
        /// Nothing wrong but things left in the chamber - so "take it out and the kiln is
        /// ready" is true, which it would not be with a wall or a seal also missing.
        /// </summary>
        public bool OnlyObstructions => Total > 0 && Total == Obstructed;
    }

    /// <summary>
    /// How many structure positions are wrong, and how many of those are cracked mud seals.
    /// </summary>
    public int CountMissing(out int crackedSeals)
    {
        var survey = Survey();
        crackedSeals = survey.CrackedSeals;
        return survey.Total;
    }

    /// <summary>
    /// A concrete block to draw for a wildcard requirement. The patterns accept several blocks
    /// - any mud brick, cob, any firebox facing - so the guide has to pick one to show, and it
    /// should be one that actually exists rather than the pattern itself.
    /// </summary>
    private Block ResolveWanted(AssetLocation wanted)
    {
        string path = wanted.Path;

        if (IsChamber(wanted)) return null;
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
        if (IsChamber(wanted)) return ColorUtil.ColorFromRgba(215, 70, 70, 130);

        if (path.Contains("kilngrate")) return ColorUtil.ColorFromRgba(150, 200, 225, 55);
        if (path.Contains("kilnseal")) return ColorUtil.ColorFromRgba(90, 130, 210, 55);
        if (path.Contains("kilnvent")) return ColorUtil.ColorFromRgba(140, 140, 160, 55);
        if (path.Contains("kilnfirebox")) return ColorUtil.ColorFromRgba(240, 150, 60, 70);
        if (path.Contains("mudbrick") || path.Contains("cob")) return ColorUtil.ColorFromRgba(120, 210, 120, 45);

        return ColorUtil.ColorFromRgba(200, 200, 200, 45);
    }

    /// <summary>Whether this kiln is the one whose guide is currently up. See <see cref="FornaxModSystem.GuideOwner"/>.</summary>
    private bool OwnsGuide => Api is ICoreClientAPI && Pos.Equals(FornaxModSystem.GuideOwner);

    /// <summary>
    /// Claims the session's one build guide for this kiln. Whatever another kiln had up goes:
    /// the ghosts explicitly, and the highlights by being overwritten in the shared slot.
    /// </summary>
    private void TakeGuide()
    {
        FornaxModSystem.GhostRenderer?.Clear();
        FornaxModSystem.GuideOwner = Pos.Copy();
    }

    private void ReleaseGuide(IPlayer byPlayer)
    {
        structure?.ClearHighlights(Api.World, byPlayer);
        FornaxModSystem.GhostRenderer?.Clear();
        FornaxModSystem.GuideOwner = null;
    }

    /// <summary>Ctrl + right-click puts a ghost of the whole kiln up, and takes it down again.</summary>
    public void ToggleBuildGuide(IPlayer byPlayer)
    {
        if (Api is not ICoreClientAPI capi) return;

        if (OwnsGuide)
        {
            ReleaseGuide(byPlayer);
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

        TakeGuide();

        // Two layers: the ghost blocks say WHICH block goes where, the tinted highlight makes
        // them easy to pick out at a glance and is the only thing that can flag an obstruction.
        FornaxModSystem.GhostRenderer?.ShowGhosts(Pos, missing, wanted);
        Api.World.HighlightBlocks(byPlayer, MultiblockStructure.HighlightSlotId, missing, colors);

        capi.TriggerIngameError(this, "guide", Lang.Get("fornax:guide-shown", missing.Count));
    }

    /// <summary>
    /// Whether a held stack burns but not hot enough for this firebox, and the temperature it
    /// does reach. False for anything that does not burn at all, and for a lighter.
    /// </summary>
    public bool IsTooCoolToBurn(ItemStack stack, out int burnsAt)
    {
        burnsAt = 0;
        if (stack?.Collectible == null || IsIgnitionTool(stack)) return false;

        var props = stack.Collectible.CombustibleProps;
        if (props == null || props.BurnDuration <= 0) return false;

        burnsAt = props.BurnTemperature;
        return burnsAt < Cfg.MinFuelBurnTemperature;
    }

    /// <summary>Names the fuel, what it burns at, and what the firebox wants. See the caller.</summary>
    private void ExplainColdFuel(ItemStack stack)
    {
        if (Api is not ICoreClientAPI capi) return;
        if (!IsTooCoolToBurn(stack, out int burnsAt)) return;

        capi.TriggerIngameError(this, "coldfuel",
            Lang.Get("fornax:fuel-too-cold", stack.GetName(), burnsAt, Cfg.MinFuelBurnTemperature));
    }

    private void TriggerError(string code, string langKey)
    {
        if (Api is ICoreClientAPI capi) capi.TriggerIngameError(this, code, Lang.Get(langKey));
    }

    /// <summary>
    /// Paints every wrong position in the guide's shared highlight slot. The vanilla
    /// MultiblockStructure.HighlightIncompleteParts would do this, but it re-runs its own
    /// code-only check, which no longer agrees with <see cref="Matches"/> about the chamber.
    /// </summary>
    private void HighlightProblems(IPlayer byPlayer)
    {
        if (byPlayer == null || Api is not ICoreClientAPI) return;

        var colors = new List<int>();
        var missing = MissingStructurePositions(colors);

        TakeGuide();
        Api.World.HighlightBlocks(byPlayer, MultiblockStructure.HighlightSlotId, missing, colors);
    }

    private void ShowStructureProblems(IPlayer byPlayer)
    {
        if (Api is not ICoreClientAPI capi || structure == null) return;

        var survey = Survey();

        if (survey.Total > 0)
        {
            // Nothing wrong but the seals the firing cracked: that is a finished kiln, not a
            // broken one. Still highlight them, since they are what has to be broken to unload.
            if (BatchFired && survey.OnlyCrackedSeals)
            {
                capi.TriggerIngameError(this, "done", Lang.Get("fornax:firing-done"));
            }
            else if (survey.OnlyObstructions)
            {
                capi.TriggerIngameError(this, "obstructed",
                    Lang.Get("fornax:structure-obstructed", survey.ObstructionName));
            }
            else
            {
                capi.TriggerIngameError(this, "incomplete", Lang.Get("fornax:structure-incomplete", survey.Total));
            }

            HighlightProblems(byPlayer);
            return;
        }

        // The shell is sound, so anything left to say is about what is on the grate.
        string unfireable = FirstUnfireableOnGrate();
        if (unfireable != null)
        {
            capi.TriggerIngameError(this, "unfireable", Lang.Get("fornax:cannot-fire", unfireable));
        }

        var overfull = FirstOverfullOnGrate();
        if (overfull != null)
        {
            capi.TriggerIngameError(this, "overfull",
                Lang.Get("fornax:too-many-to-fire", overfull.GetName(), MaxFireable(overfull)));
        }

        var tooCold = FirstTooColdOnGrate();
        if (tooCold != null)
        {
            ChamberVersus(tooCold, out int reaches, out int needs);
            capi.TriggerIngameError(this, "toocold",
                Lang.Get("fornax:too-cold-to-fire", reaches, tooCold.GetName(), needs));
        }

        if (OwnsGuide) ReleaseGuide(byPlayer);
    }

    private void BurnNearbyEntities()
    {
        var mouth = Pos.ToVec3d().Add(0.5, 0.5, 0.5)
            .Add(Orientation.Opposite.Normali.X * 0.6, 0, Orientation.Opposite.Normali.Z * 0.6);

        var entities = Api.World.GetEntitiesAround(mouth, Cfg.FireboxBurnRadius, 2f, e => e.Alive && e is EntityAgent);

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
        litByUid = tree.GetString("litByUid");
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

        // Only when there is one: a StringAttribute holding null throws on serialization, which
        // takes the whole savegame write down rather than just this kiln.
        if (litByUid != null) tree.SetString("litByUid", litByUid);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (structure == null) return;

        var survey = Survey();
        int missing = survey.Total;

        // A finished batch cracks every seal, so the kiln calls itself broken at the exact
        // moment it has done its job. Lead with what actually happened, and only add the raw
        // count when something other than the seals is wrong with it too.
        if (BatchFired)
        {
            dsc.AppendLine(Lang.Get("fornax:firing-done"));
            if (missing > survey.CrackedSeals) dsc.AppendLine(Lang.Get("fornax:structure-incomplete", missing));
        }
        else if (survey.OnlyObstructions)
        {
            dsc.AppendLine(Lang.Get("fornax:structure-obstructed", survey.ObstructionName));
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

        // Say it here as well as on the click, so a kiln loaded with something it will never
        // fire says so while you are still looking at it rather than after it has burned a
        // firing's worth of firewood for nothing.
        string unfireable = FirstUnfireableOnGrate();
        if (unfireable != null) dsc.AppendLine(Lang.Get("fornax:cannot-fire", unfireable));

        var overfull = FirstOverfullOnGrate();
        if (overfull != null)
        {
            dsc.AppendLine(Lang.Get("fornax:too-many-to-fire", overfull.GetName(), MaxFireable(overfull)));
        }

        var tooCold = FirstTooColdOnGrate();
        if (tooCold != null)
        {
            ChamberVersus(tooCold, out int reaches, out int needs);
            dsc.AppendLine(Lang.Get("fornax:too-cold-to-fire", reaches, tooCold.GetName(), needs));
        }

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
        }
        else if (FiredEnergyHours > 0)
        {
            // A firing that ran out of fuel looks exactly like one that was never lit, right
            // down to the firebox going dark - and the hours banked in it are perishable.
            dsc.AppendLine(Lang.Get("fornax:firing-paused", FiredEnergyHours, Cfg.FiringEnergyHours));
        }
        else if (missing == 0 && fuel > 0)
        {
            dsc.AppendLine(Lang.Get("fornax:not-lit"));
        }

        // Not just while lit: a kiln that has gone out is at its hottest in the minutes after,
        // and that is exactly when someone comes to see what happened and breaks a seal.
        if (ChamberTemperature > Cfg.AmbientTemperature)
        {
            dsc.AppendLine(Lang.Get("fornax:chamber-temp", ChamberTemperature));

            if (ChamberTemperature >= Cfg.ShatterSafeTemperature)
            {
                dsc.AppendLine(Lang.Get("fornax:chamber-hot-warning"));
            }
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        ClearHighlights();

        // The draft vent is scenery - it has no block entity and nothing else ever changes it
        // back. A firebox broken while lit would leave a dismantled kiln smoking for good.
        if (Api?.Side == EnumAppSide.Server && centerPos != null)
        {
            SwapVariant(centerPos.UpCopy(VentLevel), "state", "idle", exchange: false);
        }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        ClearHighlights();
    }

    /// <summary>
    /// Takes the guide down when this kiln goes away - but only if it still has it. A kiln that
    /// lost the guide to another one must leave well alone; clearing regardless is what used to
    /// wipe the guide off the kiln the player had just walked over to.
    /// </summary>
    private void ClearHighlights()
    {
        if (OwnsGuide && Api is ICoreClientAPI capi) ReleaseGuide(capi.World.Player);
    }
}
