// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System.Linq;
using System.Threading.Tasks;
using Fornax;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// What the kiln does when other people's mods are in the world with it.
///
/// Every test here skips itself when the mod it is about is not installed, so this file costs
/// an ordinary run nothing and only says something under <c>scripts/test-matrix.sh</c>, which
/// provisions the mod sets and runs the suite against each. That is not ceremony: four
/// separate defects in this mod were invisible with Fornax loaded alone and obvious the moment
/// a real mod was in the world -
///
///   - Stackable Kiln Shelves' block entity DERIVES from BlockEntityGroundStorage, so the
///     obvious "is" test read a shelf as an ordinary pile and fired two courses of shelving
///     whatever the config said;
///   - Dense Ground Storage REGISTERS its subclass as "GroundStorage", so the exact-type test
///     that fixed the shelf found no plain ground storage anywhere and the kiln refused every
///     ware in the game;
///   - BulkQuicklime ships no lang file at all, so a name lookup fell through to a raw lang
///     key and the kiln offered to fire "bulkquicklime:block-limepile";
///   - Bricklayers raises raw brick's maxFireable from 12 to 16 and its pile from 24 to 32, so
///     a test that wrote vanilla's numbers in failed against a kiln behaving perfectly.
/// </summary>
public class CompatTests
{
    private static BlockPos Fb() => P(8, 1, 12);
    private static BlockPos Grate(int dx, int dz) => Fb().AddCopy(dx, 1, dz - 2);
    private static BlockPos Ware(int dx, int dz) => Fb().AddCopy(dx, 2, dz - 2);

    private static BlockEntityUpdraftFirebox Be() => World.BE<BlockEntityUpdraftFirebox>(Fb());

    private static async Task Tick3s()
    {
        for (int i = 0; i < 3; i++) await World.TickNow(Fb());
    }

    /// <summary>Logs and returns false when the mod is absent, so the caller can bow out.</summary>
    private static bool Needs(string modid)
    {
        if (Sapi.ModLoader.IsModEnabled(modid)) return true;

        Log($"{modid} is not installed - skipping. Run scripts/test-matrix.sh to exercise this.");
        return false;
    }

    // ------------------------------------------------------------------
    //  The original bug report
    // ------------------------------------------------------------------

    /// <summary>
    /// The four ware types a player reported the kiln as rejecting. Every one of them is
    /// GroundStorable and Unplaceable, so BlockGroundStorage.CreateStorage puts plain
    /// game:groundstorage in the world and the kiln has always taken them - the report was a
    /// generalisation from the lime piles, which genuinely were rejected. Asserted here so the
    /// next person to touch the chamber rule finds out at once if that stops being true.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task TheWaresFromTheBugReportAllFire()
    {
        string[] wares =
        {
            "game:storagevessel-red-raw",
            "game:toolmold-red-raw-hoe",
            "ceramicbucketbarrel:claybarrel-red-raw",
            "kilnshelves:kilnsupport-fire-raw",
        };

        FornaxTests.Build(Fb());
        await Ticks(4);
        await Tick3s();
        Assert.True(Be().StructureComplete, "the kiln should be complete before anything goes in it");

        int checkedWares = 0;

        foreach (var code in wares)
        {
            var collectible = (CollectibleObject)Sapi.World.GetBlock(new AssetLocation(code))
                           ?? Sapi.World.GetItem(new AssetLocation(code));

            if (collectible == null) { Log($"{code}: not registered - skipping"); continue; }

            World.SetBlock("game:air", Ware(0, 0));
            await Ticks(1);
            World.SetBlock("game:groundstorage", Ware(0, 0));

            var storage = World.BEOrNull<BlockEntityGroundStorage>(Ware(0, 0));
            Assert.NotNull(storage, $"{code} should sit in ordinary ground storage");

            storage.Inventory[0].Itemstack = new ItemStack(collectible, 1);
            storage.Inventory[0].MarkDirty();
            storage.MarkDirty(true);
            await Ticks(2);
            await Tick3s();

            Log($"{code} -> block={World.BlockCode(Ware(0, 0))} complete={Be().StructureComplete} " +
                $"wares={Be().CountWares()}");

            Assert.Equal("game:groundstorage", World.BlockCode(Ware(0, 0)));
            Assert.True(Be().StructureComplete, $"{code} on the grate must not break the structure");
            Assert.Equal(1, Be().CountWares(), $"{code} should count as a ware");
            checkedWares++;
        }

        Assert.Greater(checkedWares, 1, "at least the two vanilla wares should have been checked");
    }

