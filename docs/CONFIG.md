# Fornax settings

Every number the kiln uses lives in `ModConfig/fornax.json`, written on first run. Edit it
by hand, or install [ConfigKit](https://github.com/dizzyd/configkit) and change these from
an in-game settings screen (**P**, or the pause menu) with the server's values synced to
every player.

On a server, the server's file is the one that counts.

---

## How firing works

A firing costs a fixed amount of **fuel energy**, measured in *fuel-hours*. Each fuel item
is worth its `burnDuration` divided by `BurnDurationPerFuelHour`, so a hotter fuel is not
cheaper, it just burns through the same energy faster:

| Fuel | For one full firing | Takes about |
|---|---|---|
| Firewood | 30 | 13 in-game hours |
| Peat brick | 29 | 8 hours |
| Charcoal | 18 | 6 hours |

Fuel temperature decides **how hot the chamber gets**, not how much it costs. A ware that
melts above the chamber temperature simply is not fired.

`FiringEnergyHours` and `NominalBurnRate` move together: change one without the other and
you change how long a firing takes as well as what it costs.

---

## Firing

| Setting | Default | Range | What it does |
|---|---|---|---|
| `FiringEnergyHours` | 60 | 1–300 | Fuel energy one complete firing costs. Raise it to make firing dearer. |
| `NominalBurnRate` | 7.5 | 0.1–20 | Fuel-hours drawn per in-game hour at the normal rate. Raise it to finish sooner at the same cost. |
| `BurnDurationPerFuelHour` | 12 | 1–60 | Divides a fuel's `burnDuration` to give its energy. Lower means every fuel is worth more. |
| `DraftTemperatureBonus` | 250 | 0–1000 | Extra chamber heat the updraft is worth over the raw fuel temperature. |
| `ChamberMaxTemperature` | 1200 | 0–2000 | Ceiling on chamber temperature however hot the fuel burns. |
| `ChamberHeatingPerHour` | 450 | | How fast the chamber warms. |
| `ChamberCoolingPerHour` | 300 | | How fast it cools once the fire is out. |
| `AmbientTemperature` | 20 | -50–100 | What it cools back down to. |
| `FiringGlowTemperature` | 450 | 0–2000 | Where the firebox stops looking like it is warming and starts looking like it is firing. |
| `HeatSourceStrength` | 8 | 0–30 | Warmth given to nearby players while lit. A firepit is 10. |
| `FireboxBurnRadius` | 1.5 | 0–8 | How far in front of the mouth the flames will burn you. |
| `FireboxBurnDamage` | 2 | 0–20 | Damage the mouth deals. |

## Fuel

| Setting | Default | Range | What it does |
|---|---|---|---|
| `MinFuelBurnTemperature` | 650 | 0–2000 | Anything burning cooler is refused as kiln fuel. |
| `WarmFuelTemperature` | 900 | 0–2000 | Burn temperature at which fuel draws at the normal rate. |
| `HotFuelTemperature` | 1200 | 0–2000 | Burn temperature at which fuel draws at the fastest rate. |
| `CoolFuelRate` | 0.6 | | Rate multiplier below `WarmFuelTemperature`. |
| `WarmFuelRate` | 1.0 | | Rate multiplier at or above `WarmFuelTemperature`. |
| `HotFuelRate` | 1.3 | | Rate multiplier at or above `HotFuelTemperature`. |
| `FuelSlots` | 4 | 1–12 | Fuel slots in the firebox. |

## Brick kiln

The fired-brick shell is not a hotter kiln; it is a tighter one. Fewer draughts through the
joints means more of the fuel reaches the wares, so a firing costs less. Against that, dense
brick has more mass than straw-tempered cob: slower to heat, and slower to give the heat up
again. It rewards being run back to back.

| Setting | Default | Range | What it does |
|---|---|---|---|
| `BrickFiringEnergyHours` | 48 | 1–300 | Fuel energy one firing costs in the brick kiln. |
| `BrickChamberHeatingPerHour` | 350 | 1–2000 | Warm-up per hour. Lower than the mud kiln: more mass to heat. |
| `BrickChamberCoolingPerHour` | 180 | 1–2000 | Cooling per hour. Lower too: it holds its heat. |
| `ColdRestartPenaltyHours` | 5.0 | | Extra energy a firing costs when starting from cold. |

## Opening the kiln early

| Setting | Default | Range | What it does |
|---|---|---|---|
| `ShatterSafeTemperature` | 450 | 0–2000 | Below this, opening the kiln costs nothing. |
| `ShatterPeakTemperature` | 1050 | 0–2000 | At and above this, opening it costs the most it can. |
| `MaxShatterChance` | 0.5 | 0–1 | Share of the load lost to thermal shock at peak temperature. |

## What it fires

| Setting | Default | What it does |
|---|---|---|
| `FireLime` | on | Crushed lime fires into quicklime on the grate, two lime to one. |
| `FireLimeInBeehiveKiln` | off | Tag lime for the vanilla beehive kiln too, which has no lime recipe of its own. |
| `FireContainersInChamber` | off | Read wares out of any container in the chamber and out of the headspace course. Makes kiln shelves work. |
| `RespectMaxFireable` | off | Respect each ware's own `maxFireable` cap, as a pit kiln does. Off lets a full ground-storage pile fire at once. |

## Other mods

| Setting | Default | Range | What it does |
|---|---|---|---|
| `GrantXSkillsExperience` | on | | Credit a firing to whoever lit the kiln, in XSkills' Pottery skill, as the vanilla kilns do. |
| `XSkillsExperienceVsBeehiveKiln` | 0.75 | 0–4 | What a full firing here earns, against a full beehive kiln firing. |

---

## Common changes

**Make firing cheaper without making it faster** — lower `FiringEnergyHours` and
`NominalBurnRate` by the same proportion.

**Let the kiln fire hotter wares** — raise `ChamberMaxTemperature`, and remember fuel
temperature still has to reach it. `DraftTemperatureBonus` is what the chimney adds.

**Allow poorer fuels** — lower `MinFuelBurnTemperature`. They will fire the chamber less
hot, so check your wares still reach their firing temperature.

**Make opening early less punishing** — lower `MaxShatterChance`, or raise
`ShatterSafeTemperature` so the safe window is wider.
