using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fornax;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The sagger driven the way a player drives it: real clicks, a real sneak key and a real Ctrl
/// key through the input layer, and the client's own error and hint output read back.
///
/// Writing Controls.ShiftKey on the server, as SaggerPlayerTests does for the lid, proves the
/// branch works and nothing about whether the key gets there - the client recomputes its
/// controls from the keyboard every frame. These go through the keyboard.
/// </summary>
public class SaggerClientTests
{
    private const string Open = "fornax:sagger-open";
    private const string Lidded = "fornax:sagger-closed";

    private static BlockPos Spot() => P(8, 1, 8);

    private static async Task<BlockEntitySagger> PlaceSagger(BlockPos pos, string state, string code = null, int count = 0)
    {
        World.SetBlock(state, pos);
        var sagger = World.BE<BlockEntitySagger>(pos);
        if (code != null)
        {
            sagger.Slot.Itemstack = World.Stack(code, count);
            sagger.Slot.MarkDirty();
            sagger.MarkDirty(true);
        }

        await Ticks(4);
        return sagger;
    }

    private static async Task EmptyHand()
    {
        var hand = Player.Me.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = null;
        hand.MarkDirty();
        await Ticks(4);
    }

    /// <summary>
    /// Sneak-click, releasing the key whatever happens. The shift modifier goes with the key:
    /// the bare key is enough for a block's own click, but not for setting a held item down.
    /// </summary>
    public static async Task SneakUse(BlockPos pos, BlockFacing face = null)
    {
        await Input.KeyDown(GlKeys.ShiftLeft, shift: true);

        // Sneaking lowers the eye, and over several frames. Aim before it settles and the
        // selection can change under the click - to whatever the lower sightline hits first.
        await Frames.Wait(20);
        try
        {
            await Interact.UseBlock(pos, face);
            await Ticks(6);
        }
        finally
        {
            await Input.KeyUp(GlKeys.ShiftLeft);
            await Frames.Wait(3);
        }
    }

    /// <summary>Ctrl-click, releasing the key whatever happens.</summary>
    public static async Task CtrlUse(BlockPos pos, BlockFacing face = null)
    {
        await Input.KeyDown(GlKeys.ControlLeft, ctrl: true);
        try
        {
            await Interact.UseBlock(pos, face);
        }
        finally
        {
            await Input.KeyUp(GlKeys.ControlLeft);
        }

        await Ticks(4);
    }

    /// <summary>Every in-game error the client shows while <paramref name="act"/> runs, as "code|text".</summary>
    public static async Task<List<string>> ErrorsDuring(Func<Task> act)
    {
        var seen = new List<string>();
        IngameErrorDelegate onError = (_, code, text) => seen.Add(code + "|" + text);

        await OnClient();
        Capi.Event.InGameError += onError;
        await OnServer();

        try
        {
            await act();
            await Ticks(4);
        }
        finally
        {
            await OnClient();
            Capi.Event.InGameError -= onError;
            await OnServer();
        }

        return seen;
    }

    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task TheLidGoesOnAndOffWithARealSneakClick()
    {
        var sagger = await PlaceSagger(Spot(), Open, "game:flint", 4);
        await Player.StandNear(Spot());
        await EmptyHand();

        await SneakUse(Spot());
        Log($"after a sneak-click: {World.BlockCode(Spot())}");
        Assert.Equal(Lidded, World.BlockCode(Spot()), "a sneak-click with an empty hand puts the lid on");
        Assert.Equal(4, World.BE<BlockEntitySagger>(Spot()).Slot.StackSize, "and keeps what is inside");

        await SneakUse(Spot());
        Assert.Equal(Open, World.BlockCode(Spot()), "and the same lifts it");
        Assert.True(Player.Held == null, "neither click put anything in the hand");
    }

