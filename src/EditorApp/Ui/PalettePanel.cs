using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Three separate things that used to be one:
///
/// a fixed reference **library** that clicking never rewrites; a row of **custom slots** holding
/// whatever colours have been picked; and a **picker** that chooses a colour without touching
/// either until asked.
///
/// Editing a library entry is still possible — recolouring every voxel that uses an index is a real
/// feature of storing indices — but it is behind a right-click now rather than being what the
/// colour picker does by accident.
/// </summary>
public sealed class PalettePanel
{
    private const int Columns = 16;
    private const float SwatchGap = 2f;
    private const float PickerWidth = 240f;

    private const string PickerPopupId = "free-picker";
    private const string EditPopupId = "library-edit";

    private Vector3 _working = new(0.85f, 0.35f, 0.25f);

    private int _editingIndex = -1;
    private Color32 _editingBefore;
    private bool _openEditPopup;

    /// <summary>Swatch size in the quick palette: small enough for the whole library in one glance.</summary>
    private const float QuickSwatch = 15f;

    private static readonly Dictionary<int, (Vector2 Min, Vector2 Max)> QuickRects = [];

    /// <summary>Where a swatch of the quick palette was drawn last frame — for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max)? QuickSwatchRect(int index) =>
        QuickRects.TryGetValue(index, out var rect) ? rect : null;

    /// <summary>The colour wheel's width in the quick popover: big enough to aim in, small beside the swatches.</summary>
    private const float QuickPickerWidth = 176f;

    /// <summary>What the quick popover's wheel shows; taken from the colour in hand each time it opens.</summary>
    private Vector3 _quickWorking = new(0.85f, 0.35f, 0.25f);

    /// <summary>Where the quick popover's colour wheel was drawn last frame — for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max) QuickPickerRect { get; private set; }

    /// <summary>
    /// The palette at a glance, for the popover under the tool column: a colour wheel for any colour
    /// at all, and beside it the saved colours and the library in small swatches. Returns true when
    /// a swatch was picked, so the popover can close — a swatch is a choice made. The wheel is not:
    /// it is dragged about until the colour is right, and closing under it would end that at once.
    /// </summary>
    public bool DrawQuickPalette(EditorSession session)
    {
        Palette palette = session.Scene.Palette;
        bool picked = false;

        // From the colour in hand, not from wherever the wheel was left last time.
        if (ImGui.IsWindowAppearing())
        {
            Vector4 current = palette[session.ActiveColorIndex].ToVector4();
            _quickWorking = new Vector3(current.X, current.Y, current.Z);
        }

        ImGui.BeginGroup();
        DrawQuickWheel(session);
        ImGui.EndGroup();

        ImGui.SameLine(0f, 12f);

        ImGui.BeginGroup();
        int[] saved = [.. palette.SavedCustomSlots()];
        if (saved.Length > 0)
        {
            ImGui.TextDisabled("Custom");
            picked |= DrawQuickRow(session, palette, saved);
            ImGui.Spacing();
        }

        ImGui.TextDisabled("Library");
        picked |= DrawQuickRow(session, palette, [.. Enumerable.Range(1, Palette.CustomStart - 1)]);

        ImGui.Spacing();
        ImGui.TextDisabled("Alt+click the model samples a colour.");
        ImGui.EndGroup();
        return picked;
    }