    /// <summary>
    /// A lime pile is its own block, not ground storage, so it was the one thing in that report
    /// the kiln really did reject - as "structure incomplete", which told the player nothing.
    /// It is legal in the chamber now, and the kiln says plainly that it will not fire it:
    /// BulkQuicklime cooks its piles with a Harmony patch on BlockEntityBeeHiveKiln, which no
    /// amount of heat from this kiln will reach.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task ALimePileIsLegalInTheChamberButWillNotFire()
    {
        if (!Needs("bulkquicklime")) return;

        FornaxTests.Build(Fb());
        await Ticks(4);
        World.SetBlock("bulkquicklime:limepile", Ware(0, 0));
        await Ticks(2);
        await Tick3s();

        var be = Be();
        string name = be.FirstUnfireableOnGrate();
        Log($"lime pile in the chamber -> complete={be.StructureComplete} unfireable={name}");

        Assert.True(be.StructureComplete, "a lime pile is not a solid cube and must not break the kiln");
        Assert.NotNull(name, "but the kiln has to say it cannot fire it");
        Assert.True(!name.Contains(":block-"), "and name it, rather than quoting a lang key at the player");
    }

    // ------------------------------------------------------------------
    //  Stackable Kiln Shelves
    // ------------------------------------------------------------------

    /// <summary>
    /// The FireContainersInChamber switch, against the real mod rather than the ground storage
    /// stand-in the main suite uses.
    ///
    /// This is the one that matters most: BlockEntityKilnShelf derives from
    /// BlockEntityGroundStorage, so "be is BlockEntityGroundStorage" reads a shelf as a pile and
    /// fires it no matter what the config says. Nothing but a real shelf catches that.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task AKilnShelfFiresOnlyWhenContainersAreEnabled()
    {
        if (!Needs("kilnshelves")) return;

        var shelf = Sapi.World.GetBlock(new AssetLocation("kilnshelves:kilnshelf-good-north"));
        if (shelf == null) { Log("kilnshelf block not registered under that code - skipping"); return; }

        FornaxTests.Build(Fb());
        await Ticks(4);
        World.SetBlock("kilnshelves:kilnshelf-good-north", Ware(0, 0));
        await Ticks(2);

        var shelfBe = World.BEOrNull<BlockEntity>(Ware(0, 0));
        Assert.True(shelfBe is BlockEntityGroundStorage,
            "the trap this test exists for: a shelf IS a BlockEntityGroundStorage by inheritance");
        Assert.True(!BlockEntityUpdraftFirebox.IsPlainGroundStorage(Sapi, shelfBe),
            "which is why the kiln compares the type exactly instead");

        var inventory = ((BlockEntityContainer)shelfBe).Inventory;
        inventory[0].Itemstack = World.Stack("game:rawbrick-blue", 4);
        inventory[0].MarkDirty();
        shelfBe.MarkDirty(true);
        await Tick3s();

        var be = Be();
        Assert.True(!FornaxModSystem.Config.FireContainersInChamber, "off by default");
        Log($"containers off -> complete={be.StructureComplete} wares={be.CountWares()} " +
            $"unfireable={be.FirstUnfireableOnGrate() ?? "(none)"}");

        Assert.True(be.StructureComplete, "a shelf is not an obstruction");
        Assert.Equal(0, be.CountWares(), "and its contents are not part of the batch by default");
        Assert.NotNull(be.FirstUnfireableOnGrate(), "the kiln should say so rather than sit silent");

        try
        {
            FornaxModSystem.Config.FireContainersInChamber = true;
            await Tick3s();

            Log($"containers on -> wares={be.CountWares()}");
            Assert.Equal(4, be.CountWares(), "with the switch on, the shelf is read");

            be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
            be.Inventory[0].MarkDirty();
            await Tick3s();
            be.TryIgnite(null);

            for (int i = 0; i < 10 && be.Lit; i++) { await Hours(3); await Tick3s(); }

            var fired = ((World.BEOrNull<BlockEntity>(Ware(0, 0))) as BlockEntityContainer)?.Inventory[0].Itemstack;
            Log($"after firing, the shelf holds: {fired?.StackSize}x {fired?.Collectible?.Code}");
            Assert.Equal("game:burnedbrick-gray", fired?.Collectible?.Code?.ToString(),
                "the shelf's contents should have fired like any other ware");
        }
        finally
        {
            FornaxModSystem.Config.FireContainersInChamber = false;
        }
    }

