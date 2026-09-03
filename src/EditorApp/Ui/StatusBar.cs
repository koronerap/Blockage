using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The bottom strip: what the cursor is over, what is focused, and what a running drag is doing.
/// Live readouts belong here rather than in a panel, because the eye is already near the model.
/// </summary>
public static class StatusBar
{
    public static void Draw(ShellContext context)
    {
        EditorSession session = context.Session;

        ImGui.AlignTextToFramePadding();

        if (context.Hover is { } hit)
        {
            ImGui.TextDisabled($"{hit.Voxel.X}, {hit.Voxel.Y}, {hit.Voxel.Z}");
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"· {hit.Face}");
        }
        else
        {
            ImGui.TextDisabled("-");
        }

        // What is selected, and how big it is. The cursor's own coordinates answer "where am I";
        // this answers "how much have I got", which is the question a selection actually raises.
        //
        // Only while Extrude is the tool, for the same reason the outline is only drawn then: a
        // selection is kept when the tool is left so coming back finds it, and reporting a size
        // for something no longer on screen is worse than saying nothing.
        if (session.ActiveTool == EditorTool.Extrude
            && session.Selection is { IsEmpty: false } selection
            && selection.TryGetBounds(out Int3 min, out Int3 max))
        {
            Int3 size = max - min + Int3.One;

            ImGui.SameLine(0f, 20f);
            ImGui.TextColored(Theme.Highlight, $"{size.X} x {size.Y} x {size.Z}");
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"· {selection.Count:N0} faces");
        }

        if (session.Scene.Focus is { } focus)
        {
            ImGui.SameLine(0f, 20f);
            ImGui.TextColored(Theme.Highlight, focus.Name);
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"· {focus.Grid.SolidCount:N0} voxels");
        }

        // A drag's own number, in the one place the user is already looking.
        if (context.DragReadout.Length > 0)
        {
            ImGui.SameLine(0f, 20f);
            ImGui.TextColored(Theme.Highlight, context.DragReadout);
        }

        if (session.History.CanUndo)
        {
            ImGui.SameLine(0f, 20f);
            ImGui.TextDisabled($"Last: {session.History.NextUndoName}");
        }

        DrawRightAligned(context);
    }

    private static void DrawRightAligned(ShellContext context)
    {
        float fps = context.FrameSeconds > 0f ? 1f / context.FrameSeconds : 0f;
        string text = $"{context.Renderer.VisibleChunks} chunks · {context.Renderer.DrawnTriangles:N0} tris · {fps:0} fps";

        float width = ImGui.CalcTextSize(text).X;
        ImGui.SameLine(ImGui.GetWindowWidth() - width - ImGui.GetStyle().WindowPadding.X);
        ImGui.TextDisabled(text);
    }
}
