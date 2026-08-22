// Builds a row of updraft kilns, one per construction stage, on the flat around spawn.
// Run through vstestkit's eval endpoint; see scripts/make-showcase-world.sh.
var ba = sapi.World.BlockAccessor;
System.Func<string,int> id = c => {
    var b = sapi.World.GetBlock(new Vintagestory.API.Common.AssetLocation(c));
    if (b == null) throw new System.Exception("no such block: " + c);
    return b.Id;
};
System.Func<int,int,int,Vintagestory.API.MathTools.BlockPos> BP =
    (x,y,z) => new Vintagestory.API.MathTools.BlockPos(x,y,z);

int AIR = 0;
int WALL = id("game:mudbrick-dark");
int GRATE = id("fornax:kilngrate");
int SEAL = id("fornax:kilnseal-intact");
int VENT = id("fornax:kilnvent-idle");
int FIREBOX = id("fornax:kilnfirebox-cold-south");   // mouth faces +z, body runs to -z
int SOIL = id("game:soil-medium-none");        // bare soil below
int GRASS = id("game:soil-medium-normal");     // grassy top

var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
int gy = spawn.Y;
// find the actual surface at spawn. The superflat floor sits near y=2, so every
// vertical loop below has to be clamped - SetBlock below y=0 is a null deref.
for (int probe = spawn.Y + 20; probe > 1; probe--) {
    if (ba.GetBlock(BP(spawn.X, probe, spawn.Z)).Id != 0) { gy = probe + 1; break; }
}
int subFloor = System.Math.Max(1, gy - 4);

int STAGES = 7;
int PITCH = 9;
int originX = spawn.X - (STAGES / 2) * PITCH;
int originZ = spawn.Z - 10;

// Flatten and clear a strip wide enough for the whole row plus a viewing apron.
for (int x = originX - 5; x <= originX + (STAGES - 1) * PITCH + 5; x++) {
    for (int z = originZ - 8; z <= originZ + 10; z++) {
        for (int y = gy; y <= gy + 12; y++) ba.SetBlock(AIR, BP(x, y, z));
        ba.SetBlock(GRASS, BP(x, gy - 1, z));
        for (int y = subFloor; y < gy - 1; y++) ba.SetBlock(SOIL, BP(x, y, z));
    }
}

// One kiln per stage. `upto` is the highest course to place; course 5 is the cap.
System.Action<int,int,bool> build = (fx, upto, sealIt) => {
    for (int y = 0; y <= System.Math.Min(upto, 3); y++) {
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = 0; dz >= -4; dz--) {
                bool isCorner = System.Math.Abs(dx) == 2 && (dz == 0 || dz == -4);
                bool perim = System.Math.Abs(dx) == 2 || dz == 0 || dz == -4;
                int b;
                if (isCorner && y >= 2) b = AIR;    // corners stop at grate level
                else if (perim) {
                    if (y == 0 && dx == 0 && dz == 0) b = FIREBOX;
                    else if ((y == 2 || y == 3) && dz == 0 && System.Math.Abs(dx) <= 1) b = sealIt ? SEAL : AIR;
                    else b = WALL;
                } else {
                    if (y == 0) b = (dx == 0 && dz == -2) ? WALL : AIR;
                    else if (y == 1) b = GRATE;
                    else b = AIR;
                }
                ba.SetBlock(b, BP(fx + dx, gy + y, originZ + dz));
            }
        }
    }
    if (upto >= 4) {                       // corbelled neck
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz >= -3; dz--)
                ba.SetBlock((dx == 0 && dz == -2) ? AIR : WALL, BP(fx + dx, gy + 4, originZ + dz));
    }
    if (upto >= 5) {                       // cap + draft vent
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz >= -3; dz--)
                ba.SetBlock((dx == 0 && dz == -2) ? VENT : WALL, BP(fx + dx, gy + 5, originZ + dz));
    }
};

string[] labels = {
    "1. Ground course\nring, firebox\nand pillar",
    "2. Grate floor\nnine pit-fired\ntiles",
    "3. Ware level\nload green ware\non the grate",
    "4. Flue course",
    "5. Corbel\nsteps in to 3x3",
    "6. Capped\nfuelled, not lit",
    "7. Sealed\nand firing"
};

