using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The four tools plus View (EditorApp.md, "Araçlar"). There is no Place and no Erase: adding
/// voxels is pulling Extrude out, removing them is pushing it in.
/// </summary>
public static class ToolPanel
{
    private static readonly (EditorTool Tool, string Label, string Shortcut, string Help)[] Tools =
    [
        (EditorTool.Transform, "Transform", "Q", "Moves and rotates a whole object. Default tool."),
        (EditorTool.Extrude, "Extrude", "W", "Select a surface, then drag its arrow. Out adds voxels, in deletes them."),
        (EditorTool.Paint, "Paint", "E", "Recolors existing, visible voxels. Never creates or deletes."),
        (EditorTool.LoopCut, "Loop Cut", "R", "Splits the model at a grid plane into two independent objects."),
        (EditorTool.View, "View", "V", "Camera only — no editing."),
    ];

    public static void Draw(EditorSession session, RaycastHit? hover)
    {
        ImGui.SetNextWindowPos(new Vector2(12f, 250f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420f, 250f), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Tools"))
        {
            ImGui.End();
            return;
        }

        DrawToolButtons(session);
        ImGui.Separator();
        DrawSubModes(session);
        ImGui.Separator();
        DrawHover(session, hover);

        ImGui.End();
    }

    private static void DrawToolButtons(EditorSession session)
    {
        foreach ((EditorTool tool, string label, string shortcut, string help) in Tools)
        {
            if (tool != Tools[0].Tool)
            {
                ImGui.SameLine();
            }

            if (ImGui.RadioButton($"{label} ({shortcut})", session.ActiveTool == tool))
            {
                session.ActiveTool = tool;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(help);
            }
        }
    }

    private static void DrawSubModes(EditorSession session)
    {
        switch (session.ActiveTool)
        {
            case EditorTool.Extrude:
                DrawExtrudeModes(session);
                break;

            case EditorTool.Paint:
                DrawPaintModes(session);
                break;

            case EditorTool.LoopCut:
                ImGui.TextDisabled("Hover the model; the nearest grid plane through it is previewed.");
                ImGui.TextDisabled("Click to split it into two independent objects.");
                if (session.PreviewCutPlane is { } plane)
                {
                    ImGui.Text($"Cut at {plane.Axis} = {plane.Coordinate}");
                }

                break;

            case EditorTool.Transform:
                DrawTransformModes(session);
                break;

            default:
                ImGui.TextDisabled("Camera only.");
                break;
        }
    }

    private static void DrawTransformModes(EditorSession session)
    {
        ImGui.Text("Mode (F)");
        foreach (TransformMode mode in new[] { TransformMode.Move, TransformMode.Rotate })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{mode}##transform", session.TransformMode == mode))
            {
                session.TransformMode = mode;
            }
        }

        ImGui.BeginDisabled(session.TransformMode != TransformMode.Move);
        ImGui.Text("Space (X)");
        foreach (TransformSpace space in new[] { TransformSpace.Global, TransformSpace.Local })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{space}##space", session.TransformSpace == space))
            {
                session.TransformSpace = space;
            }
        }

        ImGui.EndDisabled();

        ImGui.TextDisabled("Drag an arrow to move, a box edge to hinge about the nearest corner.");
        ImGui.TextDisabled("Snap is always on; hold Shift for free movement.");

        if (session.Scene.Focus is { } focus)
        {
            Vector3 position = focus.Transform.Position;
            ImGui.Text($"{focus.Name}  at {position.X:0.##}, {position.Y:0.##}, {position.Z:0.##}");
        }
    }

    private static void DrawExtrudeModes(EditorSession session)
    {
        ImGui.Text("Selection (F)");
        ImGui.SameLine();
        if (ImGui.RadioButton("Box##sel", session.ExtrudeSelectionMode == ExtrudeSelectionMode.Box))
        {
            session.ExtrudeSelectionMode = ExtrudeSelectionMode.Box;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Face##sel", session.ExtrudeSelectionMode == ExtrudeSelectionMode.Face))
        {
            session.ExtrudeSelectionMode = ExtrudeSelectionMode.Face;
        }

        bool creates = session.ExtrudeCreatesObject;
        if (ImGui.Checkbox("Create a new object (X)", ref creates))
        {
            session.ExtrudeCreatesObject = creates;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Pulled voxels become a separate object instead of joining this one.\nNot built yet — see R7.");
        }

        if (session.Selection is { IsEmpty: false } selection)
        {
            ImGui.Text($"Selected: {selection.Count} face(s), {selection.Direction}, plane {selection.Plane}");
        }
        else
        {
            ImGui.TextDisabled("Drag on a surface to select. Shift adds, Alt subtracts.");
        }

        if (session.IsExtruding)
        {
            ImGui.TextColored(
                Theme.Highlight,
                $"{session.ExtrudeSteps:+0;-0} units — Enter confirms, Esc cancels");
        }
    }

    private static void DrawPaintModes(EditorSession session)
    {
        ImGui.Text("Mode (X)");
        foreach (PaintMode mode in new[] { PaintMode.Brush, PaintMode.Bucket, PaintMode.Pattern })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{mode}##paint", session.PaintMode == mode))
            {
                session.PaintMode = mode;
            }
        }

        if (session.PaintMode == PaintMode.Pattern)
        {
            ImGui.TextDisabled("Pattern is not built yet — see R7.");
        }

        if (session.PaintMode == PaintMode.Bucket)
        {
            int threshold = session.BucketThreshold;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderInt("Colour threshold", ref threshold, 0, 128))
            {
                session.BucketThreshold = threshold;
            }
        }
        else
        {
            float radius = session.BrushRadius;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Radius", ref radius, 0f, 12f, "%.1f"))
            {
                session.BrushRadius = radius;
            }

            ImGui.SameLine();
            ImGui.TextDisabled("Ctrl+Scroll");
        }

        ImGui.TextDisabled("Hold Alt to sample the colour under the cursor.");
    }

    private static void DrawHover(EditorSession session, RaycastHit? hover)
    {
        ImGui.BeginDisabled(!session.History.CanUndo);
        if (ImGui.Button($"Undo ({session.History.NextUndoName ?? "-"})"))
        {
            session.Undo();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!session.History.CanRedo);
        if (ImGui.Button($"Redo ({session.History.NextRedoName ?? "-"})"))
        {
            session.Redo();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextDisabled($"{session.History.RetainedCells:N0} cells retained");

        if (hover is { } hit)
        {
            ImGui.Text($"Hover  {hit.Voxel}  face {hit.Face}");
        }
        else
        {
            ImGui.TextDisabled("Hover  -");
        }
    }
}