    /// <summary>
    /// Ctrl puts in as much as fits, which only works if the Ctrl key reaches the server with
    /// the click. The firebox's bulk fuel load leans on exactly the same thing.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task CtrlPutsInAsMuchAsFits()
    {
        var sagger = await PlaceSagger(Spot(), Open);
        await Player.StandNear(Spot());
        await Player.Hold("game:flint", 20);

        await CtrlUse(Spot());

        Log($"ctrl-click with 20 flint: sagger {sagger.Slot.StackSize}, hand {Player.Held?.StackSize}");
        Assert.Equal(Cfg.SaggerCapacity, sagger.Slot.StackSize, "a Ctrl-click fills the sagger");
        Assert.Equal(20 - Cfg.SaggerCapacity, Player.Held.StackSize, "and leaves the rest in the hand");
    }

    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task EveryRefusalSaysWhy()
    {
        var sagger = await PlaceSagger(Spot(), Open);
        await Player.StandNear(Spot());

        await Player.Hold("game:clay-blue", 4);
        var clay = await ErrorsDuring(() => Interact.UseBlock(Spot()));
        Log("clay: " + string.Join("; ", clay));
        Assert.True(clay.Contains("flintonly|" + Lang.Get("fornax:sagger-flint-only")), "clay is refused, and the player told why");

        sagger.Slot.Itemstack = World.Stack("game:flint", Cfg.SaggerCapacity);
        sagger.Slot.MarkDirty();
        sagger.MarkDirty(true);
        await Ticks(4);
        await Player.Hold("game:flint", 4);
        var full = await ErrorsDuring(() => Interact.UseBlock(Spot()));
        Log("full: " + string.Join("; ", full));
        Assert.True(full.Contains("full|" + Lang.Get("fornax:sagger-full")), "a full sagger says it is full");

        sagger.Slot.Itemstack = World.Stack("game:calcined-flint", 4);
        sagger.Slot.MarkDirty();
        sagger.MarkDirty(true);
        await Ticks(4);
        var calcined = await ErrorsDuring(() => Interact.UseBlock(Spot()));
        Log("calcined: " + string.Join("; ", calcined));
        Assert.True(calcined.Contains("emptyfirst|" + Lang.Get("fornax:sagger-empty-it-first")),
            "flint onto calcined flint says to empty it first");

        await EmptyHand();
        await SneakUse(Spot());
        Assert.Equal(Lidded, World.BlockCode(Spot()));
        await Player.Hold("game:flint", 4);
        var lidded = await ErrorsDuring(() => Interact.UseBlock(Spot()));
        Log("lidded: " + string.Join("; ", lidded));
        Assert.True(lidded.Contains("lidded|" + Lang.Get("fornax:sagger-lidded")), "a lidded sagger says how to lift the lid");
        Assert.Equal(4, Player.Held.StackSize, "and none of those clicks took anything");
    }

