// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Fornax;

/// <summary>
/// Every tunable number the updraft kiln uses. Written to ModConfig/fornax.json on first
/// run so a server can retune the kiln without a rebuild.
///
/// The defaults are calibrated against vanilla fuels (firewood 700C/24, peat brick 900C/25,
/// charcoal 1300C/40) so that one full firing costs the same energy whatever you burn, and
/// hotter fuel simply gets through that energy faster:
///
/// Faster, and no further: fuel decides how hot the chamber gets, and a ware that melts above
/// that is not fired. No vanilla ware can reach that - the hottest melts at 850 and the coolest
/// fuel the firebox accepts still drives the chamber to 900 - but a mod can tag one that does.
///
///     30 firewood   -> ~13 in-game hours
///     29 peat brick -> ~8 in-game hours
///     18 charcoal   -> ~6 in-game hours
///
/// The cost and the draw rate move together, so retuning one without the other changes how
/// long a firing takes as well as what it costs.
/// </summary>
public class FornaxConfig
{
    // The floating point settings here are float rather than double on purpose. ConfigLib 1.12.0
    // classifies both as its "float" setting type and then unboxes the field value with a hard
    // (float) cast, which throws InvalidCastException on a boxed double and takes the whole
    // registration down with it - so a double field silently costs the GUI and the server sync.
    // Every value here is small and exactly representable either way, so this costs nothing.

    /// <summary>Fuel energy, in fuel-hours, that one complete firing consumes.</summary>
    [Category("Firing")]
    [Description("Fuel energy, in fuel-hours, that one complete firing costs.")]
    [Range(1, 300)]
    public float FiringEnergyHours = 60.0f;

    /// <summary>Fuel-hours drawn out of the firebox per in-game hour at the nominal (1.0x) rate.</summary>
    [Category("Firing")]
    [Description("Fuel-hours drawn from the firebox per in-game hour at the 1.0x rate.")]
    [Range(0.1, 20)]
    public float NominalBurnRate = 7.5f;

    /// <summary>A fuel's burnDuration divided by this gives its energy in fuel-hours.</summary>
    [Category("Firing")]
    [Description("Divides a fuel's burnDuration to give its energy in fuel-hours.")]
    [Range(1, 60)]
    public float BurnDurationPerFuelHour = 12.0f;

    /// <summary>Anything that burns cooler than this is refused as kiln fuel.</summary>
    [Category("Fuel")]
    [Description("Anything burning cooler than this is refused as kiln fuel.")]
    [Range(0, 2000)]
    public int MinFuelBurnTemperature = 650;

    /// <summary>Fuel at or above this burn temperature draws at <see cref="WarmFuelRate"/>.</summary>
    [Category("Fuel")]
    [Description("Burn temperature at which fuel reaches the nominal rate.")]
    [Range(0, 2000)]
    public int WarmFuelTemperature = 900;

    /// <summary>Fuel at or above this burn temperature draws at <see cref="HotFuelRate"/>.</summary>
    [Category("Fuel")]
    [Description("Burn temperature at which fuel reaches the fastest rate.")]
    [Range(0, 2000)]
    public int HotFuelTemperature = 1200;

    public float CoolFuelRate = 0.6f;
    public float WarmFuelRate = 1.0f;
    public float HotFuelRate = 1.3f;

    /// <summary>The chimney draft lets the fire run hotter than it would in the open.</summary>
    [Category("Firing")]
    [Description("Extra chamber temperature the updraft is worth over the raw fuel.")]
    [Range(0, 1000)]
    public int DraftTemperatureBonus = 250;

    [Category("Firing")]
    [Description("Ceiling on chamber temperature however hot the fuel burns.")]
    [Range(0, 2000)]
    public int ChamberMaxTemperature = 1200;
    public int ChamberHeatingPerHour = 450;
    public int ChamberCoolingPerHour = 300;

    [Category("Firing")]
    [Description("Temperature the chamber cools back down to.")]
    [Range(-50, 100)]
    public int AmbientTemperature = 20;

    // --- the brick kiln ----------------------------------------------------
    //
    // The second shell material, and a deliberately sideways one. Fired brick laid in lime
    // mortar is not a hotter kiln - common brick softens around the temperature it was fired
    // at, and a cob shell self-fires on its inner face over its first few firings anyway, so
    // both settle in the same earthenware range. The ceiling and the draft bonus are therefore
    // shared, and so is the capacity: nine grate positions either way.
    //
    // What mortared brick actually buys is a shell that does not leak. Less parasitic cold air
    // through the joints means more of the fuel reaches the wares, which is the one number
    // below that moves in the kiln's favour. Against it, dense brick has more mass and conducts
    // better than straw-tempered cob: it is slower to bring up to heat, and slower to let go of
    // it afterwards. So the brick kiln is cheaper per firing and worse at a single firing -
    // it wants to be run back to back, which is what a permanent kiln is for.

