namespace EditorApp.Core.Voxels;

/// <summary>Address of a chunk in the sparse world grid, in units of <see cref="Chunk.Size"/> voxels.</summary>
public readonly record struct ChunkCoord(int X, int Y, int Z)
{
    /// <summary>The world-space position of this chunk's minimum corner.</summary>
    public Int3 Origin => new(X << Chunk.SizeShift, Y << Chunk.SizeShift, Z << Chunk.SizeShift);

    public static ChunkCoord FromWorld(int x, int y, int z) =>
        new(x >> Chunk.SizeShift, y >> Chunk.SizeShift, z >> Chunk.SizeShift);

    public static ChunkCoord FromWorld(Int3 position) => FromWorld(position.X, position.Y, position.Z);

    public ChunkCoord Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    // The default record hash combines the three fields well enough, but levels are built on axis
    // aligned grids where two of the three coordinates repeat constantly; mixing with odd primes
    // keeps those buckets from clustering.
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = X * 73856093;
            hash ^= Y * 19349663;
            hash ^= Z * 83492791;
            return hash;
        }
    }

    public bool Equals(ChunkCoord other) => X == other.X && Y == other.Y && Z == other.Z;

    public override string ToString() => $"({X}, {Y}, {Z})";
}