    /// <summary>The hints under the crosshair offer exactly what a click would do, in every state.</summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task TheHintsFollowTheState()
    {
        var cases = new (string State, string Code, int Count, string[] Expected)[]
        {
            (Open, null, 0, new[] { "sagger-add", "sagger-lid-on" }),
            (Open, "game:flint", 4, new[] { "sagger-add", "sagger-take", "sagger-lid-on" }),
            (Open, "game:flint", -1, new[] { "sagger-take", "sagger-lid-on" }),
            (Open, "game:calcined-flint", 4, new[] { "sagger-take", "sagger-lid-on" }),
            (Lidded, "game:flint", 4, new[] { "sagger-lid-off" }),
        };

        foreach (var (state, code, count, expected) in cases)
        {
            await PlaceSagger(Spot(), state, code, count < 0 ? Cfg.SaggerCapacity : count);
            await Ticks(4);

            await OnClient();
            var block = Capi.World.BlockAccessor.GetBlock(Spot());
            var sel = new BlockSelection { Position = Spot().Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
            string[] got = block.GetPlacedBlockInteractionHelp(Capi.World, sel, Capi.World.Player)
                .Select(h => h.ActionLangCode)
                .Where(c => c.StartsWith("fornax:blockhelp-fornax-sagger-"))
                .Select(c => c.Substring("fornax:blockhelp-fornax-".Length))
                .ToArray();
            await OnServer();

            string label = $"{state} with {(code == null ? "nothing" : $"{(count < 0 ? "a full load of" : count.ToString())} {code}")}";
            Log($"{label}: {string.Join(", ", got)}");
            Assert.Equal(string.Join(",", expected), string.Join(",", got), label);
        }
    }

    /// <summary>
    /// Broken by hand in survival, a lidded sagger comes back as an open one - a lid is not a
    /// thing to carry - and what was inside spills rather than vanishing with it.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task BreakingALiddedSaggerByHandDropsAnOpenOneAndItsFlint()
    {
        var mode = Player.Me.WorldData.CurrentGameMode;
        try
        {
            await Player.SetGameMode(EnumGameMode.Survival);
            await PlaceSagger(Spot(), Lidded, "game:flint", 12);
            await Player.StandNear(Spot());
            await EmptyHand();

            await Interact.BreakBlock(Spot());
            await Ticks(10);

            var drops = World.Entities(Spot(), 3)
                .OfType<EntityItem>()
                .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));
            Log("drops: " + string.Join(", ", drops.Select(d => $"{d.Value}x {d.Key}")));

            Assert.Equal("game:air", World.BlockCode(Spot()));
            Assert.Equal(1, drops.GetValueOrDefault(Open), "one open sagger");
            Assert.Equal(0, drops.GetValueOrDefault(Lidded), "never a lidded one");
            Assert.Equal(12, drops.GetValueOrDefault("game:flint"), "and every flint that was in it");
        }
        finally
        {
            await Player.SetGameMode(mode);
        }
    }

    /// <summary>The firebox's own click says when a sagger on the grate has its lid off.</summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task ClickingTheFireboxSaysASaggerIsOpen()
    {
        BlockPos fb = P(8, 1, 12);
        FornaxTests.Build(fb);
        await PlaceSagger(fb.AddCopy(0, 2, -2), Open, "game:flint", 8);
        for (int i = 0; i < 3; i++) await World.TickNow(fb);

        await Player.StandNear(fb);
        await EmptyHand();
        var errors = await ErrorsDuring(() => Interact.UseBlock(fb, BlockFacing.SOUTH));

        Log("firebox click: " + string.Join("; ", errors));
        Assert.True(errors.Contains("saggeropen|" + Lang.Get("fornax:sagger-open-in-kiln")),
            "the firebox says the lid is off");
    }

    private static FornaxConfig Cfg => FornaxModSystem.Config;
}

/// <summary>A real hopper over a sagger: the lid shuts it out, and only flint goes in.</summary>
public class SaggerHopperTests
{
    [VsTest(TimeoutMs = 120000)]
    public async Task AHopperFeedsAnOpenSaggerFlintAndALiddedOneNothing()
    {
        BlockPos below = P(8, 1, 8);
        BlockPos hopperPos = below.UpCopy();

        World.SetBlock("fornax:sagger-closed", below);
        World.SetBlock("game:hopper", hopperPos);
        await Ticks(2);

        var hopper = World.BE<BlockEntityItemFlow>(hopperPos);
        hopper.Inventory[0].Itemstack = World.Stack("game:flint", 6);
        hopper.Inventory[1].Itemstack = World.Stack("game:clay-blue", 2);
        hopper.Inventory[0].MarkDirty();
        hopper.Inventory[1].MarkDirty();

        for (int i = 0; i < 20; i++) await World.TickNow(hopperPos);
        var sagger = World.BE<BlockEntitySagger>(below);
        Log($"lidded: sagger holds {sagger.Slot.StackSize}");
        Assert.True(sagger.Slot.Empty, "nothing goes into a lidded sagger");

        Sapi.World.BlockAccessor.ExchangeBlock(World.Block("fornax:sagger-open").Id, below);
        await Ticks(2);

        // Past the six flint, so the hopper gets its turns at the clay as well.
        for (int i = 0; i < 60; i++) await World.TickNow(hopperPos);

        sagger = World.BE<BlockEntitySagger>(below);
        Log($"open: sagger holds {sagger.Slot.StackSize}x {sagger.Slot.Itemstack?.Collectible?.Code}, " +
            $"hopper still has {hopper.Inventory[1].Itemstack?.StackSize}x {hopper.Inventory[1].Itemstack?.Collectible?.Code}");
        Assert.Equal("game:flint", sagger.Slot.Itemstack?.Collectible?.Code?.ToString());
        Assert.Equal(6, sagger.Slot.StackSize, "the flint goes in");
        Assert.Equal(2, hopper.Inventory[1].Itemstack?.StackSize ?? 0, "and the clay stays in the hopper");
    }
}

