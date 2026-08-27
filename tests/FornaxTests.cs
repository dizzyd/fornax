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
/// The updraft kiln is almost entirely string- and structure-driven: a 110-entry multiblock
/// definition, block codes resolved by wildcard, and wares converted through combustibleProps.
/// None of that is checked by the compiler, so all of it is checked here.
/// </summary>
public class FornaxTests
{
    private const string Wall = "game:mudbrick-dark";
    private const string Grate = "fornax:kilngrate";
    private const string Seal = "fornax:kilnseal-intact";
    private const string Cracked = "fornax:kilnseal-cracked";
    private const string Vent = "fornax:kilnvent-idle";

    // "side" is the face the block shows the player, so a south-facing mouth puts the kiln to the north (-z).
    private const string Firebox = "fornax:kilnfirebox-cold-south";

    // One course above the ground block, which is where a player building by hand puts it -
    // sunk flush with the terrain the firebox mouth cannot even be clicked.
    private static BlockPos Fb() => P(8, 1, 12);
    private static BlockPos Center() => Fb().AddCopy(0, 0, -2);
    private static BlockPos Ware(int dx, int dz) => Fb().AddCopy(dx, 2, dz - 2);

    // ------------------------------------------------------------------
    //  Construction
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the kiln in its canonical orientation: mouth facing +z, body running to -z,
    /// 5x5 drum for four courses then corbelled in to a 3x3 neck and cap.
    /// </summary>
    public static void Build(BlockPos f, bool sealEntrance = true)
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

                    // The corners stop at grate level; the two courses above are open.
                    if (isCorner && y >= 2) code = "game:air";
                    else if (perimeter)
                    {
                        if (y == 0 && dx == 0 && dz == 0) code = Firebox;
                        else if ((y == 2 || y == 3) && dz == 0 && Math.Abs(dx) <= 1) code = sealEntrance ? Seal : "game:air";
                        else code = Wall;
                    }
                    else
                    {
                        if (y == 0) code = (dx == 0 && dz == -2) ? Wall : "game:air";
                        else if (y == 1) code = Grate;
                        else code = "game:air";
                    }

