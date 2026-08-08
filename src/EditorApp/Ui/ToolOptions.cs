using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The header strip: the active tool's own settings laid out horizontally, and nothing belonging to
/// any other tool. Controls that do not apply are absent rather than dimmed, so the strip is always
/// short enough to read at a glance.
/// </summary>
public static class ToolOptions
{
    public static void Draw(EditorSession session)
    {
        switch (session.ActiveTool)
        {
            case EditorTool.Transform:
                DrawTransform(session);
                break;

            case EditorTool.Extrude:
                DrawExtrude(session);
                break;

            case EditorTool.Paint:
                DrawPaint(session);
                break;

            case EditorTool.LoopCut:
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Hover the model to preview a cut plane, click to split it in two.");
                break;

            default:
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Camera only. Hold the right mouse button to look, WASD/QE to fly.");
                break;
        }
    }

    private static void DrawTransform(EditorSession session)
    {
        Segmented("##transform-mode", ["Move", "Rotate"], (int)session.TransformMode, "F", value =>
            session.TransformMode = (TransformMode)value);

        ImGui.SameLine(0f, 18f);

        ImGui.BeginDisabled(session.TransformMode != TransformMode.Move);
        Segmented("##transform-space", ["Global", "Local"], (int)session.TransformSpace, "X", value =>
            session.TransformSpace = (TransformSpace)value);
        ImGui.EndDisabled();

        ImGui.SameLine(0f, 18f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Snap on - hold Shift for free movement");
    }

    private static void DrawExtrude(EditorSession session)
    {
        Segmented("##extrude-select", ["Box", "Face"], (int)session.ExtrudeSelectionMode, "F", value =>
            session.ExtrudeSelectionMode = (ExtrudeSelectionMode)value);

        ImGui.SameLine(0f, 18f);

        bool creates = session.ExtrudeCreatesObject;
        if (ImGui.Checkbox("New object (X)", ref creates))
        {
            session.ExtrudeCreatesObject = creates;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Pulled voxels become a separate object instead of joining this one.");
        }

        ImGui.SameLine(0f, 18f);
        ImGui.AlignTextToFramePadding();

        if (session.IsExtruding)
        {
            ImGui.TextColored(Theme.Highlight, $"{session.ExtrudeSteps:+0;-0} units - Enter confirms, Esc cancels");
        }
        else if (session.Selection is { IsEmpty: false } selection)
        {
            ImGui.TextDisabled($"{selection.Count} face(s) selected - drag the arrow");
        }
        else
        {
            ImGui.TextDisabled("Drag a surface to select. Shift adds, Alt subtracts.");
        }
    }

    private static void DrawPaint(EditorSession session)
    {
        Segmented("##paint-mode", ["Brush", "Bucket", "Pattern"], (int)session.PaintMode, "X", value =>
            session.PaintMode = (PaintMode)value);

        ImGui.SameLine(0f, 18f);

        if (session.PaintMode == PaintMode.Bucket)
        {
            int threshold = session.BucketThreshold;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.DragInt("Colour threshold", ref threshold, 1f, 0, 128))
            {
                session.BucketThreshold = threshold;
            }
        }
        else
        {
            float radius = session.BrushRadius;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.DragFloat("Radius", ref radius, 0.1f, 0f, 12f, "%.1f voxels"))
            {
                session.BrushRadius = radius;
            }

            ImGui.SameLine(0f, 18f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Ctrl+Scroll resizes · Shift drag = line · Ctrl drag = box · Alt = sample");
        }

        if (session.PaintMode == PaintMode.Pattern)
        {
            ImGui.SameLine(0f, 18f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Highlight, "Not built yet");
        }
    }

    /// <summary>
    /// A joined run of buttons rather than a row of radio circles — the same choice, but it reads as
    /// a mode switch instead of a questionnaire.
    /// </summary>
    private static void Segmented(string id, string[] labels, int selected, string shortcut, Action<int> onChange)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(2f, 0f));

        for (int i = 0; i < labels.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            bool active = i == selected;
            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHovered);
            }

            if (ImGui.Button($"{labels[i]}{id}{i}") && !active)
            {
                onChange(i);
            }

            if (active)
            {
                ImGui.PopStyleColor(2);
            }
        }

        ImGui.PopStyleVar();

        ImGui.SameLine(0f, 6f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"({shortcut})");
    }
}