/// <summary>
/// The whole sagger, start to finish, the way a player gets there: a raw sagger set on the grate,
/// fired, picked up, set down again as a sagger, filled with flint, lidded and fired again, and
/// the calcined flint taken out by hand. Every step is a real click through the client.
///
/// A brick kiln, because its wicket is luted with a click and swings open by itself when the
/// firing is done - so loading and unloading both happen through the doorway, by hand, with no
/// wall to rebuild in between. The clay forming is the one step not done here: the recipe's
/// layers are asserted in SaggerTests, and placing six hundred voxels by hand proves nothing more.
/// </summary>
public class SaggerEndToEndTests
{
    private static BlockPos Fb() => P(8, 1, 12);

    /// <summary>The front-centre grate, straight in through the wicket, and the ware cell above it.</summary>
    private static BlockPos FrontGrate() => Fb().AddCopy(0, 1, -1);
    private static BlockPos FrontWare() => Fb().AddCopy(0, 2, -1);

    private static BlockPos Wicket(int y) => Fb().AddCopy(0, y, 0);

    /// <summary>
    /// A two-block step in front of the wicket. From the ground the eye is below the grate's top
    /// face and cannot see it at all; from one block up it can, but a sneaking eye looks along
    /// the top of the wall under the wicket and the click lands on the wall. Two up, it looks
    /// down through the doorway with room to spare.
    /// </summary>
    private static BlockPos Step() => Fb().AddCopy(0, 0, 2);

    private static BlockEntityUpdraftFirebox Be() => World.BE<BlockEntityUpdraftFirebox>(Fb());

    private static async Task Tick3s()
    {
        for (int i = 0; i < 3; i++) await World.TickNow(Fb());
    }

    private static async Task StandAtTheWicket()
    {
        World.SetBlock("game:claybricks-good-fire", Step());
        World.SetBlock("game:claybricks-good-fire", Step().UpCopy());
        await Ticks(2);
        await Player.Teleport(Step().UpCopy(2));
        await Ticks(6);
    }

    /// <summary>Off the step and in front of the firebox mouth, where the firewood goes in.</summary>
    private static async Task StandAtTheFirebox()
    {
        World.SetBlock("game:air", Step());
        World.SetBlock("game:air", Step().UpCopy());
        await Ticks(2);
        await Player.Teleport(Fb().AddCopy(0, 0, 3));
        await Ticks(6);
    }

    private static async Task EmptyHand()
    {
        var hand = Player.Me.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = null;
        hand.MarkDirty();
        await Ticks(4);
    }

    /// <summary>
    /// Player.Hold writes the server's copy of the hand. A click on a block is answered from
    /// that copy, but setting something on the ground starts in the held item's own handler on
    /// the client - which acts on whatever the client thinks is in the hand, and that lags.
    /// </summary>
    private static async Task HoldOnTheClientToo(string code, int quantity)
    {
        await Player.Hold(code, quantity);

        string clientHas = null;
        for (int i = 0; i < 60 && clientHas != code; i++)
        {
            await OnClient();
            clientHas = Capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
            await OnServer();
            if (clientHas != code) await Ticks(1);
        }

        Assert.Equal(code, clientHas, "the client should be holding it before it is used");
    }

