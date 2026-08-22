// Reports how much of the showcase strip is resident, for the caller to poll on.
var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
int have = 0, want = 0;
for (int x = spawn.X - 48; x <= spawn.X + 48; x += 16) {
    for (int z = spawn.Z - 32; z <= spawn.Z + 32; z += 16) {
        want++;
        if (sapi.World.BlockAccessor.GetChunkAtBlockPos(
                new Vintagestory.API.MathTools.BlockPos(x, spawn.Y, z)) != null) have++;
    }
}
return have + "/" + want;
