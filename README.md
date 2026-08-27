# Fornax

A mid-game, multi-block updraft kiln for Vintage Story 1.22 that bridges the gap between the
early-game pit kiln and the late-game beehive kiln. Built from raw mud brick on a pit-fired clay
grate floor, fuelled with firewood or peat, and requiring **no iron at all** — so it lands squarely
in the Copper and Bronze Age.

## Why it exists

|                  | pit kiln   | **updraft kiln** | beehive kiln |
|------------------|------------|------------------|--------------|
| wares per firing | 4          | **~36**          | ~108         |
| fuel per firing  | 4 firewood | **30 firewood**  | up to 144 coal |
| fuel per ware    | 1.0        | **0.83**         | ~1.3         |
| firing time      | 20 h       | **8–13 h**       | 9 h          |
| iron required    | no         | **no**           | yes (door)   |

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
     ▓▓▒▓▓     y=1   grate tiles     ▓···▓     ▓···▓
     ▓▓█▓▓     y=0   chamber+pillar  ▓···▓     ▓···▓
       F       y=0   firebox         ▓▓▓▓▓     ·▓▓▓·

  ▓ mud brick or cob   ▒ kiln grate tile   · open
  █ mud brick pillar   F firebox           v draft vent
```

The drum's four corners stop at grate level: the two courses above them are left open, so the
shell reads as an octagon narrowing into the corbelled neck. The middle three blocks of the
front wall at y=2 and y=3 are the mud seal.

That silhouette is the shape the kiln is *meant* to have, not a rule it enforces. The corner
notches and the ring around the neck sit outside the sealed chamber, so the structure check
ignores them entirely — fill them in, lean something against them, hang a tool rack there.
The nine chamber positions above the grate are checked, but only for being *clear*: anything
that is not a solid cube is allowed to sit in there, which is what keeps the kiln working
with whatever a mod invents to hold wares. See `IsChamberClear`.

Shopping list: **66 mud brick or cob**, **9 kiln grate tiles**, **6 mud seals**, **1 firebox**,
**1 draft vent**.

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
| `FireLimeInBeehiveKiln` | `true` | tags lime for the **vanilla** beehive kiln too, which has no lime recipe of its own. Adds an attribute rather than taking anything away, so it needs no Harmony patch — but note this reaches outside the mod and changes a vanilla block's behaviour |
| `FireContainersInChamber` | `false` | read wares out of any container in the chamber, and out of the headspace course as well as the grate. This is what makes Stackable Kiln Shelves work, and it is off because shelves are a capacity multiplier this kiln is not costed for |
| `RespectMaxFireable` | `false` | cap each pile at the ware's own vanilla `maxFireable`, as a pit kiln does — raw brick 12 rather than a full pile of 24. Off because tripling the fuel cost was the correction the kiln needed; this is the tighter version for anyone who wants it |

`FireContainersInChamber` turns on the **exact-type** check in `IsPlainGroundStorage`, and that
subtlety is the whole reason the switch works: `BlockEntityKilnShelf` *derives from*
`BlockEntityGroundStorage`, so `be is BlockEntityGroundStorage` reads a shelf as an ordinary pile
and fires two courses of shelving no matter what the config says. Compare types exactly.

### BulkQuicklime takes the lime away

BulkQuicklime patches `remove /behaviors/0` onto vanilla lime, which is exactly its
`GroundStorable`, and replaces it with a `limepile` block cooked by a Harmony patch on
`BlockEntityBeeHiveKiln`. With that mod installed there is no way to get lime onto this kiln's
grate at all, and the pile it substitutes will never fire here. The two features are mutually
exclusive; nothing on this side can reconcile them, so the kiln simply says it cannot fire the
pile. `LimeFiresIntoQuicklime` detects this and skips rather than failing.

Note the kiln does **not** check `meltingPoint`. Nothing it fires today melts above the chamber
temperature, so it has never mattered, but a tag on something that needs more heat than the kiln
reaches would fire anyway.

Grate tiles are clay-formed and then pit-fired, so you must use the old technology once to build
the new one. The clay-forming pattern is the grate itself — a frame with two cross bars —
built up over **three layers**, since the fired tile is eight voxels thick and a one-layer form
finishes the moment you close the outline. That is 8 clay for three tiles, so nine tiles is
three formings and 24 clay. Raw tiles stack eight to a ground pile and all eight fire together.

The frame and bars are one voxel wide rather than the fired tile's two: at three layers a
two-voxel frame costs 14 clay, which is absurd for a floor tile. The outline still reads as a
grate, and the finished item uses the real shape either way.

The pile is drawn as whole grates rather than blank slabs: ground storage groups shape elements
by `cuboidsPerModel`, so setting that to 6 (the frame's four pieces plus two bars) reveals one
complete tile per item. `modelItemsToStackSizeRatio`, which the older mods use, is obsolete and
can only manage one element per item.

Place the firebox first, then **`Ctrl` + right-click it with an empty hand**: a blueprint of ghost
blocks shows everything still to place — **drawn with each block's own texture**, so you can see
that a course is mud brick and the floor is grate tiles, with a faint colour wash on top as a
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
| draft vent | 5 mud bricks |
| mud seal | 4 clay + 1 dirt, each |
| grate tile ×3 | 8 clay, clay-formed over three layers, then pit-fired |

The mud seals are the kiln's running cost — six per firing at 4 clay and a dirt block apiece, so
**24 clay and 6 dirt per batch** on top of fuel. That is deliberate: the kiln's throughput and fuel
economy are paid for in clay rather than firewood. Everything else is built once.

## Firing

1. Place green pottery or raw bricks on the grate as ordinary ground storage — around 36 pieces
   of small pottery, or over 100 raw bricks.
2. Close the loading entrance with six mud seals.
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

| fuel       | burn temp | pieces per batch | firing time |
|------------|-----------|------------------|-------------|
| firewood   | 700 °C    | 10               | ~13 h       |
| peat brick | 900 °C    | 10               | ~8 h        |
| charcoal   | 1300 °C   | 6                | ~6 h        |

Anything burning below 650 °C is refused. Wares convert through `combustibleProps.SmeltedStack`,
so every modded clay ware works with no coordination — and the beehive kiln keeps its exclusive
palette of fired colours.

## Opening it early

Breaking a seal — or any part of the shell — while the kiln is hot lets cold air in, and thermal
shock shatters a share of the load. The chance per ware ramps from nothing at 450 °C to 50% at
peak heat, so an early change of mind is free and a late one is expensive. Whatever survives keeps
the heat it has already taken, so re-sealing resumes the firing where it left off.

The mud seals crack in every firing and must be replaced. The shell itself lasts indefinitely.

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