                    World.SetBlock(code, f.AddCopy(dx, y, dz));
                }
            }
        }

        // corbel: 3x3 neck around a 1x1 flue, everything outside it cleared
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = 0; dz >= -4; dz--)
            {
                bool neck = Math.Abs(dx) <= 1 && dz >= -3 && dz <= -1;
                string code = neck ? ((dx == 0 && dz == -2) ? "game:air" : Wall) : "game:air";
                World.SetBlock(code, f.AddCopy(dx, 4, dz));
            }
        }

        // 3x3 cap with the draft vent at its centre
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz >= -3; dz--)
            {
                World.SetBlock((dx == 0 && dz == -2) ? Vent : Wall, f.AddCopy(dx, 5, dz));
            }
        }
    }

    private static void BuildKiln(bool sealEntrance = true) => Build(Fb(), sealEntrance);

    /// <summary>The firebox does its real work every third one-second tick.</summary>
    private static async Task Tick3s()
    {
        for (int i = 0; i < 3; i++) await World.TickNow(Fb());
    }

    private static BlockEntityUpdraftFirebox Be() => World.BE<BlockEntityUpdraftFirebox>(Fb());

    private static FornaxConfig Cfg => FornaxModSystem.Config;

    private static void LoadWare(int dx, int dz, string code, int count)
    {
        BlockPos pos = Ware(dx, dz);
        World.SetBlock("game:groundstorage", pos);

        var gs = World.BE<BlockEntityGroundStorage>(pos);
        gs.Inventory[0].Itemstack = World.Stack(code, count);
        gs.Inventory[0].MarkDirty();
        gs.MarkDirty(true);
    }

    private static void Fuel(string code, int count)
    {
        var be = Be();
        be.Inventory[0].Itemstack = World.Stack(code, count);
        be.Inventory[0].MarkDirty();
        be.MarkDirty(true);
    }

    // ------------------------------------------------------------------
    //  Registration
    // ------------------------------------------------------------------

    [VsTest]
    public async Task EveryKilnBlockRegisters()
    {
        string[] codes = { Grate, Seal, Cracked, Vent, Firebox, "fornax:kilnfirebox-cold-north", "fornax:kilnfirebox-warming-south", "fornax:kilnvent-drafting" };

        for (int i = 0; i < codes.Length; i++)
        {
            BlockPos pos = P(1 + i, 1, 1);
            World.SetBlock(codes[i], pos);
            await Ticks(1);
            Assert.Equal(codes[i], World.BlockCode(pos));
        }
    }

    [VsTest]
    public async Task RawGrateItemFiresIntoTheGrateBlock()
    {
        ItemStack raw = World.Stack("fornax:kilngrateraw", 1);
        Assert.NotNull(raw);

        var props = raw.Collectible.CombustibleProps;
        Assert.NotNull(props);
        Assert.Equal(EnumSmeltType.Fire, props.SmeltingType);
        Assert.Equal("fornax:kilngrate", props.SmeltedStack.ResolvedItemstack.Collectible.Code.ToString());

        // The raw tile should BE the fired tile, just unfired: same shape, so it reads as
        // one object through the whole chain rather than turning from a brick into a grate.
        var fired = World.Block("fornax:kilngrate");
        Assert.NotNull(fired);
        var rawItem = raw.Collectible as Item;
        Assert.NotNull(rawItem);
        string rawShape = rawItem.Shape?.Base?.ToString();
        string firedShape = fired.Shape?.Base?.ToString();
        Log($"raw shape   = {rawShape}");
        Log($"fired shape = {firedShape}");
        Assert.Equal(firedShape, rawShape);

        // The shape's faces reference #front1, so a texture override has to use that code -
        // declaring "all" silently does nothing. Textures are only resolved client-side, so
        // this half of the check only runs when a client is attached.
        if (fired.Textures != null && rawItem.Textures != null)
        {
            Log($"texture codes: fired={string.Join(",", fired.Textures.Keys)} raw={string.Join(",", rawItem.Textures.Keys)}");
            Assert.True(fired.Textures.ContainsKey("front1"), "fired tile must override front1");
            Assert.True(rawItem.Textures.ContainsKey("front1"), "raw tile must override front1");
        }
        else Log("textures not resolved on this side - skipping the texture-code check");

        await Ticks(1);
    }


    [VsTest]
    public async Task RecipesResolve()
    {
        // A malformed recipe does not throw - it just quietly never loads, so assert on the
        // registry rather than trusting a clean startup log. The ingredient counts are here
        // too, because a mispriced recipe is just as silent.
        var grid = Sapi.World.GridRecipes.FindAll(r =>
            r.Output?.ResolvedItemStack?.Collectible?.Code?.Domain == "fornax");

        var made = new Dictionary<string, GridRecipe>();
        foreach (var r in grid) made[r.Output.ResolvedItemStack.Collectible.Code.Path] = r;

        Log("grid recipes: " + string.Join(", ", made.Keys));
        Assert.True(made.ContainsKey("kilnfirebox-cold-north"), "no grid recipe produces kilnfirebox-cold-north");
        Assert.True(made.ContainsKey("kilnvent-idle"), "no grid recipe produces kilnvent-idle");
        Assert.True(made.ContainsKey("kilnseal-intact"), "no grid recipe produces kilnseal-intact");

        AssertIngredients(made["kilnfirebox-cold-north"], "burnedbrick", 5, "mudbrick", 3);
        AssertIngredients(made["kilnseal-intact"], "clay", 4, "soil", 1);
        // One seal per craft: six seals per firing is 24 clay and 6 dirt, which is
        // deliberately the kiln's main running cost.
        Assert.Equal(1, made["kilnseal-intact"].Output.Quantity);
        await Ticks(1);
    }

    /// <summary>
    /// A seal is a lump of daub, so where the clay and the dirt sit in the grid should not
    /// matter.
    ///
    /// Shapeless matching merges identical supplied stacks before it matches, and then consumes
    /// one supplied stack per ingredient - so the four clay have to be one ingredient of
    /// quantity four. Written as the letter four times the recipe resolves, reads correctly and
    /// matches nothing at all, in any arrangement. Measured: that form returns false both for
    /// four separate clay and for a stack of four.
    ///
    /// Note the allowed soil grades make this two registered recipes, one per grade, which is
    /// why crafting means "some seal recipe matches" rather than "the seal recipe matches".
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task ASealCraftsFromAnyArrangement()
    {
        var recipes = Sapi.World.GridRecipes.FindAll(r =>
            r.Output?.ResolvedItemStack?.Collectible?.Code?.Path == "kilnseal-intact");

        Log($"{recipes.Count} seal recipes: " + string.Join(" ; ", recipes.Select(r =>
            string.Join("+", r.ResolvedIngredients.Where(i => i != null).Select(i => $"{i.Code}x{i.Quantity}")))));

        Assert.True(recipes.Count > 0, "no grid recipe produces a mud seal");
        foreach (var r in recipes) Assert.True(r.Shapeless, "the seal recipe should be shapeless");

        var player = Sapi.World.AllOnlinePlayers.FirstOrDefault();
        Assert.NotNull(player);

        var grid = new ItemSlot[9];
        for (int i = 0; i < 9; i++) grid[i] = new DummySlot();

        void Lay(string dirt, int clayCount, bool stacked)
        {
            for (int i = 0; i < 9; i++) grid[i].Itemstack = null;
            if (dirt != null) grid[6].Itemstack = World.Stack(dirt);

            if (stacked)
            {
                if (clayCount > 0) grid[2].Itemstack = World.Stack("game:clay-blue", clayCount);
                return;
            }

            // scattered through the grid, in no order the pattern would recognise
            int[] cells = { 8, 1, 4, 3 };
            for (int i = 0; i < clayCount; i++) grid[cells[i]].Itemstack = World.Stack("game:clay-blue");
        }

        bool Crafts() => recipes.Exists(r => r.Matches(player, Sapi.World, grid, 3));

        Lay("game:soil-medium-none", 4, stacked: false);
        Assert.True(Crafts(), "four loose clay and a block of dirt should craft a seal wherever they sit");

        Lay("game:soil-low-none", 4, stacked: false);
        Assert.True(Crafts(), "and with either grade of dirt");

        Lay("game:soil-low-none", 4, stacked: true);
        Assert.True(Crafts(), "a single stack of four clay should work too");

        Lay("game:soil-low-none", 3, stacked: false);
        Assert.True(!Crafts(), "three clay is not enough");

        Lay(null, 4, stacked: false);
        Assert.True(!Crafts(), "and the dirt is not optional");

        Lay("game:soil-high-none", 4, stacked: false);
        Assert.True(!Crafts(), "high fertility soil is too good to daub with");

        await Ticks(1);
    }

    private static void AssertIngredients(GridRecipe recipe, string aPart, int aCount, string bPart, int bCount)
    {
        var counts = new Dictionary<string, int>();
        foreach (var ing in recipe.ResolvedIngredients)
        {
            if (ing?.Code == null) continue;
            string key = ing.Code.Path.Split('-')[0];
            counts[key] = (counts.TryGetValue(key, out var n) ? n : 0) + Math.Max(1, ing.Quantity);
        }

        Log($"  {recipe.Output.ResolvedItemStack.Collectible.Code.Path}: " +
            string.Join(", ", counts.Select(kv => $"{kv.Value}x {kv.Key}")));

        Assert.Equal(aCount, counts.TryGetValue(aPart, out var a) ? a : 0);
        Assert.Equal(bCount, counts.TryGetValue(bPart, out var b) ? b : 0);
    }

    /// <summary>
    /// Which way a placed firebox ends up facing. HorizontalOrientable points "side" AWAY from
    /// whoever places the block, which put the mouth - and the kiln's whole front - facing away
    /// from the player. Placement is overridden to point it at them instead.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task FireboxOrientsItsMouthTowardsWhoeverPlacesIt()
    {
        BlockPos at = P(8, 1, 10);

        var cases = new (double dx, double dz, string expect)[]
        {
            (0, 4, "south"), (0, -4, "north"), (4, 0, "east"), (-4, 0, "west"),
            (0.5, 4, "south"), (-0.5, -4, "north"),     // slightly off-axis stays put
            (3.5, 1.0, "east"), (-3.5, -1.0, "west"),
        };

        foreach (var c in cases)
        {
            var got = BlockUpdraftFirebox.FacingTowards(at.X + 0.5 + c.dx, at.Z + 0.5 + c.dz, at);
            Log($"observer at ({c.dx:+0.0;-0.0},{c.dz:+0.0;-0.0}) -> mouth faces {got.Code}");
            Assert.Equal(c.expect, got.Code);
        }
        await Ticks(1);
    }

    /// <summary>
    /// And the wiring: place one for real through the input system and check the mouth, the
    /// derived body direction, and that the two agree.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task PlacingAFireboxForRealPointsItAtYou()
    {
        BlockPos ground = P(8, 0, 10);
        BlockPos onTop = ground.UpCopy();
        World.SetBlock("game:air", onTop);
        await Ticks(4);

        await Player.StandNear(P(8, 1, 13));           // standing to the SOUTH
        await Ticks(8);
        await Player.Hold("fornax:kilnfirebox-cold-north", 4);
        await Ticks(4);

        await Interact.UseBlock(ground, BlockFacing.UP);
        await Ticks(6);

        string placed = World.BlockCode(onTop);
        var be = World.BEOrNull<BlockEntityUpdraftFirebox>(onTop);
        Log($"placed from the south -> {placed}, body runs {be?.Orientation}");

        Assert.Equal("fornax:kilnfirebox-cold-south", placed);
        Assert.Equal(BlockFacing.NORTH, be.Orientation);
    }

    /// <summary>
    /// The grate tile is eight voxels thick, so forming it should take more than one pass at
    /// the clay. A single-layer recipe completes the moment you finish the outline and the
    /// tiles just appear, which is not what shaping a floor tile should feel like.
    /// </summary>
    [VsTest]
    public async Task GrateClayFormingIsMoreThanOneLayer()
    {
        var recipes = Sapi.GetClayformingRecipes()
            .Where(r => r.Output?.ResolvedItemstack?.Collectible?.Code?.Domain == "fornax")
            .ToList();

        // clay-* with three allowed variants expands into one recipe per clay colour
        Assert.Equal(3, recipes.Count);
        var recipe = recipes[0];

        // Voxels[x, y, z] - y is the layer. Count what is filled on each.
        int layersUsed = 0, total = 0;
        for (int y = 0; y < recipe.Voxels.GetLength(1); y++)
        {
            int onThisLayer = 0;
            for (int x = 0; x < recipe.Voxels.GetLength(0); x++)
                for (int z = 0; z < recipe.Voxels.GetLength(2); z++)
                    if (recipe.Voxels[x, y, z]) onThisLayer++;

            if (onThisLayer > 0) { layersUsed++; total += onThisLayer; }
        }

        int clay = Math.Max(1, (int)Math.Ceiling((total - 64) / 25f));
        Log($"{layersUsed} layers, {total} voxels -> {clay} clay for {recipe.Output.Quantity} tiles");

        Assert.Greater(layersUsed, 1, "forming a grate tile should take more than a single layer");
        Assert.Equal(3, layersUsed);
        Assert.Equal(3, recipe.Output.Quantity);
        await Ticks(1);
    }

    /// <summary>
    /// modinfo.json may only carry keys that ModInfo actually declares. The game tolerates
    /// extras and loads the mod anyway, but ModDB parses strictly and refuses the upload with
    /// "Unexpected property 'x' on modinfo.json" - so a bad key gets all the way to a released
    /// zip before anything complains.
    /// </summary>
    [VsTest]
    public async Task ModinfoCarriesOnlyKeysModInfoDeclares()
    {
        // Mod.SourcePath points at the loaded mod folder (or zip), which is where the real
        // modinfo.json lives - the parsed ModInfo object cannot show us keys it ignored.
        Mod mod = null;
        foreach (var m in Sapi.ModLoader.Mods) if (m.Info?.ModID == "fornax") mod = m;
        Assert.NotNull(mod);

        string dir = mod.SourcePath;
        if (System.IO.File.Exists(dir)) dir = System.IO.Path.GetDirectoryName(dir);
        string file = System.IO.Path.Combine(dir, "modinfo.json");
        Log("reading " + file);
        Assert.True(System.IO.File.Exists(file), "cannot find modinfo.json at " + file);

        string raw = System.IO.File.ReadAllText(file);

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in typeof(ModInfo).GetMembers())
        {
            if (m is System.Reflection.PropertyInfo || m is System.Reflection.FieldInfo) declared.Add(m.Name);
        }

        var doc = Newtonsoft.Json.Linq.JObject.Parse(raw);
        var unknown = new List<string>();
        foreach (var prop in doc.Properties())
        {
            string k = prop.Name;
            if (k.StartsWith("//")) continue;
            if (!declared.Contains(k) && !declared.Contains(k.Replace("modid", "ModID"))) unknown.Add(k);
        }

        Log($"modinfo keys: {string.Join(", ", doc.Properties().Select(p => p.Name))}");
        if (unknown.Count > 0) Log("NOT declared by ModInfo: " + string.Join(", ", unknown));
        Assert.Equal(0, unknown.Count);
        await Ticks(1);
    }

    /// <summary>What each seal state actually yields when broken.</summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task SealDropsAreWhatTheyShouldBe()
    {
        foreach (var code in new[] { "fornax:kilnseal-intact", "fornax:kilnseal-cracked" })
        {
            BlockPos at = P(4, 1, 4);
            World.SetBlock(code, at);
            await Ticks(2);

            var block = World.GetBlock(at);
            var drops = block.GetDrops(Sapi.World, at, null);
            string got = drops == null || drops.Length == 0
                ? "nothing"
                : string.Join(", ", drops.Select(d => d.StackSize + "x " + d.Collectible.Code));
            Log($"{code} -> {got}");
        }
        await Ticks(1);
    }

    // ------------------------------------------------------------------
    //  Multiblock validation
    // ------------------------------------------------------------------

    [VsTest(TimeoutMs = 120000)]
    public async Task CompleteStructureValidates()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        Assert.True(Be().StructureComplete, "a correctly built kiln should validate");
    }

    [VsTest(TimeoutMs = 120000)]
    public async Task MissingVentBreaksTheStructure()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();
        Assert.True(Be().StructureComplete);

        World.SetBlock("game:air", Center().AddCopy(0, 5, 0));
        await Tick3s();

        Assert.True(!Be().StructureComplete, "removing the draft vent should invalidate the structure");
    }

    /// <summary>
    /// The corbelled silhouette is what the kiln should look like, not a rule. The corner
    /// notches and the ring around the neck sit outside the sealed chamber, so pinning them
    /// to air only ever stopped players leaning things against their own kiln.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task TheCorbelSilhouetteIsCosmetic()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();
        Assert.True(Be().StructureComplete);

        // fill the stepped-in course back out to the full 5x5, and block up the corner
        // notches of the drum for good measure
        foreach (var at in new[]
        {
            Fb().AddCopy(2, 4, 0), Fb().AddCopy(-2, 4, 0), Fb().AddCopy(2, 4, -4), Fb().AddCopy(0, 4, 0),
            Fb().AddCopy(2, 2, 0), Fb().AddCopy(-2, 3, -4)
        })
        {
            World.SetBlock(Wall, at);
        }

        await Tick3s();
        Assert.True(Be().StructureComplete, "building onto the outside of the kiln must not break it");
    }

    /// <summary>
    /// The flue up the middle of the neck is not decoration, so it is still checked - by the
    /// same "is it a solid cube" rule as the rest of the chamber.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task BlockingTheFlueBreaksTheStructure()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();
        Assert.True(Be().StructureComplete);

        World.SetBlock(Wall, Fb().AddCopy(0, 4, -2));
        await Tick3s();

        Assert.True(!Be().StructureComplete, "a bricked-up flue should invalidate the structure");
    }

    /// <summary>
    /// The chamber is judged by whether it is clear, not by block code. Matching codes the way
    /// the beehive kiln does makes every mod that invents a new way to hold wares - a lime pile,
    /// a kiln shelf - an incompatibility that reads to the player as "structure incomplete".
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task AnythingThatIsNotSolidMayStandInTheChamber()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();
        Assert.True(Be().StructureComplete);

        // stand-ins for what mods put in a kiln: none of these are game:groundstorage
        foreach (var code in new[] { "game:coalpile", "game:torch-basic-lit-up", "game:tallgrass-tall-free" })
        {
            var block = Sapi.World.GetBlock(new AssetLocation(code));
            if (block == null) { Log($"{code} is not registered, skipping"); continue; }

            World.SetBlock(code, Ware(1, -1));
            await Tick3s();

            Log($"{code} in the chamber -> complete={Be().StructureComplete}");
            Assert.True(Be().StructureComplete, $"{code} is not a solid cube and should not break the kiln");

            World.SetBlock("game:air", Ware(1, -1));
            await Tick3s();
        }
    }

    /// <summary>
    /// A brick left in the chamber and forty walls never built are the same number to the
    /// structure check and nothing alike to the player, so they must not get the same sentence.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task AnObstructionIsReportedAsOneRatherThanAsMissingBlocks()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        World.SetBlock(Wall, Ware(0, 0));
        await Tick3s();

        var survey = Be().Survey();
        Log($"blocked chamber -> total={survey.Total} unbuilt={survey.Unbuilt} " +
            $"obstructed={survey.Obstructed} name={survey.ObstructionName}");

        Assert.Equal(1, survey.Total);
        Assert.Equal(1, survey.Obstructed);
        Assert.Equal(0, survey.Unbuilt);
        Assert.Equal(Ware(0, 0), survey.FirstObstruction);
        Assert.True(!string.IsNullOrWhiteSpace(survey.ObstructionName));

        var dsc = new System.Text.StringBuilder();
        Be().GetBlockInfo(null, dsc);
        string info = dsc.ToString();
        Log("block info:\n" + info.TrimEnd());

        Assert.True(info.Contains(Lang.Get("fornax:structure-obstructed", survey.ObstructionName)),
            "it should name what is in the way");
        Assert.True(!info.Contains(Lang.Get("fornax:structure-incomplete", 1)),
            "and must not call an obstruction a missing block");
        Assert.True(!info.Contains(Lang.Get("fornax:cannot-fire", survey.ObstructionName)),
            "nor say the same thing twice as something it cannot fire");

        // knock a wall out too: "take it out and the kiln is ready" stops being true, so the
        // count has to come back
        World.SetBlock("game:air", Fb().AddCopy(2, 1, -2));
        await Tick3s();

        survey = Be().Survey();
        Assert.Equal(2, survey.Total);
        Assert.True(!survey.OnlyObstructions, "a hole in the wall is not an obstruction");

        dsc = new System.Text.StringBuilder();
        Be().GetBlockInfo(null, dsc);
        Log("with a wall missing too:\n" + dsc.ToString().TrimEnd());
        Assert.True(dsc.ToString().Contains(Lang.Get("fornax:structure-incomplete", 2)),
            "with a wall missing as well it is back to a count");
    }

    /// <summary>
    /// Things the kiln will never fire used to sit on the grate in complete silence, and a
    /// firing's worth of firewood would go by before anyone noticed. Two ways in: a container
    /// that is not ground storage, and an item with no fire-smelting path.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task TheKilnSaysWhatItWillNotFire()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();
        Assert.True(Be().StructureComplete);
        Assert.Null(Be().FirstUnfireableOnGrate(), "an empty grate has nothing to complain about");

        // a block in a ware slot that is not ground storage: legal structurally, but its
        // contents are not part of the batch
        World.SetBlock("game:coalpile", Ware(1, 1));
        await Tick3s();

        string named = Be().FirstUnfireableOnGrate();
        Log($"coal pile on the grate -> {named}");
        Assert.True(Be().StructureComplete, "a coal pile is not an obstruction");
        Assert.True(!string.IsNullOrWhiteSpace(named), "and it has to be nameable, or the message reads \"can't fire .\"");
        Assert.True(!named.Contains(":block-"), "and nameable means a name, not an untranslated lang key");

        World.SetBlock("game:air", Ware(1, 1));
        await Tick3s();

        // and an item that simply has no fire-smelting path
        LoadWare(0, 0, "game:stone-granite", 4);
        await Tick3s();

        named = Be().FirstUnfireableOnGrate();
        Log($"granite on the grate -> {named}");
        Assert.True(!string.IsNullOrWhiteSpace(named));

        var dsc = new System.Text.StringBuilder();
        Be().GetBlockInfo(null, dsc);
        string info = dsc.ToString();
        Log("block info:\n" + info.TrimEnd());
        Assert.True(info.Contains(Lang.Get("fornax:cannot-fire", named)), "it should say so on the block info");

        // wares it can fire draw no complaint
        World.SetBlock("game:air", Ware(0, 0));
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        await Tick3s();
        Assert.Null(Be().FirstUnfireableOnGrate(), "green wares are not a problem");
    }

    /// <summary>
    /// Crushed lime is ground-storable in vanilla and already knows it becomes quicklime, but
    /// its smeltingType is "cook" and its product is an item, so the kiln's two combustibleProps
    /// clauses both miss it. The fornaxkiln tag is what lets it in.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task LimeFiresIntoQuicklime()
    {
        var lime = Sapi.World.GetItem(new AssetLocation("game:lime"));
        Assert.NotNull(lime, "vanilla crushed lime should be registered");

        Assert.True(lime.Attributes?["fornaxkiln"].Exists == true, "fornax should have tagged lime");

        // The other half of it, and the half another mod can take away: BulkQuicklime patches
        // "remove /behaviors/0" onto vanilla lime, which is exactly its GroundStorable, and
        // replaces it with a limepile block that only its own beehive kiln patch cooks. With
        // that mod installed there is no way to get lime onto the grate at all, so the two
        // features are mutually exclusive and this test has nothing left to check.
        if (lime.GetBehavior<CollectibleBehaviorGroundStorable>() == null)
        {
            Log("lime is not ground storable - another mod has removed the behaviour, " +
                "so it cannot be put on the grate. Skipping the firing.");
            return;
        }

        // Messy12 holds 12 to a block, so a full grate is 9 x 12 lime, not 9 x 64.
        BuildKiln();
        LoadWare(0, 0, "game:lime", 12);
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.Equal(12, be.CountWares(), "tagged lime should count as a ware");
        Assert.Null(be.FirstUnfireableOnGrate(), "and must not be reported as something it cannot fire");

        be.TryIgnite(null);
        for (int i = 0; i < 8 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(!be.Lit, "the firing should have finished");

        var gs = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        var got = gs.Inventory[0].Itemstack;
        Log($"12 lime -> {got?.StackSize}x {got?.Collectible?.Code}  " +
            $"(a full grate of nine would be 108 -> 54)");

        Assert.Equal("game:quicklime", got.Collectible.Code.ToString());
        Assert.Equal(6, got.StackSize, "smeltedRatio 2 - two lime to one quicklime");
    }

    /// <summary>
    /// maxFireable is vanilla's per-ware cap on how many go in at once - raw brick is 12 - and a
    /// pit kiln enforces it. Ground storage holds twice that, so ignoring it made this kiln 216
    /// raw bricks a firing against a pit kiln's 12. An overfull pile is not fired, and is said
    /// out loud rather than quietly skipped.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task AnOverfullPileIsNotFiredAndSaysSo()
    {
        Assert.True(!Cfg.RespectMaxFireable, "off by default - tripling the fuel cost was the correction");

        // Read both numbers off the ware rather than writing vanilla's in: Bricklayers, for one,
        // raises raw brick's maxFireable from 12 to 16, and a hardcoded 12 here fails against a
        // kiln that is behaving perfectly.
        var brick = Sapi.World.GetItem(new AssetLocation("game:rawbrick-blue"));
        var storage = brick.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps;
        Assert.NotNull(storage);

        int cap = storage.MaxFireable;
        int pile = storage.StackingCapacity;
        Log($"raw brick: maxFireable={cap}, a full pile={pile}");
        Assert.Greater(pile, cap, "the test needs a pile that can hold more than the cap allows");

        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", pile);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Log($"cap off, {pile} raw bricks -> wares={be.CountWares()}");
        Assert.Equal(pile, be.CountWares(), "by default a full pile fires");
        Assert.Null(be.FirstOverfullOnGrate());

        try
        {
            Cfg.RespectMaxFireable = true;
            await Tick3s();

            var overfull = be.FirstOverfullOnGrate();
            Log($"cap on, {pile} raw bricks -> wares={be.CountWares()}, overfull={overfull?.Collectible?.Code}");

            Assert.Equal(0, be.CountWares(), "an overfull pile is not a ware");
            Assert.NotNull(overfull, "and the kiln has to say so");

            var dsc = new System.Text.StringBuilder();
            be.GetBlockInfo(null, dsc);
            string info = dsc.ToString();
            Log("block info:\n" + info.TrimEnd());
            Assert.True(info.Contains(Lang.Get("fornax:too-many-to-fire", overfull.GetName(), cap)),
                "the block info should name the ware and its own cap");

            // at the cap it fires normally
            World.SetBlock("game:air", Ware(0, 0));
            LoadWare(0, 0, "game:rawbrick-blue", cap);
            await Tick3s();

            Log($"cap on, {cap} raw bricks -> wares={be.CountWares()}");
            Assert.Equal(cap, be.CountWares());
            Assert.Null(be.FirstOverfullOnGrate());
        }
        finally
        {
            Cfg.RespectMaxFireable = false;
        }
    }

    /// <summary>A firing costs what the config says it costs, in whole fuel items.</summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task AFiringCostsThirtyFirewood()
    {
        Assert.Equal(60f, Cfg.FiringEnergyHours);

        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 12);
        Fuel("game:firewood", 30);                 // 30 x (24 burnDuration / 12) = 60 fuel-hours
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        for (int i = 0; i < 10 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Log($"30 firewood -> lit={be.Lit} batchFired={be.BatchFired} fuel left={be.FuelItemCount()}");
        Assert.True(!be.Lit, "30 firewood should be exactly enough to see the batch out");
        Assert.True(be.BatchFired, "and the batch should have fired");
    }

    /// <summary>
    /// The beehive kiln has no lime recipe of its own either, and its gate takes the same kind
    /// of attribute. Tagging is all it needs - no Harmony patch, and nothing taken away from
    /// vanilla, which is the difference between this and how BulkQuicklime does it.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task LimeIsTaggedForTheBeehiveKilnToo()
    {
        var lime = Sapi.World.GetItem(new AssetLocation("game:lime"));
        var tag = lime.Attributes?["beehivekiln"];
        Assert.NotNull(lime);

        Log($"FireLimeInBeehiveKiln={Cfg.FireLimeInBeehiveKiln} (default), tag exists={tag?.Exists}");
        Assert.True(!Cfg.FireLimeInBeehiveKiln, "off by default - it changes a vanilla block");
        Assert.True(tag?.Exists != true, "so vanilla's kiln should see nothing");

        try
        {
            Cfg.FireLimeInBeehiveKiln = true;
            FornaxModSystem.ApplyLimeTags(Sapi);

            tag = lime.Attributes?["beehivekiln"];
            Assert.True(tag?.Exists == true, "vanilla's kiln gates on this attribute alone");

            // all four door counts, because quicklime is quicklime however much air reached it
            for (int open = 0; open <= 3; open++)
            {
                Assert.Equal("quicklime", tag[open.ToString()]["code"].AsString(),
                    $"door count {open} should still give quicklime");
            }
        }
        finally
        {
            Cfg.FireLimeInBeehiveKiln = false;
            FornaxModSystem.ApplyLimeTags(Sapi);
        }

        await Ticks(1);
    }

    /// <summary>
    /// Kiln shelves are off by default, and "off" has to mean the headspace course is not read
    /// at all - not merely that shelves are unrecognised. Ground storage put up there stands in
    /// for a shelf, so this holds without the Stackable Kiln Shelves mod installed.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task TheHeadspaceCourseIsOnlyFiredWhenContainersAreEnabled()
    {
        Assert.True(!Cfg.FireContainersInChamber, "containers in the chamber should be off by default");

        BuildKiln();

        // one pile on the grate, one in the headspace above it
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        BlockPos above = Ware(0, 0).UpCopy();
        World.SetBlock("game:groundstorage", above);
        var upper = World.BE<BlockEntityGroundStorage>(above);
        upper.Inventory[0].Itemstack = World.Stack("game:rawbrick-blue", 8);
        upper.Inventory[0].MarkDirty();
        upper.MarkDirty(true);

        await Ticks(2);
        await Tick3s();

        var be = Be();
        Log($"default: wares counted = {be.CountWares()}");
        Assert.Equal(8, be.CountWares(), "only the grate course should be read by default");

        try
        {
            Cfg.FireContainersInChamber = true;
            await Tick3s();

            Log($"containers enabled: wares counted = {be.CountWares()}");
            Assert.Equal(16, be.CountWares(), "with containers on, the headspace counts too");

            Fuel("game:firewood", 32);
            await Tick3s();
            be.TryIgnite(null);
            for (int i = 0; i < 8 && be.Lit; i++)
            {
                await Hours(3);
                await Tick3s();
            }

            var lower = World.BE<BlockEntityGroundStorage>(Ware(0, 0)).Inventory[0].Itemstack;
            var higher = World.BE<BlockEntityGroundStorage>(above).Inventory[0].Itemstack;
            Log($"after firing: grate={lower?.Collectible?.Code}, headspace={higher?.Collectible?.Code}");

            Assert.Equal("game:burnedbrick-gray", lower.Collectible.Code.ToString());
            Assert.Equal("game:burnedbrick-gray", higher.Collectible.Code.ToString(),
                "the headspace course should have fired as well");
        }
        finally
        {
            Cfg.FireContainersInChamber = false;
        }
    }

    /// <summary>A stand-in for what Stackable Kiln Shelves' block entity actually is.</summary>
    private class ShelfLikeStorage : BlockEntityGroundStorage { }

    /// <summary>
    /// The switch above is only worth anything if a shelf can be told apart from a pile, and the
    /// obvious test cannot do it: BlockEntityKilnShelf derives from BlockEntityGroundStorage, so
    /// "is BlockEntityGroundStorage" says true and the kiln fires two courses of shelving with
    /// the config switched off. Exact type, not "is". Verified against the real mod by hand; this
    /// keeps it honest without needing the mod installed.
    /// </summary>
    [VsTest]
    public async Task ASubclassOfGroundStorageIsNotPlainGroundStorage()
    {
        var plain = new BlockEntityGroundStorage();
        var shelfLike = new ShelfLikeStorage();

        Assert.True(shelfLike is BlockEntityGroundStorage, "the trap: an is-test cannot tell them apart");

        Assert.True(BlockEntityUpdraftFirebox.IsPlainGroundStorage(plain));
        Assert.True(!BlockEntityUpdraftFirebox.IsPlainGroundStorage(shelfLike),
            "a subclass is a mod doing more than a heap on the floor, and the config decides about it");
        Assert.True(!BlockEntityUpdraftFirebox.IsPlainGroundStorage(null));

        await Ticks(1);
    }

    /// <summary>
    /// The lime tags follow the config in both directions, so a setting changed in a running
    /// world takes hold without a reload - both kilns read these attributes live. One-way
    /// tagging would make the config switches lies until the next restart.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task LimeTagsFollowTheConfigBothWays()
    {
        var lime = Sapi.World.GetItem(new AssetLocation("game:lime"));
        Assert.NotNull(lime);

        try
        {
            Cfg.FireLime = false;
            Cfg.FireLimeInBeehiveKiln = false;
            FornaxModSystem.ApplyLimeTags(Sapi);

            Log($"both off -> fornaxkiln={lime.Attributes?["fornaxkiln"].Exists}, " +
                $"beehivekiln={lime.Attributes?["beehivekiln"].Exists}");
            Assert.True(lime.Attributes?["fornaxkiln"].Exists != true, "the tag should have been taken off again");
            Assert.True(lime.Attributes?["beehivekiln"].Exists != true);

            // and the kiln stops taking lime as a ware, live
            BuildKiln();
            LoadWare(0, 0, "game:lime", 12);
            await Ticks(2);
            await Tick3s();

            Log($"both off -> wares={Be().CountWares()}, unfireable={Be().FirstUnfireableOnGrate()}");
            Assert.Equal(0, Be().CountWares(), "untagged lime is not a ware");
            Assert.NotNull(Be().FirstUnfireableOnGrate(), "and the kiln should say so");

            Cfg.FireLime = true;
            Cfg.FireLimeInBeehiveKiln = false;   // the default; leaving it on would leak into other tests
            FornaxModSystem.ApplyLimeTags(Sapi);
            await Tick3s();

            Log($"both on -> wares={Be().CountWares()}");
            Assert.Equal(12, Be().CountWares(), "tagging again should take hold without a reload");
        }
        finally
        {
            Cfg.FireLime = true;
            Cfg.FireLimeInBeehiveKiln = false;   // the default; leaving it on would leak into other tests
            FornaxModSystem.ApplyLimeTags(Sapi);
        }
    }

    /// <summary>
    /// The two lime switches have to be independent, and one specific combination says whether
    /// they are: lime off here, on for the beehive kiln. The kiln honours a beehivekiln tag as
    /// an opt-in, so without care the tag this mod writes for the OTHER kiln reads back as
    /// permission for this one and quietly undoes FireLime=false. Found by running the config
    /// permutations rather than by reading the code.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task TaggingLimeForTheBeehiveKilnDoesNotFireItHere()
    {
        try
        {
            Cfg.FireLime = false;
            Cfg.FireLimeInBeehiveKiln = true;
            FornaxModSystem.ApplyLimeTags(Sapi);

            var lime = Sapi.World.GetItem(new AssetLocation("game:lime"));
            Log($"fornaxkiln={lime.Attributes?["fornaxkiln"].Exists} " +
                $"beehivekiln={lime.Attributes?["beehivekiln"].Exists}");

            Assert.True(lime.Attributes?["fornaxkiln"].Exists != true, "this kiln's own tag should be off");
            Assert.True(lime.Attributes?["beehivekiln"].Exists == true, "the other kiln's should be on");

            BuildKiln();
            LoadWare(0, 0, "game:lime", 12);
            await Ticks(2);
            await Tick3s();

            Log($"wares={Be().CountWares()}, unfireable={Be().FirstUnfireableOnGrate() ?? "(none)"}");
            Assert.Equal(0, Be().CountWares(),
                "FireLime=false must hold even while lime is tagged for the beehive kiln");
            Assert.NotNull(Be().FirstUnfireableOnGrate());

            // and a beehivekiln tag somebody else wrote still opts a ware in, which is the
            // whole point of honouring it
            Assert.True(!FornaxModSystem.OurBeehiveTags.Contains(new AssetLocation("game:rawbrick-blue")));
        }
        finally
        {
            Cfg.FireLime = true;
            Cfg.FireLimeInBeehiveKiln = false;   // the default; leaving it on would leak into other tests
            FornaxModSystem.ApplyLimeTags(Sapi);
        }
    }

    /// <summary>
    /// ConfigLib is reached by reflection so it stays optional at build time as well as at run
    /// time - which means a signature change upstream would otherwise fail silently, leaving the
    /// settings out of the GUI and unsynced from the server with nothing to notice it by. This is
    /// what notices. Skips when ConfigLib is not installed, which is the ordinary case.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task ConfigLibTakesTheConfigWhenItIsInstalled()
    {
        if (!Sapi.ModLoader.IsModEnabled("configlib"))
        {
            Log("configlib is not installed - nothing to bind against, skipping");
            await Ticks(1);
            return;
        }

        Log($"configlib present, bound = {FornaxModSystem.ConfigLibBound}");
        Assert.True(FornaxModSystem.ConfigLibBound,
            "configlib is installed but did not take the config - the reflection binding has drifted");

        await Ticks(1);
    }

    /// <summary>
    /// The beehivekiln tag is read only when combustibleProps has nothing to say. Honouring it
    /// whenever it exists would be the truer simulation - a sealed chamber is a reducing firing,
    /// key "0" - but it would hand this kiln the tans and creams that are a beehive kiln's to
    /// give. Blue raw brick has both, and must still fire to what it has always fired to.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task TheBeehiveTagDoesNotOverrideAWaresOwnResult()
    {
        var brick = Sapi.World.GetItem(new AssetLocation("game:rawbrick-blue"));
        var beehive = brick.Attributes?["beehivekiln"];

        Assert.True(beehive?.Exists == true, "vanilla blue raw brick should carry the beehivekiln tag");
        Log($"beehivekiln[0] = {beehive["0"]?["code"]?.AsString()}, " +
            $"combustibleProps = {brick.CombustibleProps?.SmeltedStack?.Code}");

        // the two disagree, which is what makes this worth asserting at all
        Assert.Equal("burnedbrick-cream", beehive["0"]["code"].AsString());
        Assert.Equal("burnedbrick-gray", brick.CombustibleProps.SmeltedStack.Code.Path);

        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        for (int i = 0; i < 8 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        var got = World.BE<BlockEntityGroundStorage>(Ware(0, 0)).Inventory[0].Itemstack;
        Log($"8 blue raw brick -> {got?.StackSize}x {got?.Collectible?.Code}");
        Assert.Equal("game:burnedbrick-gray", got.Collectible.Code.ToString(),
            "combustibleProps wins; the beehive tag is a fallback, not an override");
    }

    [VsTest(TimeoutMs = 120000)]
    public async Task UnsealedEntranceBreaksTheStructure()
    {
        BuildKiln(sealEntrance: false);
        await Ticks(2);
        await Tick3s();

        Assert.True(!Be().StructureComplete, "an open loading entrance should invalidate the structure");
    }

    /// <summary>
    /// A kiln built on grass has soil under the combustion chamber, and soil grows tall grass
    /// into the air above it. That used to invalidate a finished kiln all by itself, days after
    /// it was built, with the culprit hidden under the grate.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task GrassGrowingInTheChamberDoesNotBreakTheStructure()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();
        Assert.True(Be().StructureComplete);

        // what BlockSoil.OnServerGameTick does to the cell above a grassy block
        BlockPos weed = Center().AddCopy(1, 0, 0);
        World.SetBlock("game:tallgrass-tall-free", weed);
        await Tick3s();

        Log($"chamber cell after a tick: {World.GetBlock(weed).Code}");
        Assert.Equal(0, World.GetBlock(weed).Id, "the chamber cell should have been cleared back to air");
        Assert.True(Be().StructureComplete, "grass in the chamber should be weeded, not fatal");
    }

    /// <summary>
    /// Weeding keeps the kiln working; firing it is what settles the matter, by turning the
    /// soil that does the growing into packed dirt that cannot.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task FiringBakesTheChamberFloorToPackedDirt()
    {
        BuildKiln();

        // grassy soil under the chamber, and one cell a player deliberately floored with brick
        BlockPos grassy = Center().AddCopy(1, -1, 0);
        BlockPos laid = Center().AddCopy(-1, -1, 0);
        World.SetBlock("game:soil-medium-normal", grassy);
        World.SetBlock(Wall, laid);

        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(be.StructureComplete);
        Assert.Equal("game:soil-medium-normal", World.GetBlock(grassy).Code.ToString());

        be.TryIgnite(null);
        await Hours(4);
        await Tick3s();
        await Tick3s();   // the floor is tended at the top of the tick, on the heat it had then

        Log($"at {be.ChamberTemperature:0}C the floor is {World.GetBlock(grassy).Code} / {World.GetBlock(laid).Code}");
        Assert.Greater(be.ChamberTemperature, 450f);
        Assert.Equal("game:packeddirt", World.GetBlock(grassy).Code.ToString(), "fired soil should bake");
        Assert.Equal(Wall, World.GetBlock(laid).Code.ToString(), "a floor someone laid should be left alone");
    }

    [VsTest(TimeoutMs = 120000)]
    public async Task CobIsAcceptedInPlaceOfMudBrick()
    {
        BuildKiln();
        await Ticks(2);
        World.SetBlock("game:cob-none", Fb().AddCopy(2, 0, -2));   // mid-wall, not a corner
        await Tick3s();

        Assert.True(Be().StructureComplete, "cob should be a legal wall material");
    }

    // ------------------------------------------------------------------
    //  Firing
    // ------------------------------------------------------------------

    [VsTest(TimeoutMs = 180000)]
    public async Task FiringConvertsGreenWaresAndCracksTheSeals()
    {
        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(be.StructureComplete);
        Assert.True(be.CanIgnite, "a complete, fuelled, unlit kiln should be ignitable");

        be.TryIgnite(null);
        Assert.True(be.Lit);

        // A firewood batch needs ~13 in-game hours; give it plenty and tick it through.
        for (int i = 0; i < 8 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(!be.Lit, "the firing should have finished and put itself out");

        var gs = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        Assert.NotNull(gs.Inventory[0].Itemstack);
        Assert.Equal("game:burnedbrick-gray", gs.Inventory[0].Itemstack.Collectible.Code.ToString());
        Assert.Equal(8, gs.Inventory[0].Itemstack.StackSize);

        Assert.Equal(Cracked, World.BlockCode(Fb().AddCopy(0, 2, 0)));
    }

    /// <summary>
    /// The firing cracks all six seals, so a kiln that has just done its job fails its own
    /// structure check. It must not report that as damage: what it says is that the batch is
    /// done, and the fired wares stay counted even though they are no longer fireable.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task AFinishedKilnSaysItIsDoneRatherThanBroken()
    {
        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        for (int i = 0; i < 8 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(!be.Lit, "the firing should have finished");
        Assert.True(be.BatchFired, "a finished batch should be flagged as waiting");

        int missing = be.CountMissing(out int cracked);
        Log($"after firing: {missing} wrong, {cracked} of them cracked seals");
        Assert.Equal(6, cracked, "every seal across the entrance should have cracked");
        Assert.Equal(missing, cracked, "nothing but the seals should be wrong with a fired kiln");

        Assert.Equal(0, be.CountWares(), "fired bricks are no longer green wares");
        Assert.Equal(8, be.CountFinishedWares(), "but they are still sitting on the grate");

        var dsc = new System.Text.StringBuilder();
        be.GetBlockInfo(null, dsc);
        string info = dsc.ToString();
        Log("block info:\n" + info.TrimEnd());

        Assert.True(info.Contains(Lang.Get("fornax:firing-done")), "it should say the firing is done");
        Assert.True(!info.Contains(Lang.Get("fornax:structure-incomplete", missing)),
            "and must not report the cracked seals as a broken structure");
        Assert.True(!info.Contains(Lang.Get("fornax:fuel-short", 50)),
            "nor nag about fuel for the next batch while this one is still in there");

        // Re-sealing starts the next cycle, and the kiln goes back to its ordinary reporting.
        for (int y = 2; y <= 3; y++)
        {
            for (int dx = -1; dx <= 1; dx++) World.SetBlock(Seal, Fb().AddCopy(dx, y, 0));
        }

        await Tick3s();

        Assert.True(be.StructureComplete);
        Assert.True(!be.BatchFired, "a re-sealed kiln is ready for the next batch, not holding the last one");
    }

    /// <summary>
    /// Running dry part way through must not throw the batch away. The kiln goes out, keeps the
    /// hours of heat it has already put in, and leaves the shell sealed and the wares green, so
    /// topping the firebox up and lighting it again finishes the same batch.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task RunningOutOfFuelPausesTheFiringRatherThanLosingIt()
    {
        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        Fuel("game:firewood", 4);          // 8 fuel-hours against a 60 fuel-hour batch
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);

        for (int i = 0; i < 10 && be.Lit; i++)
        {
            await Hours(2);
            await Tick3s();
        }

        Log($"out of fuel at {be.FiredEnergyHours:0.#}/20 hours, chamber {be.ChamberTemperature:0}C");

        Assert.True(!be.Lit, "an empty firebox should put the kiln out");
        Assert.Greater(be.FiredEnergyHours, 0.0);
        Assert.Less(be.FiredEnergyHours, 20.0);
        Assert.True(be.StructureComplete, "the shell should still be sealed");
        Assert.True(!be.BatchFired, "an unfinished batch is not a finished one");
        Assert.Equal(Seal, World.BlockCode(Fb().AddCopy(0, 2, 0)), "the seals only crack on a completed firing");

        var gs = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        Assert.Equal("game:rawbrick-blue", gs.Inventory[0].Itemstack.Collectible.Code.ToString());

        // still warm, so nothing has been forfeited yet: top it up and the batch carries on
        Assert.Greater(be.ChamberTemperature, (float)Cfg.AmbientTemperature);

        double banked = be.FiredEnergyHours;
        Fuel("game:firewood", 32);
        await Tick3s();

        Assert.True(be.CanIgnite, "a re-fuelled kiln should light without breaking the seals");
        Assert.Equal(banked, be.FiredEnergyHours, "refuelling must not reset the progress");

        be.TryIgnite(null);
        for (int i = 0; i < 10 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(be.BatchFired, "the resumed firing should finish the batch");
        Assert.Equal("game:burnedbrick-gray", gs.Inventory[0].Itemstack.Collectible.Code.ToString());
    }

    /// <summary>
    /// Leaving it, though, does cost. The climb to temperature is most of what a firing is, and
    /// a kiln that has gone stone cold has to make it again.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task LettingTheChamberGoColdForfeitsProgress()
    {
        BuildKiln();
        Fuel("game:firewood", 4);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);

        for (int i = 0; i < 10 && be.Lit; i++)
        {
            await Hours(2);
            await Tick3s();
        }

        double banked = be.FiredEnergyHours;
        Assert.Greater(banked, 0.0);
        Assert.Greater(be.ChamberTemperature, (float)Cfg.AmbientTemperature);

        // let it stand until the chamber is stone cold
        for (int i = 0; i < 10 && be.ChamberTemperature > Cfg.AmbientTemperature; i++)
        {
            await Hours(2);
            await Tick3s();
        }

        Log($"banked {banked:0.#}h, chamber now {be.ChamberTemperature:0}C, left with {be.FiredEnergyHours:0.#}h");

        Assert.Equal((float)Cfg.AmbientTemperature, be.ChamberTemperature, "the chamber should be back to ambient");
        Assert.Equal(Math.Max(0, banked - Cfg.ColdRestartPenaltyHours), be.FiredEnergyHours,
            "going cold should cost exactly the restart penalty");

        // and it is charged once, not on every tick from here on
        double afterCooling = be.FiredEnergyHours;
        await Hours(6);
        await Tick3s();
        Assert.Equal(afterCooling, be.FiredEnergyHours, "a kiln that is already cold cannot go cold again");
    }

    /// <summary>
    /// The draft vent is scenery with no block entity of its own, so only the firebox ever
    /// changes it. Break a lit firebox and nothing was left to turn the plume off.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task BreakingALitFireboxStopsTheVentSmoking()
    {
        BuildKiln();
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        await Tick3s();

        BlockPos vent = Center().AddCopy(0, 5, 0);
        Assert.Equal("fornax:kilnvent-drafting", World.BlockCode(vent), "a lit kiln should be drafting");

        World.SetBlock("game:air", Fb());
        await Ticks(4);

        Log($"firebox broken while lit: vent is {World.BlockCode(vent)}");
        Assert.Equal(Vent, World.BlockCode(vent), "a dismantled kiln should not keep smoking");
    }

    /// <summary>
    /// The multiblock definition is shared between kilns rather than deserialized per block
    /// entity, cached per rotation. A mistake in that key would have every kiln checking one
    /// orientation's layout, so each facing has to still lay its structure out its own way.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task EachFacingLaysItsStructureOutItsOwnWay()
    {
        // "side" is the face shown to the player, so the body runs the opposite way
        var expect = new (string side, int dx, int dz)[]
        {
            ("south", 0, -2), ("north", 0, 2), ("east", -2, 0), ("west", 2, 0)
        };

        BlockPos at = P(8, 1, 10);

        foreach (var c in expect)
        {
            World.SetBlock("game:air", at);
            await Ticks(1);
            World.SetBlock($"fornax:kilnfirebox-cold-{c.side}", at);
            await Ticks(2);

            var missing = World.BE<BlockEntityUpdraftFirebox>(at).MissingStructurePositions();
            double cx = missing.Average(p => p.X - at.X);
            double cz = missing.Average(p => p.Z - at.Z);

            Log($"{c.side}: {missing.Count} positions, centred on ({cx:0.##}, {cz:0.##})");
            Assert.Equal(c.dx, (int)Math.Round(cx), $"a {c.side}-facing kiln should run its body along x");
            Assert.Equal(c.dz, (int)Math.Round(cz), $"a {c.side}-facing kiln should run its body along z");
        }

        var cached = Sapi.ObjectCache.Keys.Where(k => k.StartsWith("fornax:multiblock-")).ToList();
        Log("shared structures: " + string.Join(", ", cached));
        Assert.Equal(4, cached.Count, "one shared definition per rotation, not one per firebox");
    }

    [VsTest(TimeoutMs = 180000)]
    public async Task HotterFuelFiresTheSameBatchFaster()
    {
        BuildKiln();
        Fuel("game:firewood", 40);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        await Hours(4);
        await Tick3s();
        double withWood = be.FiredEnergyHours;

        // Same elapsed time, hotter fuel: charcoal burns at 1.3x where firewood burns at 0.6x.
        World.SetBlock("game:air", Fb());
        BuildKiln();
        Fuel("game:charcoal", 40);
        await Ticks(2);
        await Tick3s();

        be = Be();
        be.TryIgnite(null);
        await Hours(4);
        await Tick3s();
        double withCharcoal = be.FiredEnergyHours;

        Log($"4h of firing: firewood {withWood:0.##} fuel-hours, charcoal {withCharcoal:0.##}");
        Assert.Greater(withCharcoal, withWood);
    }

    [VsTest(TimeoutMs = 180000)]
    public async Task FuelIsConsumedAsItBurns()
    {
        BuildKiln();
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        int before = be.FuelItemCount();
        be.TryIgnite(null);

        await Hours(6);
        await Tick3s();

        Log($"fuel {before} -> {be.FuelItemCount()} after 6h");
        Assert.Less(be.FuelItemCount(), before);
    }

    [VsTest(TimeoutMs = 120000)]
    public async Task ColdFuelIsRefused()
    {
        BuildKiln();
        await Ticks(2);

        var be = Be();
        // Dry grass burns at 600C, below the 650C floor.
        Assert.True(!be.IsValidFuel(World.Stack("game:drygrass", 4)), "dry grass is too cool to fire a kiln");
        Assert.True(be.IsValidFuel(World.Stack("game:firewood", 1)));
        Assert.True(be.IsValidFuel(World.Stack("game:charcoal", 1)));
        Assert.True(be.IsValidFuel(World.Stack("game:peatbrick", 1)));
    }


    // ------------------------------------------------------------------
    //  Lighting it, the way a player actually does
    // ------------------------------------------------------------------

    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task PlayerCanFuelAndLightTheKiln()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(be.StructureComplete, "structure should be complete before we try to light it");

        // 1. put fuel in the way a player does: hold it, right-click the firebox
        await Player.StandNear(Fb().AddCopy(0, 0, 2));
        await Player.Hold("game:firewood", 16);
        await Ticks(2);

        bool handled = be.OnPlayerInteract(Player.Me);
        await Ticks(2);
        Log($"fuel interaction handled={handled}, fuel now {be.FuelItemCount()}");
        Assert.True(be.FuelItemCount() > 0, "right-clicking with firewood should load the firebox");

        // 2. now the ignite path the firestarter takes
        Assert.True(be.CanIgnite, "a complete, fuelled, unlit kiln must report CanIgnite");

        var block = World.GetBlock(Fb()) as BlockUpdraftFirebox;
        Assert.NotNull(block);

        var state0 = block.OnTryIgniteBlock(Player.Me.Entity, Fb(), 0f);
        var state2 = block.OnTryIgniteBlock(Player.Me.Entity, Fb(), 2f);
        Log($"OnTryIgniteBlock: at 0s={state0}, at 2s={state2}");

        // ItemFirestarter bails unless the very first probe returns exactly Ignitable
        Assert.Equal(EnumIgniteState.Ignitable, state0);
        Assert.Equal(EnumIgniteState.IgniteNow, state2);

        var handling = EnumHandling.PassThrough;
        block.OnTryIgniteBlockOver(Player.Me.Entity, Fb(), 2f, ref handling);
        await Ticks(2);

        Assert.True(be.Lit, "the kiln should be lit after a completed firestarter use");
    }

    /// <summary>
    /// The same thing again, but driven entirely through the input system: aim at the firebox
    /// and hold right-click with a firestarter, exactly as a player does. The direct-call test
    /// above can pass while this one fails, because the real path goes through
    /// Block.OnBlockInteractStart first and only reaches ItemFirestarter if that declines.
    /// </summary>
    [VsTest(TimeoutMs = 240000)]
    [RequiresClient]
    public async Task LightingItWithARealFirestarterWorks()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.True(be.StructureComplete);

        await Player.StandNear(Fb().AddCopy(0, 0, 3));
        await Ticks(5);

        await Player.Hold("game:firewood", 16);
        await Ticks(5);
        await Interact.UseBlock(Fb(), BlockFacing.SOUTH);
        await Ticks(5);
        Log($"after right-clicking with firewood: fuel={be.FuelItemCount()}");
        Assert.True(be.FuelItemCount() > 0, "right-clicking the firebox with firewood should load it");

        // What does the CLIENT think? ItemFirestarter probes the client-side block entity
        // first, and gives up for good unless that one says Ignitable.
        await OnClient();
        var cbe = Vs.Capi.World.BlockAccessor.GetBlockEntity(Fb()) as BlockEntityUpdraftFirebox;
        string clientState = cbe == null
            ? "client BE MISSING"
            : $"lit={cbe.Lit} complete={cbe.StructureComplete} fuel={cbe.FuelItemCount()} canIgnite={cbe.CanIgnite}";
        await OnServer();
        Log("client sees: " + clientState);
        Log($"server sees: lit={be.Lit} complete={be.StructureComplete} fuel={be.FuelItemCount()} canIgnite={be.CanIgnite}");

        // ItemFirestarter.OnHeldInteractStop rolls world.Rand and gives up 75% of the time,
        // so a single hold proves nothing either way. This is vanilla and applies to firepits
        // too - a player just holds it again. Twenty attempts fail together ~0.3% of the time.
        await Player.Hold("game:firestarter", 1);
        await Ticks(5);

        int attempts = 0;
        for (; attempts < 20 && !be.Lit; attempts++)
        {
            await Interact.UseBlock(Fb(), BlockFacing.SOUTH, holdFrames: 150);
            await Ticks(4);
        }

        Log($"lit={be.Lit} after {attempts} firestarter attempt(s)");
        Assert.True(be.Lit, "holding a firestarter on the firebox should light the kiln");
    }

    /// <summary>
    /// The "your batch is done" line is only worth anything if it reaches the copy of the block
    /// entity the player is actually looking at. The client keeps its own, fed from the server's
    /// tree attributes, so a flag that never gets synced would leave the client still reporting
    /// the cracked seals as a broken kiln.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task TheClientIsToldTheBatchIsDone()
    {
        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 8);
        Fuel("game:firewood", 32);
        await Player.StandNear(Fb().AddCopy(0, 0, 3));
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        for (int i = 0; i < 8 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(be.BatchFired, "the server should be holding a finished batch");

        // The tree attributes go out on the next server tick and the client applies them when
        // the packet lands, so this is a wait, not a single reading.
        bool clientFired = false;
        bool haveBe = false;
        string info = "";

        for (int i = 0; i < 10 && !clientFired; i++)
        {
            await Ticks(2);

            await OnClient();
            var cbe = Vs.Capi.World.BlockAccessor.GetBlockEntity(Fb()) as BlockEntityUpdraftFirebox;
            var dsc = new System.Text.StringBuilder();
            cbe?.GetBlockInfo(Vs.Capi.World.Player, dsc);

            haveBe = cbe != null;
            clientFired = cbe?.BatchFired ?? false;
            info = dsc.ToString();
            await OnServer();
        }

        Log("client block info:\n" + info.TrimEnd());
        Assert.True(haveBe, "the client should have a block entity for the firebox");
        Assert.True(clientFired, "the client's block entity should know the batch is finished");
        Assert.True(info.Contains(Lang.Get("fornax:firing-done")),
            "and should say so where the player will read it");
    }

    /// <summary>
    /// Both halves of the build guide - the ghost mesh and the engine's highlight slot - are one
    /// per session, so a second kiln's guide necessarily replaces the first's. The first must
    /// know it has lost it: it used to go on believing it owned a guide it no longer had, and
    /// wipe the new one when its own chunk unloaded.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task OneKilnsBuildGuideDoesNotWipeAnothers()
    {
        BlockPos a = P(3, 1, 4);
        BlockPos b = P(9, 1, 12);
        World.SetBlock(Firebox, a);
        World.SetBlock(Firebox, b);
        await Ticks(6);

        async Task<(BlockPos owner, bool ghosts)> Toggle(BlockPos at)
        {
            await OnClient();
            var cbe = Vs.Capi.World.BlockAccessor.GetBlockEntity(at) as BlockEntityUpdraftFirebox;
            cbe?.ToggleBuildGuide(Vs.Capi.World.Player);
            var state = (FornaxModSystem.GuideOwner, FornaxModSystem.GhostRenderer?.HasGhosts ?? false);
            await OnServer();
            return state;
        }

        var up = await Toggle(a);
        Log($"guide up on A: owner={up.owner} ghosts={up.ghosts}");
        Assert.Equal(a, up.owner, "the kiln the guide was opened on should own it");
        Assert.True(up.ghosts);

        var moved = await Toggle(b);
        Log($"guide up on B: owner={moved.owner} ghosts={moved.ghosts}");
        Assert.Equal(b, moved.owner, "opening a second guide should hand ownership over");
        Assert.True(moved.ghosts);

        // A is dismantled while B still has the guide up: it must leave B's alone
        World.SetBlock("game:air", a);
        await Ticks(6);

        await OnClient();
        var ownerAfter = FornaxModSystem.GuideOwner;
        bool ghostsAfter = FornaxModSystem.GhostRenderer?.HasGhosts ?? false;
        await OnServer();

        Log($"after breaking A: owner={ownerAfter} ghosts={ghostsAfter}");
        Assert.Equal(b, ownerAfter, "breaking a kiln that lost the guide must not take B's");
        Assert.True(ghostsAfter, "and must not clear B's ghosts");

        // B closing its own guide still works
        var down = await Toggle(b);
        Log($"B toggled off: owner={down.owner} ghosts={down.ghosts}");
        Assert.Null(down.owner);
        Assert.True(!down.ghosts);
    }

    /// <summary>
    /// Singleplayer runs a mod system per side out of one assembly and disposes both, so the
    /// server side must keep its hands off the client's renderer. Calling its Dispose directly
    /// is safe - ModSystem.Dispose is empty, and the mod's override touches nothing else.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task DisposingTheServerSideLeavesTheRendererAlone()
    {
        BlockPos fb = P(6, 1, 8);
        World.SetBlock(Firebox, fb);
        await Ticks(6);

        await OnClient();
        var cbe = Vs.Capi.World.BlockAccessor.GetBlockEntity(fb) as BlockEntityUpdraftFirebox;
        cbe?.ToggleBuildGuide(Vs.Capi.World.Player);
        bool ghostsUp = FornaxModSystem.GhostRenderer?.HasGhosts ?? false;
        var clientSystem = Vs.Capi.ModLoader.GetModSystem<FornaxModSystem>();
        await OnServer();

        Assert.True(ghostsUp, "the guide should be up before we start tearing things down");

        var serverSystem = Sapi.ModLoader.GetModSystem<FornaxModSystem>();
        Assert.True(!ReferenceEquals(serverSystem, clientSystem), "each side should have its own instance");

        serverSystem.Dispose();

        await OnClient();
        bool alive = FornaxModSystem.GhostRenderer != null;
        bool stillDrawn = FornaxModSystem.GhostRenderer?.HasGhosts ?? false;
        var owner = FornaxModSystem.GuideOwner;
        await OnServer();

        Log($"after the server side disposed: renderer={alive} ghosts={stillDrawn} owner={owner}");
        Assert.True(alive, "the server side must not throw away the client's renderer");
        Assert.True(stillDrawn, "nor the guide the player is looking at");
        Assert.Equal(fb, owner);

        // put it back the way the test found it
        await OnClient();
        cbe?.ToggleBuildGuide(Vs.Capi.World.Player);
        await OnServer();
    }

    /// <summary>
    /// A lit torch is the reliable way in: BlockBehaviorCanIgnite wants a three second hold
    /// but rolls no dice, where the firestarter is 1.5s with a 25% chance. An UNLIT torch has
    /// no CanIgnite behaviour at all and does nothing, which is an easy thing to be caught by.
    /// </summary>
    [VsTest(TimeoutMs = 240000)]
    [RequiresClient]
    public async Task ALitTorchLightsItToo()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 8);
        be.Inventory[0].MarkDirty();
        be.MarkDirty(true);
        await Ticks(5);
        Assert.True(be.CanIgnite);

        await Player.StandNear(Fb().AddCopy(0, 0, 3));
        await Player.Hold("game:torch-basic-lit-up", 1);
        await Ticks(5);

        // needs secondsUsed >= 3, so hold well past that
        await Interact.UseBlock(Fb(), BlockFacing.SOUTH, holdFrames: 300);
        await Ticks(10);

        Log($"after a lit torch: lit={be.Lit}");
        Assert.True(be.Lit, "a lit torch held on the firebox should light the kiln");
    }


    /// <summary>Needs a client: the ignite probe is driven through the player's own entity.</summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task AnIncompleteKilnRefusesToLightAndSaysWhy()
    {
        BuildKiln(sealEntrance: false);        // entrance left open
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 8);
        be.Inventory[0].MarkDirty();

        Assert.True(!be.StructureComplete);
        Assert.True(!be.CanIgnite, "an unsealed kiln must not light");

        var block = World.GetBlock(Fb()) as BlockUpdraftFirebox;
        var state = block.OnTryIgniteBlock(Player.Me.Entity, Fb(), 0f);
        Log("unsealed kiln ignite probe: " + state);
        Assert.Equal(EnumIgniteState.NotIgnitablePreventDefault, state);

        // and with no fuel at all, once it is sealed
        BuildKiln();
        be.Inventory[0].Itemstack = null;
        be.Inventory[0].MarkDirty();
        await Tick3s();

        Assert.True(be.StructureComplete);
        Assert.True(!be.CanIgnite, "a fuelless kiln must not light");
        Assert.Equal(EnumIgniteState.NotIgnitablePreventDefault,
            block.OnTryIgniteBlock(Player.Me.Entity, Fb(), 0f));
    }


    /// <summary>
    /// A firing kiln has to look like one from the outside. The flames are sealed inside the
    /// chamber where nothing can see them, so lighting it swaps the firebox and the vent to
    /// variants that emit light, glow, and carry the plume - and putting it out swaps back.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task LightingItChangesHowItLooksFromOutside()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        BlockPos ventPos = Center().AddCopy(0, 5, 0);
        Assert.Equal("fornax:kilnvent-idle", World.BlockCode(ventPos));
        Assert.Equal(Firebox, World.BlockCode(Fb()));
        Assert.Equal(0, (int)World.GetBlock(Fb()).LightHsv[2]);

        // Two full slots, well past the 60 fuel-hours a batch needs. The test steps time in
        // three-hour jumps and the kiln draws a whole step's worth at once, so a batch that
        // finishes mid-step swallows whatever is left - fund it generously or the "ready"
        // check below turns into a check on the step size.
        var be = Be();
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[1].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[0].MarkDirty();
        be.Inventory[1].MarkDirty();
        be.TryIgnite(null);
        await Ticks(4);

        Assert.Equal("fornax:kilnfirebox-warming-south", World.BlockCode(Fb()));
        Assert.Equal("fornax:kilnvent-drafting", World.BlockCode(ventPos));

        var lit = World.GetBlock(Fb());
        Log($"lit firebox: light={lit.LightHsv[0]},{lit.LightHsv[1]},{lit.LightHsv[2]} particles={lit.ParticleProperties?.Length ?? 0}");
        Assert.Greater((int)lit.LightHsv[2], 0, "a lit firebox must emit light");
        Assert.Greater(lit.ParticleProperties?.Length ?? 0, 0, "a lit firebox must declare particles");

        var vent = World.GetBlock(ventPos);
        Log($"drafting vent: particles={vent.ParticleProperties?.Length ?? 0}");
        Assert.Greater(vent.ParticleProperties?.Length ?? 0, 0, "a drafting vent must declare a plume");

        // exchanging the firebox must not have thrown the fuel or the progress away
        Assert.NotNull(World.BEOrNull<BlockEntityUpdraftFirebox>(Fb()));
        Assert.True(Be().Lit);
        Assert.True(Be().FuelItemCount() > 0, "swapping the block must keep the fuel");

        // and it all goes back when the firing ends
        for (int i = 0; i < 10 && Be().Lit; i++) { await Hours(3); await Tick3s(); }
        Assert.True(!Be().Lit);
        Assert.Equal("fornax:kilnvent-idle", World.BlockCode(ventPos));

        // fuel is left over, so it settles on "ready" rather than "cold"
        Log("after the firing: " + World.BlockCode(Fb()) + ", fuel left=" + Be().FuelItemCount());
        Assert.Equal("fornax:kilnfirebox-ready-south", World.BlockCode(Fb()));
        Assert.Equal(0, (int)World.GetBlock(Fb()).LightHsv[2]);
    }


    /// <summary>
    /// Block particles hang off Block.TopMiddlePos, which is the block's TOP face - and the
    /// firebox's top face is buried under the wall course above it. Particles declared without
    /// compensating for that spawn inside solid stone and are never seen. Rendering cannot be
    /// photographed in this harness (a vanilla lit firepit photographs no flames either), so
    /// this checks the thing that IS checkable: where the engine will put them.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task ParticlesSpawnInOpenAirNotInsideTheStructure()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        // Two full slots, well past the 60 fuel-hours a batch needs. The test steps time in
        // three-hour jumps and the kiln draws a whole step's worth at once, so a batch that
        // finishes mid-step swallows whatever is left - fund it generously or the "ready"
        // check below turns into a check on the step size.
        var be = Be();
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[1].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[0].MarkDirty();
        be.Inventory[1].MarkDirty();
        be.TryIgnite(null);
        await Ticks(4);

        CheckSpawns(Fb(), "firebox");
        CheckSpawns(Center().AddCopy(0, 5, 0), "vent");
    }

    private static void CheckSpawns(BlockPos pos, string what)
    {
        var block = World.GetBlock(pos);
        Assert.NotNull(block.ParticleProperties);
        Assert.Greater(block.ParticleProperties.Length, 0, what + " declares no particles");

        for (int i = 0; i < block.ParticleProperties.Length; i++)
        {
            var pp = block.ParticleProperties[i];
            if (pp.Quantity.avg <= 0) continue;      // registration placeholder

            // exactly what Block.OnAsyncClientParticleTick does
            double x = pos.X + block.TopMiddlePos.X + pp.PosOffset[0].avg;
            double y = pos.Y + block.TopMiddlePos.Y + pp.PosOffset[1].avg;
            double z = pos.Z + block.TopMiddlePos.Z + pp.PosOffset[2].avg;

            var at = new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), pos.dimension);
            var hit = World.GetBlock(at);
            Log($"{what}[{i}] spawns at {x:0.00},{y:0.00},{z:0.00} -> {hit.Code}");

            Assert.True(hit.Id == 0 || hit.CollisionBoxes == null || hit.CollisionBoxes.Length == 0,
                $"{what} particle group {i} spawns inside {hit.Code} at {at} - nothing will ever be seen");
        }
    }


    /// <summary>cold = empty, ready = fuelled and waiting, lit = firing.</summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task FireboxShowsWhetherItIsEmptyFuelledOrFiring()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();
        Assert.Equal("fornax:kilnfirebox-cold-south", World.BlockCode(Fb()));

        var be = Be();

        // a player shoving fuel in should flip it to "ready" straight away
        await Player.Hold("game:firewood", 16);
        be.OnPlayerInteract(Player.Me);
        await Ticks(2);
        Assert.Equal("fornax:kilnfirebox-ready-south", World.BlockCode(Fb()));
        Assert.Equal(0, (int)World.GetBlock(Fb()).LightHsv[2]);

        Be().TryIgnite(null);
        await Ticks(2);
        Assert.Equal("fornax:kilnfirebox-warming-south", World.BlockCode(Fb()));
        Assert.Greater((int)World.GetBlock(Fb()).LightHsv[2], 0);

        // burn it out: no fuel left means it must land on cold, not ready
        for (int i = 0; i < 15 && Be().Lit; i++) { await Hours(3); await Tick3s(); }
        Assert.True(!Be().Lit);
        Log("after burning out: " + World.BlockCode(Fb()) + ", fuel=" + Be().FuelItemCount());
        Assert.Equal("fornax:kilnfirebox-cold-south", World.BlockCode(Fb()));
    }

    /// <summary>
    /// The firebox goes red-and-smoky while the chamber comes up, then bright orange once it
    /// is hot. The colour change lands on the shatter threshold on purpose: when it turns, the
    /// kiln has become expensive to open.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task FireboxLooksDifferentWarmingUpThanFiring()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        // Two full slots, well past the 60 fuel-hours a batch needs. The test steps time in
        // three-hour jumps and the kiln draws a whole step's worth at once, so a batch that
        // finishes mid-step swallows whatever is left - fund it generously or the "ready"
        // check below turns into a check on the step size.
        var be = Be();
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[1].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[0].MarkDirty();
        be.Inventory[1].MarkDirty();
        be.TryIgnite(null);
        await Ticks(2);

        Assert.Equal("fornax:kilnfirebox-warming-south", World.BlockCode(Fb()));
        var warming = World.GetBlock(Fb());
        Log($"warming: light={warming.LightHsv[0]},{warming.LightHsv[1]},{warming.LightHsv[2]} temp={Be().ChamberTemperature:0}");

        // heat it past the threshold
        while (Be().Lit && Be().ChamberTemperature < 500) { await Hours(1); await Tick3s(); }

        Assert.Equal("fornax:kilnfirebox-firing-south", World.BlockCode(Fb()));
        var firing = World.GetBlock(Fb());
        Log($"firing:  light={firing.LightHsv[0]},{firing.LightHsv[1]},{firing.LightHsv[2]} temp={Be().ChamberTemperature:0}");

        // a genuine difference in both hue and brightness, not just one of them
        Assert.True(warming.LightHsv[0] != firing.LightHsv[0], "warming and firing should differ in hue");
        Assert.Greater((int)firing.LightHsv[2], (int)warming.LightHsv[2]);
        Assert.Greater((int)warming.LightHsv[2], 0, "warming should still glow a little");
    }

    /// <summary>A sealed, burning kiln is not something you can reach into.</summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task FuelCannotBeAddedOrRemovedWhileFiring()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        var be = Be();
        await Player.Hold("game:firewood", 16);
        be.OnPlayerInteract(Player.Me);
        await Ticks(2);

        int loaded = be.FuelItemCount();
        Assert.True(loaded > 0);

        be.TryIgnite(null);
        await Ticks(2);
        Assert.True(be.Lit);

        // adding more is refused, and the click is swallowed rather than falling through
        await Player.Hold("game:firewood", 16);
        bool handled = be.OnPlayerInteract(Player.Me);
        await Ticks(2);
        Log($"refuel while firing: handled={handled}, fuel {loaded} -> {be.FuelItemCount()}");
        Assert.True(handled, "the interaction should be consumed, not passed on");
        Assert.Equal(loaded, be.FuelItemCount());

        // and so is taking it back out. Emptying the hand and holding shift is what
        // actually reaches the unload branch - without the sneak it would fall through
        // to the status readout and pass for the wrong reason.
        var hand = Player.Me.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = null;
        hand.MarkDirty();
        Player.Me.WorldData.EntityControls.ShiftKey = true;
        await Ticks(2);

        int before = be.FuelItemCount();
        bool unloadHandled = be.OnPlayerInteract(Player.Me);
        await Ticks(2);
        Player.Me.WorldData.EntityControls.ShiftKey = false;

        Log($"unload while firing: handled={unloadHandled}, fuel {before} -> {be.FuelItemCount()}");
        Assert.True(unloadHandled);
        Assert.Equal(before, be.FuelItemCount());

        // once it is out, the fuel is reachable again
        be.Lit = false;
        be.RefreshAppearance();
        Player.Me.WorldData.EntityControls.ShiftKey = true;
        be.OnPlayerInteract(Player.Me);
        await Ticks(2);
        Player.Me.WorldData.EntityControls.ShiftKey = false;
        Log($"unload once out: fuel {before} -> {be.FuelItemCount()}");
        Assert.Less(be.FuelItemCount(), before);
    }

    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task BlockInfoOnlyMentionsFuelWhenItIsShort()
    {
        BuildKiln();
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 2);
        be.Inventory[0].MarkDirty();
        var dsc = new System.Text.StringBuilder();
        be.GetBlockInfo(Player.Me, dsc);
        Log("2 firewood -> " + dsc.ToString().Replace("\n", " | ").Trim());
        Assert.Contains(dsc.ToString(), "%");

        be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[0].MarkDirty();
        dsc.Clear();
        be.GetBlockInfo(Player.Me, dsc);
        string plenty = dsc.ToString();
        Log("16 firewood -> " + plenty.Replace("\n", " | ").Trim());
        Assert.True(!plenty.Contains("enough"), "no line at all when the fuel will see the batch out");
        Assert.True(!plenty.Contains("%"), "and no percentage either");
        await Ticks(1);
    }

    // ------------------------------------------------------------------
    //  Build guide
    // ------------------------------------------------------------------

    /// <summary>
    /// Drop a firebox on its own and the guide should ghost in the entire rest of the kiln;
    /// finish the kiln and there should be nothing left to show.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task BuildGuideGhostsInEverythingStillToPlace()
    {
        // a lone firebox, nothing else
        World.SetBlock(Firebox, Fb());
        await Ticks(4);

        var be = Be();
        Assert.NotNull(be);

        var colors = new System.Collections.Generic.List<int>();
        var wanted = new System.Collections.Generic.List<Block>();
        var missing = be.MissingStructurePositions(colors, wanted);
        Log($"lone firebox -> {missing.Count} blocks ghosted, {colors.Count} colours, {wanted.Count} blocks");

        // Every ghost must name a real block to draw, or the guide shows a shape with no
        // identity - which is the whole point of it.
        var byName = new System.Collections.Generic.Dictionary<string, int>();
        for (int i = 0; i < wanted.Count; i++)
        {
            Assert.NotNull(wanted[i]);
            Assert.True(wanted[i].Id != 0, $"ghost {i} at {missing[i]} resolved to air");
            string code = wanted[i].Code.ToString();
            byName[code] = byName.TryGetValue(code, out var n) ? n + 1 : 1;
        }
        foreach (var kv in byName) Log($"   {kv.Value,3} x {kv.Key}");

        Assert.Equal(66, byName["game:mudbrick-dark"]);
        Assert.Equal(9, byName["fornax:kilngrate"]);
        Assert.Equal(6, byName["fornax:kilnseal-intact"]);
        Assert.Equal(1, byName["fornax:kilnvent-idle"]);

        // Of the 110 structure positions: the firebox is already right and the 27 chamber
        // positions are already clear, so what is ghosted is exactly what you must build -
        // 66 walls + 9 grate tiles + 6 mud seals + 1 vent.
        Assert.Equal(missing.Count, colors.Count);
        Assert.Equal(66 + 9 + 6 + 1, missing.Count);

        // every ghost must sit inside the kiln's footprint
        foreach (var p in missing)
        {
            Assert.InRange(p.X - Fb().X, -2, 2);
            Assert.InRange(p.Y - Fb().Y, 0, 5);
            Assert.InRange(p.Z - Fb().Z, -4, 0);
        }

        // build it properly and the guide should have nothing to say
        BuildKiln();
        await Ticks(4);
        var left = Be().MissingStructurePositions();
        Log($"finished kiln -> {left.Count} blocks ghosted");
        Assert.Equal(0, left.Count);
    }

    /// <summary>An obstruction in the chamber is a different problem from an unbuilt wall.</summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task BuildGuideFlagsThingsInTheWay()
    {
        BuildKiln();
        await Ticks(4);
        Assert.Equal(0, Be().MissingStructurePositions().Count);

        // drop a block into the ware chamber, which has to stay clear
        World.SetBlock(Wall, Ware(0, 0));
        await Ticks(4);

        var colors = new System.Collections.Generic.List<int>();
        var missing = Be().MissingStructurePositions(colors);
        Log($"blocked chamber -> {missing.Count} flagged at {missing[0]}");

        Assert.Equal(1, missing.Count);
        Assert.Equal(Ware(0, 0), missing[0]);
        // flagged red, unlike anything merely unbuilt
        Assert.Equal(ColorUtil.ColorFromRgba(215, 70, 70, 130), colors[0]);
    }

    // ------------------------------------------------------------------
    //  Handbook
    // ------------------------------------------------------------------

    /// <summary>
    /// The handbook page exists, and every handbooksearch:// link in it actually names
    /// something in the game. A dead link renders as a link and simply finds nothing, so
    /// there is no way to notice one by reading the page.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task HandbookEntryResolvesAllOfItsLinks()
    {
        // Read the page config the game reads, so a mismatch between it and the lang file
        // fails here rather than rendering the raw key in-game.
        var page = Sapi.Assets.TryGet("fornax:config/handbook/50-fornax.json")
            ?.ToObject<Dictionary<string, string>>();
        Assert.NotNull(page);

        string title = Lang.Get(page["title"]);
        string text = Lang.Get(page["text"]);
        Log($"page title key = {page["title"]}");

        Assert.True(!title.Contains("gamemechanicinfo"), "title lang key is missing");
        Assert.True(!text.Contains("gamemechanicinfo"), "text lang key is missing");
        Assert.Greater(text.Length, 1000, "handbook page looks too thin");
        Assert.Contains(text, "<strong>Updraft Kiln</strong>");

        // Every display name in the game. GetMatching, not Get: names are often declared
        // with wildcard keys (block-kilnfirebox-cold-*) which a literal Get will not resolve.
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var b in Sapi.World.Blocks)
        {
            if (b?.Code == null) continue;
            names.Add(Lang.GetMatching(b.Code.Domain + ":block-" + b.Code.Path).ToLowerInvariant());
        }
        foreach (var i in Sapi.World.Items)
        {
            if (i?.Code == null) continue;
            names.Add(Lang.GetMatching(i.Code.Domain + ":item-" + i.Code.Path).ToLowerInvariant());
        }

        var links = System.Text.RegularExpressions.Regex.Matches(text, "handbooksearch://([^\"]+)");
        Assert.Greater(links.Count, 5, "expected the page to cross-link the parts");

        var dead = new System.Collections.Generic.List<string>();
        foreach (System.Text.RegularExpressions.Match m in links)
        {
            string term = m.Groups[1].Value.ToLowerInvariant();
            bool found = false;
            foreach (var n in names) if (n.Contains(term)) { found = true; break; }
            if (!found) dead.Add(term);
        }

        Log($"{links.Count} handbook links checked, {dead.Count} dead");
        if (dead.Count > 0) Log("dead: " + string.Join(", ", dead));
        Assert.Equal(0, dead.Count);
        await Ticks(1);
    }

    // ------------------------------------------------------------------
    //  Thermal shock
    // ------------------------------------------------------------------

    [VsTest(TimeoutMs = 180000)]
    public async Task BreachingAColdKilnCostsNothing()
    {
        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 16);
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        await Tick3s();   // barely any heat yet

        Log($"chamber at breach: {be.ChamberTemperature:0}C");
        Assert.Less(be.ChamberTemperature, 450f);

        World.SetBlock("game:air", Fb().AddCopy(0, 2, 0));   // pull a seal
        await Tick3s();

        var gs = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        Assert.Equal(16, gs.Inventory[0].Itemstack.StackSize);
    }

    [VsTest(TimeoutMs = 180000)]
    public async Task BreachingAHotKilnShattersSomeWares()
    {
        BuildKiln();
        LoadWare(0, 0, "game:rawbrick-blue", 24);   // a full pile; the maxFireable cap is off by default
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        await Hours(4);
        await Tick3s();

        Log($"chamber before breach: {be.ChamberTemperature:0}C");
        Assert.Greater(be.ChamberTemperature, 450f);

        World.SetBlock("game:air", Fb().AddCopy(0, 2, 0));
        await Tick3s();

        var gs = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        int left = gs.Inventory[0].Itemstack?.StackSize ?? 0;

        Log($"24 raw bricks -> {left} survived a breach at {be.ChamberTemperature:0}C");
        Assert.Less(left, 24);
        Assert.Greater(left, 0);
        Assert.True(!be.StructureComplete, "a breached kiln should be paused");
    }
}