    // ------------------------------------------------------------------
    //  Dense Ground Storage
    // ------------------------------------------------------------------

    /// <summary>
    /// A pile is still a pile when another mod supplies the class it is built from.
    ///
    /// The mirror image of the shelf test above, and the reason that one's fix could not simply
    /// be typeof(BlockEntityGroundStorage). Dense Ground Storage calls
    /// RegisterBlockEntityClass("GroundStorage", typeof(BlockEntityDenseGroundStorage)) in its
    /// Start - unconditionally, on both sides, before it reads any config of its own - and the
    /// class registry takes the last registration, so every pile in the world is that subclass
    /// whether or not the player ever placed anything densely.
    ///
    /// Against a literal typeof, "plain ground storage" then described nothing at all: the kiln
    /// counted no wares, refused to fire anything in the game, and named what it was refusing
    /// through the pile's placed-block name - "cannot fire Raw brick". The structure survey said
    /// complete throughout, so there was nothing to go on.
    ///
    /// Only the real mod catches this. The class it registers exists solely when it is
    /// installed, and a stand-in registered from the test would be this mod asserting against
    /// its own fixture rather than against what somebody actually ships.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task APileIsStillAPileWhenAnotherModSuppliesItsClass()
    {
        if (!Needs("densegroundstorage")) return;

        var registered = Sapi.ClassRegistry.GetBlockEntity(BlockEntityUpdraftFirebox.GroundStorageClass);
        Log($"'{BlockEntityUpdraftFirebox.GroundStorageClass}' is registered as {registered}");
        Assert.True(registered != typeof(BlockEntityGroundStorage),
            "with the mod installed, ordinary ground storage is somebody else's class");

        FornaxTests.Build(Fb());
        await Ticks(4);
        World.SetBlock("game:groundstorage", Ware(0, 0));
        await Ticks(1);

        var storage = World.BEOrNull<BlockEntityGroundStorage>(Ware(0, 0));
        Assert.NotNull(storage, "a pile is one of these by inheritance, however it was registered");
        Assert.True(storage.GetType() != typeof(BlockEntityGroundStorage),
            "but not that class exactly - which is the trap this test exists for");
        Assert.True(BlockEntityUpdraftFirebox.IsPlainGroundStorage(Sapi, storage),
            "and the kiln has to read it as a plain pile all the same");

        storage.Inventory[0].Itemstack = World.Stack("game:rawbrick-blue", 8);
        storage.Inventory[0].MarkDirty();
        storage.MarkDirty(true);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(!FornaxModSystem.Config.FireContainersInChamber,
            "and on the default config, rather than by way of the containers switch");

        Log($"complete={be.StructureComplete} wares={be.CountWares()} " +
            $"unfireable={be.FirstUnfireableOnGrate() ?? "(none)"}");

        Assert.Equal(8, be.CountWares(), "the bricks are part of the batch");
        Assert.Null(be.FirstUnfireableOnGrate(), "and the kiln does not offer to refuse them");

        be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[0].MarkDirty();
        await Tick3s();
        be.TryIgnite(null);

        for (int i = 0; i < 10 && be.Lit; i++) { await Hours(3); await Tick3s(); }

        var fired = World.BEOrNull<BlockEntityGroundStorage>(Ware(0, 0))?.Inventory[0].Itemstack;
        Log($"after firing, the pile holds: {fired?.StackSize}x {fired?.Collectible?.Code}");
        Assert.Equal("game:burnedbrick-gray", fired?.Collectible?.Code?.ToString(),
            "and the batch fired like any other");
    }

