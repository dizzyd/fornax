// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace Fornax;

/// <summary>
/// Hands each finished ware to XSkills, so a firing here counts for the Pottery skill exactly
/// as a pit kiln's or a beehive kiln's does.
///
/// XSkills gets its own kilns by Harmony-patching them, which reaches this mod not at all: the
/// updraft kiln is a block entity XSkills has never heard of, so without this a firing is worth
/// nothing at all to a potter. Everything the patched kilns do on a finished ware is one public
/// static call - XSkills.PotteryUtil.ApplyOnStack - which awards the experience, sends the
/// Pottery Timer notification and rolls the Inspiration variant swap. Calling that rather than
/// XLib's AddExperience directly is what keeps a firing here worth the same *kind* of thing as a
/// firing there, whatever the skill does with one next - see <see cref="GrantFiring"/> for how
/// many of those calls a batch is worth, which is the part this mod does decide.
///
/// Bound by reflection for the same reasons as ConfigLib in <see cref="FornaxModSystem"/>:
/// XSkills stays optional at build time as well as at run time, and no third-party binary has
/// to live in the repo or the release zip. Two details make it cheap here -
///
///   - ApplyOnStack's whole signature is (IPlayer, IWorldAccessor, ItemSlot), all vanilla API
///     types, so nothing but the lookup crosses the boundary;
///   - the mod is reached by its ModSystem type name rather than its modid, because those
///     differ between the original xSkills ("xskills", Xandu) and the maintained fork
///     ("xskillsfork", El_Neuman) while the assembly and its namespaces do not.
///
/// The usual cost of reflection - a signature change upstream failing silently - is what
/// <c>ConfigLibTakesTheConfig</c>'s sibling in tests/CompatTests.cs is for.
/// </summary>
public static class XSkillsPottery
{
    private const string ModSystemName = "XSkills.XSkills";
    private const string UtilTypeName = "XSkills.PotteryUtil";
    private const string ApplyMethodName = "ApplyOnStack";

    /// <summary>Whether XSkills is installed and its firing hook was found. Asserted in a test.</summary>
    public static bool Bound { get; private set; }

    /// <summary>True once XSkills has been looked for, whether or not it was there.</summary>
    public static bool Resolved { get; private set; }

    private static MethodInfo applyOnStack;

    /// <summary>
    /// Looks XSkills up once, at startup, so a firing never pays for the search.
    ///
    /// Safe to call from either side and from both: XSkills registers its skills in StartPre, so
    /// by Start the type is loaded whichever side asks, and re-resolving only reassigns the same
    /// MethodInfo.
    /// </summary>
    public static void Resolve(ICoreAPI api)
    {
        Resolved = true;
        Bound = false;
        applyOnStack = null;

        var system = api.ModLoader.GetModSystem(ModSystemName);
        if (system == null) return;   // not installed, which is the ordinary case

        var util = system.GetType().Assembly.GetType(UtilTypeName);
        var method = util?.GetMethod(ApplyMethodName, BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(IPlayer), typeof(IWorldAccessor), typeof(ItemSlot) }, null);

        if (method == null)
        {
            api.Logger.Warning("[fornax] xskills is installed but has no {0}.{1}(IPlayer, IWorldAccessor, ItemSlot) - " +
                "a firing here will not count towards the pottery skill.", UtilTypeName, ApplyMethodName);
            return;
        }

        applyOnStack = method;
        Bound = true;
    }

    /// <summary>Ware positions in a beehive kiln: nine columns, three courses each.</summary>
    private const int BeehiveWarePositions = 27;

    /// <summary>
    /// Ware positions here: one course of nine, which is what the 75% is measured against.
    ///
    /// Deliberately the nominal figure rather than <c>WalkGrate</c>'s reach, which doubles when
    /// FireContainersInChamber puts the headspace course in play. A kiln that holds twice as much
    /// should earn twice as much, not the same amount spread thinner.
    /// </summary>
    private const int WarePositions = 9;

    /// <summary>
    /// Credits a finished batch to whoever lit the kiln, at a set fraction of what the same
    /// loading would have earned in a beehive kiln.
    ///
    /// Getting that fraction right needs XSkills' own arithmetic rather than a guess at it, and
    /// the shape of it is not obvious. XSkills patches BlockEntityBeeHiveKiln.ConvertItemToBurned,
    /// which vanilla calls once per slot as that slot finishes - and the patch then awards one
    /// experience for every non-empty slot in the whole pile. So a pile of n occupied slots is
    /// worth n conversions x n slots = <b>n squared</b>, not n: quadruple-stacked ground storage
    /// earns 16 from a pile, a single stack earns 1. Matching that curve is what makes this
    /// kiln's reward track the beehive's across every way of loading it, instead of only at the
    /// one loading the ratio was measured at.
    ///
    /// So: each fired slot is worth <c>share x peers</c>, where peers is how many slots fired in
    /// the same pile - which sums to <c>share x n squared</c> per pile, as above. The share is
    /// the configured fraction scaled by the two kilns' capacities, since this one has a third of
    /// the beehive's ware positions:
    ///
    ///     0.75 x (27 / 9) = 2.25 per peer slot
    ///
    ///     full of piles:      9 x 2.25 x 1x1  =  20  against the beehive's 27
    ///     full of quads:      9 x 2.25 x 4x4  = 324  against the beehive's 432
    ///
    /// XSkills awards a whole experience per call and takes no amount, so the fractional part is
    /// carried across the batch and spent when it comes to one whole - which is why this takes
    /// the firing rather than a slot. Whatever is left under 1 at the end is dropped; the largest
    /// that can be is one experience, on a firing worth twenty.
    ///
    /// XSkills may replace the stack in a slot - that is the Inspiration ability - so the caller
    /// must not hold a reference to an itemstack across this, and must have finished converting.
    /// </summary>
    public static void GrantFiring(ICoreAPI api, IPlayer byPlayer, IList<(BlockEntity Holder, ItemSlot Slot)> fired)
    {
        if (!Bound || byPlayer?.Entity == null || fired == null) return;

        var peers = new Dictionary<BlockEntity, int>();
        foreach (var ware in fired)
        {
            peers.TryGetValue(ware.Holder, out int seen);
            peers[ware.Holder] = seen + 1;
        }

        float share = FornaxModSystem.Config.XSkillsExperienceVsBeehiveKiln
                    * BeehiveWarePositions / WarePositions;

        float credit = 0;
        foreach (var ware in fired)
        {
            credit += share * peers[ware.Holder];

            while (credit >= 1f)
            {
                credit -= 1f;
                if (!Apply(api, byPlayer, ware.Slot)) return;
            }
        }

        // An Inspiration swap happens after the kiln has finished marking its piles dirty, so
        // without this the ware the player is handed is not the one on their screen.
        foreach (var holder in peers.Keys) holder.MarkDirty(true);
    }

    /// <summary>One experience, and one roll of whatever else the skill does with a fired ware.</summary>
    private static bool Apply(ICoreAPI api, IPlayer byPlayer, ItemSlot slot)
    {
        if (slot?.Itemstack == null) return true;   // nothing to hand over, but the batch goes on

        try
        {
            applyOnStack.Invoke(null, new object[] { byPlayer, api.World, slot });
            return true;
        }
        catch (Exception e)
        {
            // Once, not once per ware per firing: whatever went wrong inside XSkills will go
            // wrong again on the next of the 36 slots and on every firing after it.
            Bound = false;
            api.Logger.Warning("[fornax] xskills refused a fired ware, so this kiln will stop offering them: {0}",
                (e as TargetInvocationException)?.InnerException ?? e);
            return false;
        }
    }
}
