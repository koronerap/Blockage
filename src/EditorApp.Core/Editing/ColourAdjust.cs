using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>Colour as hue, saturation and value, for shifting colours the way a painter thinks of them.</summary>
public static class ColourMath
{
    /// <summary>Hue in degrees (0 to 360), saturation and value 0 to 1.</summary>
    public static (float Hue, float Saturation, float Value) ToHsv(Color32 colour)
    {
        float r = colour.R / 255f, g = colour.G / 255f, b = colour.B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        float hue = delta == 0f ? 0f
            : max == r ? 60f * (((g - b) / delta) % 6f)
            : max == g ? 60f * (((b - r) / delta) + 2f)
            : 60f * (((r - g) / delta) + 4f);

        return (hue < 0f ? hue + 360f : hue, max == 0f ? 0f : delta / max, max);
    }

    /// <summary>
    /// A colour turned round the hue circle by <paramref name="hueDegrees"/>, and its saturation and
    /// value moved by the given amounts (−1 to 1) — towards grey or full colour, black or its brightest.
    /// </summary>
    public static Color32 Shift(Color32 colour, float hueDegrees, float saturation, float value)
    {
        (float h, float s, float v) = ToHsv(colour);
        h = (((h + hueDegrees) % 360f) + 360f) % 360f;
        s = Math.Clamp(saturation >= 0f ? s + ((1f - s) * saturation) : s * (1f + saturation), 0f, 1f);
        v = Math.Clamp(value >= 0f ? v + ((1f - v) * value) : v * (1f + value), 0f, 1f);
        return Color32.FromHsv(h, s, v) with { A = colour.A };
    }
}

/// <summary>Recolouring what is selected (Fullreleaseplan 4.3).</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// The cells a recolour reaches: the chosen voxels in Edit Mode, else every voxel of the selected
    /// objects — each with the grid it is in.
    /// </summary>
    private IEnumerable<(VoxelObject Owner, IEnumerable<Int3> Cells)> RecolourTargets()
    {
        if (EditObject is { } edited)
        {
            return VoxelSelection.IsEmpty ? [(edited, ClipboardOperations.Everything(edited.Grid).ToList())] : [(edited, VoxelSelection.Cells.ToList())];
        }

        return FilterTargets().Select(o => (o, (IEnumerable<Int3>)ClipboardOperations.Everything(o.Grid).ToList()));
    }

    /// <summary>
    /// Every voxel and painted face of what is selected changed through <paramref name="map"/>, as one
    /// undo step; how many cells changed. A voxel's own colour and its painted faces are mapped apart,
    /// so painted faces stay painted.
    /// </summary>
    private int Recolour(string name, Func<byte, byte> map)
    {
        EndStroke();
        CancelExtrude();

        // Palette slots claimed along the way go in the same step, first, so undo takes them back too.
        _claims = [];
        var steps = new List<ICommand>();
        int changed = 0;
        Span<byte> faces = stackalloc byte[FaceInfo.Count];
        foreach ((VoxelObject owner, IEnumerable<Int3> cells) in RecolourTargets())
        {
            var command = new VoxelEditCommand(name, owner.Grid);
            foreach (Int3 cell in cells)
            {
                byte voxel = owner.Grid.GetVoxel(cell);
                if (voxel == Palette.EmptyIndex)
                {
                    continue;
                }

                for (int f = 0; f < FaceInfo.Count; f++)
                {
                    faces[f] = owner.Grid.GetFaceColor(cell, (Face)f);
                }

                byte recoloured = map(voxel);
                bool any = recoloured != voxel && command.Apply(cell, recoloured);
                for (int f = 0; f < FaceInfo.Count; f++)
                {
                    byte face = map(faces[f]);
                    if (face != recoloured || faces[f] != voxel)
                    {
                        any |= command.ApplyFace(cell, (Face)f, face);
                    }
                }

                changed += any ? 1 : 0;
            }

            if (!command.IsEmpty)
            {
                steps.Add(command);
            }
        }

        List<ICommand> claims = _claims;
        _claims = null;
        if (steps.Count == 0)
        {
            // Nothing recoloured: a slot claimed for it is given back.
            for (int i = claims.Count - 1; i >= 0; i--)
            {
                claims[i].Undo();
            }

            return 0;
        }

        History.Push(CompositeCommand.Of(name, [.. claims, .. steps]));
        HasUnsavedChanges = true;
        return changed;
    }

    /// <summary>Palette slots a recolour in progress has claimed.</summary>
    private List<ICommand>? _claims;

    /// <summary>"This colour, everywhere in what is selected, as that one."</summary>
    public int ReplaceColour(byte from, byte to) =>
        from == Palette.EmptyIndex || to == Palette.EmptyIndex || from == to
            ? 0
            : Recolour($"Replace colour {from} with {to}", index => index == from ? to : index);

    /// <summary>
    /// The colours of what is selected turned round the hue circle and moved in saturation and value.
    /// Each new colour is the palette's own where it has it, a custom slot where there is room, and
    /// the nearest the palette has where there is not.
    /// </summary>
    public int ShiftColours(float hueDegrees, float saturation, float value)
    {
        var chosen = new Dictionary<byte, byte>();
        Palette palette = Scene.Palette;

        byte Map(byte index)
        {
            if (index == Palette.EmptyIndex)
            {
                return index;
            }

            if (!chosen.TryGetValue(index, out byte result))
            {
                Color32 shifted = ColourMath.Shift(palette[index], hueDegrees, saturation, value);
                result = palette.FindExact(shifted) ?? ClaimSlot(shifted) ?? palette.Nearest(shifted);
                chosen[index] = result;
            }

            return result;
        }

        return Recolour("Adjust colours", Map);
    }

    /// <summary>A free custom slot given a colour and kept, or null when there is none free.</summary>
    private byte? ClaimSlot(Color32 colour)
    {
        for (int i = Palette.CustomStart; i < Palette.Size; i++)
        {
            if (Scene.Palette.IsCustomSlotFree(i))
            {
                var claim = new PaletteEditCommand(Scene, i, Scene.Palette[i], colour with { A = 255 });
                claim.Redo();
                Scene.Palette.SetCustomSaved(i, true);
                _claims?.Add(claim);
                return (byte)i;
            }
        }

        return null;
    }
}