/// <summary>
/// Everything above runs headless, where the game never builds a mesh. These do, because a
/// mistyped texture or shape path is invisible until something tries to draw it.
/// </summary>
public class FornaxVisualTests
{
    private const string Wall = "game:mudbrick-dark";
    private const string Grate = "fornax:kilngrate";
    private const string Seal = "fornax:kilnseal-intact";
    private const string Vent = "fornax:kilnvent-idle";
    private const string Firebox = "fornax:kilnfirebox-cold-south";

    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task WarmingAndFiringSideBySide()
    {
        await World.SetCalendarTo(500 * 24 + 22);   // late evening, so the glow tells

        string[] codes = {
            "fornax:kilnfirebox-cold-north",
            "fornax:kilnfirebox-ready-north",
            "fornax:kilnfirebox-warming-north",
            "fornax:kilnfirebox-firing-north"
        };
        for (int i = 0; i < codes.Length; i++) World.SetBlock(codes[i], P(5 + i * 2, 1, 10));

        await Ticks(10);
        await Player.Teleport(P(8, 2, 4));
        await Ticks(10);
        await Interact.LookAt(P(8, 1, 10));
        await Frames.Wait(60);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "firebox-states.png"));
        Log("cold / ready / warming / firing: " + path);
        Assert.NotNull(path);
    }

    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task RawAndFiredGrateTilesLookLikeTheSameObject()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        // fired tiles placed as blocks
        World.SetBlock("fornax:kilngrate", P(6, 1, 10));
        World.SetBlock("fornax:kilngrate", P(7, 1, 10));

        // raw tiles as a ground pile, and a single one
        foreach (var (pos, n) in new[] { (P(9, 1, 10), 8), (P(11, 1, 10), 1) })
        {
            World.SetBlock("game:groundstorage", pos);
            var gs = World.BEOrNull<BlockEntityGroundStorage>(pos);
            if (gs == null) continue;
            gs.Inventory[0].Itemstack = World.Stack("fornax:kilngrateraw", n);
            gs.Inventory[0].MarkDirty();
            gs.MarkDirty(true);
        }

        await Ticks(10);
        await Player.Teleport(P(8, 2, 5));
        await Ticks(10);
        await Interact.LookAt(P(8, 1, 10));
        await Frames.Wait(60);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "grate-tiles.png"));
        Log("fired blocks / raw pile of 8 / single raw: " + path);
        Assert.NotNull(path);
    }

    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task GrateTopDownAgainstVanillaGrating()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        // Hollow out under the two placed blocks only, so their slots have somewhere dark
        // to show through. Ground storage needs something to rest on, so it stays on turf.
        foreach (var x in new[] { 3, 6 })
        {
            for (int y = -1; y >= -3; y--) World.SetBlock("game:air", P(x, y, 10));
        }
        World.SetBlock("fornax:kilngrate", P(3, 0, 10));
        World.SetBlock("game:refractorybrickgrating-good-tier1", P(6, 0, 10));

        foreach (var (x, n) in new[] { (9, 1), (11, 3), (13, 8) })
        {
            World.SetBlock("game:groundstorage", P(x, 1, 10));
            var gs = World.BEOrNull<BlockEntityGroundStorage>(P(x, 1, 10));
            if (gs == null) { Log($"no ground storage at x={x}"); continue; }
            gs.Inventory[0].Itemstack = World.Stack("fornax:kilngrateraw", n);
            gs.Inventory[0].MarkDirty();
            gs.MarkDirty(true);
        }

        await Ticks(10);
        await Player.Teleport(P(8, 3, 14));
        await Ticks(10);
        await Interact.LookAt(P(8, 1, 10));
        await Frames.Wait(60);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "grate-topdown.png"));
        Log("fired / vanilla grating / raw x1 / raw x3 / raw x8: " + path);
        Assert.NotNull(path);
    }

    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    [PlotSize(24, 32)]
    public async Task BuildGuideGhostRenders()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        BlockPos fb = P(12, 1, 14);
        World.SetBlock("fornax:kilnfirebox-cold-south", fb);
        await Ticks(6);

        var be = World.BE<BlockEntityUpdraftFirebox>(fb);
        await OnClient();
        var cbe = Vs.Capi.World.BlockAccessor.GetBlockEntity(fb) as BlockEntityUpdraftFirebox;
        cbe?.ToggleBuildGuide(Vs.Capi.World.Player);
        await OnServer();

        await Ticks(10);
        await Player.Teleport(P(12, 3, 22));
        await Ticks(10);
        await Interact.LookAt(P(12, 3, 14));
        await Frames.Wait(60);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "build-guide.png"));
        Log($"ghost of {be.MissingStructurePositions().Count} blocks: {path}");
        Assert.NotNull(path);
    }

    /// <summary>
    /// Puts every one of the mod's items in the hotbar and photographs it, so the inventory
    /// icons can actually be looked at. A wrong guiTransform is invisible from code - the block
    /// is perfectly valid, it just renders flat or overflowing its slot.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task InventoryIconsRenderInTheSlot()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        string[] codes = {
            // The north variant is what you craft and what drops, and at the default block
            // gui transform it happens to present its mouth to the viewer - so it reads as a
            // firebox rather than as a plain mud brick cube.
            "fornax:kilnfirebox-cold-north",
            "fornax:kilnvent-idle",
            "fornax:kilngrate",
            "fornax:kilnseal-intact",
            "fornax:kilngrateraw",
            "game:mudbrick-dark",
        };

        var hotbar = Player.Me.InventoryManager.GetHotbarInventory();
        for (int i = 0; i < codes.Length; i++)
        {
            var stack = World.Stack(codes[i], 4);
            Assert.NotNull(stack);
            hotbar[i].Itemstack = stack;
            hotbar[i].MarkDirty();
        }

        await Ticks(10);
        await Frames.Wait(40);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "inventory-icons.png"));
        Log("firebox / vent / grate / seal / raw tile / vanilla mudbrick: " + path);
        Assert.NotNull(path);
    }

    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task EveryKilnBlockDrawsWithARealTexture()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        string[] codes = { "fornax:kilnfirebox-firing-north", Grate, Seal, Vent, "fornax:kilnseal-cracked" };
        for (int i = 0; i < codes.Length; i++)
        {
            World.SetBlock(codes[i], P(4 + i * 2, 1, 10));
        }

        await Ticks(4);
        await Player.Teleport(P(8, 2, 4));
        await Ticks(10);
        await Interact.LookAt(P(8, 1, 10));
        await Frames.Wait(30);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "updraft-blocks.png"));

        Log($"block row screenshot: {path}");
        Assert.NotNull(path);
    }

    [VsTest(TimeoutMs = 240000)]
    [RequiresClient]
    [PlotSize(40, 32)]
    public async Task AssembledKilnRendersAndFires()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        BlockPos f = P(20, 1, 16);
        FornaxTests.Build(f);

        var be = World.BE<BlockEntityUpdraftFirebox>(f);
        be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
        be.Inventory[0].MarkDirty();

        await Ticks(4);
        for (int i = 0; i < 3; i++) await World.TickNow(f);

        Assert.True(be.StructureComplete, "the assembled kiln should validate on the client run too");
        be.TryIgnite(null);
        be.MarkDirty(true);

        await Ticks(150);          // let the client sync Lit and the smoke plume build
        await Player.Teleport(P(10, 3, 31));
        await Ticks(10);
        await Interact.LookAt(P(20, 4, 16));
        await Frames.Wait(90);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "updraft-kiln-lit.png"));

        Log($"assembled kiln screenshot: {path}");
        Assert.NotNull(path);
    }

    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(90, 32)]
    public async Task ShowcaseRowRenders()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        // Same seven stages the showcase world ships: ground course, grate, wares,
        // flue, corbel, capped, then sealed and firing.
        const int pitch = 9;
        for (int i = 0; i < 7; i++)
        {
            BlockPos f = P(12 + i * pitch, 1, 20);
            int upto = Math.Min(i, 5);
            BuildStage(f, upto, sealIt: i == 6);

            if (i >= 2)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz >= -3; dz--)
                    {
                        BlockPos wp = f.AddCopy(dx, 2, dz);
                        World.SetBlock("game:groundstorage", wp);
                        var gs = World.BEOrNull<BlockEntityGroundStorage>(wp);
                        if (gs == null) continue;
                        gs.Inventory[0].Itemstack = World.Stack("game:rawbrick-blue", 12);
                        gs.Inventory[0].MarkDirty();
                        gs.MarkDirty(true);
                    }
                }
            }

            if (i == 6)
            {
                var be = World.BE<BlockEntityUpdraftFirebox>(f);
                be.Inventory[0].Itemstack = World.Stack("game:firewood", 32);
                be.Inventory[0].MarkDirty();
                await Tick3s(f);
                be.TryIgnite(null);
                be.MarkDirty(true);
            }
        }

        // The harness client runs at viewDistance 32 and this row is 63 wide, so the
        // far end simply is not sent to the client at the distance you need to stand.
        await OnClient();
        Vs.Capi.Settings.Int["viewDistance"] = 192;
        await OnServer();

        await Ticks(60);
        await Player.Teleport(P(39, 3, 62));
        await Ticks(30);
        await Interact.LookAt(P(39, 4, 20));
        await Frames.Wait(90);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "updraft-stages.png"));

        Log("stage row screenshot: " + path);
        Assert.NotNull(path);
    }

    /// <summary>Builds the kiln only up to course <paramref name="upto"/>, for the stage row.</summary>
    private static void BuildStage(BlockPos f, int upto, bool sealIt)
    {
        for (int y = 0; y <= Math.Min(upto, 3); y++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = 0; dz >= -4; dz--)
                {
                    bool isCorner = Math.Abs(dx) == 2 && (dz == 0 || dz == -4);
                    bool perim = Math.Abs(dx) == 2 || dz == 0 || dz == -4;
                    string code;
                    if (isCorner && y >= 2) code = "game:air";
                    else if (perim)
                    {
                        if (y == 0 && dx == 0 && dz == 0) code = Firebox;
                        else if ((y == 2 || y == 3) && dz == 0 && Math.Abs(dx) <= 1) code = sealIt ? Seal : "game:air";
                        else code = Wall;
                    }
                    else
                    {
                        if (y == 0) code = (dx == 0 && dz == -2) ? Wall : "game:air";
                        else if (y == 1) code = Grate;
                        else code = "game:air";
                    }
                    World.SetBlock(code, f.AddCopy(dx, y, dz));
                }
            }
        }

        if (upto >= 4)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz >= -3; dz--)
                    World.SetBlock((dx == 0 && dz == -2) ? "game:air" : Wall, f.AddCopy(dx, 4, dz));

        if (upto >= 5)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz >= -3; dz--)
                    World.SetBlock((dx == 0 && dz == -2) ? Vent : Wall, f.AddCopy(dx, 5, dz));
    }

    private static async Task Tick3s(BlockPos pos)
    {
        for (int i = 0; i < 3; i++) await World.TickNow(pos);
    }
}

/// <summary>
/// Control experiment: can this harness photograph engine-spawned block particles at all?
/// A lit vanilla firepit is known-good, so if its flames do not appear here, no screenshot
/// of our own particles proves anything either way.
/// </summary>
public class ParticleControlTests
{
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task VanillaFirepitParticlesArePhotographable()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        World.SetBlock("game:firepit-lit", P(8, 1, 10));
        await Ticks(10);

        var block = World.GetBlock(P(8, 1, 10));
        Log($"firepit-lit particle groups: {block.ParticleProperties?.Length ?? 0}, light={block.LightHsv[2]}");

        await Player.Teleport(P(8, 1, 5));
        await Ticks(10);
        await Interact.LookAt(P(8, 1, 10));

        // "Takes a few seconds for the game to register the block" - Block.OnAsyncClientParticleTick
        await Ticks(200);
        await Frames.Wait(120);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "control-firepit.png"));
        Log("control screenshot: " + path);
        Assert.NotNull(path);
    }
}