    // ------------------------------------------------------------------
    //  Bricklayers
    // ------------------------------------------------------------------

    /// <summary>
    /// The kiln reads a ware's own maxFireable, not vanilla's number for it. Bricklayers moves
    /// raw brick from 12 to 16 and its pile from 24 to 32, which is exactly the kind of thing a
    /// hardcoded constant gets wrong while the code under it is right.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task ThePileCapComesFromTheWareNotFromVanilla()
    {
        if (!Needs("bricklayers")) return;

        var brick = Sapi.World.GetItem(new AssetLocation("game:rawbrick-blue"));
        var storage = brick?.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps;
        Assert.NotNull(storage);

        Log($"with bricklayers: raw brick maxFireable={storage.MaxFireable}, pile={storage.StackingCapacity}");
        Assert.Greater(storage.MaxFireable, 12, "bricklayers is expected to raise the vanilla cap of 12");

        try
        {
            FornaxModSystem.Config.RespectMaxFireable = true;

            FornaxTests.Build(Fb());
            World.SetBlock("game:groundstorage", Ware(0, 0));
            var pile = World.BEOrNull<BlockEntityGroundStorage>(Ware(0, 0));
            pile.Inventory[0].Itemstack = World.Stack("game:rawbrick-blue", storage.MaxFireable);
            pile.Inventory[0].MarkDirty();
            pile.MarkDirty(true);
            await Ticks(2);
            await Tick3s();

            Log($"exactly the mod's cap on the grate -> wares={Be().CountWares()}");
            Assert.Equal(storage.MaxFireable, Be().CountWares(), "the mod's own cap should fire in full");
            Assert.Null(Be().FirstOverfullOnGrate());
        }
        finally
        {
            FornaxModSystem.Config.RespectMaxFireable = false;
        }
    }

    // ------------------------------------------------------------------
    //  ConfigLib
    // ------------------------------------------------------------------

    /// <summary>
    /// ConfigLib is reached by reflection so it stays optional at build time as well as at run
    /// time, which means a signature change upstream fails silently - no GUI, no server sync,
    /// nothing to notice it by. This is what notices.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task ConfigLibTakesTheConfig()
    {
        if (!Needs("configlib")) return;

        Log($"configlib present, bound = {FornaxModSystem.ConfigLibBound}");
        Assert.True(FornaxModSystem.ConfigLibBound,
            "configlib is installed but did not take the config - the reflection binding has drifted");

        await Ticks(1);
    }

    // ------------------------------------------------------------------
    //  XSkills
    // ------------------------------------------------------------------

    /// <summary>
    /// The original mod and the maintained fork ship under different modids while sharing every
    /// type name, so neither one on its own is the question worth asking.
    /// </summary>
    private static bool NeedsXSkills()
    {
        if (Sapi.ModLoader.IsModEnabled("xskills") || Sapi.ModLoader.IsModEnabled("xskillsfork")) return true;

        Log("xskills is not installed - skipping. Run scripts/test-matrix.sh to exercise this.");
        return false;
    }

    /// <summary>
    /// A player's pottery experience, read the same way the kiln writes it: by reflection, so the
    /// test assembly needs XSkills at build time no more than the mod does. "SkillSet" is
    /// PlayerSkillSet.PropertyName(), which is what an entity behaviour is looked up by.
    /// </summary>
    private static float PotteryExperience(IPlayer player)
    {
        var skillSet = player.Entity.GetBehavior("SkillSet");
        Assert.NotNull(skillSet, "xskills is installed but the player has no SkillSet behaviour");

        var find = skillSet.GetType().GetMethod("FindSkill", new[] { typeof(string), typeof(bool) });
        Assert.NotNull(find, "PlayerSkillSet.FindSkill(string, bool) has moved");

        var skill = find.Invoke(skillSet, new object[] { "pottery", false });
        Assert.NotNull(skill, "xskills is installed but has no pottery skill");

        return (float)skill.GetType().GetProperty("Experience").GetValue(skill);
    }

