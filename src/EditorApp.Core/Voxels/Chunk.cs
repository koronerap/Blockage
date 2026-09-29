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

    /// <summary>
    /// One bit per voxel: does this voxel have any face painted differently from its base colour.
    /// Meshing asks for a face colour six times per voxel, so the common answer — "no" — has to
    /// cost a bit test rather than a dictionary probe.
    ///
    /// Allocated only once something is painted. It is 4 KB, which on a level of thousands of
    /// unpainted chunks would otherwise be a tenth of the world's memory bought for nothing.
    /// </summary>
    private ulong[]? _hasFaceOverride;

    /// <summary>
    /// Faces that differ from their voxel's base colour, keyed by <c>linear * 6 + face</c>.
    ///
    /// Storing six bytes per voxel outright would cost six times the memory the spec's one-byte
    /// index was chosen for. Almost every voxel in a level is a single colour, so the exceptions
    /// are held sparsely and only the painted faces are paid for.
    /// </summary>
    private Dictionary<int, byte>? _faceOverrides;

    /// <summary>Number of solid voxels; a chunk that reaches zero can be dropped from the world.</summary>
    public int SolidCount { get; private set; }

    /// <summary>
    /// The solid voxels' bounds, worked out when first asked for after a voxel came or went. The
    /// panels and the shadow map ask every frame, and a level of millions of voxels cannot be walked
    /// that often. One object, so a reader on another thread sees both corners or neither.
    /// </summary>
    private LocalBounds? _bounds;

    private sealed record LocalBounds(Int3 Min, Int3 Max);

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

    /// <summary>Number of faces painted away from their voxel's base colour.</summary>
    public int FaceOverrideCount => _faceOverrides?.Count ?? 0;

    /// <summary>
    /// The colour of one face: the voxel's base colour unless that face has been painted
    /// separately.
    /// </summary>
    public byte GetFace(int x, int y, int z, Face face)
    {
        int linear = LinearIndex(x, y, z);
        byte baseIndex = _indices[linear];

        if (baseIndex == Palette.EmptyIndex || !HasOverride(linear))
        {
            return baseIndex;
        }

        return _faceOverrides!.TryGetValue(FaceKey(linear, face), out byte overridden) ? overridden : baseIndex;
    }

    /// <summary>
    /// Paints one face. Returns false when nothing changed. An empty cell has no faces to paint.
    /// </summary>
    public bool SetFace(int x, int y, int z, Face face, byte paletteIndex)
    {
        int linear = LinearIndex(x, y, z);
        byte baseIndex = _indices[linear];

        if (baseIndex == Palette.EmptyIndex || paletteIndex == Palette.EmptyIndex)
        {
            return false;
        }

        int key = FaceKey(linear, face);
        byte current = HasOverride(linear) && _faceOverrides!.TryGetValue(key, out byte existing)
            ? existing
            : baseIndex;

        if (current == paletteIndex)
        {
            return false;
        }

        if (paletteIndex == baseIndex)
        {
            // Back to the base colour: drop the exception rather than storing a redundant one.
            _faceOverrides?.Remove(key);
            RefreshOverrideBit(linear);
            return true;
        }

        _faceOverrides ??= [];
        _faceOverrides[key] = paletteIndex;
        SetOverrideBit(linear, true);
        return true;
    }

    /// <summary>Every painted face in this chunk, for saving.</summary>
    public IEnumerable<(int Linear, Face Face, byte PaletteIndex)> FaceOverrides()
    {
        if (_faceOverrides is null)
        {
            yield break;
        }

        foreach ((int key, byte index) in _faceOverrides)
        {
            yield return (key / FaceInfo.Count, (Face)(key % FaceInfo.Count), index);
        }
    }

    /// <summary>Restores a painted face when loading. Skips faces whose voxel is empty.</summary>
    public void LoadFaceOverride(int linear, Face face, byte paletteIndex)
    {
        if ((uint)linear >= VoxelCount || _indices[linear] == Palette.EmptyIndex)
        {
            return;
        }

        _faceOverrides ??= [];
        _faceOverrides[FaceKey(linear, face)] = paletteIndex;
        SetOverrideBit(linear, true);
    }

    private static int FaceKey(int linear, Face face) => (linear * FaceInfo.Count) + (int)face;

    private bool HasOverride(int linear) =>
        _hasFaceOverride is { } bits && (bits[linear >> 6] & (1UL << (linear & 63))) != 0;

    private void SetOverrideBit(int linear, bool value)
    {
        if (_hasFaceOverride is null)
        {
            if (!value)
            {
                return;
            }

            _hasFaceOverride = new ulong[OccupancyWords];
        }

        ref ulong word = ref _hasFaceOverride[linear >> 6];
        ulong bit = 1UL << (linear & 63);

        if (value)
        {
            word |= bit;
        }
        else
        {
            word &= ~bit;
        }
    }

    /// <summary>Clears the bit once a voxel's last painted face has gone back to the base colour.</summary>
    private void RefreshOverrideBit(int linear)
    {
        if (_faceOverrides is null)
        {
            SetOverrideBit(linear, false);
            return;
        }

        for (int face = 0; face < FaceInfo.Count; face++)
        {
            if (_faceOverrides.ContainsKey((linear * FaceInfo.Count) + face))
            {
                return;
            }
        }

        SetOverrideBit(linear, false);
    }

    /// <summary>Forgets everything painted on one voxel — used when the voxel itself changes.</summary>
    private void ClearFaceOverrides(int linear)
    {
        if (_faceOverrides is null || !HasOverride(linear))
        {
            return;
        }

        for (int face = 0; face < FaceInfo.Count; face++)
        {
            _faceOverrides.Remove((linear * FaceInfo.Count) + face);
        }

        SetOverrideBit(linear, false);
    }

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

        // Recolouring or clearing a voxel replaces it wholesale; anything painted on its faces
        // described the colour it used to be.
        ClearFaceOverrides(linear);

        if (wasSolid != isSolid)
        {
            _bounds = null;
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
        _bounds = null;
        _hasFaceOverride = null;
        _faceOverrides = null;
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
    /// Tight local bounds of the solid voxels. Returns false when the chunk holds no voxels.
    /// </summary>
    public bool TryGetLocalBounds(out Int3 min, out Int3 max)
    {
        if (IsEmpty)
        {
            min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
            max = new Int3(int.MinValue, int.MinValue, int.MinValue);
            return false;
        }

        LocalBounds bounds = _bounds ??= MeasureBounds();
        (min, max) = (bounds.Min, bounds.Max);
        return true;
    }

    /// <summary>
    /// A word of occupancy at a time, never a voxel: a word is two rows along x of one y — z even in
    /// its low half, odd in its high one — so the rows' bits together give x, the halves z, and the
    /// first and last words y.
    /// </summary>
    private LocalBounds MeasureBounds()
    {
        uint xs = 0;
        uint zs = 0;
        int first = -1;
        int last = -1;
        for (int word = 0; word < OccupancyWords; word++)
        {
            ulong bits = _occupancy[word];
            if (bits == 0)
            {
                continue;
            }

            if (first < 0)
            {
                first = word;
            }

            last = word;
            uint low = (uint)bits;
            uint high = (uint)(bits >> 32);
            xs |= low | high;
            int z = (word & 15) * 2;
            zs |= (low != 0 ? 1u << z : 0u) | (high != 0 ? 1u << (z + 1) : 0u);
        }

        return new LocalBounds(
            new Int3(System.Numerics.BitOperations.TrailingZeroCount(xs), first >> 4, System.Numerics.BitOperations.TrailingZeroCount(zs)),
            new Int3(31 - System.Numerics.BitOperations.LeadingZeroCount(xs), last >> 4, 31 - System.Numerics.BitOperations.LeadingZeroCount(zs)));
    }
}
