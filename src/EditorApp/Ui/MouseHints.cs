using EditorApp.Core.Editing;
using EditorApp.Input;

namespace EditorApp.Ui;

/// <summary>One entry in the status bar's hint strip: a button, what to hold with it, and what it does.</summary>
public readonly record struct MouseHint(Icons.Painter? Icon, string Keys, string Action);

/// <summary>
/// What the mouse does right now, for the left end of the status bar — Blender's hint strip. It
/// follows the active tool and the modifier keys held, so pressing Shift shows what Shift would do
/// before anything is clicked.
///
/// Kept apart from the drawing so what it says can be tested without ImGui.
/// </summary>
public static class MouseHints
{
    public static IReadOnlyList<MouseHint> For(EditorSession session, bool looking, bool shift, bool control, bool alt)
    {
        // While looking the mouse is spoken for, and the keys are what fly.
        if (looking)
        {
            return
            [
                new(null, "W A S D", "Fly"),
                new(null, "Q E", "Down, up"),
                new(null, "Shift", "Faster"),
            ];
        }

        var hints = new List<MouseHint>();

        if (LeftButton(session, shift, control, alt) is { } left)
        {
            hints.Add(new(Icons.MouseLeft, string.Empty, left));
        }

        hints.Add(new(Icons.MouseMiddle, string.Empty, "Orbit"));
        hints.Add(new(Icons.MouseMiddle, "Shift", "Pan"));

        hints.Add(session.ActiveTool == EditorTool.Paint && control
            ? new(Icons.MouseWheel, string.Empty, "Brush size")
            : new(Icons.MouseWheel, string.Empty, "Zoom"));

        hints.Add(new(Icons.MouseRight, string.Empty, "Look"));

        return hints;
    }

    /// <summary>The Select tool's key, or its name when it has none.</summary>
    private static string SelectKey() => Shortcut.Of(EditorAction.ToolSelect) is { Length: > 0 } key ? key : "the Select tool";

    /// <summary>What a left click or drag in the viewport does with the active tool, or null for none.</summary>
    private static string? LeftButton(EditorSession session, bool shift, bool control, bool alt) =>
        session.ActiveTool switch
        {
            EditorTool.Select when shift => "Add to the selection",
            EditorTool.Select when control => "Take from the selection",
            EditorTool.Select => "Select, drag for a box",

            EditorTool.Transform when shift => session.Snap.Enabled ? "Drag without snapping" : "Drag with snapping",
            EditorTool.Transform => "Select, drag a handle",

            // The tools that write voxels reach only what is selected.
            EditorTool.Extrude or EditorTool.Paint or EditorTool.LoopCut when session.SelectedObjects.FirstOrDefault() is null =>
                $"Nothing selected - {SelectKey()} selects",

            EditorTool.Extrude when alt => "Remove from selection",
            EditorTool.Extrude when shift => "Add to selection",
            EditorTool.Extrude when session.ExtrudeSelectionMode == ExtrudeSelectionMode.Face => "Select a whole face",
            EditorTool.Extrude => "Drag to select",

            EditorTool.Paint when alt => "Pick a colour",
            EditorTool.Paint when control => "Paint a box",
            EditorTool.Paint when shift => "Paint a line",
            EditorTool.Paint => session.PaintMode switch
            {
                PaintMode.Bucket => "Fill",
                PaintMode.Pattern => "Fill with the pattern",
                _ => "Paint",
            },

            EditorTool.LoopCut => "Cut here",

            // View never edits, so the left button has nothing to say.
            _ => null,
        };
}
