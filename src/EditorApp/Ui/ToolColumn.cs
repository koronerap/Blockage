using EditorApp.Core.Editing;

namespace EditorApp.Ui;

/// <summary>
/// The vertical tool strip down the left edge. The spec fixes the tool set at exactly four
/// (EditorApp.md, "Araçlar"); View is reachable by its shortcut but has no button, because the
/// camera already works from inside every other tool.
/// </summary>
public static class ToolColumn
{
    private const float ButtonSize = 38f;

    /// <summary>Just the button plus the panel's own padding.</summary>
    public static float Width => ButtonSize + 24f;

    private static readonly (EditorTool Tool, Icons.Painter Icon, string Name, string Shortcut, string Help)[] Tools =
    [
        (EditorTool.Transform, Icons.Move, "Transform", "Q", "Move and rotate a whole object."),
        (EditorTool.Extrude, Icons.Extrude, "Extrude", "W", "Select a surface, then drag its arrow.\nOut adds voxels, in deletes them."),
        (EditorTool.Paint, Icons.Paint, "Paint", "E", "Recolor existing, visible voxels.\nNever creates or deletes."),
        (EditorTool.LoopCut, Icons.Cut, "Loop Cut", "R", "Split the model at a grid plane\ninto two independent objects."),
    ];

    public static void Draw(EditorSession session)
    {
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
    }
}
