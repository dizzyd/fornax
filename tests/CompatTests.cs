// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

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
/// provisions the mod sets and runs the suite against each. That is not ceremony: three
/// separate defects in this mod were invisible with Fornax loaded alone and obvious the moment
/// a real mod was in the world -
///
///   - Stackable Kiln Shelves' block entity DERIVES from BlockEntityGroundStorage, so the
///     obvious "is" test read a shelf as an ordinary pile and fired two courses of shelving
///     whatever the config said;
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
        Assert.True(!BlockEntityUpdraftFirebox.IsPlainGroundStorage(shelfBe),
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
