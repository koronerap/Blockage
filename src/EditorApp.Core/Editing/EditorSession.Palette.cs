using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>The whole palette's colours and kept slots swapped for others, reversibly: an import or a ramp.</summary>
public sealed class PaletteSwapCommand(VoxelScene scene, Color32[] before, Color32[] after, bool[] savedBefore, bool[] savedAfter, string name) : ICommand
{
    public string Name { get; } = name;

    public int RetainedCells => 1;

    public void Redo() => Apply(after, savedAfter);

    public void Undo() => Apply(before, savedBefore);

    private void Apply(Color32[] colours, bool[] saved)
    {
        for (int i = 1; i < Palette.Size; i++)
        {
            scene.Palette[i] = colours[i];
            if (Palette.IsCustomIndex(i))
            {
                scene.Palette.SetCustomSaved(i, saved[i]);
            }
        }

        scene.MarkAllDirty();
    }
}

/// <summary>Palettes in and out, and ramps between two colours (Fullreleaseplan 4.4).</summary>
public sealed partial class EditorSession
{
    /// <summary>The palette's colours now, and which custom slots are kept: what a swap goes back to.</summary>
    private (Color32[] Colours, bool[] Saved) PaletteState()
    {
        Palette palette = Scene.Palette;
        var colours = new Color32[Palette.Size];
        var saved = new bool[Palette.Size];
        for (int i = 1; i < Palette.Size; i++)
        {
            colours[i] = palette[i];
            saved[i] = Palette.IsCustomIndex(i) && palette.IsCustomSaved(i);
        }

        return (colours, saved);
    }

    private bool PushPaletteSwap((Color32[] Colours, bool[] Saved) before, string name)
    {
        (Color32[] Colours, bool[] Saved) after = PaletteState();
        if (before.Colours.AsSpan().SequenceEqual(after.Colours) && before.Saved.AsSpan().SequenceEqual(after.Saved))
        {
            return false;
        }

        History.Push(new PaletteSwapCommand(Scene, before.Colours, after.Colours, before.Saved, after.Saved, name));
        Scene.MarkAllDirty();
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// A palette brought in: with <paramref name="replace"/>, its colours become entries 1, 2, 3… —
    /// what was painted with those entries takes the new colours, as loading a palette does in any
    /// voxel editor — otherwise they are kept in the free custom slots, as many as fit. One undo step;
    /// how many colours went in.
    /// </summary>
    public int ImportPalette(IReadOnlyList<Color32> colours, bool replace)
    {
        (Color32[] Colours, bool[] Saved) before = PaletteState();
        Palette palette = Scene.Palette;
        int placed = 0;

        if (replace)
        {
            for (int i = 0; i < colours.Count && i + 1 < Palette.Size; i++)
            {
                palette[i + 1] = colours[i] with { A = 255 };
                if (Palette.IsCustomIndex(i + 1))
                {
                    palette.SetCustomSaved(i + 1, true);
                }

                placed++;
            }
        }
        else
        {
            placed = Keep(colours);
        }

        PushPaletteSwap(before, replace ? "Load palette" : "Add palette colours");
        return placed;
    }

    /// <summary>Colours kept in the free custom slots, as many as fit, skipping any the palette has already.</summary>
    private int Keep(IEnumerable<Color32> colours)
    {
        Palette palette = Scene.Palette;
        int kept = 0;
        foreach (Color32 colour in colours)
        {
            Color32 opaque = colour with { A = 255 };
            if (palette.FindExact(opaque) is not null)
            {
                continue;
            }

            int slot = Enumerable.Range(Palette.CustomStart, Palette.CustomCount).FirstOrDefault(palette.IsCustomSlotFree, -1);
            if (slot < 0)
            {
                break;
            }

            palette[slot] = opaque;
            palette.SetCustomSaved(slot, true);
            kept++;
        }

        return kept;
    }

    /// <summary>
    /// <paramref name="steps"/> colours from the colour in hand to the second colour, both ends
    /// included, kept in free custom slots: a ramp for shading. One undo step; how many were kept.
    /// </summary>
    public int AddRamp(int steps)
    {
        steps = Math.Clamp(steps, 2, Palette.CustomCount);
        Color32 from = Scene.Palette[ActiveColorIndex];
        Color32 to = Scene.Palette[SecondaryColorIndex];

        var ramp = new List<Color32>();
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)(steps - 1);
            ramp.Add(new Color32(
                (byte)MathF.Round(from.R + ((to.R - from.R) * t)),
                (byte)MathF.Round(from.G + ((to.G - from.G) * t)),
                (byte)MathF.Round(from.B + ((to.B - from.B) * t))));
        }

        (Color32[] Colours, bool[] Saved) before = PaletteState();
        int kept = Keep(ramp);
        PushPaletteSwap(before, "Add ramp");
        return kept;
    }

    /// <summary>The palette's colours for writing out: every entry that is a colour — the library, and the kept custom ones.</summary>
    public List<Color32> PaletteColours()
    {
        Palette palette = Scene.Palette;
        var colours = new List<Color32>();
        for (int i = 1; i < Palette.Size; i++)
        {
            if (palette[i].A > 0 && (!Palette.IsCustomIndex(i) || palette.IsCustomSaved(i)))
            {
                colours.Add(palette[i] with { A = 255 });
            }
        }

        return colours;
    }
}