    /// <summary>
    /// Puts the player's pottery experience back to zero, so a test that measures two firings
    /// can measure them both from the same place.
    ///
    /// Experience is progress towards the next level, not a running total, so it drops when a
    /// level is reached. Any test that subtracts two readings is therefore order-dependent:
    /// measured, the four-slot firing above leaves the player at 36, a threshold sits at 40,
    /// and the very next firing reads as negative. Zeroing first is what makes the comparison
    /// mean anything regardless of what ran before it.
    /// </summary>
    private static bool TryResetPotteryExperience(IPlayer player)
    {
        var skillSet = player.Entity.GetBehavior("SkillSet");
        var find = skillSet?.GetType().GetMethod("FindSkill", new[] { typeof(string), typeof(bool) });
        var skill = find?.Invoke(skillSet, new object[] { "pottery", false });
        var property = skill?.GetType().GetProperty("Experience");

        if (property == null || !property.CanWrite)
        {
            Log("xskills' pottery Experience is not writable - skipping, this test needs a known baseline");
            return false;
        }

        property.SetValue(skill, 0f);
        return true;
    }

    /// <summary>
    /// A firing counts towards the Pottery skill, for whoever lit the kiln, at the rate the
    /// config asks for.
    ///
    /// Two things could go wrong here and only one of them is loud. XSkills.PotteryUtil
    /// .ApplyOnStack is reached by name, so a rename upstream costs the whole feature silently -
    /// that is what asserting on Bound is for, as with ConfigLib above. The other is the
    /// calibration, which is arithmetic against a *different* kiln's behaviour and cannot be
    /// checked by reading this mod's code at all: XSkills awards a beehive kiln n squared
    /// experience for a pile of n occupied slots, because it patches a method vanilla calls once
    /// per slot and then loops the whole pile. So the numbers are pinned here.
    ///
    /// One pile, four occupied slots, at the default 0.75 of a beehive kiln:
    ///
    ///     0.75 x (27 / 9) x 4 peers x 4 slots = 36
    ///
    /// The kiln lit by nobody - TryIgnite(null), which every other firing test in the suite uses
    /// - is covered by running that suite with xskills installed, mod set D in test-matrix.sh.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task AFiringCountsTowardsXSkillsPottery()
    {
        if (!NeedsXSkills()) return;

        Log($"xskills present, bound = {XSkillsPottery.Bound}");
        Assert.True(XSkillsPottery.Bound,
            "xskills is installed but the pottery hook did not resolve - the reflection binding has drifted");

        var player = Sapi.World.AllOnlinePlayers.FirstOrDefault();
        Assert.NotNull(player, "this one needs a real player to credit");

        var cfg = FornaxModSystem.Config;
        Assert.True(cfg.GrantXSkillsExperience, "the switch is on by default and this test reads its rate");

        float before = PotteryExperience(player);

        FornaxTests.Build(Fb());
        World.SetBlock("game:groundstorage", Ware(0, 0));
        await Ticks(2);

        var storage = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        for (int i = 0; i < 4; i++)
        {
            storage.Inventory[i].Itemstack = World.Stack("game:rawbrick-blue", 2);
            storage.Inventory[i].MarkDirty();
        }
        storage.MarkDirty(true);

        var be = Be();
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[0].MarkDirty();
        await Tick3s();

        Assert.Equal(8, be.CountWares(), "four slots of two, all of them part of the batch");
        Assert.True(be.CanIgnite, "a complete, fuelled kiln should be ignitable");
        be.TryIgnite(player.Entity);

        for (int i = 0; i < 10 && be.Lit; i++) { await Hours(3); await Tick3s(); }
        Assert.True(be.BatchFired, "the batch should have fired before anything is claimed about it");

        // What the same four-slot pile would have been worth in a beehive kiln: one experience
        // per slot per conversion, and every slot converts.
        const int inABeehiveKiln = 4 * 4;
        int expected = (int)(cfg.XSkillsExperienceVsBeehiveKiln * 27 / 9 * inABeehiveKiln);

        float after = PotteryExperience(player);
        Log($"pottery experience {before} -> {after} for a firing lit by {player.PlayerName}; " +
            $"a beehive kiln would have paid {inABeehiveKiln} for this pile, " +
            $"and {cfg.XSkillsExperienceVsBeehiveKiln} of it across three times the positions is {expected}");

        Assert.Equal((float)expected, after - before,
            "a firing should be worth its configured fraction of a beehive kiln's, scaled by capacity");
    }

