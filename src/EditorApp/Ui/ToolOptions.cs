using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The header strip: the settings of the tool in hand that are reached for while working, as icons
/// and small fields, and nothing belonging to any other tool. Everything else — and every setting
/// with its name spelled out — is in the Tool tab.
///
/// No sentences. How to use the tool is in the hint strip along the bottom, which changes with the
/// keys held; saying it again up here only pushed the controls apart and crowded the right-hand end.
/// </summary>
public static class ToolOptions
{
    private const float Gap = 14f;

    public static void Draw(EditorSession session)
    {
        switch (session.ActiveTool)
        {
            case EditorTool.Select when session.InEditMode:
                DrawVoxelSelect(session);
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
        }
    }

    /// <summary>The brush: what it does, its shape and its size.</summary>
    private static void DrawSculpt(EditorSession session)
    {
        string[] modes = ["Add", "Remove", "Raise", "Lower", "Flatten", "Smooth"];
        int mode = (int)session.SculptMode;
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("Flatten").X + (ImGui.GetFrameHeight() * 1.6f));
        if (ImGui.Combo("##sculpt-mode", ref mode, modes, modes.Length))
        {
            session.SculptMode = (SculptMode)mode;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("What a dab does. Ctrl turns it round - Add and Remove, Raise and Lower - and Shift smooths.");
        }

        ImGui.SameLine(0f, 4f);
        float size = ImGui.GetFrameHeight();
        if (IconButton.Toggle(
                "sculpt-shape",
                (Icons.ShapeSphere, "Sphere brush"),
                (Icons.ObjectTab, "Cube brush"),
                session.SculptShape == SculptShape.Cube,
                string.Empty,
                size))
        {
            session.SculptShape = session.SculptShape == SculptShape.Sphere ? SculptShape.Cube : SculptShape.Sphere;
        }