    /// <summary>Fuel energy one firing costs in the brick kiln. See the note above.</summary>
    [Category("Brick kiln")]
    [Description("Fuel energy, in fuel-hours, that one complete firing costs in the brick kiln.")]
    [Range(1, 300)]
    public float BrickFiringEnergyHours = 48.0f;

    /// <summary>The brick kiln's greater mass takes longer to bring up to temperature.</summary>
    [Category("Brick kiln")]
    [Description("Chamber warm-up per in-game hour in the brick kiln. Lower than the mud kiln: more mass to heat.")]
    [Range(1, 2000)]
    public int BrickChamberHeatingPerHour = 350;

    /// <summary>And longer to let it go again, which is what makes back-to-back firings cheap.</summary>
    [Category("Brick kiln")]
    [Description("Chamber cooling per in-game hour in the brick kiln. Lower than the mud kiln: it holds its heat.")]
    [Range(1, 2000)]
    public int BrickChamberCoolingPerHour = 180;

    /// <summary>
    /// Fuel-hours forfeited when a firing is left unfinished and the chamber falls all the way
    /// back to ambient. Most of what a real firing costs is the climb to temperature, and a kiln
    /// that has gone stone cold has to make that climb again: at the default heating rate,
    /// ambient to firing heat is about two in-game hours of burn, which is a quarter of the
    /// eight hours a full batch takes. Progress cannot go below zero, so a firing that had
    /// barely started simply starts again.
    /// </summary>
    public float ColdRestartPenaltyHours = 5.0f;

    /// <summary>Below this chamber temperature, breaching the kiln costs nothing at all.</summary>
    [Category("Breaching")]
    [Description("Below this, opening the kiln costs nothing.")]
    [Range(0, 2000)]
    public int ShatterSafeTemperature = 450;

    /// <summary>At or above this the shatter chance sits at <see cref="MaxShatterChance"/>.</summary>
    [Category("Breaching")]
    [Description("At and above this, opening the kiln costs the most it can.")]
    [Range(0, 2000)]
    public int ShatterPeakTemperature = 1050;

    /// <summary>Per-ware chance of shattering when the kiln is breached at peak heat.</summary>
    [Category("Breaching")]
    [Description("Share of the load lost to thermal shock at peak temperature.")]
    [Range(0, 1)]
    public float MaxShatterChance = 0.5f;

    /// <summary>
    /// Chamber temperature at which the firebox stops looking like it is warming up and starts
    /// looking like it is firing. Shares the shatter threshold deliberately: the moment the
    /// colour changes is the moment opening the kiln starts costing you wares.
    /// </summary>
    [Category("Firing")]
    [Description("Where the firebox stops looking like it is warming and starts looking like it is firing.")]
    [Range(0, 2000)]
    public int FiringGlowTemperature = 450;

    /// <summary>Warmth given to nearby players while lit. A firepit is 10.</summary>
    [Category("Firing")]
    [Description("Warmth given to nearby players while lit. A firepit is 10.")]
    [Range(0, 30)]
    public float HeatSourceStrength = 8f;

    /// <summary>How far in front of the firebox mouth the flames will burn you.</summary>
    [Category("Firing")]
    [Description("How far in front of the firebox mouth the flames will burn you.")]
    [Range(0, 8)]
    public float FireboxBurnRadius = 1.5f;

    [Category("Firing")]
    [Description("Damage the firebox mouth deals.")]
    [Range(0, 20)]
    public int FireboxBurnDamage = 2;

    /// <summary>Fuel slots in the firebox.</summary>
    [Category("Fuel")]
    [Description("Fuel slots in the firebox.")]
    [Range(1, 12)]
    public int FuelSlots = 4;

    // --- what the kiln will fire ------------------------------------------

    /// <summary>
    /// Crushed lime fires into quicklime on the grate, at vanilla's own two-to-one.
    ///
    /// Lime is already ground storable and already knows what it becomes; it is only skipped
    /// because its smeltingType is "cook" and quicklime is an item rather than a ceramic block,
    /// which is what both this kiln and the beehive kiln test for. A lime kiln is an updraft
    /// shaft kiln, so this one firing lime is the least surprising thing about it.
    ///
    /// Note BulkQuicklime removes lime's ground storable behaviour outright, so with that mod
    /// installed there is no way to get lime onto the grate and this setting does nothing.
    /// </summary>
    [Category("What it fires")]
    [Description("Crushed lime fires into quicklime on the grate, two lime to one.")]
    public bool FireLime = true;

