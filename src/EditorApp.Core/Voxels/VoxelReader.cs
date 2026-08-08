namespace EditorApp.Core.Voxels;

/// <summary>
/// A read-only cursor over the world that remembers the last chunk it touched.
///
/// Meshing walks the grid in rows, so 31 of every 32 lookups land in the chunk the previous one did.
/// Going through <see cref="VoxelWorld.GetVoxel(int,int,int)"/> hashes a coordinate every time; this
/// hashes once per chunk crossing instead, which is the difference between a large level meshing in
/// seconds and in tens of seconds.
///
/// The cache is only valid while the world is not being written to — use it inside a single read
/// pass and discard it.
/// </summary>
public struct VoxelReader(VoxelWorld world)
{
    private readonly VoxelWorld _world = world;
    private ChunkCoord _coord;
    private Chunk? _chunk;
    private bool _hasCoord;

    public byte Get(int x, int y, int z)
    {
        Chunk? chunk = ChunkFor(x, y, z);
        return chunk is null
            ? Palette.EmptyIndex
            : chunk.Get(x & Chunk.SizeMask, y & Chunk.SizeMask, z & Chunk.SizeMask);
    }

    public byte Get(Int3 position) => Get(position.X, position.Y, position.Z);

    public bool IsSolid(int x, int y, int z)
    {
        Chunk? chunk = ChunkFor(x, y, z);
        return chunk is not null && chunk.IsSolid(x & Chunk.SizeMask, y & Chunk.SizeMask, z & Chunk.SizeMask);
    }

    public bool IsSolid(Int3 position) => IsSolid(position.X, position.Y, position.Z);

    private Chunk? ChunkFor(int x, int y, int z)
    {
        ChunkCoord coord = ChunkCoord.FromWorld(x, y, z);
        if (!_hasCoord || coord != _coord)
        {
            _coord = coord;
            _chunk = _world.GetChunk(coord);
            _hasCoord = true;
        }

        return _chunk;
    }
}
