using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The 256-entry palette as a 16x16 swatch grid, plus an editor for the active entry behind a
/// popover. Changing a colour repaints every voxel that uses the index - that is what storing
/// indices buys us.
///
/// The picker is a popover rather than a permanent panel because it is the tallest control in the
/// interface and it is needed for a few seconds at a time.
/// </summary>
public sealed class PalettePanel
{
    private const int Columns = 16;
    private const float SwatchGap = 2f;

    private int _editingIndex = -1;
    private Color32 _editingBefore;

    /// <summary>Draws into whatever panel the shell has already opened.</summary>
    public void DrawContent(EditorSession session)
    {
        Palette palette = session.Scene.Palette;

        DrawActiveRow(session, palette);
        ImGui.Spacing();
        DrawSwatchGrid(session, palette);
    }

    /// <summary>The active colour, its index, and the button that opens the picker.</summary>
    private void DrawActiveRow(EditorSession session, Palette palette)
    {
        int active = session.ActiveColorIndex;
        Vector4 rgba = palette[active].ToVector4();

        float height = ImGui.GetFrameHeight();
        if (ImGui.ColorButton("##active", rgba, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip, new Vector2(height * 2f, height)))
        {
            ImGui.OpenPopup("palette-picker");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to edit this palette entry.");
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"Index {active}");
        ImGui.SameLine();
        ImGui.TextDisabled(palette[active].ToString());

        DrawPickerPopup(session, palette, active);
    }

    private void DrawPickerPopup(EditorSession session, Palette palette, int active)
    {
        if (!ImGui.BeginPopup("palette-picker"))
        {
            return;
        }

        Vector4 rgba = palette[active].ToVector4();
        var rgb = new Vector3(rgba.X, rgba.Y, rgba.Z);

        if (ImGui.ColorPicker3("##activecolor", ref rgb, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.DisplayRGB))
        {
            // Remember the pre-edit colour once, at the start of the drag, so the whole drag lands
            // in history as a single step.
            if (_editingIndex != active)
            {
                _editingIndex = active;
                _editingBefore = palette[active];
            }

            session.ApplyPaletteColor(active, Color32.FromVector4(new Vector4(rgb, 1f)));
        }

        if (_editingIndex == active && ImGui.IsItemDeactivatedAfterEdit())
        {
            session.PushPaletteEdit(active, _editingBefore, palette[active]);
            _editingIndex = -1;
        }

        ImGui.EndPopup();
    }

    private static void DrawSwatchGrid(EditorSession session, Palette palette)
    {
        // Sized from the space actually available, so the last column is never clipped off the
        // edge of the properties panel.
        float available = ImGui.GetContentRegionAvail().X;
        float swatch = MathF.Max((available - (Columns - 1) * SwatchGap) / Columns, 8f);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);

        // Index 0 is the empty marker and is never selectable; the grid starts at 1.
        for (int index = 1; index < Palette.Size; index++)
        {
            if ((index - 1) % Columns != 0)
            {
                ImGui.SameLine(0f, SwatchGap);
            }

            ImGui.PushID(index);

            Vector4 color = palette[index].ToVector4();
            bool isActive = index == session.ActiveColorIndex;

            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Border, Theme.Text);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            }

            if (ImGui.ColorButton(
                    $"##swatch{index}",
                    color,
                    ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder,
                    new Vector2(swatch, swatch)))
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
                ImGui.SetTooltip($"Index {index}\n{palette[index]}");
            }

            ImGui.PopID();
        }

        ImGui.PopStyleVar();
    }
}
