using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fornax;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The brick kiln: the same drum and the same nine grate positions laid in fired brick, with a
/// strapped wicket where the mud kiln has six seals.
///
/// Almost none of what makes it a second kiln is visible to the compiler. Which shell it accepts
/// is a wildcard in a blocktype; which numbers it fires on is a block code compared to a string;
/// and what a finished firing does to the entrance is a block swap driven by a variant. All of
/// that compiles whatever it says, so all of it is asserted here.
/// </summary>
public class BrickKilnTests
{
    private const string Wall = "game:claybricks-good-fire";
    private const string MudWall = "game:mudbrick-dark";
    private const string Grate = "fornax:kilngrate";
    private const string Vent = "fornax:kilnvent-idle";
    private const string Luted = "fornax:kilndoor-luted-south";
    private const string Open = "fornax:kilndoor-open-south";

    // "side" is the face the block shows the player, so a south-facing mouth puts the kiln to -z.
    private const string Firebox = "fornax:kilnbrickfirebox-cold-south";
    private const string MudFirebox = "fornax:kilnfirebox-cold-south";

    private static BlockPos Fb() => P(8, 1, 12);
    private static BlockPos Ware(int dx, int dz) => Fb().AddCopy(dx, 2, dz - 2);

    /// <summary>The two wicket positions, bottom first.</summary>
    private static BlockPos Wicket(int y) => Fb().AddCopy(0, y, 0);

    // ------------------------------------------------------------------
    //  Construction
    // ------------------------------------------------------------------

