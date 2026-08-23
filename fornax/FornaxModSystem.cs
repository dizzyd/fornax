// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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
        api.RegisterBlockEntityClass("UpdraftFirebox", typeof(BlockEntityUpdraftFirebox));
    }
}
