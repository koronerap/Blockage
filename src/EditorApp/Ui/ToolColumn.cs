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
/// </summary>
public static class ToolColumn
{
    private const float ButtonSize = 46f;

    /// <summary>
    /// Short marks stand in for icons until an icon font is added. They are deliberately not the
    /// tool's initial — the shortcut key is what the hand has to learn.
    /// </summary>
    private static readonly (EditorTool Tool, string Mark, string Name, string Shortcut, string Help)[] Tools =
    [
        (EditorTool.Transform, "✛", "Transform", "Q", "Move and rotate a whole object."),
        (EditorTool.Extrude, "↑", "Extrude", "W", "Select a surface, then drag its arrow.\nOut adds voxels, in deletes them."),
        (EditorTool.Paint, "●", "Paint", "E", "Recolor existing, visible voxels.\nNever creates or deletes."),
        (EditorTool.LoopCut, "✂", "Loop Cut", "R", "Split the model at a grid plane\ninto two independent objects."),
        (EditorTool.View, "□", "View", "V", "Camera only — no editing."),
    ];

    public static void Draw(EditorSession session)
    {
        foreach ((EditorTool tool, string mark, string name, string shortcut, string help) in Tools)
        {
            bool active = session.ActiveTool == tool;

            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
            }

            if (ImGui.Button($"{mark}##{name}", new Vector2(ButtonSize, ButtonSize)))
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

            // The shortcut under each button, so the keyboard is learned by using the mouse.
            float indent = (ButtonSize - ImGui.CalcTextSize(shortcut).X) * 0.5f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
            ImGui.TextDisabled(shortcut);
        }
    }
}
