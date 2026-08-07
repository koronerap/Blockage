using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The 256-entry palette as a 16x16 swatch grid plus an editor for the active entry. Changing a
/// color repaints every voxel that uses the index — that is what storing indices buys us (§3).
/// </summary>
public sealed class PalettePanel
{
    private const float SwatchSize = 22f;
    private const int Columns = 16;

    private int _editingIndex = -1;
    private Color32 _editingBefore;

    public void Draw(EditorSession session)
    {
        ImGui.SetNextWindowPos(new Vector2(12, 320), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(400, 480), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Palette"))
        {
            ImGui.End();
            return;
        }

        Palette palette = session.World.Palette;
        DrawSwatchGrid(session, palette);

        ImGui.Separator();
        DrawActiveColorEditor(session, palette);

        ImGui.End();
    }

    private static void DrawSwatchGrid(EditorSession session, Palette palette)
    {
        // Index 0 is the empty marker and is never selectable; the grid starts at 1.
        for (int index = 1; index < Palette.Size; index++)
        {
            if ((index - 1) % Columns != 0)
            {
                ImGui.SameLine(0f, 2f);
            }

            ImGui.PushID(index);

            Vector4 color = palette[index].ToVector4();
            bool isActive = index == session.ActiveColorIndex;

            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 1f, 1f, 1f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            }

            if (ImGui.ColorButton($"##swatch{index}", color, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip, new Vector2(SwatchSize, SwatchSize)))
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
    }

    private void DrawActiveColorEditor(EditorSession session, Palette palette)
    {
        int active = session.ActiveColorIndex;
        ImGui.Text($"Active index: {active}");

        Vector4 rgba = palette[active].ToVector4();
        var rgb = new Vector3(rgba.X, rgba.Y, rgba.Z);

        if (ImGui.ColorPicker3("##activecolor", ref rgb, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.DisplayRGB))
        {
            // Remember the pre-edit color once, at the start of the drag, so the whole drag lands
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
    }
}
