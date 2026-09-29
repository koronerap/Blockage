using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The Tool tab: every setting of the tool in hand, laid out like the rest of Properties. The header
/// keeps the few that are reached for mid-gesture; this is where all of them are, with room for their
/// names — Blender's Tool tab next to its tool header.
/// </summary>
public static class ToolPanel
{
    public static void DrawContent(EditorSession session)
    {
        (Icons.Painter icon, string name, string shortcut) = ToolColumn.Describe(session.ActiveTool);

        float height = ImGui.GetFrameHeight();
        ImGui.Dummy(new Vector2(height, height));
        Vector2 min = ImGui.GetItemRectMin();
        icon(new ImGuiIconCanvas(ImGui.GetWindowDrawList(), ImGui.GetColorU32(Theme.Highlight), 1.5f), min + new Vector2(height * 0.5f), height * 0.34f);
        ImGui.SameLine(0f, 6f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Highlight, name);
        if (shortcut.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({shortcut})");
        }

        switch (session.ActiveTool)
        {
            case EditorTool.Select when session.InEditMode:
                Wrapped("Inside the object: click a voxel to choose it, drag for a box - seen voxels only, or through the model with X-Ray on. The wand takes the joined voxels of a colour, Colour every voxel of it. Shift adds, Ctrl takes away. Then move and turn them with the Transform tool, P separates them, Delete empties them. Tab goes back to the objects.");
                Props.Value("Chosen", $"{session.VoxelSelection.Count:N0} voxels");
                break;

            case EditorTool.Select:
                Wrapped("Click an object or a light to select it; drag for a box around several. Shift adds, and Shift-clicking the active one lets it go; Ctrl takes away. A click on nothing selects nothing. The other tools work only on what is selected.");
                Props.Value("Selected", ObjectMenu.SelectionSummary(session));
                break;

            case EditorTool.Transform:
                DrawTransform(session);
                break;

            case EditorTool.Extrude:
                DrawExtrude(session);
                break;

            case EditorTool.Paint:
                DrawPaint(session);
                break;

            case EditorTool.Sculpt:
                DrawSculpt(session);
                break;

            case EditorTool.LoopCut:
                Wrapped("Hover the model to preview a cut plane, then click to split the object in two along it.");
                break;

            default:
                Wrapped("The camera only. Hold the right mouse button to look, W A S D and Q E to fly.");
                break;
        }
    }

    private static void DrawSculpt(EditorSession session)
    {
        if (!Props.Section("Brush"))
        {
            return;
        }

        if (Props.BeginCombo("Mode", "sculpt-mode", session.SculptMode.ToString()))
        {
            foreach (SculptMode mode in Enum.GetValues<SculptMode>())
            {
                if (ImGui.Selectable(mode.ToString(), session.SculptMode == mode))
                {
                    session.SculptMode = mode;
                }
            }

            ImGui.EndCombo();
        }

        Tooltip("Ctrl turns a dab round - Add and Remove, Raise and Lower - and Shift smooths.");

        int shape = Props.Choice("Shape", "sculpt-shape", [(Icons.ShapeSphere, "Sphere"), (Icons.ObjectTab, "Cube")], (int)session.SculptShape);
        session.SculptShape = (SculptShape)shape;

        float radius = session.SculptRadius;
        if (Props.Float("Radius", "sculpt-radius", ref radius, 0.05f, SculptOperations.MinRadius, SculptOperations.MaxRadius, "%.1f"))
        {
            session.SculptRadius = radius;
        }

        Tooltip("In voxels. Ctrl+wheel over the model changes it too.");
        Wrapped("Works on the selected objects, a stroke at a time: each stroke is one undo step. New voxels take the colour in hand; Raise keeps the surface's own.");
    }

    private static void DrawTransform(EditorSession session)
    {
        if (!Props.Section("Options"))
        {
            return;
        }

        int mode = Props.Choice("Mode", "transform-mode", [(Icons.Move, "Move"), (Icons.Rotate, "Rotate")], (int)session.TransformMode);
        session.TransformMode = (TransformMode)mode;
        Tooltip("F switches");

        ImGui.BeginDisabled(session.TransformMode != TransformMode.Move);
        int space = Props.Choice("Axes", "transform-space", [(Icons.Global, "Global"), (Icons.Local, "Local")], (int)session.TransformSpace);
        session.TransformSpace = (TransformSpace)space;
        ImGui.EndDisabled();
        Tooltip("Whether the arrows follow the world or the object's own turn.  (X)");

        int pivot = Props.Choice("Pivot", "transform-pivot", [(Icons.FrameAll, "Median"), (Icons.Select, "Active"), (Icons.Local, "Individual")], (int)session.Pivot);
        session.Pivot = (TransformPivot)pivot;
        Tooltip("What several selected things turn about: their middle, the active one's centre, or each its own.");

        Props.Value("Snap", "whole voxels, 15°");
        Tooltip("Hold Shift while dragging to move and turn freely.");
    }

