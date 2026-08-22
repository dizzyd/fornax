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

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        GhostRenderer = new KilnGhostRenderer(api);
    }

    public override void Dispose()
    {
        GhostRenderer?.Dispose();
        GhostRenderer = null;
        base.Dispose();
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("BlockUpdraftFirebox", typeof(BlockUpdraftFirebox));
        api.RegisterBlockEntityClass("UpdraftFirebox", typeof(BlockEntityUpdraftFirebox));
    }
}
