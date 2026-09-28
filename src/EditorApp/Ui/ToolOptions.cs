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
        float size = ImGui.GetFrameHeight();

        if (IconButton.Toggle(
                "transform-mode",
                (Icons.Move, "Move"),
                (Icons.Rotate, "Rotate"),
                session.TransformMode == TransformMode.Rotate,
                "F",
                size))
        {
            session.TransformMode = session.TransformMode == TransformMode.Move
                ? TransformMode.Rotate
                : TransformMode.Move;
        }

        ImGui.SameLine(0f, 8f);

        // The edge hinge a rotation turns about is always the object's own, so the choice has
        // nothing to say while rotating.
        ImGui.BeginDisabled(session.TransformMode != TransformMode.Move);
        if (IconButton.Toggle(
                "transform-space",
                (Icons.Global, "Global space"),
                (Icons.Local, "Local space"),
                session.TransformSpace == TransformSpace.Local,
                "X",
                size))
        {
            session.TransformSpace = session.TransformSpace == TransformSpace.Global
                ? TransformSpace.Local
                : TransformSpace.Global;
        }

        ImGui.EndDisabled();

        ImGui.SameLine(0f, 18f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Snap on - hold Shift for free movement");
    }

    private static void DrawExtrude(EditorSession session)
    {
        if (IconButton.Toggle(
                "extrude-select",
                (Icons.BoxSelect, "Box select"),
                (Icons.FaceSelect, "Whole face"),
                session.ExtrudeSelectionMode == ExtrudeSelectionMode.Face,
                "F",
                ImGui.GetFrameHeight()))
        {
            session.ExtrudeSelectionMode = session.ExtrudeSelectionMode == ExtrudeSelectionMode.Box
                ? ExtrudeSelectionMode.Face
                : ExtrudeSelectionMode.Box;
        }

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
        IconButton.Choice(
            "paint-mode",
            [(Icons.Brush, "Brush"), (Icons.Bucket, "Bucket fill"), (Icons.Pattern, "Pattern fill")],
            (int)session.PaintMode,
            "X",
            ImGui.GetFrameHeight(),
            value => session.PaintMode = (PaintMode)value);

        ImGui.SameLine(0f, 18f);

        if (session.PaintMode == PaintMode.Bucket)
        {
            bool whole = session.BucketWholeObject;
            if (ImGui.Checkbox("Whole object", ref whole))
            {
                session.BucketWholeObject = whole;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Recolour every voxel of the object instead of the surface under the "
                    + "cursor, including faces that are not exposed yet.");
            }

            // The threshold decides how far a surface fill spreads, and a whole-object fill does not
            // spread at all, so it has nothing left to say.
            ImGui.BeginDisabled(whole);
            ImGui.SameLine(0f, 18f);

            int threshold = session.BucketThreshold;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.DragInt("Colour match", ref threshold, 1f, 0, 128, threshold == 0 ? "exact" : "within %d"))
            {
                session.BucketThreshold = threshold;
            }

            ImGui.EndDisabled();
        }
        else
        {
            float radius = session.BrushRadius;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.DragFloat("Radius", ref radius, 0.1f, 0f, 12f, radius < 0.5f ? "one face" : "%.1f vx"))
            {
                session.BrushRadius = radius;
            }

            ImGui.SameLine(0f, 18f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Ctrl+Scroll resizes · Shift drag = line · Ctrl drag = box · Alt = sample");
        }

        if (session.PaintMode == PaintMode.Pattern)
        {
            DrawPatternControls(session);
        }
    }

    /// <summary>The pattern browser lives here rather than in a panel: it belongs to one sub-mode.</summary>
    private static readonly FileBrowserDialog PatternBrowser = new();

    private static string _patternStatus = string.Empty;

    public static void DrawDialogs() => PatternBrowser.Draw();

    private static void DrawPatternControls(EditorSession session)
    {
        ImGui.SameLine(0f, 18f);

        if (ImGui.Button("Load pattern..."))
        {
            PatternBrowser.Show(
                FileBrowserMode.Open,
                "Load a tiled pattern (.png)",
                ".png",
                PatternBrowser.CurrentDirectory,
                suggestedName: null,
                LoadPattern(session));
        }

        ImGui.SameLine(0f, 12f);
        ImGui.AlignTextToFramePadding();

        if (_patternStatus.Length > 0)
        {
            ImGui.TextColored(Theme.Danger, _patternStatus);
        }
        else if (session.Pattern is { } pattern)
        {
            ImGui.TextDisabled($"{pattern.Name}  {pattern.Width}x{pattern.Height}  ·  the clicked voxel takes its top-left pixel");
        }
        else
        {
            ImGui.TextDisabled("No pattern loaded - filling with the active colour instead.");
        }
    }

    private static Action<string> LoadPattern(EditorSession session) => path =>
    {
        try
        {
            session.Pattern = PatternSource.Load(path);
            _patternStatus = string.Empty;
        }
        catch (Exception exception) when (exception is Core.Import.ImageDecodeException or IOException)
        {
            session.Pattern = null;
            _patternStatus = exception.Message;
        }
    };
}
