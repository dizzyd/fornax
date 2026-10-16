using System;
using System.Threading.Tasks;
using Fornax;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Flint calcines in the kiln only inside a lidded sagger. Vanilla flint is left as it is -
/// not ground storable and not kiln work - so every way in that is not a sagger is checked to
/// stay shut, as well as the one that is open.
/// </summary>
public class SaggerTests
{
    private const string Open = "fornax:sagger-open";
    private const string Lidded = "fornax:sagger-closed";

    private static BlockPos Fb() => P(8, 1, 12);
    private static BlockPos Ware(int dx, int dz) => Fb().AddCopy(dx, 2, dz - 2);

    private static BlockEntityUpdraftFirebox Be() => World.BE<BlockEntityUpdraftFirebox>(Fb());

    private static FornaxConfig Cfg => FornaxModSystem.Config;

    private static async Task Tick3s()
    {
        for (int i = 0; i < 3; i++) await World.TickNow(Fb());
    }

    private static void Fuel(string code, int count)
    {
        var be = Be();
        be.Inventory[0].Itemstack = World.Stack(code, count);
        be.Inventory[0].MarkDirty();
        be.MarkDirty(true);
    }

    /// <summary>Stands a sagger on the grate holding <paramref name="count"/> flint, written past the slot's rules.</summary>
    private static BlockEntitySagger PlaceSagger(int dx, int dz, string state, int count = 16)
    {
        BlockPos pos = Ware(dx, dz);
        World.SetBlock(state, pos);

        var sagger = World.BE<BlockEntitySagger>(pos);
        Assert.NotNull(sagger, $"{state} should carry a sagger block entity");

        if (count > 0)
        {
            sagger.Slot.Itemstack = World.Stack("game:flint", count);
            sagger.Slot.MarkDirty();
            sagger.MarkDirty(true);
        }

        return sagger;
    }

    private static async Task FireToTheEnd()
    {
        var be = Be();
        be.TryIgnite(null);
        Assert.True(be.Lit, "the kiln should light");

        for (int i = 0; i < 10 && be.Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(be.BatchFired, "the kiln should have fired");
    }

    [VsTest]
    public async Task TheSaggerChainResolves()
    {
        foreach (var code in new[] { Open, Lidded })
        {
            World.SetBlock(code, P(2, 1, 2));
            await Ticks(1);
            Assert.Equal(code, World.BlockCode(P(2, 1, 2)));
            Assert.NotNull(World.BE<BlockEntitySagger>(P(2, 1, 2)), $"{code} needs its block entity");
        }

        var raw = Sapi.World.GetItem(new AssetLocation("fornax:saggerraw"));
        Assert.NotNull(raw, "the raw sagger should be registered");

        var fired = raw.CombustibleProps?.SmeltedStack?.ResolvedItemstack;
        Log($"raw sagger fires to {fired?.Collectible?.Code} at {raw.CombustibleProps?.MeltingPoint}C");
        Assert.Equal(Open, fired?.Collectible?.Code?.ToString(), "a raw sagger fires into an open one");
        Assert.NotNull(raw.GetBehavior<CollectibleBehaviorGroundStorable>(), "and must stand on a grate or in a pit kiln to fire");

        var recipe = Sapi.GetClayformingRecipes().Find(r => r.Output?.ResolvedItemstack?.Collectible == raw);
        Assert.NotNull(recipe, "the raw sagger should be clay-formed");
        Assert.Equal("game:clay-fire", recipe.Ingredient.Code.ToString(), "from fire clay alone");

        // The lid is formed with it, as vanilla's crock's is: the ninth layer covers the
        // whole interior the walls leave open, so it is paid for in clay rather than free.
        Assert.Equal(10, recipe.Pattern.Length, "box, lid and knob");
        int lidVoxels = 0, interiorCovered = 0;
        for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
            {
                if (!recipe.Voxels[x, 8, z]) continue;
                lidVoxels++;
                if (x >= 3 && x <= 12 && z >= 3 && z <= 12) interiorCovered++;
            }
        Log($"lid layer: {lidVoxels} voxels, {interiorCovered} of 100 over the interior");
        Assert.Equal(100, interiorCovered, "the lid closes the box");
        Assert.Equal(196, lidVoxels, "and overhangs the walls by one voxel all round");

        var calcined = Sapi.World.GetItem(new AssetLocation("game:calcined-flint"));
        Assert.NotNull(calcined, "vanilla's calcined flint is what comes out");
    }

    [VsTest]
    public async Task TheSlotTakesFlintOnlyAndOnlyWithTheLidOff()
    {
        var sagger = PlaceSagger(0, 0, Open, count: 0);
        await Ticks(1);

        var flint = new DummySlot(World.Stack("game:flint", 64));
        var clay = new DummySlot(World.Stack("game:clay-blue", 8));

        Assert.True(sagger.Slot.CanHold(flint), "an open sagger takes flint");
        Assert.True(!sagger.Slot.CanHold(clay), "and nothing else");

        int moved = flint.TryPutInto(Sapi.World, sagger.Slot, 64);
        Log($"64 flint offered, {moved} taken (capacity {Cfg.SaggerCapacity})");
        Assert.Equal(Cfg.SaggerCapacity, moved, "it holds SaggerCapacity and no more");

        Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation(Lidded)).Id, sagger.Pos);
        await Ticks(1);

