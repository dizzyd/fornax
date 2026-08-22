// Counts what actually made it into the savegame, after a reload.
var ba = sapi.World.BlockAccessor;
var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;

var counts = new System.Collections.Generic.SortedDictionary<string,int>();
int mud = 0, signs = 0, wareTiles = 0, litKilns = 0;

for (int x = spawn.X - 40; x <= spawn.X + 40; x++) {
    for (int y = 1; y <= 12; y++) {
        for (int z = spawn.Z - 20; z <= spawn.Z + 6; z++) {
            var p = new Vintagestory.API.MathTools.BlockPos(x, y, z);
            var b = ba.GetBlock(p);
            if (b?.Code == null) continue;

            if (b.Code.Domain == "fornax") {
                var k = b.Code.Path;
                counts[k] = counts.ContainsKey(k) ? counts[k] + 1 : 1;
                var be = ba.GetBlockEntity(p) as Fornax.BlockEntityUpdraftFirebox;
                if (be != null && be.Lit) litKilns++;
            } else if (b.Code.Path.StartsWith("mudbrick")) mud++;
            else if (b.Code.Path.StartsWith("sign-")) signs++;

            var gs = ba.GetBlockEntity(p) as Vintagestory.GameContent.BlockEntityGroundStorage;
            if (gs != null && !gs.Inventory[0].Empty) wareTiles++;
        }
    }
}

var sb = new System.Text.StringBuilder();
foreach (var kv in counts) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
sb.Append("mudbrick=").Append(mud).Append(" signs=").Append(signs)
  .Append(" waretiles=").Append(wareTiles).Append(" lit=").Append(litKilns);
return sb.ToString();