    /// <summary>
    /// How many of something the player carries, wherever it went. Taking from a pile or a
    /// sagger gives the stack to whichever slot suits it, not necessarily the one in hand.
    /// The creative inventory is left out, since it holds one of everything.
    /// </summary>
    private static int Carried(string code)
    {
        int total = 0;
        foreach (var inv in Player.Me.InventoryManager.Inventories.Values)
        {
            if (inv == null || inv.ClassName.StartsWith("creative")) continue;
            foreach (var slot in inv)
            {
                if (slot?.Itemstack?.Collectible?.Code?.ToString() == code) total += slot.StackSize;
            }
        }

        return total;
    }

    private static async Task LuteTheWicket()
    {
        await Player.Hold("game:clay-blue", 2);
        await BrickKilnTests.UsePanel(Wicket(2));
        await BrickKilnTests.UsePanel(Wicket(3));
        await Ticks(4);
        Assert.Equal("fornax:kilndoor-luted-south", World.BlockCode(Wicket(2)), "the lower panel is luted");
        Assert.Equal("fornax:kilndoor-luted-south", World.BlockCode(Wicket(3)), "and the upper one");
    }

    /// <summary>Loads the firebox with a Ctrl-click and lights it with a real firestarter.</summary>
    private static async Task FuelAndLight(string fuel, int count)
    {
        await StandAtTheFirebox();
        for (int i = 0; i < 3; i++) await World.TickNow(Fb());

        await Player.Hold(fuel, count);
        await SaggerClientTests.CtrlUse(Fb(), BlockFacing.SOUTH);
        Log($"ctrl-click with {count} {fuel}: firebox holds {Be().FuelItemCount()}");
        Assert.Equal(count, Be().FuelItemCount(), "a Ctrl-click loads the whole stack");

        await Tick3s();
        Assert.True(Be().CanIgnite, "a luted, fuelled kiln is ready to light");

        // ItemFirestarter gives up 75% of the time on its own; a player simply tries again.
        await Player.Hold("game:firestarter", 1);
        for (int i = 0; i < 20 && !Be().Lit; i++)
        {
            await Interact.UseBlock(Fb(), BlockFacing.SOUTH, holdFrames: 150);
            await Ticks(4);
        }

        Assert.True(Be().Lit, "the firestarter lights it");
    }

    /// <summary>Stands clear of the mouth and lets the firing run out.</summary>
    private static async Task FireToTheEnd()
    {
        await Player.Teleport(Fb().AddCopy(5, 0, 5));
        await Ticks(4);

        for (int i = 0; i < 12 && Be().Lit; i++)
        {
            await Hours(3);
            await Tick3s();
        }

        Assert.True(Be().BatchFired, "the firing finished");
        Assert.Equal("fornax:kilndoor-open-south", World.BlockCode(Wicket(2)), "and the wicket swung itself open");
    }

    /// <summary>
    /// Waits for what came out of the kiln to be cool enough to hold. A fired ware comes out at
    /// the chamber's heat, and vanilla burns the hand that picks up anything that hot and drops
    /// it - which is what this test did to the first sagger it fired, before it learned to wait.
    /// </summary>
    private static async Task LetItCool(Func<ItemStack> ware)
    {
        float temperature = float.MaxValue;
        for (int i = 0; i < 24 && temperature > 60; i++)
        {
            await Hours(1);
            await Tick3s();
            var stack = ware();
            temperature = stack == null ? 0 : stack.Collectible.GetTemperature(Sapi.World, stack);
        }

        Log($"cooled to {temperature:0}C");
        Assert.Less(temperature, 60f, "cool enough to pick up");
    }