    /// <summary>
    /// The same drum as the mud kiln, in brick, with the entrance narrowed to a wicket: one
    /// block wide and two tall in the middle of the front wall, with plain wall either side of
    /// it where the mud kiln has the outer two seals of its three-wide face.
    /// </summary>
    public static void Build(BlockPos f, string wall = Wall, string firebox = Firebox, string entrance = Luted)
    {
        for (int y = 0; y <= 3; y++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = 0; dz >= -4; dz--)
                {
                    bool isCorner = Math.Abs(dx) == 2 && (dz == 0 || dz == -4);
                    bool perimeter = Math.Abs(dx) == 2 || dz == 0 || dz == -4;
                    string code;

                    if (isCorner && y >= 2) code = "game:air";
                    else if (perimeter)
                    {
                        if (y == 0 && dx == 0 && dz == 0) code = firebox;
                        else if ((y == 2 || y == 3) && dz == 0 && dx == 0) code = entrance;
                        else code = wall;
                    }
                    else
                    {
                        if (y == 0) code = (dx == 0 && dz == -2) ? wall : "game:air";
                        else if (y == 1) code = Grate;
                        else code = "game:air";
                    }

                    World.SetBlock(code, f.AddCopy(dx, y, dz));
                }
            }
        }

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = 0; dz >= -4; dz--)
            {
                bool neck = Math.Abs(dx) <= 1 && dz >= -3 && dz <= -1;
                string code = neck ? ((dx == 0 && dz == -2) ? "game:air" : wall) : "game:air";
                World.SetBlock(code, f.AddCopy(dx, 4, dz));
            }
        }

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz >= -3; dz--)
            {
                World.SetBlock((dx == 0 && dz == -2) ? Vent : wall, f.AddCopy(dx, 5, dz));
            }
        }
    }

    private static async Task Tick3s(BlockPos at = null)
    {
        at ??= Fb();
        for (int i = 0; i < 3; i++) await World.TickNow(at);
    }

    private static BlockEntityUpdraftFirebox Be(BlockPos at = null) =>
        World.BE<BlockEntityUpdraftFirebox>(at ?? Fb());

    private static FornaxConfig Cfg => FornaxModSystem.Config;

    private static void LoadWare(int dx, int dz, string code, int count) =>
        LoadWareAt(Ware(dx, dz), code, count);

    private static void LoadWareAt(BlockPos pos, string code, int count)
    {
        World.SetBlock("game:groundstorage", pos);

        var gs = World.BE<BlockEntityGroundStorage>(pos);
        gs.Inventory[0].Itemstack = World.Stack(code, count);
        gs.Inventory[0].MarkDirty();
        gs.MarkDirty(true);
    }

    private static void Fuel(string code, int count, BlockPos at = null)
    {
        var be = Be(at);
        be.Inventory[0].Itemstack = World.Stack(code, count);
        be.Inventory[0].MarkDirty();
        be.MarkDirty(true);
    }

    // ------------------------------------------------------------------
    //  Registration and recipes
    // ------------------------------------------------------------------

    [VsTest]
    public async Task EveryBrickKilnBlockRegisters()
    {
        string[] codes =
        {
            Firebox, "fornax:kilnbrickfirebox-cold-north", "fornax:kilnbrickfirebox-firing-south",
            Open, Luted, "fornax:kilndoor-open-north", "fornax:kilndoor-luted-west"
        };

        for (int i = 0; i < codes.Length; i++)
        {
            BlockPos pos = P(1 + i, 1, 1);
            World.SetBlock(codes[i], pos);
            await Ticks(1);
            Assert.Equal(codes[i], World.BlockCode(pos));
        }
    }

    /// <summary>
    /// Every one of these is a name in a JSON file that nothing checks. A recipe that names a
    /// metal with no rod, or a brick with no such variant, does not throw - it silently never
    /// loads, and the kiln simply cannot be built.
    /// </summary>
    [VsTest]
    public async Task BrickKilnRecipesResolve()
    {
        var grid = Sapi.World.GridRecipes.FindAll(r =>
            r.Output?.ResolvedItemStack?.Collectible?.Code?.Domain == "fornax");

        var firebox = grid.FirstOrDefault(r =>
            r.Output.ResolvedItemStack.Collectible.Code.Path == "kilnbrickfirebox-cold-north");

        Assert.NotNull(firebox, "no grid recipe produces kilnbrickfirebox-cold-north");

        var panels = grid.FindAll(r =>
            r.Output.ResolvedItemStack.Collectible.Code.Path == "kilndoor-open-north");

        Log($"{panels.Count} wicket panel recipes");
        Assert.True(panels.Count > 0, "no grid recipe produces a wicket panel");

        // Two panels a craft, because the wicket is one block wide and two tall - one panel a
        // craft would leave a kiln that cannot be closed with an even number of crafts.
        foreach (var r in panels) Assert.Equal(2, r.Output.Quantity);

        // The whole point of the panel: it is vanilla's kiln door one metal tier down. If this
        // ever resolves to iron the brick kiln has stopped being reachable in the Bronze Age,
        // which is the mod's entire reason to exist.
        var metals = new HashSet<string>();
        foreach (var r in panels)
        {
            foreach (var ing in r.ResolvedIngredients.Where(i => i != null))
            {
                string code = ing.Code?.Path ?? "";
                if (code.StartsWith("rod-")) metals.Add(code.Substring(4));
            }
        }

        Log("wicket panel metals: " + string.Join(", ", metals.OrderBy(m => m)));
        Assert.True(metals.Contains("copper"), "copper should make a wicket panel");
        Assert.True(!metals.Contains("iron"), "iron has no business being needed here");

        await Ticks(1);
    }

    // ------------------------------------------------------------------
    //  The shell
    // ------------------------------------------------------------------

    /// <summary>
    /// Each firebox carries its own multiblock definition, so the two kilns cannot be built out
    /// of each other's materials. That separation is what lets the block code alone decide which
    /// set of numbers a firing runs on.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task TheBrickKilnNeedsABrickShell()
    {
        Build(Fb());
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(be.IsBrickKiln, "a kilnbrickfirebox should know it is the brick kiln");
        Assert.True(be.StructureComplete, "a brick shell with a luted wicket should complete");

        // One mud brick in the wall is one position wrong, and says so.
        World.SetBlock(MudWall, Fb().AddCopy(-2, 0, -2));
        await Tick3s();
        Assert.True(!be.StructureComplete, "mud brick has no business passing for fired brick");

        World.SetBlock(Wall, Fb().AddCopy(-2, 0, -2));
        await Tick3s();
        Assert.True(be.StructureComplete);

        // And the reverse: the mud kiln's firebox over the same brick shell.
        World.SetBlock(MudFirebox, Fb());
        await Ticks(2);
        await Tick3s();

        var mud = Be();
        Assert.True(!mud.IsBrickKiln, "a kilnfirebox is not the brick kiln");
        Assert.True(!mud.StructureComplete, "the mud kiln should not complete over a brick shell");
    }

    /// <summary>An unluted wicket is an open kiln, whichever way round the panels face.</summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task AnUnlutedWicketLeavesTheKilnOpen()
    {
        Build(Fb());
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(be.StructureComplete);

        World.SetBlock(Open, Wicket(2));
        await Tick3s();

        int missing = be.CountMissing(out int open);
        Log($"one panel unluted: {missing} wrong, {open} of them open panels");
        Assert.Equal(1, missing);
        Assert.Equal(1, open, "an open panel reads as an opened entrance, not as damage");
        Assert.True(!be.CanIgnite, "and an open kiln cannot be lit");
    }

    // ------------------------------------------------------------------
    //  What a firing costs
    // ------------------------------------------------------------------

    /// <summary>
    /// The brick kiln's one advantage in fuel, and the assertion that it is a real one: the
    /// same batch on twenty-four firewood, where the mud kiln wants thirty. Both kilns are
    /// built here rather than trusting the config, because the number only matters if the
    /// firebox actually reads its own.
    /// </summary>
    [VsTest(TimeoutMs = 240000)]
    public async Task TheBrickKilnFiresTheSameBatchOnLessFuel()
    {
        Assert.Equal(48f, Cfg.BrickFiringEnergyHours);
        Assert.Equal(60f, Cfg.FiringEnergyHours);

        BlockPos brick = P(4, 1, 12);
        BlockPos mud = P(12, 1, 12);

        Build(brick);
        FornaxTests.Build(mud);
        await Ticks(2);

        // 24 firewood is 48 fuel-hours: exactly a brick firing, four fifths of a mud one.
        Fuel("game:firewood", 24, brick);
        Fuel("game:firewood", 24, mud);
        await Tick3s(brick);
        await Tick3s(mud);

        var b = Be(brick);
        var m = Be(mud);
        Assert.True(b.StructureComplete, "brick kiln should be complete");
        Assert.True(m.StructureComplete, "mud kiln should be complete");

        b.TryIgnite(null);
        m.TryIgnite(null);

        for (int i = 0; i < 10 && (b.Lit || m.Lit); i++)
        {
            await Hours(3);
            await Tick3s(brick);
            await Tick3s(mud);
        }

        Log($"24 firewood -> brick fired={b.BatchFired}, mud fired={m.BatchFired}");
        Assert.True(b.BatchFired, "24 firewood should see a brick firing out");
        Assert.True(!m.BatchFired, "and should not be enough for the mud kiln");
    }

    /// <summary>
    /// The efficiency claim as a measurement rather than a threshold: fire the same full load
    /// in both kilns and count what each one actually ate.
    ///
    /// The test above proves 24 firewood is enough for one and not the other, which is a point
    /// on the curve. This is the curve - 0.67 firewood a ware against 0.83, the one number the
    /// brick kiln exists to move.
    ///
    /// Asserted as a gap rather than as exact counts: fuel is paid for in whole items once
    /// enough energy has been credited, and a tick can overshoot the target by most of an item,
    /// so the last one or two are not deterministic. The gap is six and cannot be noise.
    /// </summary>
    [VsTest(TimeoutMs = 240000)]
    public async Task TheBrickKilnBurnsLessFuelPerWare()
    {
        BlockPos brick = P(4, 1, 12);
        BlockPos mud = P(12, 1, 12);

        Build(brick);
        FornaxTests.Build(mud);
        await Ticks(2);

        // Generous, so neither runs dry - what is counted is what was consumed, not what was
        // supplied. 40 firewood is 80 fuel-hours against targets of 48 and 60.
        Fuel("game:firewood", 40, brick);
        Fuel("game:firewood", 40, mud);
        await Tick3s(brick);
        await Tick3s(mud);

        var b = Be(brick);
        var m = Be(mud);
        b.TryIgnite(null);
        m.TryIgnite(null);

        for (int i = 0; i < 24 && (b.Lit || m.Lit); i++)
        {
            await Hours(1);
            await Tick3s(brick);
            await Tick3s(mud);
        }

        Assert.True(b.BatchFired, "the brick firing should have finished");
        Assert.True(m.BatchFired, "the mud firing should have finished");

        int brickUsed = 40 - b.FuelItemCount();
        int mudUsed = 40 - m.FuelItemCount();

        // 36 wares either way - see BothKilnsHoldTheSameBatch, which is what makes per-ware
        // a fair comparison at all.
        Log($"a full firing: brick {brickUsed} firewood ({brickUsed / 36.0:0.00}/ware), " +
            $"mud {mudUsed} ({mudUsed / 36.0:0.00}/ware)");

        Assert.Greater(mudUsed, brickUsed);
        Assert.True(mudUsed - brickUsed >= 4,
            $"the brick kiln should save about six firewood a firing, saved {mudUsed - brickUsed}");
        Assert.True(brickUsed <= 27, $"a brick firing should cost about 24 firewood, cost {brickUsed}");
        Assert.True(mudUsed >= 29, $"a mud firing should cost about 30 firewood, cost {mudUsed}");
    }

    /// <summary>
    /// The half of "sideways, not upward" that nothing else guards: the brick kiln holds the
    /// same batch. Cheaper fuel per ware only means anything while the wares are the same
    /// count, and a chamber quietly widened here would turn a lateral move into power creep
    /// with every other test still green.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task BothKilnsHoldTheSameBatch()
    {
        BlockPos brick = P(4, 1, 12);
        BlockPos mud = P(12, 1, 12);

        Build(brick);
        FornaxTests.Build(mud);
        await Ticks(2);

        // Every grate position in both, loaded the same.
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz >= -3; dz--)
            {
                LoadWareAt(brick.AddCopy(dx, 2, dz), "game:rawbrick-blue", 4);
                LoadWareAt(mud.AddCopy(dx, 2, dz), "game:rawbrick-blue", 4);
            }
        }

        await Ticks(2);
        await Tick3s(brick);
        await Tick3s(mud);

        int brickWares = Be(brick).CountWares();
        int mudWares = Be(mud).CountWares();

        Log($"nine positions at four apiece: brick {brickWares}, mud {mudWares}");
        Assert.Equal(36, mudWares);
        Assert.Equal(mudWares, brickWares);
    }

    /// <summary>
    /// And the other half: it fires no hotter. The ceiling and the draft bonus are shared
    /// config, so the two kilns should settle at exactly the same temperature on the same fuel
    /// - which is what keeps the beehive kiln's range out of reach of both of them.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task TheBrickKilnFiresNoHotterThanTheMudOne()
    {
        BlockPos brick = P(4, 1, 12);
        BlockPos mud = P(12, 1, 12);

        Build(brick);
        FornaxTests.Build(mud);
        await Ticks(2);

        Fuel("game:firewood", 40, brick);
        Fuel("game:firewood", 40, mud);
        await Tick3s(brick);
        await Tick3s(mud);

        var b = Be(brick);
        var m = Be(mud);
        b.TryIgnite(null);
        m.TryIgnite(null);

        // Long enough that even the slower-warming one is at its target.
        for (int i = 0; i < 5; i++)
        {
            await Hours(1);
            await Tick3s(brick);
            await Tick3s(mud);
        }

        float target = Math.Min(700 + Cfg.DraftTemperatureBonus, Cfg.ChamberMaxTemperature);
        Log($"on firewood: brick {b.ChamberTemperature:0}C, mud {m.ChamberTemperature:0}C, target {target:0}C");

        Assert.Equal(target, m.ChamberTemperature);
        Assert.Equal(target, b.ChamberTemperature);
    }

    /// <summary>
    /// What the mass is bought with, and the reason the brick kiln wants to be run back to
    /// back: it is still warm at an interval that has taken the mud kiln stone cold - and a
    /// chamber that reaches ambient with a firing unfinished forfeits ColdRestartPenaltyHours
    /// of it.
    ///
    /// Both kilns are lit, brought to heat and then emptied of fuel, which is what running out
    /// mid-batch looks like. The same wait then costs one of them progress and the other
    /// nothing at all.
    /// </summary>
    [VsTest(TimeoutMs = 240000)]
    public async Task TheBrickKilnIsStillWarmWhenTheMudOneHasGoneCold()
    {
        BlockPos brick = P(4, 1, 12);
        BlockPos mud = P(12, 1, 12);

        Build(brick);
        FornaxTests.Build(mud);
        await Ticks(2);

        Fuel("game:firewood", 6, brick);
        Fuel("game:firewood", 6, mud);
        await Tick3s(brick);
        await Tick3s(mud);

        var b = Be(brick);
        var m = Be(mud);
        b.TryIgnite(null);
        m.TryIgnite(null);

        // Run until both have burned dry. The tick that spends the last of the fuel still
        // leaves the kiln lit - Lit only drops on the following tick, when it looks for fuel
        // and finds none - so waiting for the flag rather than counting hours is the honest
        // way to ask, and it is the flag the rest of the test depends on.
        for (int i = 0; i < 8 && (b.Lit || m.Lit); i++)
        {
            await Hours(1);
            await Tick3s(brick);
            await Tick3s(mud);
        }

        // Out of fuel, part way through a batch, both hot.
        Assert.True(!b.Lit && !m.Lit, "six firewood should not see either batch out");
        Assert.True(!b.BatchFired && !m.BatchFired, "and should be nowhere near finishing one");
        Assert.Greater(b.ChamberTemperature, 400f);
        Assert.Greater(m.ChamberTemperature, 400f);

        double brickBanked = b.FiredEnergyHours;
        double mudBanked = m.FiredEnergyHours;
        // 0d, not 0: Assert.Greater compares through IComparable, and Double.CompareTo throws
        // on a boxed int rather than widening it.
        Assert.Greater(brickBanked, 0d);
        Assert.Greater(mudBanked, 0d);

        // Long enough for 300C/h to reach ambient from there, not long enough for 180C/h.
        await Hours(3);
        await Tick3s(brick);
        await Tick3s(mud);

        Log($"three hours later: brick {b.ChamberTemperature:0}C keeping {b.FiredEnergyHours:0.#}h, " +
            $"mud {m.ChamberTemperature:0}C keeping {m.FiredEnergyHours:0.#}h");

        Assert.Equal((float)Cfg.AmbientTemperature, m.ChamberTemperature);
        Assert.Greater(b.ChamberTemperature, (float)Cfg.AmbientTemperature);

        Assert.Less(m.FiredEnergyHours, mudBanked);
        Assert.Equal(brickBanked, b.FiredEnergyHours);
    }

    /// <summary>
    /// The other side of the same trade, and the reason the brick kiln is not simply better:
    /// there is more of it to heat, so it climbs more slowly - and holds the heat longer once
    /// it is up, which is what makes a second firing cheap and a single one a poor use of it.
    /// </summary>
    [VsTest(TimeoutMs = 240000)]
    public async Task TheBrickKilnIsSlowerToHeatAndSlowerToCool()
    {
        BlockPos brick = P(4, 1, 12);
        BlockPos mud = P(12, 1, 12);

        Build(brick);
        FornaxTests.Build(mud);
        await Ticks(2);

        Fuel("game:firewood", 30, brick);
        Fuel("game:firewood", 30, mud);
        await Tick3s(brick);
        await Tick3s(mud);

        var b = Be(brick);
        var m = Be(mud);
        b.TryIgnite(null);
        m.TryIgnite(null);

        await Hours(1);
        await Tick3s(brick);
        await Tick3s(mud);

        Log($"after an hour lit: brick {b.ChamberTemperature:0}C, mud {m.ChamberTemperature:0}C");
        Assert.Greater(m.ChamberTemperature, b.ChamberTemperature);

        // Cooling, from the same starting heat with both kilns out.
        b.Lit = false;
        m.Lit = false;
        b.ChamberTemperature = 900;
        m.ChamberTemperature = 900;
        await Tick3s(brick);
        await Tick3s(mud);

        await Hours(1);
        await Tick3s(brick);
        await Tick3s(mud);

        Log($"after an hour cooling: brick {b.ChamberTemperature:0}C, mud {m.ChamberTemperature:0}C");
        Assert.Greater(b.ChamberTemperature, m.ChamberTemperature);
    }

    /// <summary>
    /// The brick kiln's whole fuel table, not just the firewood row.
    ///
    /// A batch costs a fixed amount of energy whatever is burned, so the count for each fuel
    /// falls out of its own burnDuration - 24 firewood, 24 peat bricks, 15 charcoal against 48
    /// fuel-hours. Every one of those is a number in the README and in the handbook, and every
    /// one of them moves the moment BrickFiringEnergyHours or BurnDurationPerFuelHour does.
    ///
    /// Three kilns rather than one fired three times: a finished kiln has to be unluted, cleared
    /// and re-luted before it will take another batch, and none of that is what this is about.
    /// </summary>
    [VsTest(TimeoutMs = 300000)]
    [PlotSize(32, 32)]
    public async Task TheBrickKilnFuelTableIsExact()
    {
        await AssertFuelTable(exactly: true, "game:firewood", 24, "game:peatbrick", 24, "game:charcoal", 15);
    }

    /// <summary>
    /// And the other side of "exact": one less of any of them leaves the batch unfinished. A
    /// table that is merely sufficient would pass the test above with every count doubled.
    /// </summary>
    [VsTest(TimeoutMs = 300000)]
    [PlotSize(32, 32)]
    public async Task OneLessOfAnyFuelIsNotEnoughForABrickFiring()
    {
        await AssertFuelTable(exactly: false, "game:firewood", 23, "game:peatbrick", 23, "game:charcoal", 14);
    }

    /// <summary>
    /// Builds one brick kiln per fuel, loads each with the given count, and fires them all.
    /// <paramref name="exactly"/> says whether every batch is expected to finish or none is.
    /// </summary>
    private static async Task AssertFuelTable(bool exactly, params object[] fuelAndCount)
    {
        var kilns = new List<(string Fuel, int Count, BlockPos At)>();
        for (int i = 0; i < fuelAndCount.Length; i += 2)
        {
            kilns.Add(((string)fuelAndCount[i], (int)fuelAndCount[i + 1], P(4 + i / 2 * 10, 1, 12)));
        }

        foreach (var k in kilns) Build(k.At);
        await Ticks(2);

        foreach (var k in kilns)
        {
            Fuel(k.Fuel, k.Count, k.At);
            await Tick3s(k.At);
            Assert.True(Be(k.At).StructureComplete, $"the {k.Fuel} kiln should be complete");
            Be(k.At).TryIgnite(null);
        }

        // Charcoal burns through its energy at 1.3x where firewood manages 0.6x, so the three
        // finish at very different times. Run until nothing is still burning.
        for (int i = 0; i < 20 && kilns.Exists(k => Be(k.At).Lit); i++)
        {
            await Hours(2);
            foreach (var k in kilns) await Tick3s(k.At);
        }

        foreach (var k in kilns)
        {
            var be = Be(k.At);
            Log($"{k.Count} x {k.Fuel} -> fired={be.BatchFired}, {be.FiredEnergyHours:0.#} of {Cfg.BrickFiringEnergyHours} fuel-hours");

            if (exactly) Assert.True(be.BatchFired, $"{k.Count} {k.Fuel} should be exactly enough for a brick firing");
            else Assert.True(!be.BatchFired, $"{k.Count} {k.Fuel} should fall just short of a brick firing");
        }
    }

    // ------------------------------------------------------------------
    //  The wicket
    // ------------------------------------------------------------------

    /// <summary>
    /// A firing burns the luting out and the panels swing open - it does not destroy them, and
    /// there is nothing to mine out afterwards. That is the whole of what the copper bought.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task AFiringUnlutesTheWicketRatherThanCrackingSeals()
    {
        Build(Fb());
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        Fuel("game:firewood", 26);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(be.StructureComplete);

        be.TryIgnite(null);
        for (int i = 0; i < 10 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(!be.Lit, "the firing should have finished");
        Assert.True(be.BatchFired);

        var gs = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        Assert.Equal("game:burnedbrick-gray", gs.Inventory[0].Itemstack.Collectible.Code.ToString());

        // Both panels open, still panels, still facing the way they were laid.
        Assert.Equal(Open, World.BlockCode(Wicket(2)));
        Assert.Equal(Open, World.BlockCode(Wicket(3)));

        int missing = be.CountMissing(out int opened);
        Log($"after firing: {missing} wrong, {opened} of them open panels");
        Assert.Equal(2, opened, "both panels should have come unluted");
        Assert.Equal(missing, opened, "and nothing else should be wrong with a fired kiln");

        var dsc = new System.Text.StringBuilder();
        be.GetBlockInfo(null, dsc);
        string info = dsc.ToString();
        Log("block info:\n" + info.TrimEnd());

        // The mud kiln's sentence tells you to break the seals, which would be wrong advice here.
        Assert.True(info.Contains(Lang.Get("fornax:firing-done-brick")),
            "it should say the wicket has opened itself");
        Assert.True(!info.Contains(Lang.Get("fornax:firing-done")),
            "and must not tell a brick kiln's owner to break mud seals");
    }

    /// <summary>
    /// The build guide draws a ghost of the block that belongs in each empty position, and it has
    /// to pick a concrete one out of a wildcard. The brick kiln introduces two new patterns - an
    /// alternation over every brick block in the game, and the wicket - and a pattern the guide
    /// cannot resolve draws nothing at all, silently, in the one feature whose entire job is to
    /// show you what to place.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task TheBuildGuideKnowsWhatEveryBrickPositionWants()
    {
        Build(Fb());
        await Ticks(2);
        await Tick3s();

        // Knock out one of each kind of position, so all four patterns have to resolve.
        World.SetBlock("game:air", Fb().AddCopy(-2, 0, -2));    // brick wall
        World.SetBlock("game:air", Fb().AddCopy(0, 1, -2));     // grate tile
        World.SetBlock("game:air", Wicket(2));                  // wicket panel
        World.SetBlock("game:air", Fb().AddCopy(0, 5, -2));     // draft vent
        await Tick3s();

        var colors = new List<int>();
        var wanted = new List<Block>();
        var missing = Be().MissingStructurePositions(colors, wanted);

        Assert.Equal(4, missing.Count);
        Assert.Equal(missing.Count, wanted.Count);

        for (int i = 0; i < missing.Count; i++)
        {
            Log($"{missing[i].Sub(Fb())} wants {wanted[i]?.Code?.ToString() ?? "NOTHING"}");
            Assert.NotNull(wanted[i], "every wanted position must resolve to a real block to ghost");
        }

        var codes = wanted.Select(b => b.Code.ToString()).ToList();
        Assert.True(codes.Contains("game:claybricks-good-fire"), "a brick wall position should ghost a brick");
        Assert.True(codes.Contains("fornax:kilndoor-luted-north"), "a wicket position should ghost a luted panel");
    }

    /// <summary>
    /// A whole firing cycle costs two clay and nothing else.
    ///
    /// The test below proves a panel takes one clay. This proves the *cycle* takes two - lute,
    /// fire, and be ready to lute again - which is the number the README puts against the mud
    /// kiln's twenty-four clay and six dirt, and the only form of it a player ever experiences.
    /// The distinction matters because a firing could plausibly have consumed the panels, or
    /// have wanted re-luting part way, and neither would show up in a per-click assertion.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task ABrickFiringCyclesOnTwoClay()
    {
        Build(Fb(), entrance: Open);
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        await Ticks(2);

        await Player.StandNear(Fb().AddCopy(0, 0, 2));
        await Player.Hold("game:clay-blue", 8);
        var held = Sapi.World.AllOnlinePlayers[0].InventoryManager.ActiveHotbarSlot;

        await Interact.UseBlock(Wicket(2));
        await Interact.UseBlock(Wicket(3));
        await Ticks(2);

        int afterSealing = held.Itemstack.StackSize;
        Log($"sealing the wicket: 8 clay -> {afterSealing}");
        Assert.Equal(6, afterSealing, "two panels, one clay each");

        var be = Be();
        Fuel("game:firewood", 26);
        await Tick3s();
        Assert.True(be.StructureComplete, "a luted wicket closes the kiln");

        be.TryIgnite(null);
        for (int i = 0; i < 10 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(be.BatchFired, "the batch should have fired");

        // Nothing was spent getting it open again - the fire did that.
        Assert.Equal(afterSealing, held.Itemstack.StackSize);
        Assert.Equal(Open, World.BlockCode(Wicket(2)));
        Assert.Equal(Open, World.BlockCode(Wicket(3)));

        // And the panels are still panels, ready for the next one at the same price.
        await Interact.UseBlock(Wicket(2));
        await Interact.UseBlock(Wicket(3));
        await Ticks(2);

        Log($"a full cycle: 8 clay -> {held.Itemstack.StackSize}");
        Assert.Equal(Luted, World.BlockCode(Wicket(2)));
        Assert.Equal(Luted, World.BlockCode(Wicket(3)));
        Assert.Equal(4, held.Itemstack.StackSize, "two clay a firing, every firing");
    }

    /// <summary>
    /// Luting is packed into a joint on the kiln, so a panel taken off the wall is just a panel.
    /// A luted one that came back luted could never be luted again, and would read as sealed the
    /// moment it was placed.
    /// </summary>
    [VsTest]
    public async Task AWicketPanelPicksAsAnOpenOne()
    {
        World.SetBlock(Luted, P(2, 1, 2));
        await Ticks(1);

        var block = World.GetBlock(P(2, 1, 2));
        var picked = block.OnPickBlock(Sapi.World, P(2, 1, 2));

        Log($"luted panel picks as {picked?.Collectible?.Code}");
        Assert.Equal("fornax:kilndoor-open-north", picked.Collectible.Code.ToString());

        var drops = block.GetDrops(Sapi.World, P(2, 1, 2), null);
        Assert.Equal(1, drops.Length);
        Assert.Equal("fornax:kilndoor-open-north", drops[0].Collectible.Code.ToString());
    }

    /// <summary>
    /// Closing the wicket costs one clay a panel and keeps the panel pointing the way it was
    /// laid - a panel that turned to face north every time it was luted would walk its way
    /// round the kiln over a few firings.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task LutingTheWicketCostsOneClayAndKeepsItsFacing()
    {
        Build(Fb(), entrance: Open);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(!be.StructureComplete, "an open wicket is an open kiln");

        await Player.StandNear(Fb().AddCopy(0, 0, 2));
        await Player.Hold("game:clay-blue", 4);

        await Interact.UseBlock(Wicket(2));
        await Ticks(2);

        Log($"after luting: {World.BlockCode(Wicket(2))}");
        Assert.Equal(Luted, World.BlockCode(Wicket(2)));

        var held = Sapi.World.AllOnlinePlayers[0].InventoryManager.ActiveHotbarSlot;
        Assert.Equal(3, held.Itemstack.StackSize, "one clay a panel, not the stack");

        await Interact.UseBlock(Wicket(3));
        await Ticks(2);
        Assert.Equal(Luted, World.BlockCode(Wicket(3)));
        Assert.Equal(2, held.Itemstack.StackSize);

        await Tick3s();
        Assert.True(be.StructureComplete, "a luted wicket closes the kiln");

        // And by hand again the other way, which is how you break into one early.
        await Player.Hold("game:clay-blue", 0);
        await Interact.UseBlock(Wicket(2));
        await Ticks(2);
        Assert.Equal(Open, World.BlockCode(Wicket(2)));
    }
}

/// <summary>
/// The brick kiln's own blocks draw nothing headless, and a mistyped shape or texture path is
/// invisible until something tries to build a mesh out of it. The wicket panel is two hand-written
/// shape files and three borrowed vanilla textures, so all of it is drawn here at least once.
/// </summary>
public class BrickKilnVisualTests
{
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task EveryBrickKilnBlockDrawsWithARealTexture()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        string[] codes =
        {
            "fornax:kilnbrickfirebox-firing-north",
            "fornax:kilnbrickfirebox-cold-north",
            "fornax:kilndoor-luted-north",
            "fornax:kilndoor-open-north"
        };

        for (int i = 0; i < codes.Length; i++) World.SetBlock(codes[i], P(4 + i * 2, 1, 10));

        await Ticks(4);
        await Player.Teleport(P(7, 2, 4));
        await Ticks(10);
        await Interact.LookAt(P(7, 1, 10));
        await Frames.Wait(30);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "brick-kiln-blocks.png"));

        Log($"firebox firing / cold / wicket luted / wicket open: {path}");
        Assert.NotNull(path);
    }

    /// <summary>
    /// The whole thing, luted and lit, so the wicket is seen where it actually sits - in a wall,
    /// with brick either side of it, rather than as a lone block in a row.
    /// </summary>
    [VsTest(TimeoutMs = 240000)]
    [RequiresClient]
    [PlotSize(40, 32)]
    public async Task AssembledBrickKilnRendersAndFires()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        BlockPos f = P(20, 1, 16);
        BrickKilnTests.Build(f);

        var be = World.BE<BlockEntityUpdraftFirebox>(f);
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 26);
        be.Inventory[0].MarkDirty();

        await Ticks(4);
        for (int i = 0; i < 3; i++) await World.TickNow(f);

        Assert.True(be.StructureComplete, "the assembled brick kiln should validate on the client run too");
        be.TryIgnite(null);
        be.MarkDirty(true);

        await Ticks(150);
        await Player.Teleport(P(10, 3, 31));
        await Ticks(10);
        await Interact.LookAt(P(20, 4, 16));
        await Frames.Wait(90);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "brick-kiln-lit.png"));

        Log($"assembled brick kiln screenshot: {path}");
        Assert.NotNull(path);
    }

    /// <summary>The same kiln with its wicket standing open, which is how it ends every firing.</summary>
    [VsTest(TimeoutMs = 240000)]
    [RequiresClient]
    [PlotSize(40, 32)]
    public async Task AnOpenWicketRendersAsADoorway()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        BlockPos f = P(20, 1, 16);
        BrickKilnTests.Build(f, entrance: "fornax:kilndoor-open-south");

        await Ticks(10);
        await Player.Teleport(P(20, 2, 24));
        await Ticks(10);
        await Interact.LookAt(P(20, 3, 16));
        await Frames.Wait(60);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "brick-kiln-wicket-open.png"));

        Log($"open wicket screenshot: {path}");
        Assert.NotNull(path);
    }
}
