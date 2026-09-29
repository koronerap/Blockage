namespace EditorApp.Core.Voxels;

/// <summary>
/// The level: a sparse dictionary of 32³ chunks (EditorApp.md §3). Only occupied chunks exist, so
/// there is no world boundary — the practical limit is memory, roughly 36 KB per occupied chunk.
/// </summary>
public sealed class VoxelWorld
{
    private static int _serials;

    private readonly Dictionary<ChunkCoord, Chunk> _chunks = new();
    private readonly HashSet<ChunkCoord> _dirty = new();

    /// <summary>Further readers' dirty sets: see <see cref="Listen"/>.</summary>
    private List<HashSet<ChunkCoord>>? _listeners;

    /// <summary>
    /// A number no other grid has had in this run. Linked copies share one grid, so it names the
    /// voxels themselves rather than an object: what draws them draws them once for all the copies.
    /// </summary>
    public int Serial { get; } = Interlocked.Increment(ref _serials);

    public Palette Palette { get; private set; } = Palette.CreateDefault();

    public IReadOnlyDictionary<ChunkCoord, Chunk> Chunks => _chunks;

    /// <summary>Chunks whose mesh no longer matches their voxels.</summary>
    public IReadOnlySet<ChunkCoord> DirtyChunks => _dirty;

    public int SolidCount
    {
        get
        {
            int total = 0;
            foreach (Chunk chunk in _chunks.Values)
            {
                total += chunk.SolidCount;
            }

            return total;
        }
    }

    public byte GetVoxel(int x, int y, int z)
    {
        ChunkCoord coord = ChunkCoord.FromWorld(x, y, z);
        return _chunks.TryGetValue(coord, out Chunk? chunk)
            ? chunk.Get(x & Chunk.SizeMask, y & Chunk.SizeMask, z & Chunk.SizeMask)
            : Palette.EmptyIndex;
    }

    public byte GetVoxel(Int3 position) => GetVoxel(position.X, position.Y, position.Z);

    /// <summary>
    /// The colour of one face — the voxel's own colour unless that face has been painted
    /// separately. Only exposed faces can ever be painted, so the exceptions are bounded by the
    /// model's surface, not by its volume.
    /// </summary>
    public byte GetFaceColor(int x, int y, int z, Face face)
    {
        ChunkCoord coord = ChunkCoord.FromWorld(x, y, z);
        return _chunks.TryGetValue(coord, out Chunk? chunk)
            ? chunk.GetFace(x & Chunk.SizeMask, y & Chunk.SizeMask, z & Chunk.SizeMask, face)
            : Palette.EmptyIndex;
    }

    public byte GetFaceColor(Int3 position, Face face) =>
        GetFaceColor(position.X, position.Y, position.Z, face);

    /// <summary>Paints one face. Returns false when nothing changed.</summary>
    public bool SetFaceColor(int x, int y, int z, Face face, byte paletteIndex)
    {
        ChunkCoord coord = ChunkCoord.FromWorld(x, y, z);
        if (!_chunks.TryGetValue(coord, out Chunk? chunk))
        {
            return false;
        }

        if (!chunk.SetFace(x & Chunk.SizeMask, y & Chunk.SizeMask, z & Chunk.SizeMask, face, paletteIndex))
        {
            return false;
        }

        // Only this chunk's mesh changes: a face colour is invisible to the neighbours, unlike a
        // voxel appearing or disappearing.
        Dirty(coord);
        return true;
    }

    public bool SetFaceColor(Int3 position, Face face, byte paletteIndex) =>
        SetFaceColor(position.X, position.Y, position.Z, face, paletteIndex);

    public bool IsSolid(int x, int y, int z) => GetVoxel(x, y, z) != Palette.EmptyIndex;

    public bool IsSolid(Int3 position) => IsSolid(position.X, position.Y, position.Z);

