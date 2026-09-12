using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fornax;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Produces the screenshot set for the ModDB listing. Not part of the test suite - run it
/// against a session booted with the pretty client settings (1920x1080, particles on,
/// shadows on) and VSTK_TIME=1 so the fire animates.
/// </summary>
public class ModDbShots
{
    private const string Wall = "game:mudbrick-dark";
    private const string Grate = "fornax:kilngrate-fire";
    private const string Seal = "fornax:kilnseal-intact";
    private const string Vent = "fornax:kilnvent-idle";
    private const string Firebox = "fornax:kilnfirebox-cold-south";

    private const string BrickWall = "game:claybricks-good-fire";
    private const string BrickFirebox = "fornax:kilnbrickfirebox-cold-south";
    private const string WicketLuted = "fornax:kilndoor-luted-south";
    private const string WicketOpen = "fornax:kilndoor-open-south";

    private static string Out(string name) => System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", name);

    /// <param name="upto">highest course to place; 5 is the cap</param>
    /// <param name="brick">
    /// The brick kiln instead of the mud one: fired brick shell, brick firebox, and the
    /// entrance narrowed from a three-wide face of seals to a one-wide wicket, with plain wall
    /// where the outer two seals were.
    /// </param>
    private static void Build(BlockPos f, int upto = 5, bool sealIt = true, bool openFront = false, bool brick = false)
    {
        string wall = brick ? BrickWall : Wall;
        string box = brick ? BrickFirebox : Firebox;

        for (int y = 0; y <= Math.Min(upto, 3); y++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = 0; dz >= -4; dz--)
                {
                    bool corner = Math.Abs(dx) == 2 && (dz == 0 || dz == -4);
                    bool perim = Math.Abs(dx) == 2 || dz == 0 || dz == -4;
                    bool entrance = (y == 2 || y == 3) && dz == 0 && (brick ? dx == 0 : Math.Abs(dx) <= 1);
                    string code;

                    if (corner && y >= 2) code = "game:air";
                    else if (perim)
                    {
                        if (y == 0 && dx == 0 && dz == 0) code = box;
                        // The brick kiln's panels are permanent - unsealed means unluted and
                        // standing open, not gone, which is what a finished firing leaves.
                        else if (entrance) code = brick ? (sealIt ? WicketLuted : WicketOpen)
                                                        : (sealIt ? Seal : "game:air");
                        // cutaway: drop the front wall so the chamber is visible
                        else if (openFront && dz == 0 && y >= 1) code = "game:air";
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

        if (upto >= 4)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz >= -3; dz--)
                    World.SetBlock((dx == 0 && dz == -2) ? "game:air" : wall, f.AddCopy(dx, 4, dz));

        if (upto >= 5)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz >= -3; dz--)
                    World.SetBlock((dx == 0 && dz == -2) ? Vent : wall, f.AddCopy(dx, 5, dz));
    }

