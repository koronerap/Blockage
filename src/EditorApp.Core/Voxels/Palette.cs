namespace EditorApp.Core.Voxels;

/// <summary>
/// The level's 256-entry color table. Voxels store an index into this table rather than a color
/// (EditorApp.md §3): it costs one byte instead of four, editing an entry recolors every voxel using
/// it, and the table itself becomes the exported palette texture (§6).
/// </summary>
public sealed class Palette
{
    /// <summary>Number of entries, including the reserved empty slot at index 0.</summary>
    public const int Size = 256;

    /// <summary>Index 0 means "no voxel" and is never rendered or exported.</summary>
    public const byte EmptyIndex = 0;

    /// <summary>
    /// Last entry of the default grayscale ramp, and therefore pure white — what a new level is
    /// built from, so the first thing on screen shows shape rather than colour.
    /// </summary>
    public const byte WhiteIndex = 15;

    /// <summary>
    /// Where the user's own colours start. Below this is a fixed reference library that picking a
    /// colour never rewrites; above it are slots the editor hands out as new colours are chosen.
    ///
    /// Splitting the range is what lets a colour be picked freely without silently repainting every
    /// voxel that happened to share an index with it.
    /// </summary>
    public const int CustomStart = 192;

    public const int CustomCount = Size - CustomStart;

    public static bool IsCustomIndex(int index) => index >= CustomStart;

    private readonly Color32[] _colors = new Color32[Size];

    /// <summary>
    /// Which custom slots the user actually asked to keep.
    ///
    /// A colour has to occupy a slot the moment it is painted with, because voxels store indices —
    /// but that does not make it a swatch worth showing. Only slots saved on purpose appear in the
    /// Custom row; the rest are working colours that happen to be anchored somewhere.
    /// </summary>
    private readonly bool[] _customSaved = new bool[CustomCount];

    public Palette()
    {
        _colors[EmptyIndex] = Color32.Transparent;
    }

    public Color32 this[int index]
    {
        get => _colors[index];
        set
        {
            if (index == EmptyIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index 0 is reserved for empty.");
            }

            _colors[index] = value;
        }
    }

    public ReadOnlySpan<Color32> Colors => _colors;

    /// <summary>
    /// A custom slot nobody has filled yet. Fully transparent is the marker: every real colour is
    /// written opaque, so no extra bookkeeping is needed and the state survives a save unchanged.
    /// </summary>
    public bool IsCustomSlotFree(int index) => IsCustomIndex(index) && _colors[index].A == 0;

    public int FreeCustomSlots
    {
        get
        {
            int free = 0;
            for (int i = CustomStart; i < Size; i++)
            {
                if (IsCustomSlotFree(i))
                {
                    free++;
                }
            }

            return free;
        }
    }

    /// <summary>Hands a custom slot back. Voxels still using it keep whatever colour it held.</summary>
    public void ClearCustomSlot(int index)
    {
        if (IsCustomIndex(index))
        {
            _colors[index] = Color32.Transparent;
            _customSaved[index - CustomStart] = false;
        }
    }

    /// <summary>True when this custom slot was kept on purpose rather than just used.</summary>
    public bool IsCustomSaved(int index) =>
        IsCustomIndex(index) && !IsCustomSlotFree(index) && _customSaved[index - CustomStart];

    public void SetCustomSaved(int index, bool saved)
    {
        if (IsCustomIndex(index))
        {
            _customSaved[index - CustomStart] = saved;
        }
    }

    /// <summary>The saved swatches, in slot order — what the Custom row shows.</summary>
    public IEnumerable<int> SavedCustomSlots()
    {
        for (int i = CustomStart; i < Size; i++)
        {
            if (IsCustomSaved(i))
            {
                yield return i;
            }
        }
    }

    public int SavedCustomCount
    {
        get
        {
            int count = 0;
            for (int i = CustomStart; i < Size; i++)
            {
                if (IsCustomSaved(i))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The index holding exactly this colour, ignoring alpha, or null.</summary>
    public byte? FindExact(Color32 color)
    {
        for (int i = 1; i < Size; i++)
        {
            Color32 candidate = _colors[i];
            if (candidate.A != 0 && candidate.R == color.R && candidate.G == color.G && candidate.B == color.B)
            {
                return (byte)i;
            }
        }

        return null;
    }

    public void CopyFrom(ReadOnlySpan<Color32> colors)
    {
        if (colors.Length != Size)
        {
            throw new ArgumentException($"Palette needs exactly {Size} entries.", nameof(colors));
        }

        colors.CopyTo(_colors);
        _colors[EmptyIndex] = Color32.Transparent;
    }

    public Palette Clone()
    {
        var clone = new Palette();
        _colors.CopyTo(clone._colors, 0);
        _customSaved.CopyTo(clone._customSaved, 0);
        return clone;
    }

    /// <summary>
    /// The fixed reference library: a 15-step grayscale ramp followed by 176 colors laid out as
    /// 22 hues x 2 saturations x 4 values. It stops at <see cref="CustomStart"/>, leaving the rest
    /// of the range free for whatever the user picks.
    /// </summary>
    public static Palette CreateDefault()
    {
        var palette = new Palette();

        for (int i = 0; i < 15; i++)
        {
            byte level = (byte)MathF.Round(i / 14f * 255f);
            palette._colors[1 + i] = new Color32(level, level, level);
        }

        int index = 16;
        for (int hue = 0; hue < 22; hue++)
        {
            for (int sat = 0; sat < 2; sat++)
            {
                for (int val = 0; val < 4; val++)
                {
                    palette._colors[index++] = Color32.FromHsv(
                        hue * (360f / 22f),
                        sat == 0 ? 1.0f : 0.55f,
                        0.40f + val * 0.2f);
                }
            }
        }

        // Anything left is a free custom slot, and stays transparent until it is claimed.
        return palette;
    }
}