    /// <summary>
    /// Writes a palette index at a world position. Returns false when nothing changed. Marks the
    /// owning chunk dirty, and any neighbour chunk whose face culling the edit invalidates.
    /// </summary>
    public bool SetVoxel(int x, int y, int z, byte paletteIndex)
    {
        ChunkCoord coord = ChunkCoord.FromWorld(x, y, z);
        int lx = x & Chunk.SizeMask;
        int ly = y & Chunk.SizeMask;
        int lz = z & Chunk.SizeMask;

        if (!_chunks.TryGetValue(coord, out Chunk? chunk))
        {
            if (paletteIndex == Palette.EmptyIndex)
            {
                return false;
            }

            chunk = new Chunk();
            _chunks.Add(coord, chunk);
        }

        if (!chunk.Set(lx, ly, lz, paletteIndex))
        {
            return false;
        }

        Dirty(coord);
        MarkTouchedNeighbours(coord, lx, ly, lz);
        return true;
    }

    public bool SetVoxel(Int3 position, byte paletteIndex) =>
        SetVoxel(position.X, position.Y, position.Z, paletteIndex);

    // A voxel on a chunk's edge is a neighbour's input: adding it hides one of their faces and
    // shades the corners beside it, removing it does the reverse. Without this the seam between two
    // chunks shows holes, z-fighting or a corner shaded for a voxel that is not there — so every
    // chunk it touches, across an edge or a corner too, is meshed again.
    private void MarkTouchedNeighbours(ChunkCoord coord, int lx, int ly, int lz)
    {
        int dx = lx == 0 ? -1 : lx == Chunk.SizeMask ? 1 : 0;
        int dy = ly == 0 ? -1 : ly == Chunk.SizeMask ? 1 : 0;
        int dz = lz == 0 ? -1 : lz == Chunk.SizeMask ? 1 : 0;
        if (dx == 0 && dy == 0 && dz == 0)
        {
            return;
        }

        for (int x = Math.Min(dx, 0); x <= Math.Max(dx, 0); x++)
        for (int y = Math.Min(dy, 0); y <= Math.Max(dy, 0); y++)
        for (int z = Math.Min(dz, 0); z <= Math.Max(dz, 0); z++)
        {
            if (x != 0 || y != 0 || z != 0)
            {
                MarkDirtyIfPresent(coord.Offset(x, y, z));
            }
        }
    }

    private void MarkDirtyIfPresent(ChunkCoord coord)
    {
        if (_chunks.ContainsKey(coord))
        {
            Dirty(coord);
        }
    }

    public void MarkDirty(ChunkCoord coord) => Dirty(coord);

    private void Dirty(ChunkCoord coord)
    {
        _dirty.Add(coord);
        if (_listeners is not null)
        {
            foreach (HashSet<ChunkCoord> set in _listeners)
            {
                set.Add(coord);
            }
        }
    }

    /// <summary>
    /// A dirty set of its own for another reader of these voxels — each linked copy's modifiers, over
    /// the grid the copies share — so that one reading its changes does not take them from the rest.
    /// Starts with nothing in it. Let go of with <see cref="StopListening"/>.
    /// </summary>
    public HashSet<ChunkCoord> Listen()
    {
        var set = new HashSet<ChunkCoord>();
        (_listeners ??= []).Add(set);
        return set;
    }

    public void StopListening(HashSet<ChunkCoord> set) => _listeners?.Remove(set);

    /// <summary>Marks every chunk dirty — used after a palette edit, which recolors existing meshes.</summary>
    public void MarkAllDirty()
    {
        foreach (ChunkCoord coord in _chunks.Keys)
        {
            Dirty(coord);
        }
    }

    /// <summary>Takes the current dirty set and clears it. The caller is expected to remesh them.</summary>
    public List<ChunkCoord> ConsumeDirtyChunks()
    {
        var taken = new List<ChunkCoord>(_dirty);
        _dirty.Clear();
        return taken;
    }

    public Chunk? GetChunk(ChunkCoord coord) => _chunks.GetValueOrDefault(coord);

