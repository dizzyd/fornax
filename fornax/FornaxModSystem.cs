// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Fornax;

public class FornaxModSystem : ModSystem
{
    public const string ConfigFileName = "fornax.json";

    public static FornaxConfig Config { get; private set; } = new FornaxConfig();

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);

        try
        {
            var loaded = api.LoadModConfig<FornaxConfig>(ConfigFileName);
            if (loaded == null)
            {
                loaded = new FornaxConfig();
                api.StoreModConfig(loaded, ConfigFileName);
            }

            Config = loaded;
        }
        catch (Exception e)
        {
            // A broken config should not take the mod down with it.
            api.Logger.Error("[fornax] Could not read {0}, falling back to defaults: {1}", ConfigFileName, e.Message);
            Config = new FornaxConfig();
        }
    }

    /// <summary>
    /// Draws the build guide. One renderer for the session rather than one per kiln: only one
    /// guide is ever up at a time, and a renderer per block entity would leak GPU meshes.
    /// </summary>
    public static KilnGhostRenderer GhostRenderer { get; private set; }

    /// <summary>
    /// The kiln whose build guide is currently up, or null.
    ///
    /// Both halves of the guide are one per session - the ghost mesh above, and the engine's
    /// highlight slot, which every MultiblockStructure in the game shares. So putting a guide up
    /// on a second kiln takes it away from the first, and the first must know that: without this
    /// it would go on believing it owned a guide it had lost, and clear the new one out from
    /// under the player when its own chunk unloaded.
    ///
    /// Written only from the client-guarded guide paths, so the server side of a singleplayer
    /// game never touches it.
    /// </summary>
    public static BlockPos GuideOwner { get; set; }

    /// <summary>Set only on the instance that runs the client, which is what makes this the renderer's owner.</summary>
    private ICoreClientAPI capi;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        capi = api;
        GhostRenderer = new KilnGhostRenderer(api);

        var handbook = api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
        if (handbook != null) handbook.OnInitCustomPages += FillInHandbookNumbers;
    }

    /// <summary>The handbook page, and the key its prose lives under.</summary>
    public const string HandbookPageCode = "gamemechanicinfo-fornax";
    public const string HandbookTextKey = "fornax:gamemechanicinfo-fornax-text";

    /// <summary>
    /// Puts the configured numbers into the handbook page, which otherwise states the defaults
    /// as fact.
    ///
    /// Two of the page's numbers are settings - the fuel temperature floor and the temperature
    /// below which opening the kiln is free - and they were written into the prose as literals.
    /// Change either and the handbook goes on quoting the default, which is worse than saying
    /// nothing: a player reads "anything burning cooler than 650 degrees is refused" on a server
    /// that set 600 and concludes the mod is broken rather than that the page is.
    ///
    /// Only those two. The rest of the page's numbers are spelled out as words - thirty firewood,
    /// about thirteen hours - because it reads better that way, and turning readable prose into
    /// digits to keep a rarely-changed default honest is a poor trade. The firebox itself reports
    /// its live numbers on every look, which is where a number that moves belongs.
    ///
    /// GuiHandbookTextPage.Init resolves Text through Lang.Get only when it is under 255
    /// characters, and treats anything longer as the finished VTML - so handing it the already
    /// formatted string and re-running Init composes the interpolated text without a second
    /// lookup. The pages are built once with the handbook dialog, so a value changed mid-session
    /// through ConfigLib shows up the next time the game loads rather than at once.
    /// </summary>
    public void FillInHandbookNumbers(List<GuiHandbookPage> pages)
    {
        if (capi == null) return;

        foreach (var page in pages)
        {
            if (page is not GuiHandbookTextPage text || text.PageCode != HandbookPageCode) continue;

            text.Text = Lang.Get(HandbookTextKey, Config.MinFuelBurnTemperature, Config.ShatterSafeTemperature);
            text.Init(capi);
            return;
        }
    }

    /// <summary>
    /// Tears down the renderer, but only from the instance that built it.
    ///
    /// A singleplayer game runs a mod system per side out of one assembly, and disposes both.
    /// The renderer and the guide are client things held in statics, so an unguarded Dispose let
    /// whichever side went first take down the other side's renderer. At shutdown that is
    /// invisible, which is exactly what makes it worth closing: the day something disposes a
    /// side on its own - a reload, a disconnect from an integrated server - it would not be.
    /// </summary>
    public override void Dispose()
    {
        if (capi != null)
        {
            GhostRenderer?.Dispose();
            GhostRenderer = null;
            GuideOwner = null;
            capi = null;
        }

        base.Dispose();
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("BlockUpdraftFirebox", typeof(BlockUpdraftFirebox));
        api.RegisterBlockClass("BlockKilnDoor", typeof(BlockKilnDoor));
        api.RegisterBlockEntityClass("UpdraftFirebox", typeof(BlockEntityUpdraftFirebox));

        RegisterWithConfigLib(api);
        XSkillsPottery.Resolve(api);
    }

    private const string ConfigLibSystem = "ConfigLib.ConfigLibModSystem";
    private const string ConfigLibRegister = "RegisterCustomManagedConfig";

    /// <summary>Whether ConfigLib is installed and took the config. Asserted in a test.</summary>
    public static bool ConfigLibBound { get; private set; }

    /// <summary>
    /// Hands <see cref="Config"/> to ConfigLib if it is installed.
    ///
    /// The visible half is an in-game settings GUI. The half that matters more is that ConfigLib
    /// syncs the server's values to every client before AssetsFinalize, which this mod does not
    /// do at all on its own - each side reads its own ModConfig file, so a server that has
    /// retuned the kiln has clients quoting it the untuned numbers.
    ///
    /// Bound by reflection rather than a compile-time reference, so ConfigLib is optional when
    /// building this as well as when running it and nothing third-party has to live in the repo
    /// or the release zip. The usual objection to reflection does not weigh much here: the whole
    /// surface is one method, and its callbacks are plain BCL delegates. RegisterCustomManagedConfig
    /// reflects over the config object itself, so the [Description] and [Category] attributes on
    /// FornaxConfig are all the schema it needs - and those are BCL attributes too, inert when
    /// ConfigLib is absent.
    /// </summary>
    private void RegisterWithConfigLib(ICoreAPI api)
    {
        var system = api.ModLoader.GetModSystem(ConfigLibSystem);
        if (system == null) return;   // not installed, which is the ordinary case

        var register = system.GetType().GetMethod(ConfigLibRegister);
        if (register == null)
        {
            api.Logger.Warning("[fornax] configlib is installed but has no {0} - " +
                "the kiln's settings will not appear in its GUI and will not sync from the server.",
                ConfigLibRegister);
            return;
        }

        try
        {
            register.Invoke(system, new object[]
            {
                "fornax",                                     // domain
                Config,                                       // the object it reflects over
                ConfigFileName,                               // reuse the file this mod already writes
                (Action)(() => ApplyLimeTags(api)),           // onSyncedFromServer
                (Action<string>)(_ => ApplyLimeTags(api)),    // onSettingChanged
                null,                                         // onConfigSaved
            });

            ConfigLibBound = true;
        }
        catch (Exception e)
        {
            // Reflection wraps whatever went wrong inside the call in a TargetInvocationException
            // whose own message says nothing at all, so unwrap it or this is unactionable.
            api.Logger.Warning("[fornax] could not hand the config to configlib: {0}",
                (e as System.Reflection.TargetInvocationException)?.InnerException ?? e);
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);

        ApplyLimeTags(api);
    }

    /// <summary>
    /// Puts the kiln tags on crushed lime, or takes them off again, to match the config.
    ///
    /// Lime is ground storable in vanilla and its combustibleProps already name quicklime, two
    /// lime to one, at 825 degrees. It is skipped only because both kilns test the same two
    /// things - smeltingType "fire", or a product that is a ceramic block - and lime is "cook"
    /// with a product that is an item. Both also take an attribute as a straight opt-in, and an
    /// attribute is all this adds: the conversion still comes from vanilla's own numbers.
    ///
    /// Done in code rather than as a JSON patch because patches are applied while assets load,
    /// before anything could read a config, and a switch that cannot reach the thing it names is
    /// not a switch. Reversible and idempotent for the same reason: both kilns read these
    /// attributes live, so a setting changed in a running world takes effect on the next firing
    /// tick rather than at the next reload.
    /// </summary>
    public static void ApplyLimeTags(ICoreAPI api)
    {
        var lime = api?.World?.GetItem(new AssetLocation("game", "lime"));
        if (lime == null)
        {
            api?.Logger.Notification("[fornax] no game:lime to tag - skipping");
            return;
        }

        var attributes = lime.Attributes?.Token as JObject ?? new JObject();

        Apply(attributes, "fornaxkiln", Config.FireLime, Quicklime());
        Apply(attributes, "beehivekiln", Config.FireLimeInBeehiveKiln, QuicklimeWhateverTheDoors());

        if (Config.FireLimeInBeehiveKiln) OurBeehiveTags.Add(lime.Code);
        else OurBeehiveTags.Remove(lime.Code);

        lime.Attributes = new JsonObject(attributes);
    }

    /// <summary>
    /// Whatever this mod put a "beehivekiln" tag on itself.
    ///
    /// The updraft kiln honours that tag as an opt-in, which is how a mod gets a ware fired here
    /// without patching anything - but a tag this mod wrote is about the OTHER kiln and is no
    /// evidence at all about this one. Without the distinction, switching lime on for the beehive
    /// kiln silently switches it back on here too, and FireLime=false stops meaning anything.
    /// </summary>
    public static readonly HashSet<AssetLocation> OurBeehiveTags = new();

    /// <summary>
    /// Adds a tag, or takes it away again - but only if what is there is the tag this put there.
    /// Another mod may have tagged lime itself, and turning a setting off here should not
    /// quietly delete somebody else's work.
    /// </summary>
    private static void Apply(JObject attributes, string key, bool wanted, JToken ours)
    {
        if (wanted) attributes[key] = ours;
        else if (attributes.TryGetValue(key, out var have) && JToken.DeepEquals(have, ours)) attributes.Remove(key);
    }

    /// <summary>
    /// The beehive kiln keys its results by how many of its doors stand open, since that is what
    /// decides how much air reaches the wares. Lime does not care - quicklime is quicklime
    /// however much air reached it - so all four say the same.
    /// </summary>
    private static JObject QuicklimeWhateverTheDoors()
    {
        var byDoorsOpen = new JObject();
        for (int open = 0; open <= 3; open++) byDoorsOpen[open.ToString()] = Quicklime();
        return byDoorsOpen;
    }

    private static JObject Quicklime() => new JObject { ["type"] = "item", ["code"] = "quicklime" };
}