    /// <summary>
    /// The same for the vanilla beehive kiln, which has no lime recipe of its own either.
    ///
    /// This one reaches outside the mod - it tags a vanilla item so a vanilla block will fire
    /// it - so it is worth knowing it is on. It adds an attribute rather than taking anything
    /// away, which is the polite half of what BulkQuicklime does and needs no Harmony patch.
    ///
    /// Off by default because it is a departure twice over. Vanilla draws a clean line - wares
    /// whose smeltingType is "fire" go in a kiln, everything else including "cook" goes in a
    /// firepit, and InventorySmelting says so out loud ("Can't smelt, requires a kiln"). Lime is
    /// "cook". Firing it in this kiln bends that line inside this mod, which is defensible: a
    /// lime kiln is an updraft shaft kiln, which is what this is. Bending it inside a vanilla
    /// block, for everyone who installs the mod, is somebody else's decision to make.
    /// </summary>
    [Category("What it fires")]
    [Description("Tag lime for the vanilla beehive kiln too, which has no lime recipe of its own.")]
    public bool FireLimeInBeehiveKiln = false;

    /// <summary>
    /// Read wares out of any container standing in the chamber, not just ground storage, and
    /// out of the headspace course as well as the grate.
    ///
    /// This is what makes Stackable Kiln Shelves work, and it is off by default because that is
    /// squarely a balance decision: shelves are a capacity multiplier, and two courses of them
    /// is a great deal more than the ~36 wares this kiln is costed for. Turning it on also fires
    /// the contents of anything else non-solid you leave in there, and does not wear shelves out
    /// the way their own mod's beehive kiln patch does.
    /// </summary>
    [Category("What it fires")]
    [Description("Read wares out of any container in the chamber and out of the headspace course. Makes kiln shelves work.")]
    public bool FireContainersInChamber = false;

    /// <summary>
    /// Respect each ware's own maxFireable cap, the way a pit kiln does.
    ///
    /// maxFireable is vanilla's per-ware limit on how many go in at once - raw brick is 12 -
    /// and BlockEntityGroundStorage enforces it when you light a pit kiln. Ground storage holds
    /// twice that: a brick pile stacks to 24. This kiln reads the pile, not the cap.
    ///
    /// Off by default. Tripling what a firing costs was the correction the kiln needed, and
    /// capping the piles on top of it is a second squeeze rather than more of the same one -
    /// a big kiln you have to load in half-piles stops feeling like a big kiln. It is here for
    /// anyone who wants the tighter version, and it is the one thing that still brings raw
    /// bricks into line: 216 a firing becomes 108, against 12 in a pit kiln.
    /// </summary>
    [Category("What it fires")]
    [Description("Respect each ware's own maxFireable cap, as a pit kiln does. Off lets a full ground storage pile fire at once.")]
    public bool RespectMaxFireable = false;

    // --- other mods --------------------------------------------------------

    /// <summary>
    /// Credit a finished firing to whoever lit the kiln, in XSkills' Pottery skill.
    ///
    /// On by default, because the alternative is that a potter who builds the better kiln stops
    /// levelling: XSkills patches its own two kilns, has never heard of this one, and a firing
    /// that earns nothing is a straight penalty for using the mod. What it awards is not this
    /// mod's own idea of a fair number - it hands each finished ware to the same XSkills call
    /// the vanilla kilns use, so the experience, the Pottery Timer message and the Inspiration
    /// roll are whatever that skill says they are. Does nothing at all when XSkills is absent.
    /// </summary>
    [Category("Other mods")]
    [Description("Credit a firing to whoever lit the kiln, in XSkills' Pottery skill, as the vanilla kilns do.")]
    public bool GrantXSkillsExperience = true;

    /// <summary>
    /// What a full firing here earns in XSkills' Pottery skill, as a fraction of what the same
    /// loading earns in a full beehive kiln.
    ///
    /// Three quarters, because that is about what this kiln is: it holds a third of a beehive
    /// kiln's wares and fires them in a third of the positions, but it needs no iron and no
    /// coal, so a potter working through the Bronze Age is not left levelling on pit kilns.
    /// Paying the full rate would make the cheaper kiln the better one to grind in and leave the
    /// beehive with nothing but capacity to recommend it.
    ///
    /// Scaled by capacity rather than applied flat - see XSkillsPottery.GrantFiring - so the
    /// fraction holds however the chamber is loaded, and the headspace course that
    /// FireContainersInChamber unlocks earns its own keep rather than diluting the rest.
    /// </summary>
    [Category("Other mods")]
    [Description("What a full firing here earns in XSkills' Pottery skill, against a full beehive kiln firing.")]
    [Range(0, 4)]
    public float XSkillsExperienceVsBeehiveKiln = 0.75f;
}
