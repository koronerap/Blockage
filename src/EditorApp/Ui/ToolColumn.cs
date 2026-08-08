using System.Numerics;
using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The vertical tool strip down the left edge — the four tools plus View, and nothing else, because
/// the spec fixes the set at exactly that (EditorApp.md, "Araçlar").
///
/// Buttons rather than radio buttons: a radio row reads as a settings form, a column of large
/// targets with the active one marked reads as a toolbar.
///
/// Each button carries its shortcut letter as its face. Until there is an icon font, a single
/// character is the only label that fits a square button, and it happens to be the thing worth
/// memorising — the full name and description live in the tooltip.
/// </summary>
public static class ToolColumn
{
    private const float ButtonSize = 38f;

    /// <summary>Just the button plus the panel's own padding.</summary>
    public static float Width => ButtonSize + 24f;

    private static readonly (EditorTool Tool, string Name, string Shortcut, string Help)[] Tools =
    [
        (EditorTool.Transform, "Transform", "Q", "Move and rotate a whole object."),
        (EditorTool.Extrude, "Extrude", "W", "Select a surface, then drag its arrow.\nOut adds voxels, in deletes them."),
        (EditorTool.Paint, "Paint", "E", "Recolor existing, visible voxels.\nNever creates or deletes."),
        (EditorTool.LoopCut, "Loop Cut", "R", "Split the model at a grid plane\ninto two independent objects."),
        (EditorTool.View, "View", "V", "Camera only - no editing."),
    ];

    public static void Draw(EditorSession session)
    {
        foreach ((EditorTool tool, string name, string shortcut, string help) in Tools)
        {
            bool active = session.ActiveTool == tool;

            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
            }

            if (ImGui.Button($"{shortcut}##{tool}", new Vector2(ButtonSize, ButtonSize)))
            {
                session.ActiveTool = tool;
            }

            if (active)
            {
                ImGui.PopStyleColor(3);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{name}  ({shortcut})\n\n{help}");
            }
        }
    }
}
