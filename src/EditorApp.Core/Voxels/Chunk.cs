namespace EditorApp.Core.Voxels;

/// <summary>
/// A dense 32x32x32 block of voxels (EditorApp.md §3). Holds palette indices plus a parallel
/// occupancy bitmask: face culling asks "is this cell solid" six times per voxel, and that question
/// must resolve to a single bit test without touching the 32 KB index array. The bitmask is 4 KB,
/// which is why the chunk is 32 on a side.
/// </summary>
public sealed class Chunk
{
    public const int Size = 32;
    public const int SizeShift = 5;
    public const int SizeMask = Size - 1;
    public const int VoxelCount = Size * Size * Size;
    private const int OccupancyWords = VoxelCount / 64;

    private readonly byte[] _indices = new byte[VoxelCount];
    private readonly ulong[] _occupancy = new ulong[OccupancyWords];

    /// <summary>Number of solid voxels; a chunk that reaches zero can be dropped from the world.</summary>
    public int SolidCount { get; private set; }

    public bool IsEmpty => SolidCount == 0;

    /// <summary>Linear index for a local coordinate, X varying fastest.</summary>
    public static int LinearIndex(int x, int y, int z) => (y << (SizeShift * 2)) | (z << SizeShift) | x;

    public static Int3 FromLinearIndex(int linear) =>
        new(linear & SizeMask, linear >> (SizeShift * 2), (linear >> SizeShift) & SizeMask);

    public bool IsSolid(int x, int y, int z)
    {
        int linear = LinearIndex(x, y, z);
        return (_occupancy[linear >> 6] & (1UL << (linear & 63))) != 0;
    }

    public byte Get(int x, int y, int z) => _indices[LinearIndex(x, y, z)];

    /// <summary>Writes a palette index. Returns false when the cell already held that value.</summary>
    public bool Set(int x, int y, int z, byte paletteIndex)
    {
        int linear = LinearIndex(x, y, z);
        if (_indices[linear] == paletteIndex)
        {
            return false;
        }

        bool wasSolid = _indices[linear] != Palette.EmptyIndex;
        bool isSolid = paletteIndex != Palette.EmptyIndex;
        _indices[linear] = paletteIndex;

        if (wasSolid != isSolid)
        {
            ref ulong word = ref _occupancy[linear >> 6];
            ulong bit = 1UL << (linear & 63);
            if (isSolid)
            {
                word |= bit;
                SolidCount++;
            }
            else
            {
                word &= ~bit;
                SolidCount--;
            }
        }

        return true;
    }

    public ReadOnlySpan<byte> Indices => _indices;

    /// <summary>Replaces the whole index array (used when loading a project) and rebuilds occupancy.</summary>
    public void LoadIndices(ReadOnlySpan<byte> indices)
    {
        if (indices.Length != VoxelCount)
        {
            throw new ArgumentException($"A chunk needs exactly {VoxelCount} indices.", nameof(indices));
        }

        indices.CopyTo(_indices);
        RebuildOccupancy();
    }

    private void RebuildOccupancy()
    {
        Array.Clear(_occupancy);
        SolidCount = 0;

        for (int linear = 0; linear < VoxelCount; linear++)
        {
            if (_indices[linear] == Palette.EmptyIndex)
            {
                continue;
            }

            _occupancy[linear >> 6] |= 1UL << (linear & 63);
            SolidCount++;
        }
    }

    /// <summary>
    /// Tight local bounds of the solid voxels. Skips whole 64-voxel words at a time, so an empty or
    /// sparse chunk costs almost nothing. Returns false when the chunk holds no voxels.
    /// </summary>
    public bool TryGetLocalBounds(out Int3 min, out Int3 max)
    {
        min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        max = new Int3(int.MinValue, int.MinValue, int.MinValue);

        if (IsEmpty)
        {
            return false;
        }

        for (int word = 0; word < OccupancyWords; word++)
        {
            ulong bits = _occupancy[word];
            while (bits != 0)
            {
                int bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;

                Int3 local = FromLinearIndex((word << 6) | bit);
                min = Int3.Min(min, local);
                max = Int3.Max(max, local);
            }
        }

        return true;
    }
}
