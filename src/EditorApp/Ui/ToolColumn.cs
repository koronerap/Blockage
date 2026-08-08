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
/// Labels are plain ASCII on purpose. The font atlas is built with the default glyph ranges, so a
/// symbol outside Latin-1 renders as a hollow box — icons wait for a real icon font.
/// </summary>
public static class ToolColumn
{
    /// <summary>Wide enough for the longest tool name at the UI font size.</summary>
    public const float Width = 88f;

    private const float ButtonHeight = 40f;

    private static readonly (EditorTool Tool, string Name, string Shortcut, string Help)[] Tools =
    [
        (EditorTool.Transform, "Move", "Q", "Transform - move and rotate a whole object."),
        (EditorTool.Extrude, "Extrude", "W", "Select a surface, then drag its arrow.\nOut adds voxels, in deletes them."),
        (EditorTool.Paint, "Paint", "E", "Recolor existing, visible voxels.\nNever creates or deletes."),
        (EditorTool.LoopCut, "Cut", "R", "Loop Cut - split the model at a grid plane\ninto two independent objects."),
        (EditorTool.View, "View", "V", "Camera only - no editing."),
    ];

    public static void Draw(EditorSession session)
    {
        float width = ImGui.GetContentRegionAvail().X;

        foreach ((EditorTool tool, string name, string shortcut, string help) in Tools)
        {
            bool active = session.ActiveTool == tool;

            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
            }

            if (ImGui.Button($"{name}##{tool}", new Vector2(width, ButtonHeight)))
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
            float indent = (width - ImGui.CalcTextSize(shortcut).X) * 0.5f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(indent, 0f));
            ImGui.TextDisabled(shortcut);
        }
    }
}