var notes = new System.Collections.Generic.List<string>();

for (int i = 0; i < STAGES; i++) {
    int fx = originX + i * PITCH;
    // 0 -> ground course, 1 -> +grate, 2 -> +ware, 3 -> +flue, 4 -> +neck, 5 and 6 -> capped
    int upto = System.Math.Min(i, 5);
    bool sealIt = (i == 6);
    build(fx, upto, sealIt);

    // From the ware stage on, put green pottery on the grate so it reads as loaded.
    if (i >= 2) {
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz >= -3; dz--) {
                var wp = BP(fx + dx, gy + 2, originZ + dz);
                ba.SetBlock(id("game:groundstorage"), wp);
                var gs = ba.GetBlockEntity(wp) as Vintagestory.GameContent.BlockEntityGroundStorage;
                if (gs != null) {
                    gs.Inventory[0].Itemstack = new Vintagestory.API.Common.ItemStack(
                        sapi.World.GetItem(new Vintagestory.API.Common.AssetLocation("game:rawbrick-blue")), 12);
                    gs.Inventory[0].MarkDirty();
                    gs.MarkDirty(true);
                }
            }
        }
    }

    // Stage 6 gets fuel but no light, so the row shows all three firebox states:
    // cold on stages 1-5, ready on 6, lit on 7.
    if (i == 5) {
        var rbe = ba.GetBlockEntity(BP(fx, gy, originZ)) as Fornax.BlockEntityUpdraftFirebox;
        if (rbe != null) {
            rbe.Inventory[0].Itemstack = new Vintagestory.API.Common.ItemStack(
                sapi.World.GetItem(new Vintagestory.API.Common.AssetLocation("game:firewood")), 12);
            rbe.Inventory[0].MarkDirty();
            rbe.RefreshAppearance();
            rbe.MarkDirty(true);
            notes.Add("stage6=" + ba.GetBlock(BP(fx, gy, originZ)).Code.Path);
        }
    }

    // The last one is fuelled and lit.
    if (i == 6) {
        var be = ba.GetBlockEntity(BP(fx, gy, originZ)) as Fornax.BlockEntityUpdraftFirebox;
        if (be != null) {
            be.Inventory[0].Itemstack = new Vintagestory.API.Common.ItemStack(
                sapi.World.GetItem(new Vintagestory.API.Common.AssetLocation("game:firewood")), 16);
            be.Inventory[0].MarkDirty();
            be.StructureComplete = true;   // its own tick recomputes this; set so it can light now
            be.TryIgnite(null);
            be.MarkDirty(true);
            notes.Add("stage7 lit=" + be.Lit);
        } else notes.Add("stage7 NO BE");
    }

    // A sign in front, so the row explains itself.
    var sp = BP(fx, gy, originZ + 3);
    ba.SetBlock(id("game:sign-ground-north"), sp);
    var sign = ba.GetBlockEntity(sp) as Vintagestory.GameContent.BlockEntitySign;
    if (sign != null) { sign.text = labels[i]; sign.MarkDirty(true); }
}

// Hand the clock back before this world is saved.
//
// vstestkit pins time still (SessionPrep sets the "baseline" time speed modifier to 0)
// so that tests control the calendar themselves. Everything this mod does is driven off
// Calendar.TotalHours, so a world saved with that modifier in place ships a kiln that can
// never heat up, never burn fuel and never finish - it just sits at 0 degrees forever.
//
// Note this REMOVES the modifier rather than setting it to 1: SpeedOfTime is the sum of
// all modifiers and defaults to 60, so setting it to 1 would leave time running 60x slow.
sapi.World.Calendar.RemoveTimeSpeedModifier("baseline");

// Deliberately not touching the spawn point: SetDefaultSpawnPosition throws on a
// server with nobody connected. The row is laid out ten blocks north of spawn
// instead, so you face it as soon as you load in.

return "built " + STAGES + " stages at y=" + gy + ", x " + originX + ".." + (originX + (STAGES-1)*PITCH)
     + ", z=" + originZ + " | " + string.Join(",", notes);