    private static void DrawExtrude(EditorSession session)
    {
        if (Props.Section("Options"))
        {
            int mode = Props.Choice("Select", "extrude-select", [(Icons.BoxSelect, "Box"), (Icons.FaceSelect, "Whole face")], (int)session.ExtrudeSelectionMode);
            session.ExtrudeSelectionMode = (ExtrudeSelectionMode)mode;
            Tooltip("F switches. Shift adds to a selection, Alt takes away.");

            bool creates = session.ExtrudeCreatesObject;
            if (Props.Check(string.Empty, "extrude-creates", "Pull out a new object", ref creates))
            {
                session.ExtrudeCreatesObject = creates;
            }

            Tooltip("Pulled voxels become a separate object instead of joining this one.  (X)");

            if (session.Selection is { IsEmpty: false } selection)
            {
                Props.Value("Selected", $"{selection.Count:N0} face(s)");
            }
        }

        DrawSymmetry(session);
    }

    private static void DrawPaint(EditorSession session)
    {
        if (Props.Section("Options"))
        {
            int mode = Props.Choice(
                "Mode",
                "paint-mode",
                [(Icons.Brush, "Brush"), (Icons.Bucket, "Bucket"), (Icons.Pattern, "Pattern")],
                (int)session.PaintMode);
            session.PaintMode = (PaintMode)mode;
            Tooltip("X cycles");

            DrawColour(session);

            switch (session.PaintMode)
            {
                case PaintMode.Brush:
                    float radius = session.BrushRadius;
                    if (Props.Float("Radius", "brush-radius", ref radius, 0.1f, 0f, 12f, radius < 0.5f ? "one face" : "%.1f vx"))
                    {
                        session.BrushRadius = radius;
                    }

                    Tooltip("Ctrl+Scroll over the model resizes it.");
                    break;

                case PaintMode.Bucket:
                    DrawBucket(session);
                    break;

                default:
                    DrawBucket(session);
                    DrawPattern(session);
                    break;
            }
        }

        DrawSymmetry(session);
    }

    private static void DrawColour(EditorSession session)
    {
        Props.Label("Colour");

        int index = session.ActiveColorIndex;
        Color32 colour = session.Scene.Palette[index];
        ImGui.ColorButton(
            "##paint-colour",
            colour.ToVector4(),
            ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip,
            new Vector2(ImGui.GetFrameHeight() * 2f, ImGui.GetFrameHeight()));

        Tooltip($"Index {index}  {colour}\nChosen in the Palette tab. Alt+click the model samples a colour.");

        ImGui.SameLine();
        ImGui.TextDisabled(colour.ToString());
    }

    private static void DrawBucket(EditorSession session)
    {
        bool whole = session.BucketWholeObject;
        if (Props.Check(string.Empty, "bucket-whole", "Whole object", ref whole))
        {
            session.BucketWholeObject = whole;
        }

        Tooltip("Recolour every voxel of the object instead of the surface under the cursor,\nincluding faces that are not exposed yet.");

        // How far a fill spreads; a whole-object fill does not spread at all.
        ImGui.BeginDisabled(whole);
        int threshold = session.BucketThreshold;
        if (Props.Int("Colour match", "bucket-threshold", ref threshold, 1f, 0, 128, threshold == 0 ? "exact" : "within %d"))
        {
            session.BucketThreshold = threshold;
        }

        ImGui.EndDisabled();
    }

    private static void DrawPattern(EditorSession session)
    {
        if (Props.Buttons("Pattern", "pattern-load", "Load image...") == 0)
        {
            ToolOptions.ShowPatternBrowser(session);
        }

        if (ToolOptions.PatternError.Length > 0)
        {
            Props.Note(string.Empty, ToolOptions.PatternError, Theme.Danger);
        }
        else if (session.Pattern is { } pattern)
        {
            Props.Value(string.Empty, $"{pattern.Name}  {pattern.Width}×{pattern.Height}");
            Tooltip("The voxel clicked takes the image's top-left pixel.");
        }
        else
        {
            Props.Value(string.Empty, "none - fills with the colour");
        }
    }

    /// <summary>
    /// The mirror planes: three switches lit in their axis colours, and Recentre while any is on.
    /// Only for the tools that mirror — Paint and Extrude.
    /// </summary>
    private static void DrawSymmetry(EditorSession session)
    {
        if (!Props.Section("Symmetry"))
        {
            return;
        }

        Symmetry symmetry = session.Symmetry;
        Props.Label("Mirror");

        float available = ImGui.GetContentRegionAvail().X;
        const float Gap = 2f;
        float width = (available - (Gap * 2f)) / 3f;

        foreach ((Axis axis, string label, Vector4 colour) in new[]
                 {
                     (Axis.X, "X", Theme.AxisX),
                     (Axis.Y, "Y", Theme.AxisY),
                     (Axis.Z, "Z", Theme.AxisZ),
                 })
        {
            if (axis != Axis.X)
            {
                ImGui.SameLine(0f, Gap);
            }

            bool on = symmetry[axis];
            if (on)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, colour with { W = 0.55f });
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colour with { W = 0.75f });
            }

            if (ImGui.Button($"{label}##tool-mirror-{label}", new Vector2(width, 0f)))
            {
                symmetry[axis] = !on;
            }

            if (on)
            {
                ImGui.PopStyleColor(2);
            }

            Tooltip($"Repeat every edit on the other side of the {label} plane.");
        }

        if (symmetry.IsOn && session.Scene.Focus is { } focus && Props.Buttons(string.Empty, "mirror-recentre", "Recentre") == 0)
        {
            symmetry.Recentre(focus);
        }

        Tooltip("Move the planes to the middle of the object as it is now.");
    }

    private static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
