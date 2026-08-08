using EditorApp.Core.Editing;
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
            ImGui.TextDisabled("—");
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