    /// <summary>
    /// The brick kiln pays the same rate.
    ///
    /// XSkillsExperienceVsBeehiveKiln is scaled by *positions*, and both kilns have nine, so
    /// the same pile in either should be worth exactly the same - a firing in the cheaper-to-run
    /// kiln must not also be the better one to grind in. Nothing about that is guaranteed by
    /// construction: XSkillsPottery.GrantFiring is handed whatever the firebox collected, and a
    /// brick-specific rate could be introduced without a single other test noticing.
    ///
    /// Asserted against the mud kiln's actual payout rather than against the arithmetic, so this
    /// keeps holding if the rate is retuned.
    ///
    /// The pile is deliberately small, and the skill is zeroed before each firing. XSkills'
    /// Experience is progress towards the *next* level, not a running total, so it drops when a
    /// level is reached - and a test that fires twice and subtracts reads the second firing as
    /// negative when the level-up lands between them. Measured: two firings of the four-slot
    /// pile the test above uses pay 36 each, cross a threshold at 40, and the second reads as
    /// -4. Two occupied slots pay 9, and each is measured from a reset, so neither the pile nor
    /// whatever ran earlier in the session can push a boundary between the two readings.
    ///
    /// Nine rather than any other number for a second reason: the rate is 0.75 x 27/9 = 2.25 per
    /// slot-peer, and XSkills takes whole experience with the remainder carried across the batch.
    /// A pile whose product is not a multiple of four leaves a different carry behind in each
    /// kiln, and two payouts that are genuinely equal compare unequal. Two slots of two is four
    /// slot-peers, which is exactly nine.
    /// </summary>
    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(32, 32)]
    public async Task TheBrickKilnPaysTheSamePotteryExperience()
    {
        if (!NeedsXSkills()) return;

        Assert.True(XSkillsPottery.Bound, "xskills is installed but the pottery hook did not resolve");

        var player = Sapi.World.AllOnlinePlayers.FirstOrDefault();
        Assert.NotNull(player, "this one needs a real player to credit");
        Assert.True(FornaxModSystem.Config.GrantXSkillsExperience, "the switch is on by default and this test reads its rate");
        if (!TryResetPotteryExperience(player)) return;

        BlockPos mud = P(6, 1, 12);
        BlockPos brick = P(20, 1, 12);

        FornaxTests.Build(mud);
        BrickKilnTests.Build(brick);
        await Ticks(2);

        // The same pile in each: one position, two occupied slots of two. See the note above
        // for why it is two and not four.
        foreach (var at in new[] { mud.AddCopy(0, 2, -2), brick.AddCopy(0, 2, -2) })
        {
            World.SetBlock("game:groundstorage", at);
            await Ticks(1);
            var storage = World.BE<BlockEntityGroundStorage>(at);
            for (int i = 0; i < 2; i++)
            {
                storage.Inventory[i].Itemstack = World.Stack("game:rawbrick-blue", 2);
                storage.Inventory[i].MarkDirty();
            }
            storage.MarkDirty(true);
        }

        async Task<float> Fire(BlockPos at)
        {
            var be = World.BE<BlockEntityUpdraftFirebox>(at);
            be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
            be.Inventory[0].MarkDirty();
            for (int i = 0; i < 3; i++) await World.TickNow(at);

            Assert.Equal(4, be.CountWares(), "two slots of two, both of them part of the batch");
            Assert.True(be.CanIgnite, "a complete, fuelled kiln should be ignitable");

            TryResetPotteryExperience(player);
            float before = PotteryExperience(player);
            be.TryIgnite(player.Entity);

            for (int i = 0; i < 10 && be.Lit; i++)
            {
                await Hours(3);
                for (int t = 0; t < 3; t++) await World.TickNow(at);
            }

            Assert.True(be.BatchFired, "the batch should have fired before anything is claimed about it");
            return PotteryExperience(player) - before;
        }

        float paidByMud = await Fire(mud);
        float paidByBrick = await Fire(brick);

        Log($"the same pile: mud kiln paid {paidByMud}, brick kiln paid {paidByBrick}");
        Assert.Greater(paidByMud, 0f, "the mud kiln should have paid something to compare against");
        Assert.Greater(paidByBrick, 0f,
            "a negative reading means a level-up landed between the two firings - shrink the pile");
        Assert.Equal(paidByMud, paidByBrick, "both kilns hold nine positions, so both pay the same");
    }

    // ------------------------------------------------------------------
    //  Config permutations
    // ------------------------------------------------------------------

    /// <summary>
    /// Config-agnostic, so it can be run under any permutation of the switches: whatever the
    /// config file said at startup, the kiln has to match it. This exercises the whole startup
    /// path - AssetsFinalize acting on a config loaded back in StartPre - which the rest of the
    /// suite only ever sees at its defaults. See scripts/test-matrix.sh.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task BehaviourMatchesWhateverTheConfigSaidAtStartup()
    {
        var cfg = FornaxModSystem.Config;
        var lime = Sapi.World.GetItem(new AssetLocation("game:lime"));
        Assert.NotNull(lime);

        bool fornaxTag = lime.Attributes?["fornaxkiln"].Exists == true;
        bool beehiveTag = lime.Attributes?["beehivekiln"].Exists == true;

        Log($"config   : FireLime={cfg.FireLime} FireLimeInBeehiveKiln={cfg.FireLimeInBeehiveKiln} " +
            $"FireContainersInChamber={cfg.FireContainersInChamber} RespectMaxFireable={cfg.RespectMaxFireable}");
        Log($"lime tags: fornaxkiln={fornaxTag} beehivekiln={beehiveTag}");

        Assert.Equal(cfg.FireLime, fornaxTag, "the fornaxkiln tag must match the config it started with");
        Assert.Equal(cfg.FireLimeInBeehiveKiln, beehiveTag, "and so must the beehivekiln tag");

        FornaxTests.Build(Fb());
        await Ticks(2);

        // lime: fired exactly when this kiln's own tag is on. The beehive tag must not stand in
        // for it, or FireLimeInBeehiveKiln quietly overrides FireLime being off.
        World.SetBlock("game:groundstorage", Ware(0, 0));
        var limePile = World.BEOrNull<BlockEntityGroundStorage>(Ware(0, 0));
        if (limePile == null) { Log("lime is not ground storable here - another mod removed it"); return; }

        limePile.Inventory[0].Itemstack = World.Stack("game:lime", 12);
        limePile.Inventory[0].MarkDirty();
        limePile.MarkDirty(true);
        await Ticks(2);
        await Tick3s();

        Log($"12 lime -> wares={Be().CountWares()}, unfireable={Be().FirstUnfireableOnGrate() ?? "(none)"}");
        Assert.Equal(cfg.FireLime ? 12 : 0, Be().CountWares(),
            "the kiln counts lime exactly when its own tag is on");

        // and the pile cap, the other switch that decides what counts as a ware
        var brick = Sapi.World.GetItem(new AssetLocation("game:rawbrick-blue"));
        var storage = brick.GetBehavior<CollectibleBehaviorGroundStorable>().StorageProps;

        World.SetBlock("game:air", Ware(0, 0));
        World.SetBlock("game:groundstorage", Ware(0, 0));
        var bricks = World.BEOrNull<BlockEntityGroundStorage>(Ware(0, 0));
        bricks.Inventory[0].Itemstack = World.Stack("game:rawbrick-blue", storage.StackingCapacity);
        bricks.Inventory[0].MarkDirty();
        bricks.MarkDirty(true);
        await Ticks(2);
        await Tick3s();

        Log($"a full pile of {storage.StackingCapacity} raw bricks (cap {storage.MaxFireable}) -> " +
            $"wares={Be().CountWares()}");
        Assert.Equal(cfg.RespectMaxFireable ? 0 : storage.StackingCapacity, Be().CountWares(),
            "a full pile fires unless the cap is on, in which case none of it does");
    }
}