    public Chunk GetOrCreateChunk(ChunkCoord coord)
    {
        if (!_chunks.TryGetValue(coord, out Chunk? chunk))
        {
            chunk = new Chunk();
            _chunks.Add(coord, chunk);
            Dirty(coord);
        }

        return chunk;
    }

    /// <summary>Drops chunks that no longer hold any voxels. Safe to call after a large delete.</summary>
    public void PruneEmptyChunks()
    {
        List<ChunkCoord>? empty = null;
        foreach ((ChunkCoord coord, Chunk chunk) in _chunks)
        {
            if (chunk.IsEmpty)
            {
                (empty ??= []).Add(coord);
            }
        }

        if (empty is null)
        {
            return;
        }

        foreach (ChunkCoord coord in empty)
        {
            _chunks.Remove(coord);
            _dirty.Remove(coord);
            _listeners?.ForEach(set => set.Remove(coord));
        }
    }

    /// <summary>
    /// Tight world-space bounds of all solid voxels, inclusive. False when the world is empty.
    /// </summary>
    public bool TryGetBounds(out Int3 min, out Int3 max)
    {
        min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        max = new Int3(int.MinValue, int.MinValue, int.MinValue);
        bool any = false;

        foreach ((ChunkCoord coord, Chunk chunk) in _chunks)
        {
            if (!chunk.TryGetLocalBounds(out Int3 localMin, out Int3 localMax))
            {
                continue;
            }

            Int3 origin = coord.Origin;
            min = Int3.Min(min, origin + localMin);
            max = Int3.Max(max, origin + localMax);
            any = true;
        }

        return any;
    }

    public void Clear()
    {
        foreach (ChunkCoord coord in _chunks.Keys)
        {
            Dirty(coord);
        }

        _chunks.Clear();
    }

    public void ReplacePalette(Palette palette)
    {
        Palette = palette;
        MarkAllDirty();
    }

    /// <summary>
    /// Takes on another grid's voxels and painted faces, in place — for an object remade where it
    /// stands, which whatever holds this grid sees at once. Keeps its own palette. Every chunk either
    /// grid had is marked to be meshed again.
    /// </summary>
    public void ReplaceWith(VoxelWorld other)
    {
        Clear();

        foreach ((ChunkCoord coord, Chunk chunk) in other._chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            Chunk target = GetOrCreateChunk(coord);
            target.LoadIndices(chunk.Indices);

            foreach ((int linear, Face face, byte index) in chunk.FaceOverrides())
            {
                target.LoadFaceOverride(linear, face, index);
            }
        }
    }

    /// <summary>
    /// A deep copy: every voxel and every painted face, sharing only the palette — which every grid
    /// in a scene shares anyway. Chunks are copied whole rather than cell by cell, so even a large
    /// object costs one memory copy per chunk.
    /// </summary>
    public VoxelWorld Copy()
    {
        var copy = new VoxelWorld();
        copy.ReplacePalette(Palette);

        foreach ((ChunkCoord coord, Chunk chunk) in _chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            Chunk target = copy.GetOrCreateChunk(coord);
            target.LoadIndices(chunk.Indices);

            foreach ((int linear, Face face, byte index) in chunk.FaceOverrides())
            {
                target.LoadFaceOverride(linear, face, index);
            }
        }

        return copy;
    }

    /// <summary>Order-independent hash of the world contents. Used by tests to compare states.</summary>
    public ulong ContentHash()
    {
        ulong total = 0;
        foreach ((ChunkCoord coord, Chunk chunk) in _chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            ulong chunkHash = 1469598103934665603UL;
            ReadOnlySpan<byte> indices = chunk.Indices;
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] == Palette.EmptyIndex)
                {
                    continue;
                }

                chunkHash = (chunkHash ^ (ulong)i) * 1099511628211UL;
                chunkHash = (chunkHash ^ indices[i]) * 1099511628211UL;
            }

            chunkHash ^= (ulong)coord.GetHashCode() * 0x9E3779B97F4A7C15UL;
            total += chunkHash;
        }

        return total;
    }
}