    private static void LoadWares(BlockPos f, string code = "game:rawbrick-blue", int n = 12)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz >= -3; dz--)
            {
                BlockPos wp = f.AddCopy(dx, 2, dz);
                World.SetBlock("game:groundstorage", wp);
                var gs = World.BEOrNull<BlockEntityGroundStorage>(wp);
                if (gs == null) continue;
                gs.Inventory[0].Itemstack = World.Stack(code, n);
                gs.Inventory[0].MarkDirty();
                gs.MarkDirty(true);
            }
        }
    }

    private static async Task Light(BlockPos f, string fuel = "game:firewood", int n = 16)
    {
        var be = World.BE<BlockEntityUpdraftFirebox>(f);
        be.Inventory[0].Itemstack = World.Stack(fuel, n);
        be.Inventory[0].MarkDirty();
        for (int i = 0; i < 3; i++) await World.TickNow(f);
        be.TryIgnite(null);
        be.MarkDirty(true);
    }

    /// <summary>
    /// Empties a generous box around the subject and lays fresh turf. Test plots sit close
    /// enough together that a neighbour's leftovers wander into frame otherwise.
    ///
    /// The load is not optional. A plot has only its own chunks resident, and every one of
    /// these SetBlocks past that boundary is a silent no-op - which is why the neighbouring
    /// kiln on this shot's horizon survived a clear that reached well past it, and why raising
    /// the radius never once helped.
    /// </summary>
    private static async Task ClearAround(BlockPos centre, int r = 40)
    {
        await World.LoadArea(centre.AddCopy(-r, -1, -r), centre.AddCopy(r, 16, r));

        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dy = 0; dy <= 16; dy++)
                    World.SetBlock("game:air", centre.AddCopy(dx, dy, dz));
                World.SetBlock("game:soil-medium-normal", centre.AddCopy(dx, -1, dz));
            }
        }
    }

    private static async Task Aim(BlockPos from, BlockPos at, int settle = 90)
    {
        await Player.Teleport(from);
        await Ticks(12);
        await Interact.LookAt(at);
        await Frames.Wait(settle);
    }

    /// <summary>
    /// Closes every HUD element for a clean frame. ICoreClientAPI.HideGuis is read-only with
    /// no reachable backing field, but the hotbar, stat bars, minimap, block tooltip and
    /// interaction help are all just dialogs, and dialogs can be told to close.
    /// </summary>
    private static async Task HideHud()
    {
        // Empty the hand: a held block renders large in the corner of every frame.
        var hand = Player.Me?.InventoryManager?.ActiveHotbarSlot;
        if (hand != null) { hand.Itemstack = null; hand.MarkDirty(); }
        await Ticks(4);

        await OnClient();
        Vs.Capi.Settings.Int["cloudRenderMode"] = 0;
        // Ordinary first person floats the seraph's hand in the corner of every frame.
        // Immersive mode renders the body from the eyes instead, which keeps it out of shot.
        Vs.Capi.Settings.Bool["immersiveFpMode"] = true;

        // Kill the distance haze. Ambient modifiers blend in order with a weight of 1 fully
        // overriding, so this flattens fog density and minimum to nothing for the shot.
        Vs.Capi.Ambient.CurrentModifiers["fornaxshot"] = new AmbientModifier
        {
            FogDensity = new WeightedFloat(0f, 1f),
            FogMin = new WeightedFloat(0f, 1f),
        }.EnsurePopulated();
        var open = new List<Vintagestory.API.Client.GuiDialog>();
        foreach (var d in Vs.Capi.Gui.OpenedGuis) open.Add(d);
        foreach (var d in open)
        {
            string n = d.GetType().Name;
            if (n.StartsWith("Hud") || n == "GuiDialogWorldMap") d.TryClose();
        }
        await OnServer();
        await Frames.Wait(10);
    }

    // ------------------------------------------------------------------

    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(48, 32)]
    public async Task Shot01_FinishedKilnFiring()
    {
        await World.SetCalendarTo(500 * 24 + 9);       // low morning sun, long shadows
        BlockPos f = P(24, 1, 20);
        await ClearAround(f);
        Build(f);
        LoadWares(f);
        await Ticks(10);
        await Light(f);
        await Ticks(200);                               // let it warm through and smoke build

        await Aim(P(19, 2, 28), P(24, 3, 19));
        await HideHud();
        Log("01 -> " + await Shot.Take(Out("01-kiln-firing.png")));
    }

    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(48, 32)]
    public async Task Shot02_BuildGuide()
    {
        await World.SetCalendarTo(500 * 24 + 11);
        BlockPos f = P(24, 1, 20);
        await ClearAround(f);
        World.SetBlock(Firebox, f);
        await Ticks(8);

        await OnClient();
        var cbe = Vs.Capi.World.BlockAccessor.GetBlockEntity(f) as BlockEntityUpdraftFirebox;
        cbe?.ToggleBuildGuide(Vs.Capi.World.Player);
        await OnServer();

        await Aim(P(24, 2, 28), P(24, 3, 19));
        await HideHud();
        Log("02 -> " + await Shot.Take(Out("02-build-guide.png")));
    }

    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(80, 32)]
    public async Task Shot03_ConstructionStages()
    {
        await World.SetCalendarTo(500 * 24 + 10);
        await ClearAround(P(33, 1, 22), 52);
        for (int i = 0; i < 7; i++)
        {
            BlockPos f = P(9 + i * 8, 1, 22);
            Build(f, upto: Math.Min(i, 5), sealIt: i == 6);
            if (i >= 2) LoadWares(f);
            if (i == 6) await Light(f);
        }
        await Ticks(120);

        await OnClient();
        Vs.Capi.Settings.Int["viewDistance"] = 320;
        await OnServer();
        await Ticks(40);

        await Aim(P(33, 7, 53), P(33, 3, 21));
        await HideHud();
        Log("03 -> " + await Shot.Take(Out("03-construction-stages.png")));
    }

    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(48, 32)]
    public async Task Shot04_LoadedGrateCutaway()
    {
        await World.SetCalendarTo(500 * 24 + 14);
        BlockPos f = P(24, 1, 20);
        await ClearAround(f);
        Build(f, openFront: true, sealIt: false);
        LoadWares(f, "game:bowl-blue-raw", 4);
        await Ticks(10);
        await Light(f);
        await Ticks(120);

        await Aim(P(24, 3, 25), P(24, 3, 19));
        await HideHud();
        Log("04 -> " + await Shot.Take(Out("04-loaded-grate.png")));
    }

    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(32, 32)]
    public async Task Shot05_FireboxStates()
    {
        await World.SetCalendarTo(500 * 24 + 19);      // dusk: shape still readable, glow still reads
        string[] codes = {
            "fornax:kilnfirebox-cold-north",
            "fornax:kilnfirebox-ready-north",
            "fornax:kilnfirebox-warming-north",
            "fornax:kilnfirebox-firing-north",
        };
        await ClearAround(P(16, 1, 18), 34);
        for (int i = 0; i < codes.Length; i++) World.SetBlock(codes[i], P(11 + i * 3, 1, 18));
        await Ticks(60);

        await Aim(P(16, 2, 12), P(16, 1, 18));
        await HideHud();
        Log("05 -> " + await Shot.Take(Out("05-firebox-states.png")));
    }

    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(48, 32)]
    public async Task Shot06_FiringAtNight()
    {
        await World.SetCalendarTo(500 * 24 + 20);      // late dusk
        BlockPos f = P(24, 1, 20);
        await ClearAround(f);
        Build(f);
        LoadWares(f);
        await Ticks(10);
        await Light(f);
        await Ticks(300);                               // fully up to temperature

        await Aim(P(21, 2, 27), P(24, 3, 19));
        await HideHud();
        Log("06 -> " + await Shot.Take(Out("06-firing-at-night.png")));
    }

    // ---- the brick kiln ----------------------------------------------
    //
    // Appended rather than slotted in among the mud kiln's shots, so the numbering of a set
    // already published on ModDB does not shift under it.
    //
    // These three call HideGuis as well as HideHud, so that each is complete on its own. The
    // mud kiln's shots do not, and come out with the hotbar and stat bars to be cropped off
    // afterwards - which works only because Shot07 turns the HUD off for the rest of the
    // session. Run one of those alone with --filter and the crop is back.

    /// <summary>The brick kiln, lit — framed as shot 01 is, so the two read as a pair.</summary>
    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(80, 32)]
    public async Task Shot08_BrickKilnFiring()
    {
        await World.SetCalendarTo(500 * 24 + 9);       // low morning sun, long shadows
        BlockPos f = P(40, 1, 20);
        // Reaches past the neighbouring plot, which used to stand whole on this shot's horizon.
        //
        // One speck is still out there and no radius removes it - 44, 56 and 80 all leave it
        // the same size in the same place, so it belongs to a plot further off than LoadArea
        // will reach from here. It is about fifty pixels of distant kiln on the horizon and
        // reads as another kiln in the yard rather than as a mistake, which is a better trade
        // than rewriting a quarter of a million blocks to chase it.
        await ClearAround(f, 56);
        Build(f, brick: true);
        LoadWares(f);
        await Ticks(10);
        await Light(f);
        await Ticks(200);

        // Aimed at the middle of the drum - the body runs away from the mouth, so the ground
        // in front of the firebox is not where the mass is, and framing on it throws the kiln
        // into the top right of the shot.
        await Aim(P(35, 2, 26), P(40, 4, 18));
        await HideHud();
        await HideGuis();
        Log("08 -> " + await Shot.Take(Out("08-brick-kiln-firing.png")));
    }

    /// <summary>
    /// Both kilns in one frame, both lit. The whole point of the brick one is that it is the
    /// same kiln in a different material, and a single shot says that better than two do —
    /// same drum, same corbel, same plume, and a front face that has grown a wicket.
    ///
    /// Shot from far enough back to hold seventeen blocks of subject: the pair spans two 5-wide
    /// drums twelve apart, and at the default field of view anything closer crops a kiln.
    /// </summary>
    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(48, 32)]
    public async Task Shot09_MudAndBrickSideBySide()
    {
        await World.SetCalendarTo(500 * 24 + 10);
        BlockPos mud = P(19, 1, 20);
        BlockPos brick = P(29, 1, 20);
        await ClearAround(P(24, 1, 20), 44);

        Build(mud);
        Build(brick, brick: true);
        LoadWares(mud);
        LoadWares(brick);
        await Ticks(10);
        await Light(mud);
        await Light(brick);
        await Ticks(220);

        await Aim(P(24, 3, 31), P(24, 4, 18));
        await HideHud();
        await HideGuis();
        Log("09 -> " + await Shot.Take(Out("09-mud-and-brick.png")));
    }

    /// <summary>
    /// The wicket, close, in both of its states — luted on the left and standing open on the
    /// right, which is what a firing leaves behind. Framed at ware height so the green wares
    /// show on the grate through the open one.
    ///
    /// This is the shot that has to carry the copper: the strapping is two bands across a brick
    /// panel, and at the distance shots 08 and 09 are taken from it is a few pixels.
    /// </summary>
    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(48, 32)]
    public async Task Shot10_TheWicket()
    {
        await World.SetCalendarTo(500 * 24 + 10);      // the same morning as 08 and 09, and a blue sky rather than the haze hour 13 gives
        BlockPos shut = P(20, 1, 20);
        BlockPos open = P(28, 1, 20);
        await ClearAround(P(24, 1, 20), 52);

        Build(shut, brick: true);
        Build(open, brick: true, sealIt: false);
        LoadWares(shut);
        LoadWares(open);
        await Ticks(10);
        await Light(shut);
        await Ticks(200);

        await Aim(P(24, 3, 28), P(24, 4, 19));
        await HideHud();
        await HideGuis();
        Log("10 -> " + await Shot.Take(Out("10-wicket.png")));
    }

    /// <summary>
    /// The mod icon: one kiln, lit, filling a square frame.
    ///
    /// Shot square at 1000x1000 and scaled down to the required 480, rather than cropped out of
    /// a 16:9 frame - a crop of the default 960x600 window can only give 600 pixels of subject,
    /// and the icon is looked at small enough that the softness shows.
    ///
    /// Framed from the corner, close, at the height of the wares: an icon is read at 100 pixels,
    /// so it needs the two things that say what this is - the drum's silhouette narrowing into
    /// the neck, and the mouth glowing - and nothing else. Late afternoon rather than the night
    /// shot's dusk, because a thumbnail that is mostly dark reads as a dark rectangle.
    /// </summary>
    [VsTest(TimeoutMs = 300000)]
    [RequiresClient]
    [PlotSize(48, 32)]
    public async Task Shot07_ModIcon()
        => await IconShot("modicon", P(21, 3, 25), P(24, 3, 18), 15);

    /// <summary>
    /// One lit kiln, framed square for the icon, and the source of fornax/modicon.png - shot at
    /// 1000x1000 and scaled down to the required 480 rather than cropped out of a 16:9 frame,
    /// since a crop of the default 960x600 window can only give 600 pixels of subject.
    ///
    /// Framed from the corner at the height of the wares. An icon is read at about a hundred
    /// pixels, so it needs the two things that say what this is - the drum's silhouette
    /// narrowing into the neck, and the mouth glowing - and nothing else. Mid-afternoon rather
    /// than the night shot's dusk: a thumbnail that is mostly dark reads as a dark rectangle.
    ///
    /// Each framing gets its own test rather than sharing a scene. A second teleport within one
    /// test lands the camera somewhere it has no business being, and the frame comes back as the
    /// inside of a block.
    ///
    /// The window is square because the slot's own copy of templates/clientsettings.json says
    /// so - 1000x1000, particles and shadows on, ssaa 2. sync-linux.sh replaces that file, so it
    /// is set again before each run rather than kept.
    /// </summary>
    private async Task IconShot(string name, BlockPos from, BlockPos at, int hour)
    {
        BlockPos f = P(24, 1, 20);
        await World.SetCalendarTo(500 * 24 + hour);
        await ClearAround(f);
        Build(f);
        LoadWares(f);
        await Ticks(10);
        await Light(f);
        await Ticks(300);                               // up to temperature, plume established

        await Aim(from, at);
        await HideHud();
        await HideGuis();
        Log($"{name} -> " + await Shot.Take(Out($"{name}.png")));
    }

    /// <summary>
    /// F4 mode, which takes the crosshair with it - HideHud closes dialogs and the crosshair is
    /// not one. The hotkey toggles, and session state outlives a test, so a second shot in the
    /// same session turns the HUD back on unless this checks first.
    /// </summary>
    private static async Task HideGuis()
    {
        await OnClient();
        bool hidden = Vs.Capi.HideGuis;
        await OnServer();

        if (!hidden) await Input.Hotkey("togglehud");
        await Frames.Wait(20);
    }
}