    /// <summary>
    /// A hue ring with the triangle inside it, as Blender and Krita have: the hue round the ring,
    /// how pale and how dark in the triangle. Applied live, like the Palette tab's picker, into a
    /// working slot that is not a swatch until saved.
    /// </summary>
    private void DrawQuickWheel(EditorSession session)
    {
        ImGui.SetNextItemWidth(QuickPickerWidth);
        if (ImGui.ColorPicker3(
                "##quick-wheel",
                ref _quickWorking,
                ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview
                | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoAlpha))
        {
            session.SelectColor(Color32.FromVector4(new Vector4(_quickWorking, 1f)));
        }

        QuickPickerRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        bool alreadySaved = session.Scene.Palette.IsCustomSaved(session.ActiveColorIndex);
        bool inLibrary = !Palette.IsCustomIndex(session.ActiveColorIndex);

        ImGui.BeginDisabled(alreadySaved || inLibrary);
        if (ImGui.Button("Save colour##quick", new Vector2(QuickPickerWidth, 0f)))
        {
            session.SaveActiveColor();
        }

        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                inLibrary ? "Already in the library."
                : alreadySaved ? "Already saved."
                : "Keeps it among the Custom swatches.");
        }
    }

    private static bool DrawQuickRow(EditorSession session, Palette palette, int[] indices)
    {
        bool picked = false;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);

        for (int i = 0; i < indices.Length; i++)
        {
            if (i % Columns != 0)
            {
                ImGui.SameLine(0f, SwatchGap);
            }

            int index = indices[i];
            bool isActive = index == session.ActiveColorIndex;

            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Border, Theme.Text);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            }

            if (ImGui.ColorButton(
                    $"##quick{index}",
                    palette[index].ToVector4(),
                    ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder,
                    new Vector2(QuickSwatch, QuickSwatch)))
            {
                session.ActiveColorIndex = (byte)index;
                picked = true;
            }

            QuickRects[index] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

            if (isActive)
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Index {index}  {palette[index]}");
            }
        }

        ImGui.PopStyleVar();
        return picked;
    }

    public void DrawContent(EditorSession session)
    {
        Palette palette = session.Scene.Palette;

        DrawActiveRow(session, palette);

        if (Props.Section("Material", openByDefault: false))
        {
            DrawMaterial(session, palette);
        }

        if (Props.Section("Custom"))
        {
            DrawSavedSwatches(session, palette);
        }

        if (Props.Section("Library"))
        {
            DrawSwatches(session, palette, 1, Palette.CustomStart, custom: false);
        }

        DrawLibraryEditPopup(session, palette);
    }

    private static readonly FileBrowserDialog Browser = new();

    /// <summary>The file dialog the palette menu opens; drawn at the top level every frame.</summary>
    public static void DrawDialogs() => Browser.Draw();

    private static readonly (string Extension, string Name)[] Formats =
    [
        (".gpl", "GIMP palette (.gpl)"),
        (".hex", "Hex list (.hex)"),
        (".png", "Image (.png)"),
    ];

    /// <summary>
    /// Palettes in and out (Fullreleaseplan 4.4): a palette loaded over the level's — what was painted
    /// takes the new colours — or added to the custom slots; the palette written out for other tools;
    /// a ramp from the colour in hand to the second one; and a library of palettes kept for any level.
    /// </summary>
    private static void DrawPaletteMenu(EditorSession session)
    {
        if (!ImGui.BeginPopup("##palette-menu"))
        {
            return;
        }

        if (ImGui.BeginMenu("Load Palette"))
        {
            ImGui.TextDisabled("Its colours become entries 1, 2, 3...: what is painted recolours.");
            foreach ((string extension, string name) in Formats)
            {
                if (ImGui.MenuItem(name))
                {
                    Browser.Show(FileBrowserMode.Open, $"Load a palette ({extension})", extension, null, null, path => Import(session, path, replace: true));
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Add to Custom"))
        {
            ImGui.TextDisabled("Its colours go into the free custom slots.");
            foreach ((string extension, string name) in Formats)
            {
                if (ImGui.MenuItem(name))
                {
                    Browser.Show(FileBrowserMode.Open, $"Add a palette's colours ({extension})", extension, null, null, path => Import(session, path, replace: false));
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Export Palette"))
        {
            foreach ((string extension, string name) in Formats)
            {
                if (ImGui.MenuItem(name))
                {
                    Browser.Show(FileBrowserMode.Save, $"Export the palette ({extension})", extension, null, session.ProjectName, path => Export(session, path));
                }
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.BeginMenu("Ramp to the Second Colour"))
        {
            foreach (int steps in new[] { 3, 4, 5, 6, 8 })
            {
                if (ImGui.MenuItem($"{steps} colours"))
                {
                    int kept = session.AddRamp(steps);
                    ReportLog.Shared.Post(kept > 0 ? $"Kept {kept} ramp colours in Custom." : "No room in Custom, or the colours are there already.", kept > 0 ? ReportKind.Info : ReportKind.Warning);
                }
            }

            ImGui.TextDisabled("From the colour in hand to the second colour,\nboth ends in, into the free custom slots.");
            ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.BeginMenu("Library"))
        {
            if (ImGui.MenuItem("Keep This Palette..."))
            {
                Directory.CreateDirectory(PaletteFiles.LibraryDirectory);
                Browser.Show(FileBrowserMode.Save, "Keep the palette in the library", ".gpl", PaletteFiles.LibraryDirectory, session.ProjectName, path => Export(session, path));
            }

            string[] kept = Directory.Exists(PaletteFiles.LibraryDirectory)
                ? [.. Directory.EnumerateFiles(PaletteFiles.LibraryDirectory).Where(f => PaletteFiles.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Order()]
                : [];

            if (kept.Length > 0)
            {
                ImGui.Separator();
            }

            foreach (string path in kept)
            {
                if (ImGui.BeginMenu(Path.GetFileNameWithoutExtension(path)))
                {
                    if (ImGui.MenuItem("Load"))
                    {
                        Import(session, path, replace: true);
                    }

                    if (ImGui.MenuItem("Add to Custom"))
                    {
                        Import(session, path, replace: false);
                    }

                    ImGui.EndMenu();
                }
            }

            if (kept.Length == 0)
            {
                ImGui.TextDisabled("Nothing kept yet.");
            }

            ImGui.EndMenu();
        }

        ImGui.EndPopup();
    }

    private static void Import(EditorSession session, string path, bool replace)
    {
        try
        {
            List<Color32> colours = PaletteFiles.Read(path);
            if (colours.Count == 0)
            {
                ReportLog.Shared.Post($"{Path.GetFileName(path)} has no colours in it.", ReportKind.Warning);
                return;
            }

            int placed = session.ImportPalette(colours, replace);
            ReportLog.Shared.Post(replace
                ? $"Loaded {placed} colours from {Path.GetFileName(path)}."
                : $"Kept {placed} of {colours.Count} colours from {Path.GetFileName(path)} in Custom.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or Core.Import.ImageDecodeException)
        {
            ReportLog.Shared.Post($"Could not read {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
        }
    }

    private static void Export(EditorSession session, string path)
    {
        try
        {
            List<Color32> colours = session.PaletteColours();
            PaletteFiles.Write(path, colours, Path.GetFileNameWithoutExtension(path));
            ReportLog.Shared.Post($"Wrote {colours.Count} colours to {Path.GetFileName(path)}.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ReportLog.Shared.Post($"Could not write {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
        }
    }

    /// <summary>The entry a material drag started on, and what its material was then; null between drags.</summary>
    private (int Index, VoxelMaterial Before)? _materialEdit;

    /// <summary>
    /// What the colour in hand is made of: how it glows, how metallic, how rough, how see-through.
    /// It belongs to the colour, so every voxel painted with it has it, and it goes to the game.
    /// </summary>
    private void DrawMaterial(EditorSession session, Palette palette)
    {
        int index = session.ActiveColorIndex;
        VoxelMaterial material = palette.Material(index);

        float emission = material.Emission;
        float metallic = material.Metallic;
        float roughness = material.Roughness;
        float opacity = material.Opacity;

        bool changed = Props.Slider("Emission", "material-emission", ref emission, 0f, 1f, "%.2f");
        Hint("Its own light, whatever lights it: lamps, screens, lava.");
        changed |= Props.Slider("Metallic", "material-metallic", ref metallic, 0f, 1f, "%.2f");
        Hint("A metal reflects in its own colour.");
        changed |= Props.Slider("Roughness", "material-roughness", ref roughness, 0f, 1f, "%.2f");
        Hint("1 matte, 0 a sharp highlight.");
        changed |= Props.Slider("Opacity", "material-opacity", ref opacity, 1f - VoxelMaterial.MaxTransparency, 1f, "%.2f");
        Hint("Below 1 it lets light through: glass, water, ice.");

        if (changed)
        {
            _materialEdit ??= (index, material);
            session.SetMaterial(index, VoxelMaterial.Of(emission, metallic, roughness, opacity));
        }

        if (!material.IsPlain && Props.Buttons(string.Empty, "material-plain", "Back to plain") == 0)
        {
            session.SetMaterial(index, VoxelMaterial.Plain);
            session.PushMaterialEdit(index, material);
        }

        // One drag, one undo step.
        if (_materialEdit is { } edit && !ImGui.IsAnyItemActive())
        {
            session.PushMaterialEdit(edit.Index, edit.Before);
            _materialEdit = null;
        }
    }

    private static void Hint(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    private void DrawActiveRow(EditorSession session, Palette palette)
    {
        int active = session.ActiveColorIndex;
        float height = ImGui.GetFrameHeight();

        // The swatch itself opens the picker. Clicking a colour to change it is the obvious
        // gesture, and a button beside it was the only way in.
        bool openPicker = ImGui.ColorButton(
            "##active",
            palette[active].ToVector4(),
            ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip,
            new Vector2(height * 2f, height));

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to choose any colour.");
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"Index {active}");
        ImGui.SameLine();
        ImGui.TextDisabled(palette[active].ToString());

        const float pickWidth = 52f;
        float more = ImGui.GetFrameHeight();
        ImGui.SameLine(ImGui.GetContentRegionMax().X - pickWidth - more - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.Button("Pick", new Vector2(pickWidth, 0f)))
        {
            openPicker = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("...", new Vector2(more, 0f)))
        {
            ImGui.OpenPopup("##palette-menu");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Palettes in and out, ramps, the library.");
        }

        DrawPaletteMenu(session);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Choose any colour. It is matched to the palette,\nor saved to a custom slot if it is new.");
        }

        if (openPicker)
        {
            // Start from where the user already is, rather than from whatever was picked last.
            Vector4 current = palette[active].ToVector4();
            _working = new Vector3(current.X, current.Y, current.Z);
            ImGui.OpenPopup(PickerPopupId);
        }

        DrawPickerPopup(session);
    }

    /// <summary>
    /// A colour chosen on its own terms. Nothing happens to the level until "Use colour", which
    /// selects a matching entry if one exists and claims a custom slot if not.
    /// </summary>
    private void DrawPickerPopup(EditorSession session)
    {
        if (!ImGui.BeginPopup(PickerPopupId))
        {
            return;
        }

        // An explicit width is required. A popup auto-sizes to its contents, and the picker sizes
        // itself from the window, so left to themselves on the first frame they collapse each
        // other to nothing and the picker cannot be used at all.
        ImGui.SetNextItemWidth(PickerWidth);

        // Applied as it is dragged, so the active colour is whatever the picker shows. It goes to a
        // working slot, which is not a swatch until it is saved.
        if (ImGui.ColorPicker3(
                "##picker",
                ref _working,
                ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.DisplayRGB))
        {
            session.SelectColor(Color32.FromVector4(new Vector4(_working, 1f)));
        }

        bool alreadySaved = session.Scene.Palette.IsCustomSaved(session.ActiveColorIndex);
        bool inLibrary = !Palette.IsCustomIndex(session.ActiveColorIndex);

        ImGui.BeginDisabled(alreadySaved || inLibrary);
        if (ImGui.Button("Save colour", new Vector2(PickerWidth, 0f)))
        {
            session.SaveActiveColor();
        }

        ImGui.EndDisabled();

        ImGui.TextDisabled(
            inLibrary ? $"Already in the library at index {session.ActiveColorIndex}."
            : alreadySaved ? "Saved."
            : $"Keeps it in Custom ({session.Scene.Palette.FreeCustomSlots} slots free).");

        ImGui.EndPopup();
    }

    /// <summary>
    /// Only the colours kept on purpose. Working colours occupy slots too — they have to, once a
    /// voxel carries one — but showing them would turn this into a log of everything ever used.
    /// </summary>
    private void DrawSavedSwatches(EditorSession session, Palette palette)
    {
        int[] saved = [.. palette.SavedCustomSlots()];

        if (saved.Length == 0)
        {
            ImGui.TextDisabled("None yet.");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Pick a colour and press Save colour to keep it here.");
            }

            return;
        }

        float available = ImGui.GetContentRegionAvail().X;
        float swatch = MathF.Max((available - ((Columns - 1) * SwatchGap)) / Columns, 8f);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);

        for (int i = 0; i < saved.Length; i++)
        {
            if (i % Columns != 0)
            {
                ImGui.SameLine(0f, SwatchGap);
            }

            ImGui.PushID(saved[i]);
            DrawSwatch(session, palette, saved[i], swatch, custom: true);
            ImGui.PopID();
        }

        ImGui.PopStyleVar();
    }

    private void DrawSwatches(EditorSession session, Palette palette, int from, int to, bool custom)
    {
        float available = ImGui.GetContentRegionAvail().X;
        float swatch = MathF.Max((available - ((Columns - 1) * SwatchGap)) / Columns, 8f);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);

        for (int index = from; index < to; index++)
        {
            if ((index - from) % Columns != 0)
            {
                ImGui.SameLine(0f, SwatchGap);
            }

            ImGui.PushID(index);
            DrawSwatch(session, palette, index, swatch, custom);
            ImGui.PopID();
        }

        ImGui.PopStyleVar();
    }

    private void DrawSwatch(EditorSession session, Palette palette, int index, float size, bool custom)
    {
        bool free = custom && palette.IsCustomSlotFree(index);
        bool isActive = index == session.ActiveColorIndex;

        // A free slot is drawn as a recess rather than a colour, so the row reads as "space for
        // more" instead of a run of black swatches.
        Vector4 colour = free ? Theme.Sunken : palette[index].ToVector4();

        if (isActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.Text);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
        }

        if (ImGui.ColorButton(
                $"##swatch{index}",
                colour,
                ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder,
                new Vector2(size, size))
            && !free)
        {
            session.ActiveColorIndex = (byte)index;
        }

        if (isActive)
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(free
                ? $"Empty custom slot {index}"
                : $"Index {index}\n{palette[index]}\n\nRight-click for options.");
        }

        DrawSwatchMenu(session, index, custom, free);
    }

    private void DrawSwatchMenu(EditorSession session, int index, bool custom, bool free)
    {
        if (free || !ImGui.BeginPopupContextItem("##swatch-menu"))
        {
            return;
        }

        if (ImGui.MenuItem("Edit colour..."))
        {
            // Only record the intent. Opening a popup from inside the popup that is closing puts
            // it on the wrong ID stack, so the panel opens it next frame instead.
            _editingIndex = index;
            _editingBefore = session.Scene.Palette[index];
            _openEditPopup = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Recolours every voxel already using this index.");
        }

        if (custom && ImGui.MenuItem("Clear slot"))
        {
            session.ClearCustomColor(index);
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// Editing an entry in place. Kept explicit because it changes the level, not the selection:
    /// every voxel on that index is repainted.
    /// </summary>
    private void DrawLibraryEditPopup(EditorSession session, Palette palette)
    {
        if (_openEditPopup)
        {
            ImGui.OpenPopup(EditPopupId);
            _openEditPopup = false;
        }

        if (_editingIndex < 1 || !ImGui.BeginPopup(EditPopupId))
        {
            return;
        }

        ImGui.SeparatorText($"Palette index {_editingIndex}");
        ImGui.TextDisabled("Every voxel using this index is recoloured.");

        Vector4 rgba = palette[_editingIndex].ToVector4();
        var rgb = new Vector3(rgba.X, rgba.Y, rgba.Z);

        ImGui.SetNextItemWidth(PickerWidth);
        if (ImGui.ColorPicker3("##edit", ref rgb, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.DisplayRGB))
        {
            session.ApplyPaletteColor(_editingIndex, Color32.FromVector4(new Vector4(rgb, 1f)));
        }

        if (ImGui.Button("Done", new Vector2(PickerWidth, 0f)))
        {
            // The whole drag lands in history as one step, not one per frame.
            session.PushPaletteEdit(_editingIndex, _editingBefore, palette[_editingIndex]);
            _editingIndex = -1;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