        ImGui.SameLine(0f, Gap);
        float radius = session.SculptRadius;
        ImGui.SetNextItemWidth(ImGui.GetFrameHeight() * 5f);
        if (ImGui.DragFloat("##sculpt-radius", ref radius, 0.05f, SculptOperations.MinRadius, SculptOperations.MaxRadius, "Radius %.1f"))
        {
            session.SculptRadius = radius;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The brush's size in voxels. Ctrl+wheel over the model changes it too.");
        }
    }

    /// <summary>Inside an object: what a click of the Select tool takes.</summary>
    private static void DrawVoxelSelect(EditorSession session)
    {
        float size = ImGui.GetFrameHeight();
        (VoxelSelectMode Mode, Icons.Painter Icon, string Tip)[] modes =
        [
            (VoxelSelectMode.Box, Icons.BoxSelect, "Box - a click takes one voxel, a drag the voxels in a box"),
            (VoxelSelectMode.Wand, Icons.Pattern, "Wand - a click takes the joined voxels of its colour"),
            (VoxelSelectMode.Colour, Icons.Bucket, "Colour - a click takes every voxel of its colour"),
        ];

        foreach ((VoxelSelectMode mode, Icons.Painter icon, string tip) in modes)
        {
            if (IconButton.Draw($"voxel-select-{mode}", icon, session.VoxelSelectMode == mode, $"{tip}{Shortcut.Hint(EditorAction.ToolOtherMode)}", size))
            {
                session.VoxelSelectMode = mode;
            }

            ImGui.SameLine(0f, 4f);
        }

        ImGui.NewLine();
    }

    private static void DrawTransform(EditorSession session)
    {
        float size = ImGui.GetFrameHeight();

        if (IconButton.Toggle(
                "transform-mode",
                (Icons.Move, "Move"),
                (Icons.Rotate, "Rotate"),
                session.TransformMode == TransformMode.Rotate,
                Shortcut.Of(EditorAction.ToolOtherMode),
                size))
        {
            session.TransformMode = session.TransformMode == TransformMode.Move
                ? TransformMode.Rotate
                : TransformMode.Move;
        }

        ImGui.SameLine(0f, 4f);

        // The edge hinge a rotation turns about is always the object's own, so the choice has
        // nothing to say while rotating.
        ImGui.BeginDisabled(session.TransformMode != TransformMode.Move);
        if (IconButton.Toggle(
                "transform-space",
                (Icons.Global, "Global axes"),
                (Icons.Local, "Local axes"),
                session.TransformSpace == TransformSpace.Local,
                Shortcut.Of(EditorAction.ToolCycleMode),
                size))
        {
            session.TransformSpace = session.TransformSpace == TransformSpace.Global
                ? TransformSpace.Local
                : TransformSpace.Global;
        }

        ImGui.EndDisabled();

        // What several things turn about. Named in full in the Tool tab; short here.
        ImGui.SameLine(0f, 4f);
        string[] pivots = ["Median", "Active", "Individual"];
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("Individual").X + (ImGui.GetFrameHeight() * 1.6f));
        int pivot = (int)session.Pivot;
        if (ImGui.Combo("##transform-pivot", ref pivot, pivots, pivots.Length))
        {
            session.Pivot = (TransformPivot)pivot;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Pivot: what several selected things turn about -\ntheir middle, the active one's centre, or each its own.");
        }

        ImGui.SameLine(0f, Gap);
        SnapMenu.DrawButtons(session.Snap, size);
    }

    private static void DrawExtrude(EditorSession session)
    {
        float size = ImGui.GetFrameHeight();

        (ExtrudeSelectionMode Mode, Icons.Painter Icon, string Tip)[] shapes =
        [
            (ExtrudeSelectionMode.Box, Icons.BoxSelect, "Box - drag a rectangle on a face"),
            (ExtrudeSelectionMode.Ellipse, Icons.EllipseSelect, "Ellipse - drag an ellipse on a face"),
            (ExtrudeSelectionMode.Line, Icons.LineSelect, "Line - drag a line on a face, a voxel wide"),
            (ExtrudeSelectionMode.Face, Icons.FaceSelect, "Whole face - a click takes the flat patch entire"),
        ];

        foreach ((ExtrudeSelectionMode mode, Icons.Painter icon, string tip) in shapes)
        {
            if (IconButton.Draw($"extrude-select-{mode}", icon, session.ExtrudeSelectionMode == mode, $"{tip}{Shortcut.Hint(EditorAction.ToolOtherMode)}", size))
            {
                session.ExtrudeSelectionMode = mode;
            }

            ImGui.SameLine(0f, 4f);
        }

        ImGui.SameLine(0f, 8f);

        // A switch that stays lit while on: pulled voxels go into an object of their own.
        if (IconButton.Draw(
                "extrude-creates",
                Icons.NewObject,
                session.ExtrudeCreatesObject,
                session.ExtrudeCreatesObject
                    ? $"Pulling out a new object{Shortcut.Hint(EditorAction.ToolCycleMode)}\nClick to extrude into this one instead"
                    : $"Extruding into this object{Shortcut.Hint(EditorAction.ToolCycleMode)}\nClick to pull out a new object instead",
                size))
        {
            session.ExtrudeCreatesObject = !session.ExtrudeCreatesObject;
        }

        ImGui.SameLine(0f, Gap);
        DrawSymmetry(session);
    }

    private static void DrawPaint(EditorSession session)
    {
        float size = ImGui.GetFrameHeight();

        IconButton.Choice(
            "paint-mode",
            [(Icons.Brush, "Brush"), (Icons.Bucket, "Bucket fill"), (Icons.Pattern, "Pattern fill")],
            (int)session.PaintMode,
            Shortcut.Of(EditorAction.ToolCycleMode),
            size,
            value => session.PaintMode = (PaintMode)value);

        ImGui.SameLine(0f, 4f);

        if (session.PaintMode == PaintMode.Brush)
        {
            float radius = session.BrushRadius;
            Field("Radius", 84f);
            if (ImGui.DragFloat("##brush-radius", ref radius, 0.1f, 0f, 12f, radius < 0.5f ? "1 face" : "%.1f", ImGuiSliderFlags.AlwaysClamp))
            {
                session.BrushRadius = radius;
            }

            Tooltip("Brush radius in voxels  -  Ctrl+Scroll over the model");
        }
        else
        {
            bool whole = session.BucketWholeObject;
            if (IconButton.Draw(
                    "bucket-whole",
                    Icons.ObjectTab,
                    whole,
                    whole ? "Filling the whole object\nClick to fill only the surface under the cursor" : "Filling the surface under the cursor\nClick to fill the whole object instead",
                    size))
            {
                session.BucketWholeObject = !whole;
            }

            // How far a fill spreads; a whole-object fill does not spread at all.
            ImGui.SameLine(0f, 4f);
            ImGui.BeginDisabled(whole);
            Field("Match", 96f);
            int threshold = session.BucketThreshold;
            if (ImGui.DragInt("##bucket-threshold", ref threshold, 1f, 0, 128, threshold == 0 ? "exact" : "±%d", ImGuiSliderFlags.AlwaysClamp))
            {
                session.BucketThreshold = threshold;
            }

            ImGui.EndDisabled();
            Tooltip("How close a colour has to be to be filled over");

            if (session.PaintMode == PaintMode.Pattern)
            {
                ImGui.SameLine(0f, 4f);
                if (ImGui.Button("Pattern..."))
                {
                    ShowPatternBrowser(session);
                }

                Tooltip(session.Pattern is { } pattern
                    ? $"{pattern.Name}  {pattern.Width}×{pattern.Height}\nThe voxel clicked takes the image's top-left pixel."
                    : "No image loaded - fills with the colour instead.");
            }
        }

        ImGui.SameLine(0f, Gap);
        DrawSymmetry(session);
    }

    /// <summary>A small label before the next field, and the field sized to hold a number rather than stretching.</summary>
    private static void Field(string label, float width)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(label);
        ImGui.SameLine(0f, 4f);
        ImGui.SetNextItemWidth(width);
    }

    /// <summary>
    /// Three switches, one per axis, lit in the axis's colour when on. The planes go through the
    /// middle of the object when symmetry comes on and stay there; Recentre is in the Tool tab.
    /// </summary>
    private static void DrawSymmetry(EditorSession session)
    {
        float size = ImGui.GetFrameHeight();
        Symmetry symmetry = session.Symmetry;

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Mirror");

        foreach ((Axis axis, string label, Vector4 colour) in new[]
                 {
                     (Axis.X, "X", Theme.AxisX),
                     (Axis.Y, "Y", Theme.AxisY),
                     (Axis.Z, "Z", Theme.AxisZ),
                 })
        {
            ImGui.SameLine(0f, 2f);
            bool on = symmetry[axis];

            if (on)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, colour with { W = 0.55f });
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colour with { W = 0.75f });
            }

            if (ImGui.Button($"{label}##mirror-{label}", new Vector2(size, size)))
            {
                symmetry[axis] = !on;
            }

            if (on)
            {
                ImGui.PopStyleColor(2);
            }

            Tooltip($"Mirror across {label}: every edit is repeated on the other side of the {label} plane.");
        }
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    /// <summary>The pattern browser: a file dialog drawn at the top level, owned here because one sub-mode uses it.</summary>
    private static readonly FileBrowserDialog PatternBrowser = new();

    private static string _patternStatus = string.Empty;

    public static void DrawDialogs() => PatternBrowser.Draw();

    /// <summary>Opens the browser for a pattern image. Shared by the header and the Tool tab.</summary>
    public static void ShowPatternBrowser(EditorSession session) =>
        PatternBrowser.Show(
            FileBrowserMode.Open,
            "Load a tiled pattern (.png)",
            ".png",
            PatternBrowser.CurrentDirectory,
            suggestedName: null,
            LoadPattern(session));

    /// <summary>What went wrong loading the last pattern, or empty.</summary>
    public static string PatternError => _patternStatus;

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
