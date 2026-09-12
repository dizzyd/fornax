# Fornax

A mid-game, multi-block updraft kiln for Vintage Story 1.22 that bridges the gap between the
early-game pit kiln and the late-game beehive kiln. Built from raw mud brick on a pit-fired clay
grate floor, fuelled with firewood or peat, and requiring **no iron at all** — so it lands squarely
in the Copper and Bronze Age.

There are two of it. The **mud kiln** is what you build first, out of what the ground gives you.
The **brick kiln** is the same kiln laid in fired brick with a copper-strapped wicket, built out
of what the first one fired — cheaper to run, slower to warm, and still no iron. See
[The brick kiln](#the-brick-kiln).

## Why it exists

|                  | pit kiln   | **updraft, mud** | **updraft, brick** | beehive kiln |
|------------------|------------|------------------|--------------------|--------------|
| wares per firing | 4          | **~36**          | **~36**            | ~108         |
| fuel per firing  | 4 firewood | **30 firewood**  | **24 firewood**    | up to 144 coal |
| fuel per ware    | 1.0        | **0.83**         | **0.67**           | ~1.3         |
| firing time      | 20 h       | **8–13 h**       | **8–13 h**         | 9 h          |
| per-firing clay  | —          | **24 + 6 dirt**  | **2**              | none         |
| metal required   | no         | **no**           | **copper**         | iron (door)  |

The pit kiln figure is its four `fuel` build stages at one firewood each, plus 10 drygrass and
8 sticks it also eats; a beehive kiln's is nine coal piles at `BlockEntityCoalPile.MaxStackSize`
16. The point of this kiln is **batch size without iron**, not fuel efficiency — per ware it is
only slightly better than a pit kiln, and it fires 36 at a time instead of 4. Every firing also
cracks six mud seals, 24 clay and 6 soil, which no fuel table shows.

Raw brick is the one ware that stays well ahead: a full pile is 24 and there are nine of them,
so 216 a firing at 0.14 firewood each against a pit kiln's 0.33. Vanilla's own `maxFireable`
would halve that, and `RespectMaxFireable` turns it on for anyone who wants it.

## Building one

The kiln is a 5x5 drum around a 3x3x4 chamber, four courses tall, that then **corbels in** to a
3x3 chimney neck and cap — so it reads as a kiln rather than a cube.

```
  side view                     footprint, per course

      ▓v▓      y=5   cap + vent      y=0,1     y=2,3
      ▓·▓      y=4   neck, 1x1 flue
     ·▓▓▓·     y=3   corners open    ▓▓▓▓▓     ·▓▓▓·
     ·▓·▓·     y=2   corners open    ▓···▓     ▓···▓
     ▓▓▒▓▓     y=1   grates          ▓···▓     ▓···▓
     ▓▓█▓▓     y=0   chamber+pillar  ▓···▓     ▓···▓
       F       y=0   firebox         ▓▓▓▓▓     ·▓▓▓·

  ▓ mud brick or cob   ▒ kiln grate        · open
  █ mud brick pillar   F firebox           v draft vent
```

The drum's four corners stop at grate level: the two courses above them are left open, so the
shell reads as an octagon narrowing into the corbelled neck. The middle three blocks of the
front wall at y=2 and y=3 are the mud seal. (The brick kiln is the same drum with a narrower
entrance — see [The brick kiln](#the-brick-kiln).)

That silhouette is the shape the kiln is *meant* to have, not a rule it enforces. The corner
notches and the ring around the neck sit outside the sealed chamber, so the structure check
ignores them entirely — fill them in, lean something against them, hang a tool rack there.
The nine chamber positions above the grate are checked, but only for being *clear*: anything
that is not a solid cube is allowed to sit in there, which is what keeps the kiln working
with whatever a mod invents to hold wares. See `IsChamberClear`.

Shopping list, mud kiln: **66 mud brick or cob**, **9 kiln grates**, **6 mud seals**,
**1 firebox**, **1 draft vent**.

Shopping list, brick kiln: **70 fired brick blocks** (any clay or refractory brick, in any mix),
**9 kiln grates**, **2 wicket panels**, **1 brick firebox**, **1 draft vent**. Four more wall
blocks than the mud kiln, because the narrower wicket turns four of the six seal positions into
plain wall.

## The brick kiln

The same drum, the same nine grate positions, the same wares and the same temperatures — laid in
fired brick instead of raw mud, with a strapped wicket where the mud kiln has six seals. A second
firebox, `kilnbrickfirebox`, is what tells the two apart; it carries its own multiblock definition,
so a brick firebox only ever completes over a brick shell and a mud one only over mud. There is no
surveying of wall materials and no question of what a shell of 65 mud and one brick ought to do.

### It is not a hotter kiln, and that is deliberate

Common fired brick softens at about the temperature it was fired at, and a cob shell bakes its own
inner face hard over its first few firings — after a season it *is* low-fired brick, in situ. Both
settle in the same earthenware range, so the ceiling, the draft bonus and the capacity are shared.
The material that genuinely raises a ceiling is refractory, and vanilla already puts that behind
the beehive kiln. A brick kiln that fired hotter or bigger would simply replace the beehive one,
and the point of this mod is the rung below it.

What mortared brick actually buys is a shell that does not leak. Tight joints mean less cold air
diluting the flame, so more of the fuel reaches the wares:

| | mud | brick |
|---|---|---|
| fuel per firing | 60 fuel-hours (30 firewood) | **48** (24 firewood) |
| chamber warm-up | 450 °C/h | **350 °C/h** |
| chamber cooling | 300 °C/h | **180 °C/h** |

The last two rows are the price. Dense brick has more mass than straw-tempered cob and conducts
better, so there is more of it to bring up to heat — and more of it holding that heat afterwards.
A brick kiln fired once and left is a worse kiln than a mud one. Fired batch after batch, the
chamber never falls far enough to forfeit anything and the fuel saving is the whole story. That is
what a permanent kiln is for.

### The wicket

The front wall's entrance narrows from a 3×2 face of mud seals to **one block wide and two tall**,
closed by two **kiln wicket panels** — fire clay brick strapped in copper or bronze.

```
  mud kiln, front face      brick kiln, front face

   y=3   ▒ ▒ ▒                y=3   ▓ ▐ ▓
   y=2   ▒ ▒ ▒                y=2   ▓ ▐ ▓
         ▓▓█▓▓                      ▓▓█▓▓

  ▒ mud seal (consumed)      ▐ wicket panel (permanent)
```

The recipe is vanilla's own kiln door with the metal list moved down a tier:

```
  B R      B = 4 fireclay brick        metals: copper, tin bronze,
  B N      R = 2 rod-{metal}                   bismuth bronze, black bronze
  B R      N = 4 nails and strips       → 2 wicket panels
```

Copper melts at 1084 °C and the chamber runs to 1200, so copper *in* the chamber would be
nonsense. The strapping and the pintles are on the **outer** face, where a thick brick panel keeps
them a couple of hundred degrees at most — the brick takes the fire, the metal only holds the
brick. Bronze is allowed on the same argument; tin bronze melts cooler than copper does and
neither is anywhere near the flame.

### Luting is the running cost

The panels are permanent. What seals them is a bead of clay luted round the joint — right-click a
panel with clay, one clay each — and the fire burns that luting out again, so a finished batch
swings its own wicket open. Nothing to mine out and nothing to rebuild.

| | per firing |
|---|---|
| mud kiln | 6 seals = 24 clay + 6 dirt, and six blocks to break |
| brick kiln | 2 clay, and two right-clicks |

Right-clicking a luted panel with an empty hand breaks the luting by hand, which is how you get
into a kiln early — and costs exactly what prising a mud seal out costs, because the firebox sees
an opened structure either way and applies the same thermal shock.

Real potters lute a wicket before every firing; the luting burning through is what a finished
firing looks like from outside. So the mechanic is not a concession, it is the thing itself — and
it happens to be *less* tedious than the seals rather than more.

### Where the bricks come from

Nothing in the brick kiln needs iron, and the mud kiln makes most of it. Fired brick is
`rawbrick` → pit kiln or mud kiln → `burnedbrick`, laid up with mortar; mortar is slaked lime and
sand, and slaked lime is quicklime, which **this kiln already fires** (see [Lime](#lime)). So the
first kiln fires the brick and the lime for the second one. That is not a designed loop so much as
the order it happened in historically: the mud-brick kiln that made the fired brick for the
permanent kiln that replaced it.

## What it fires

Anything vanilla's beehive kiln would take: `smeltingType: fire`, or a ware whose product is a
ceramic block, or anything carrying a `beehivekiln` attribute — that last clause is the
ecosystem's own opt-in, and honouring it is what lets a mod add a ware without patching
anything here. A `fornaxkiln` attribute does the same thing aimed at this kiln, and wins over
`beehivekiln` where both are present.

What a ware turns into is resolved in this order: `fornaxkiln` first, since that is a statement
about *this* kiln and whoever set it meant it; then `combustibleProps.smeltedStack`; and
`beehivekiln` last, only as a fallback for a ware that has nothing else.

That last ordering is deliberate and not the obvious one. `beehivekiln` is keyed `0`–`3` by how
many of that kiln's doors stand open, because that is what decides how much air reaches the
wares, and key `0` is the fully reducing firing that a mud-sealed chamber physically is. Taking
it whenever it exists would be the truer simulation — but it would also hand this kiln the tans
and creams that are a beehive kiln's to give, and a red raw brick would fire to tan here rather
than red. Reading it only when `combustibleProps` is silent leaves every existing ware firing to
exactly what it fired to before, and still lets a tag-only ware convert rather than sit on the
grate forever.

### Lime

Crushed lime is ground-storable in vanilla and already knows what it becomes — quicklime, two to
one, at 825 °C. It fails both `combustibleProps` clauses only because its `smeltingType` is
`cook` and quicklime is an item rather than a ceramic block. `FornaxModSystem.TagLime` adds the
opt-in attribute; the conversion still comes from vanilla's own numbers. Lime is `Messy12`, so it
stores 12 to a block: a full grate is 108 lime, giving 54 quicklime a firing.

Tagging happens in `AssetsFinalize` rather than as a JSON patch because patches are applied while
assets load, before anything can read a config — and a switch that cannot reach the thing it
names is not a switch.

### Config

| setting | default | |
|---|---|---|
| `FireLime` | `true` | lime fires into quicklime on the grate |
| `FireLimeInBeehiveKiln` | `false` | tags lime for the **vanilla** beehive kiln too, which has no lime recipe of its own. Adds an attribute rather than taking anything away, so it needs no Harmony patch. Off because vanilla draws a clean line — `fire` wares go in a kiln, everything else including `cook` goes in a firepit — and bending that inside this mod is one thing, bending it inside a vanilla block for everyone who installs the mod is another |
| `FireContainersInChamber` | `false` | read wares out of any container in the chamber, and out of the headspace course as well as the grate. This is what makes Stackable Kiln Shelves work, and it is off because shelves are a capacity multiplier this kiln is not costed for |
| `RespectMaxFireable` | `false` | cap each pile at the ware's own vanilla `maxFireable`, as a pit kiln does — raw brick 12 rather than a full pile of 24. Off because tripling the fuel cost was the correction the kiln needed; this is the tighter version for anyone who wants it |
| `GrantXSkillsExperience` | `true` | credit a firing to whoever lit the kiln, in XSkills' Pottery skill. Does nothing when XSkills is absent |
| `XSkillsExperienceVsBeehiveKiln` | `0.75` | what a full firing here earns in that skill, against a full beehive kiln firing |
| `BrickFiringEnergyHours` | `48` | fuel energy one firing costs in the **brick** kiln, against `FiringEnergyHours` 60 for the mud one |
| `BrickChamberHeatingPerHour` | `350` | how fast the brick chamber warms, against 450 for mud — more mass to heat |
| `BrickChamberCoolingPerHour` | `180` | and how fast it cools, against 300 — which is what makes back-to-back firings cheap |

`FireContainersInChamber` turns on the **exact-type** check in `IsPlainGroundStorage`, and that
subtlety is the whole reason the switch works: `BlockEntityKilnShelf` *derives from*
`BlockEntityGroundStorage`, so `be is BlockEntityGroundStorage` reads a shelf as an ordinary pile
and fires two courses of shelving no matter what the config says. Compare types exactly.

Exactly — but not against a `typeof`. The type a pile is built from is a *registration*, and a
mod may supply its own: Dense Ground Storage calls
`RegisterBlockEntityClass("GroundStorage", typeof(BlockEntityDenseGroundStorage))` in its `Start`,
unconditionally and on both sides, and the class registry takes the last one in. Every pile in
that world is then a subclass, densely placed or not. Against `typeof(BlockEntityGroundStorage)`
nothing anywhere was plain ground storage, so the kiln counted no wares, refused every ware in
the game, and named what it was refusing through the pile's placed-block name — *"cannot fire Raw
brick"*, with the structure reading complete throughout. So the comparison asks
`Api.ClassRegistry.GetBlockEntity("GroundStorage")` what a pile is now. Kiln shelves register
under their own name, `BEKilnShelf`, so a shelf is still not a pile.

### Heat

A ware that needs more heat than the loaded fuel will give is not fired, and the kiln says which
temperature it will reach and which the ware wants — before it is lit, since finding out
afterwards costs a whole firing. Fuel therefore decides *what* the kiln can fire as well as how
fast it gets through it:

| fuel | chamber |
|---|---|
| coolest the firebox accepts (`MinFuelBurnTemperature` 650) | 900 |
| firewood (700) | 950 |
| peat brick (900) | 1150 |
| charcoal (1300) | 1200 *(`ChamberMaxTemperature`)* |

No vanilla ware can trigger it: the hottest this kiln fires melts at 850 — raw brick, refractory
brick, shingle — against 900 from the coolest legal fuel. The check exists because the kiln takes
anything carrying a kiln tag now, and a mod is free to tag something that wants more heat than
this makes.

### BulkQuicklime takes the lime away

BulkQuicklime patches `remove /behaviors/0` onto vanilla lime, which is exactly its
`GroundStorable`, and replaces it with a `limepile` block cooked by a Harmony patch on
`BlockEntityBeeHiveKiln`. With that mod installed there is no way to get lime onto this kiln's
grate at all, and the pile it substitutes will never fire here. The two features are mutually
exclusive; nothing on this side can reconcile them, so the kiln simply says it cannot fire the
pile. `LimeFiresIntoQuicklime` detects this and skips rather than failing.

### XSkills counts a firing here

XSkills gets its Pottery experience out of the vanilla kilns by Harmony-patching them, which
reaches this kiln not at all — so without help, a potter who builds the better kiln stops
levelling, which is a straight penalty for installing the mod. Everything the patched kilns do to
a finished ware is one public static call, `XSkills.PotteryUtil.ApplyOnStack`, and this kiln makes
the same call per fired slot: the experience, the *Pottery Timer* message and the *Inspiration*
variant swap are then whatever that skill says they are rather than a second opinion that drifts.

A full firing here is worth **75%** of what the same loading earns in a beehive kiln
(`XSkillsExperienceVsBeehiveKiln`). That is the rate this kiln should pay: it holds a third of a
beehive kiln's wares in a third of the positions, but needs no iron and no coal, so a potter
working through the Bronze Age is not left levelling on pit kilns — while the beehive still keeps
a clear lead per firing.

Matching that fraction across every way of loading the chamber takes XSkills' own arithmetic
rather than a guess at it, and the shape of it is not obvious. XSkills patches
`ConvertItemToBurned`, which vanilla calls **once per slot** as that slot finishes, and the patch
then awards one experience for **every non-empty slot in the pile**. A pile of `n` occupied slots
is therefore worth `n²`, not `n`: 16 from a quadruple-stacked pile, 1 from a single stack. So this
kiln pays `share × peers` per fired slot, where `peers` is how many slots fired in the same pile —
the same curve, scaled by the two kilns' capacities:

| | beehive (27 positions) | fornax (9 positions) |
|---|---|---|
| full of single-stack piles | 27 | 20 |
| full of quadruple-stacked piles | 432 | 324 |

XSkills awards a whole experience per call and takes no amount, so the fractional part is carried
across the batch and spent when it comes to one whole. The credit goes to whoever struck the
firestarter, which this kiln knows and neither vanilla kiln does (XSkills has to stamp an owner on
whoever last touched those). If they have logged out by the time it finishes, nobody is credited.

Bound by reflection, like ConfigLib, so XSkills is optional at build time as well as at run time.
It is found by its ModSystem type name rather than its modid, because the original
([`xskills`](https://mods.vintagestory.at/xskills), Xandu) and the maintained fork
([`xskillsfork`](https://mods.vintagestory.at/show/mod/44074), El_Neuman) differ in the latter and
not the former. `AFiringCountsTowardsXSkillsPottery` is what notices when the call is renamed
upstream, since reflection that stops resolving is otherwise silent.


A grate is clay-formed **whole**, as the grating it will be: a two-voxel frame with two
two-voxel cross bars, built up over **eight layers**, 160 voxels a layer. That is 49 clay once
the free starting blob is counted — between a storage vessel (35) and a clay oven (68), which is
about right for a solid half-block of ceramic. Nine grates is nine formings and 441 clay.

The raw grate is one to a ground pile and a pit kiln fires exactly one, so the first floor is
nine pit firings — you use the old technology once, and nine times over, to build the new one.
A finished updraft kiln fires nine raw grates at a go, which is the quicker way to a second
kiln. Earlier versions clay-formed a thin tile in a three-layer form and pit-fired eight to a
pile, each coming out half a block of ceramic; a player who counts voxels noticed, and was right.

Raw grates carry their clay — blue, fire or red — and fire to the colour vanilla's bricks do
(blue clay fires gray), so the grate block comes in three colours and the structure check takes
any mix of them. The coloured brick faces are the cream base with the colour laid over it, as
vanilla's own brick courses do it: the gray and red textures carry alpha and go translucent drawn
on their own.

A world built before 1.8 has `fornax:kilngrate` blocks and `fornax:kilngrateraw` items in it.
`config/remaps.json` maps them to the fireclay grate and the blue raw tile; the server applies
the group once per save on load, the same way the game's own version remaps run.

The frame and bars are one voxel wide rather than the fired tile's two: at three layers a
two-voxel frame costs 14 clay, which is absurd for a floor tile. The outline still reads as a
grate, and the finished item uses the real shape either way.

The pile is drawn as whole grates rather than blank slabs: ground storage groups shape elements
by `cuboidsPerModel`, so setting that to 6 (the frame's four pieces plus two bars) reveals one
complete tile per item. `modelItemsToStackSizeRatio`, which the older mods use, is obsolete and
can only manage one element per item.

Place the firebox first, then **`Ctrl` + right-click it with an empty hand**: a blueprint of ghost
blocks shows everything still to place — **drawn with each block's own texture**, so you can see
that a course is mud brick and the floor is grates, with a faint colour wash on top as a
hint and bright red for anything in the way. The same click takes it down.

`IWorldAccessor.HighlightBlocks` can only paint flat colours, so the textured part is a custom
`IRenderer` (`Client/KilnGhostRenderer.cs`) that builds one mesh from the missing blocks' default
meshes and draws it translucent. Two things to know if you touch it: the standard shader's sampler
is named **`tex`**, not the widely-quoted `tex2d` — passing the wrong name throws
`KeyNotFoundException` on the first frame and takes the client down — and there is one renderer for
the session rather than one per block entity, since only one guide is ever up and a renderer per
kiln would leak GPU meshes. This is the gesture the
beehive kiln uses on its door, so it is one thing to learn rather than two.

The firebox **points its mouth at you when you place it**, and the kiln body runs away from you
from there — so stand where you want the front to be. (Vanilla's `HorizontalOrientable` points
`side` the other way, away from the placer, which is right for a chest and wrong for something
you have to load through; placement is overridden for this block.)

## Recipes

| | costs |
|---|---|
| firebox | 5 fired bricks + 3 mud bricks |
| brick firebox | 5 fired bricks + 3 fireclay brick blocks |
| draft vent | 5 mud bricks |
| mud seal | 4 clay + 1 dirt, each |
| wicket panel ×2 | 12 fireclay bricks + 4 copper/bronze rods + 4 nails and strips |
| kiln grate | 49 clay, clay-formed whole over eight layers, then pit-fired one at a time |

The mud seals are the mud kiln's running cost — six per firing at 4 clay and a dirt block apiece,
so **24 clay and 6 dirt per batch** on top of fuel. That is deliberate: the kiln's throughput and
fuel economy are paid for in clay rather than firewood. Everything else is built once.

The brick kiln pays that cost once, in copper, and then **2 clay a batch** to lute its wicket.
See [Luting is the running cost](#luting-is-the-running-cost).

## Firing

1. Place green pottery or raw bricks on the grate as ordinary ground storage — around 36 pieces
   of small pottery, or over 100 raw bricks.
2. Close the loading entrance: six mud seals on the mud kiln, or right-click each of the two
   wicket panels with clay on the brick one.
3. Right-click fuel into the firebox (ctrl-click for a whole stack), then light it.

   **A firestarter takes about four goes.** `ItemFirestarter` rolls a 25% chance every time you
   finish a hold and silently does nothing otherwise — that is vanilla, and firepits behave the
   same way. A **lit** torch is the reliable alternative: hold it on the firebox for three full
   seconds and it always works. An *unlit* torch has no ignition behaviour at all and will never
   do anything, however long you hold it.

   If the kiln refuses to light it now tells you why, and highlights any structure blocks that
   are missing or wrong.

   The firebox shows its state at a glance:

   | | looks like | means |
   |---|---|---|
   | `cold` | dark, empty mouth | no fuel in it |
   | `ready` | firewood in the mouth | fuelled, waiting for a light |
   | `warming` | glowing red coals, red light, thin smoke | lit, chamber still coming up |
   | `firing` | bright orange flame, strong orange light, sparks | up to temperature |

   The change from red to orange lands on the shatter threshold deliberately: the moment the
   colour turns is the moment opening the kiln starts costing you wares.

   Look at it for the exact figures — hours of burn loaded, firing progress, chamber temperature.
   It only comments on the fuel when there is *not* enough to see the batch out.

   **Stand to the side while it burns.** Unlike a firepit, the mouth of a lit firebox hurts:
   anything within about 1.5 blocks in front of it — you or an animal — takes a point of fire
   damage a second and is shoved back away from the kiln. Three blocks out is clear. The firebox
   says so when you look at it, the handbook says so too, and all three numbers are settings
   (`FireboxBurnRadius`, `FireboxBurnDamage`, `FireboxBurnKnockback`); a damage of 0 switches
   the burn off and the warning with it. The damage was 2 before 1.7.0, and a config file
   written by an earlier version keeps 2 until edited.

   **Once it is lit the firebox is sealed**: fuel can be neither added nor removed until the
   batch finishes. Load it before you light it. The vent swaps to a drafting variant
   carrying a smoke plume while it fires, and everything reverts when the batch is done.

   The flames themselves are sealed inside the chamber where nothing can see them, so all of the
   visible signal is on the firebox and the vent. Particles are declared on the blocktypes and
   spawned by the engine, the same way a firepit does it.

   **A note for anyone editing those particles:** the engine anchors them at
   `Block.TopMiddlePos`, which is the block's *top face*. The firebox's top face is buried under
   the wall course above it, so particles declared without a downward offset spawn inside solid
   stone and are never seen. The offsets are per-side, because the mouth faces a different way in
   each variant. `ParticlesSpawnInOpenAirNotInsideTheStructure` guards this.
4. Walk away. Progress runs off the calendar, so it finishes whether or not you stay.

Hot gas is drawn up through the wares and out of the vent, so the whole chamber comes to
temperature rather than only what touches the fire.

**A batch costs a fixed amount of fuel energy no matter what you burn; hotter fuel just gets
through it faster.** The chimney draft adds 250 °C over what the fuel would manage in the open,
which is what lets 700 °C firewood fire pottery at all.

| fuel       | burn temp | per batch, mud | per batch, brick | firing time |
|------------|-----------|----------------|------------------|-------------|
| firewood   | 700 °C    | 30             | 24               | ~13 h       |
| peat brick | 900 °C    | 29             | 24               | ~8 h        |
| charcoal   | 1300 °C   | 18             | 15               | ~6 h        |

Anything burning below 650 °C is refused, which `MinFuelBurnTemperature` moves. A mod that
retunes fuel wants that: BTRO-Fuels puts firewood, brushwood, bamboo, sticks and dried peat all
at 600, so a world running it should set 600 here. That is the floor of the useful range — the
chamber then reaches 850 °C, exactly what raw brick melts at.

Wares convert through `combustibleProps.SmeltedStack`, so every modded clay ware works with no
coordination — and the beehive kiln keeps its exclusive palette of fired colours.

## Opening it early

Breaking a seal — or any part of the shell — while the kiln is hot lets cold air in, and thermal
shock shatters a share of the load. The chance per ware ramps from nothing at 450 °C to 50% at
peak heat, so an early change of mind is free and a late one is expensive. Whatever survives keeps
the heat it has already taken, so re-sealing resumes the firing where it left off.

The mud seals crack in every firing and must be replaced. The brick kiln's wicket panels do not:
the firing burns their luting out and they swing open, ready to be luted again for a clay apiece.
Either shell itself lasts indefinitely.

## Configuration

Every number above lives in `ModConfig/fornax.json`, written on first run.

## Building

```bash
export VINTAGE_STORY="$(ls -d ~/.cairn/games/*.app | sort -V | tail -1)"
./build.sh
```

The zip lands in `Releases/`.

## A world with one kiln per stage

```bash
bash scripts/make-showcase-world.sh
```

Builds a savegame containing seven kilns side by side, each stopped one course further
along — ground course, grate floor, loaded wares, flue, corbel, capped, and finally sealed
and firing — with a sign in front of each. It installs to
`$VS_DEV_DATA/Saves/fornaxcreative.vcdbs`, which is exactly the world
`fornax/scripts/runClient.sh` opens, so:

```bash
cd fornax && bash scripts/runClient.sh "$VINTAGE_STORY" "$PWD" Debug
```

drops you in facing the row.

If Cairn is on the machine, the script also builds a pack called `fornax` holding the
**packaged** mod plus the same world, so you can launch it the way a player would:

```bash
~/src/cairn/artifacts/osx-arm64/cairn-cli launch fornax
```

The two launchers are not interchangeable, and the difference is worth understanding:

| | `runClient.sh` | `cairn-cli launch` |
|---|---|---|
| loads | `bin/<config>/Mods` + `--addOrigin assets` | the release zip in the pack's `Mods/` |
| data path | `$VS_DEV_DATA` | the pack's own `data/` |
| opens | straight into the world (`--openWorld`) | the main menu; pick the world |
| asset edits | live-reload, no rebuild | need a repackage |

So `runClient.sh` is the dev loop and Cairn is the shipping check — Cairn has no `--addOrigin`,
and this mod keeps its assets in the source tree, so pointing a pack at the build tree would
load the code and register no blocks at all. Set `NO_CAIRN=1` to skip the pack step.

The script uses [vstestkit](../vstestkit) purely as a way to run code against a live world.
Three things about that are worth knowing if you edit it:

- vstestkit **freezes the clock** (`SetTimeSpeedModifier("baseline", 0)`, the same thing
  `/time stop` does) so tests control time themselves — and that setting is kept by the
  savegame. Everything this mod does runs off `Calendar.TotalHours`, so a world built without
  `VSTK_TIME=1` ships a kiln that can never heat up, burn fuel or finish: it just sits at
  0 °C forever. Both boots in the script set `VSTK_TIME=1`.

- `scripts/stop.sh` shuts the endpoint down **without writing the world** — test worlds are
  meant to be disposable. The build has to `SIGTERM` the server instead, which is the path
  that runs the game's own save.
- `scripts/boot.sh` **wipes its run directory** unless `VSTK_KEEP=1`. Re-booting to check
  your work without it silently inspects a freshly generated world.

The script re-opens the finished savegame and counts what is in it before installing, so a
silent failure of either kind fails the build rather than shipping an empty world.

## Testing

In-game tests run under [vstestkit](../vstestkit):

```bash
cd ../vstestkit
bash scripts/run.sh ../fornax/tests --mod ../fornax/fornax            # headless
bash scripts/run.sh ../fornax/tests --mod ../fornax/fornax --client   # + renderer
```

## Licence

Copyright (C) 2026 Dave (Dizzy) Smith.

Released under the **GNU Lesser General Public License, version 3 or later**. See
[COPYING.LESSER](COPYING.LESSER) for the Lesser terms and [COPYING](COPYING) for the GPLv3 they
build on.
