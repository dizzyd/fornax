// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

namespace Fornax;

/// <summary>
/// Every tunable number the updraft kiln uses. Written to ModConfig/fornax.json on first
/// run so a server can retune the kiln without a rebuild.
///
/// The defaults are calibrated against vanilla fuels (firewood 700C/24, peat brick 900C/25,
/// charcoal 1300C/40) so that one full firing costs the same energy whatever you burn, and
/// hotter fuel simply gets through that energy faster:
///
///     10 firewood   -> ~13 in-game hours
///     10 peat brick -> ~8 in-game hours
///      6 charcoal   -> ~6 in-game hours
/// </summary>
public class FornaxConfig
{
    /// <summary>Fuel energy, in fuel-hours, that one complete firing consumes.</summary>
    public double FiringEnergyHours = 20.0;

    /// <summary>Fuel-hours drawn out of the firebox per in-game hour at the nominal (1.0x) rate.</summary>
    public double NominalBurnRate = 2.5;

    /// <summary>A fuel's burnDuration divided by this gives its energy in fuel-hours.</summary>
    public double BurnDurationPerFuelHour = 12.0;

    /// <summary>Anything that burns cooler than this is refused as kiln fuel.</summary>
    public int MinFuelBurnTemperature = 650;

    /// <summary>Fuel at or above this burn temperature draws at <see cref="WarmFuelRate"/>.</summary>
    public int WarmFuelTemperature = 900;

    /// <summary>Fuel at or above this burn temperature draws at <see cref="HotFuelRate"/>.</summary>
    public int HotFuelTemperature = 1200;

    public float CoolFuelRate = 0.6f;
    public float WarmFuelRate = 1.0f;
    public float HotFuelRate = 1.3f;

    /// <summary>The chimney draft lets the fire run hotter than it would in the open.</summary>
    public int DraftTemperatureBonus = 250;

    public int ChamberMaxTemperature = 1200;
    public int ChamberHeatingPerHour = 450;
    public int ChamberCoolingPerHour = 300;
    public int AmbientTemperature = 20;

    /// <summary>
    /// Fuel-hours forfeited when a firing is left unfinished and the chamber falls all the way
    /// back to ambient. Most of what a real firing costs is the climb to temperature, and a kiln
    /// that has gone stone cold has to make that climb again: at the default heating rate,
    /// ambient to firing heat is about two in-game hours of burn, which is a quarter of the
    /// eight hours a full batch takes. Progress cannot go below zero, so a firing that had
    /// barely started simply starts again.
    /// </summary>
    public double ColdRestartPenaltyHours = 5.0;

    /// <summary>Below this chamber temperature, breaching the kiln costs nothing at all.</summary>
    public int ShatterSafeTemperature = 450;

    /// <summary>At or above this the shatter chance sits at <see cref="MaxShatterChance"/>.</summary>
    public int ShatterPeakTemperature = 1050;

    /// <summary>Per-ware chance of shattering when the kiln is breached at peak heat.</summary>
    public float MaxShatterChance = 0.5f;

    /// <summary>
    /// Chamber temperature at which the firebox stops looking like it is warming up and starts
    /// looking like it is firing. Shares the shatter threshold deliberately: the moment the
    /// colour changes is the moment opening the kiln starts costing you wares.
    /// </summary>
    public int FiringGlowTemperature = 450;

    /// <summary>Warmth given to nearby players while lit. A firepit is 10.</summary>
    public float HeatSourceStrength = 8f;

    /// <summary>How far in front of the firebox mouth the flames will burn you.</summary>
    public double FireboxBurnRadius = 1.5;

    public int FireboxBurnDamage = 2;

    /// <summary>Fuel slots in the firebox.</summary>
    public int FuelSlots = 4;
}
