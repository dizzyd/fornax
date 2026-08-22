// Force-loads the strip the showcase row is built on. A headless server with no
// players connected has nothing loaded, and SetBlock into an absent chunk is a
// null dereference, not a no-op.
var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
int size = sapi.WorldManager.ChunkSize;

int x1 = spawn.X - 48, x2 = spawn.X + 48;
int z1 = spawn.Z - 32, z2 = spawn.Z + 32;

sapi.WorldManager.LoadChunkColumnPriority(
    x1 / size - 1, z1 / size - 1, x2 / size + 1, z2 / size + 1,
    new Vintagestory.API.Server.ChunkLoadOptions { KeepLoaded = true });

return "requested x " + x1 + ".." + x2 + ", z " + z1 + ".." + z2;
