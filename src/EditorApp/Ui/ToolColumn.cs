using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The four tools, floating over the top-left of the viewport. The spec fixes the tool set at
/// exactly four (EditorApp.md, "Araçlar"); View is reachable by its shortcut but has no button,
/// because the camera already works from inside every other tool.
/// </summary>
public static class ToolColumn
{
    private const float ButtonSize = 38f;

    private static readonly (EditorTool Tool, Icons.Painter Icon, string Name, string Shortcut, string Help)[] Tools =
    [
        (EditorTool.Transform, Icons.Move, "Transform", "Q", "Move and rotate a whole object."),
        (EditorTool.Extrude, Icons.Extrude, "Extrude", "W", "Select a surface, then drag its arrow.\nOut adds voxels, in deletes them."),
        (EditorTool.Paint, Icons.Paint, "Paint", "E", "Recolor existing, visible voxels.\nNever creates or deletes."),
        (EditorTool.LoopCut, Icons.Cut, "Loop Cut", "R", "Split the model at a grid plane\ninto two independent objects."),
    ];

    public static void Draw(EditorSession session)
    {
        // Translucent, since there is no panel behind them any more: the model shows through a
        // little, so the buttons read as floating over the scene rather than as holes cut in it.
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.SurfaceRaised with { W = 0.82f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.ControlHovered with { W = 0.92f });

        foreach ((EditorTool tool, Icons.Painter icon, string name, string shortcut, string help) in Tools)
        {
            if (IconButton.Draw(
                    tool.ToString(),
                    icon,
                    session.ActiveTool == tool,
                    $"{name}  ({shortcut})\n\n{help}",
                    ButtonSize))
            {
                session.ActiveTool = tool;
            }
        }

        ImGui.PopStyleColor(2);
    }
}
