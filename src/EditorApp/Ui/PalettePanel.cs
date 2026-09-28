using System.Numerics;
using EditorApp.Core.Editing;
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

    /// <summary>
    /// The palette at a glance, for the popover under the tool column: the saved colours, then the
    /// library, in small swatches. Returns true when a colour was picked, so the popover can close —
    /// choosing a colour is the one thing it is opened for.
    /// </summary>
    public bool DrawQuickPalette(EditorSession session)
    {
        Palette palette = session.Scene.Palette;
        bool picked = false;

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
        ImGui.TextDisabled("More in the Palette tab. Alt+click the model samples a colour.");
        return picked;
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
        ImGui.SameLine(ImGui.GetContentRegionMax().X - pickWidth);
        if (ImGui.Button("Pick", new Vector2(pickWidth, 0f)))
        {
            openPicker = true;
        }

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