        var same = World.BE<BlockEntitySagger>(sagger.Pos);
        Assert.True(same.Lidded, "the lid is the block variant");
        Assert.Equal(Cfg.SaggerCapacity, same.Slot.StackSize, "and putting it on keeps what is inside");
        Assert.True(!same.Slot.CanTake(), "nothing comes out with the lid on");
        Assert.True(!same.Slot.CanHold(new DummySlot(World.Stack("game:flint", 1))), "and nothing goes in");
    }

    [VsTest(TimeoutMs = 180000)]
    public async Task ALiddedSaggerOfFlintCalcines()
    {
        FornaxTests.Build(Fb());
        PlaceSagger(0, 0, Lidded);
        Fuel("game:charcoal", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.Equal(16, be.CountWares(), "lidded flint is a ware");
        Assert.Null(be.FirstUnfireableOnGrate());
        Assert.Null(be.FirstTooColdOnGrate(), "charcoal reaches flint's thousand degrees");
        Assert.True(!be.HasOpenSaggerOnGrate());

        await FireToTheEnd();

        var got = World.BE<BlockEntitySagger>(Ware(0, 0)).Slot.Itemstack;
        Log($"16 flint -> {got?.StackSize}x {got?.Collectible?.Code}");
        Assert.Equal("game:calcined-flint", got?.Collectible?.Code?.ToString());
        Assert.Equal(16, got.StackSize, "smeltedRatio 1");
        Assert.Null(be.FirstUnfireableOnGrate(), "a finished sagger is not something the kiln failed at");
        Assert.Equal(16, be.CountFinishedWares());
    }

    [VsTest(TimeoutMs = 180000)]
    public async Task AnOpenSaggerDoesNotFireAndSaysSo()
    {
        FornaxTests.Build(Fb());
        PlaceSagger(0, 0, Open);
        Fuel("game:charcoal", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        Assert.Equal(0, be.CountWares(), "flint in an open sagger is not a ware");
        Assert.True(be.HasOpenSaggerOnGrate(), "and the kiln says why");
        Assert.Null(be.FirstUnfireableOnGrate(), "rather than calling flint unfireable, which sends the player the wrong way");

        var dsc = new System.Text.StringBuilder();
        be.GetBlockInfo(null, dsc);
        Assert.True(dsc.ToString().Contains(Lang.Get("fornax:sagger-open-in-kiln")), "on the block info too");
    }

    /// <summary>
    /// A finished kiln counts what it fired. Flint in a sagger left open did not fire, and
    /// counting it as finished told the player it had.
    /// </summary>
    [VsTest(TimeoutMs = 180000)]
    public async Task AnOpenSaggersFlintIsNotCountedAsFired()
    {
        FornaxTests.Build(Fb());
        PlaceSagger(0, 0, Lidded);
        PlaceSagger(1, 0, Open);
        Fuel("game:charcoal", 32);
        await Ticks(2);
        await Tick3s();

        await FireToTheEnd();

        var be = Be();
        Log($"after firing: finished={be.CountFinishedWares()}, open sagger still holds " +
            $"{World.BE<BlockEntitySagger>(Ware(1, 0)).Slot.Itemstack?.Collectible?.Code}");
        Assert.Equal("game:flint", World.BE<BlockEntitySagger>(Ware(1, 0)).Slot.Itemstack?.Collectible?.Code?.ToString());
        Assert.Equal(16, be.CountFinishedWares(), "the lidded sagger's calcined flint, and not the open one's flint");
    }

    [VsTest(TimeoutMs = 120000)]
    public async Task FirewoodCannotReachFlintsTemperature()
    {
        FornaxTests.Build(Fb());
        PlaceSagger(0, 0, Lidded);
        Fuel("game:firewood", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        var tooCold = be.FirstTooColdOnGrate();
        be.ChamberVersus(tooCold ?? World.Stack("game:flint", 1), out int reaches, out int needs);
        Log($"firewood -> reaches {reaches}, flint needs {needs}");

        Assert.NotNull(tooCold, "firewood's 950 does not reach flint's 1000, and the kiln says so");
        Assert.Equal(1000, needs);
        Assert.Equal(0, be.CountWares());
    }

    /// <summary>
    /// The sagger is the only way in. A pile is not something vanilla flint can be made into,
    /// but a mod could make it one, and that must not turn flint into kiln work behind its back.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    public async Task FlintOutsideASaggerIsNotKilnWork()
    {
        FornaxTests.Build(Fb());
        World.SetBlock("game:groundstorage", Ware(0, 0));
        var gs = World.BE<BlockEntityGroundStorage>(Ware(0, 0));
        gs.Inventory[0].Itemstack = World.Stack("game:flint", 12);
        gs.Inventory[0].MarkDirty();
        gs.MarkDirty(true);

        Fuel("game:charcoal", 32);
        await Ticks(2);
        await Tick3s();

        Log($"flint in a pile -> wares={Be().CountWares()}, unfireable={Be().FirstUnfireableOnGrate() ?? "(none)"}");
        Assert.Equal(0, Be().CountWares());
        Assert.NotNull(Be().FirstUnfireableOnGrate(), "and it is reported like anything else the kiln will not fire");
    }

    [VsTest(TimeoutMs = 180000)]
    public async Task ALiddedSaggerKeepsItsFlintThroughABreach()
    {
        FornaxTests.Build(Fb());
        PlaceSagger(0, 0, Lidded);
        World.SetBlock("game:groundstorage", Ware(1, 0));
        var gs = World.BE<BlockEntityGroundStorage>(Ware(1, 0));
        gs.Inventory[0].Itemstack = World.Stack("game:rawbrick-blue", 24);
        gs.Inventory[0].MarkDirty();
        gs.MarkDirty(true);

        Fuel("game:charcoal", 32);
        await Ticks(2);
        await Tick3s();

        var be = Be();
        be.TryIgnite(null);
        await Hours(3);
        await Tick3s();

        Log($"chamber before breach: {be.ChamberTemperature:0}C");
        Assert.Greater(be.ChamberTemperature, 900f, "hot enough that half the bricks are at risk");

        World.SetBlock("game:air", Fb().AddCopy(0, 2, 0));
        await Tick3s();

        int bricks = gs.Inventory[0].Itemstack?.StackSize ?? 0;
        int flint = World.BE<BlockEntitySagger>(Ware(0, 0)).Slot.StackSize;
        Log($"after breach: {bricks}/24 bricks, {flint}/16 flint");

        Assert.Less(bricks, 24, "the open pile beside it shattered");
        Assert.Equal(16, flint, "the sagger's flint did not");
    }
}

/// <summary>
/// The sagger worked by hand. Real clicks for flint in and out, since those go client first and
/// the server is the one that moves anything; the lid through the server with sneak held,
/// which is how the firebox's own sneak test reaches its branch.
/// </summary>
public class SaggerPlayerTests
{
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task APlayerFillsLidsAndEmptiesASagger()
    {
        BlockPos pos = P(8, 1, 8);
        World.SetBlock("fornax:sagger-open", pos);
        await Ticks(2);

        var sagger = World.BE<BlockEntitySagger>(pos);
        await Player.StandNear(pos);

        await Player.Hold("game:clay-blue", 4);
        await Interact.UseBlock(pos);
        await Ticks(2);
        Assert.True(sagger.Slot.Empty, "clay is refused");

        await Player.Hold("game:flint", 8);
        var held = Player.Me.InventoryManager.ActiveHotbarSlot;
        await Interact.UseBlock(pos);
        await Interact.UseBlock(pos);
        await Ticks(2);

        Log($"two clicks with 8 flint: sagger holds {sagger.Slot.StackSize}, hand {held.StackSize}");
        Assert.Equal(2, sagger.Slot.StackSize, "one flint a click");
        Assert.Equal(6, held.StackSize);

        held.Itemstack = null;
        held.MarkDirty();
        Player.Me.WorldData.EntityControls.ShiftKey = true;
        await Ticks(2);
        sagger.OnInteract(Player.Me);
        await Ticks(2);
        Player.Me.WorldData.EntityControls.ShiftKey = false;

        Assert.Equal("fornax:sagger-closed", World.BlockCode(pos), "sneak with an empty hand puts the lid on");
        Assert.Equal(Lang.Get("fornax:block-sagger-closed"), World.GetBlock(pos).GetPlacedBlockName(Sapi.World, pos), "and the name says so");

        await Interact.UseBlock(pos);
        await Ticks(2);
        Assert.Equal(2, World.BE<BlockEntitySagger>(pos).Slot.StackSize, "nothing comes out with the lid on");

        Player.Me.WorldData.EntityControls.ShiftKey = true;
        await Ticks(2);
        World.BE<BlockEntitySagger>(pos).OnInteract(Player.Me);
        await Ticks(2);
        Player.Me.WorldData.EntityControls.ShiftKey = false;
        Assert.Equal("fornax:sagger-open", World.BlockCode(pos), "and the same lifts it");

        await Interact.UseBlock(pos);
        await Ticks(2);
        Log($"empty-hand click: sagger holds {World.BE<BlockEntitySagger>(pos).Slot.StackSize}, hand {held.StackSize}x {held.Itemstack?.Collectible?.Code}");
        Assert.True(World.BE<BlockEntitySagger>(pos).Slot.Empty, "an empty hand takes the contents back");
        Assert.Equal(2, held.StackSize);

        // A sagger that has fired takes no fresh flint until it is emptied: flint does not
        // stack onto calcined flint, so the click is refused rather than quietly doing nothing.
        var after = World.BE<BlockEntitySagger>(pos);
        after.Slot.Itemstack = World.Stack("game:calcined-flint", 4);
        after.Slot.MarkDirty();
        after.MarkDirty(true);
        await Ticks(2);

        Assert.True(!after.HasRoomForFlint, "a fired sagger has no room for flint, whatever its space");
        await Interact.UseBlock(pos);
        await Ticks(2);
        Log($"flint onto calcined: sagger {after.Slot.StackSize}x {after.Slot.Itemstack?.Collectible?.Code}, hand {held.StackSize}");
        Assert.Equal("game:calcined-flint", after.Slot.Itemstack?.Collectible?.Code?.ToString());
        Assert.Equal(2, held.StackSize, "and the hand keeps its flint");
    }
}

/// <summary>The sagger's shapes and textures, which nothing headless ever draws.</summary>
public class SaggerVisualTests
{
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task SaggersDrawOpenLiddedAndRaw()
    {
        await World.SetCalendarTo(500 * 24 + 12);

        World.SetBlock("fornax:sagger-open", P(6, 1, 10));
        World.SetBlock("fornax:sagger-closed", P(8, 1, 10));

        World.SetBlock("game:groundstorage", P(10, 1, 10));
        var gs = World.BE<BlockEntityGroundStorage>(P(10, 1, 10));
        gs.Inventory[0].Itemstack = World.Stack("fornax:saggerraw", 1);
        gs.Inventory[0].MarkDirty();
        gs.MarkDirty(true);

        var hotbar = Player.Me.InventoryManager.GetHotbarInventory();
        hotbar[0].Itemstack = World.Stack("fornax:sagger-open", 1);
        hotbar[1].Itemstack = World.Stack("fornax:saggerraw", 1);
        hotbar[0].MarkDirty();
        hotbar[1].MarkDirty();

        await Ticks(4);
        await Player.Teleport(P(8, 2, 6));
        await Ticks(10);
        await Interact.LookAt(P(8, 1, 10));
        await Frames.Wait(30);

        string path = await Shot.Take(System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", "saggers.png"));
        Log($"open / lidded / raw-in-a-pile, and both in the hotbar: {path}");
        Assert.NotNull(path);
    }
}
