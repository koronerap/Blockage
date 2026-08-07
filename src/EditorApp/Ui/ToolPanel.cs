using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>Tool selection, brush size, undo/redo and what the cursor is currently over.</summary>
public static class ToolPanel
{
    private static readonly (EditorTool Tool, string Label, string Shortcut, string Help)[] Tools =
    [
        (EditorTool.Place, "Place", "1", "Adds a voxel against the face under the cursor."),
        (EditorTool.Erase, "Erase", "2", "Removes the voxel under the cursor."),
        (EditorTool.Paint, "Paint", "3", "Recolors the voxel under the cursor."),
        (EditorTool.Fill, "Fill", "4", "Recolors the connected run of matching voxels."),
        (EditorTool.Pick, "Pick", "5", "Adopts the color under the cursor."),
        (EditorTool.BoxSelect, "Select", "6", "Drags out a box selection. Esc clears it."),
        (EditorTool.Extrude, "Extrude", "7", "Pulls the connected surface out one layer. Hold Alt to push it in."),
    ];

    public static void Draw(EditorSession session, RaycastHit? hover)
    {
        ImGui.SetNextWindowPos(new Vector2(12, 170), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(400, 140), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Tools"))
        {
            ImGui.End();
            return;
        }

        foreach ((EditorTool tool, string label, string shortcut, string help) in Tools)
        {
            if (tool != Tools[0].Tool)
            {
                ImGui.SameLine();
            }

            if (ImGui.RadioButton($"{label} ({shortcut})", session.ActiveTool == tool))
            {
                session.ActiveTool = tool;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(help);
            }
        }

        int radius = session.BrushRadius;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderInt("Brush radius", ref radius, 0, 8))
        {
            session.BrushRadius = radius;
        }

        ImGui.SameLine();
        int side = session.BrushRadius * 2 + 1;
        ImGui.TextDisabled($"{side}x{side}x{side}");

        ImGui.BeginDisabled(!session.History.CanUndo);
        if (ImGui.Button($"Undo ({session.History.NextUndoName ?? "-"})"))
        {
            session.Undo();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.History.CanRedo);
        if (ImGui.Button($"Redo ({session.History.NextRedoName ?? "-"})"))
        {
            session.Redo();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextDisabled($"{session.History.RetainedCells:N0} cells retained");

        if (hover is { } hit)
        {
            ImGui.Text($"Hover  {hit.Voxel}  face {hit.Face}  ->  place at {hit.Placement}");
        }
        else
        {
            ImGui.TextDisabled("Hover  -");
        }

        ImGui.End();
    }
}