    [VsTest(TimeoutMs = 600000)]
    [RequiresClient]
    [PlotSize(24, 32)]
    public async Task FromARawSaggerToCalcinedFlintByHand()
    {
        await World.SetCalendarTo(500 * 24 + 12);
        BrickKilnTests.Build(Fb(), entrance: "fornax:kilndoor-open-south");
        await Ticks(4);

        // 1. The raw sagger goes on the grate the way any green ware does: sneak-click.
        await StandAtTheWicket();
        await HoldOnTheClientToo("fornax:saggerraw", 1);
        await SaggerClientTests.SneakUse(FrontGrate(), BlockFacing.UP);

        var pile = World.BE<BlockEntityGroundStorage>(FrontWare());
        Log($"on the grate: {pile.Inventory[0].Itemstack?.Collectible?.Code}");
        Assert.Equal("fornax:saggerraw", pile.Inventory[0].Itemstack?.Collectible?.Code?.ToString());

        // 2. Fired on firewood: a raw sagger wants only 650.
        await LuteTheWicket();
        await FuelAndLight("game:firewood", 24);
        await FireToTheEnd();

        pile = World.BE<BlockEntityGroundStorage>(FrontWare());
        Assert.Equal("fornax:sagger-open", pile.Inventory[0].Itemstack?.Collectible?.Code?.ToString(), "it fired into a sagger");
        await LetItCool(() => World.BE<BlockEntityGroundStorage>(FrontWare()).Inventory[0].Itemstack);

        // 3. Picked up off the grate with an empty hand, and set straight back down as a block.
        await StandAtTheWicket();
        await EmptyHand();
        int saggersBefore = Carried("fornax:sagger-open");
        await Interact.UseBlock(FrontWare());
        await Ticks(4);
        Log($"carried after taking it: {Carried("fornax:sagger-open")} sagger(s)");
        Assert.Equal(saggersBefore + 1, Carried("fornax:sagger-open"), "picked up as an open sagger");
        Assert.True(World.BlockCode(FrontWare()) == "game:air", "and the grate is clear again");

        // Selecting it from the inventory is not this mod's business; in hand it goes.
        await HoldOnTheClientToo("fornax:sagger-open", 1);
        await Interact.UseBlock(FrontGrate(), BlockFacing.UP);
        await Ticks(4);
        Assert.Equal("fornax:sagger-open", World.BlockCode(FrontWare()), "and placed on the grate");

        // 4. Filled with a Ctrl-click, and lidded with a sneak-click.
        await Player.Hold("game:flint", 20);
        await SaggerClientTests.CtrlUse(FrontWare());
        var sagger = World.BE<BlockEntitySagger>(FrontWare());
        Log($"filled: {sagger.Slot.StackSize} flint, {Player.Held?.StackSize} left in hand");
        Assert.Equal(16, sagger.Slot.StackSize);

        await EmptyHand();
        await SaggerClientTests.SneakUse(FrontWare());
        Assert.Equal("fornax:sagger-closed", World.BlockCode(FrontWare()), "lidded");

        // 5. Fired again, on charcoal this time: flint wants a thousand degrees.
        await LuteTheWicket();
        await FuelAndLight("game:charcoal", 15);
        Assert.Equal(16, Be().CountWares(), "the lidded flint is the batch");
        await FireToTheEnd();
        await LetItCool(() => World.BE<BlockEntitySagger>(FrontWare()).Slot.Itemstack);

        // 6. Lid off and the calcined flint out, by hand.
        await StandAtTheWicket();
        await EmptyHand();
        await SaggerClientTests.SneakUse(FrontWare());
        Assert.Equal("fornax:sagger-open", World.BlockCode(FrontWare()), "the lid comes off");

        int calcinedBefore = Carried("game:calcined-flint");
        await Interact.UseBlock(FrontWare());
        await Ticks(4);
        int calcined = Carried("game:calcined-flint") - calcinedBefore;
        Log($"carried at the end: {calcined} calcined flint");
        Assert.Equal(16, calcined, "all sixteen flint came out calcined");
        Assert.True(World.BE<BlockEntitySagger>(FrontWare()).Slot.Empty, "and the sagger is empty, ready for the next");
    }
}
