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

    private readonly Color32[] _colors = new Color32[Size];

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
        return clone;
    }

    /// <summary>
    /// A usable starting palette: a 15-step grayscale ramp followed by 240 colors laid out as
    /// 24 hues x 5 values x 2 saturations.
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
        for (int hue = 0; hue < 24; hue++)
        {
            for (int sat = 0; sat < 2; sat++)
            {
                for (int val = 0; val < 5; val++)
                {
                    palette._colors[index++] = Color32.FromHsv(
                        hue * 15f,
                        sat == 0 ? 1.0f : 0.55f,
                        0.35f + val * 0.1625f);
                }
            }
        }

        return palette;
    }
}
