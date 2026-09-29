using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Text and 2D marks drawn over the 3D view: dimensions and the drag readout under the cursor. Which
/// way the axes lie is the navigation gizmo's job now, in the opposite corner, where it can also be
/// clicked.
///
/// These are ImGui draw-list calls rather than 3D geometry because they must stay upright and
/// legible at any camera angle — a label billboarded in the scene shrinks with distance and rotates
/// with the view, which is exactly what a measurement must not do.
/// </summary>
public static class ViewportOverlay
{
    private const float LabelPadding = 5f;

    /// <summary>Screen length below which an edge counts as seen end-on and loses its label.</summary>
    private const float MinimumEdgeOnScreen = 6f;

    public static void Draw(
        EditorSession session,
        FlyCamera camera,
        ViewportRect viewport,
        bool showMeasurements,
        string dragReadout,
        SelectionOperation? cursorMark = null,
        Color32? cursorSample = null,
        bool textInfo = false)
    {
        // The background list, not the foreground one. Both draw over the 3D scene, since all of
        // ImGui is composited on top of GL — but the foreground list is submitted after every
        // window, so labels punched through panels and dialogs alike. The background list is
        // submitted first, which puts these marks over the model and under the interface.
        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();

        if (showMeasurements)
        {
            DrawObjectDimensions(drawList, session, camera, viewport);
        }

        if (textInfo)
        {
            DrawTextInfo(drawList, session, camera, viewport);
        }

        if (dragReadout.Length > 0)
        {
            DrawCursorReadout(drawList, dragReadout);
        }

        if (cursorMark is { } operation)
        {
            DrawCursorMark(drawList, operation);
        }

        if (cursorSample is { } sample)
        {
            DrawCursorSample(drawList, sample);
        }
    }

    /// <summary>
    /// The eyedropper by the pointer while Alt is held in Paint, with the colour under it in a
    /// swatch: what a click would pick, seen before it is picked. A pointer that looks the same
    /// whether it is about to paint or to sample is how the wrong one happens.
    /// </summary>
    private static void DrawCursorSample(ImDrawListPtr drawList, Color32 sample)
    {
        const float Icon = 8f;
        const float Swatch = 18f;

        Vector2 at = ImGui.GetIO().MousePos + new Vector2(14f, 16f);
        Vector2 plateMin = at - new Vector2(4f);
        Vector2 plateMax = at + new Vector2((Icon * 2f) + 6f + Swatch + 4f, (Icon * 2f) + 4f);

        drawList.AddRectFilled(plateMin, plateMax, Colour(Theme.Background with { W = 0.9f }), 4f);
        Icons.Eyedropper(new ImGuiIconCanvas(drawList, Colour(Theme.Text), 1.4f), at + new Vector2(Icon), Icon);

        Vector2 swatchMin = at + new Vector2((Icon * 2f) + 6f, 0f);
        Vector2 swatchMax = swatchMin + new Vector2(Swatch, Icon * 2f);
        drawList.AddRectFilled(swatchMin, swatchMax, Colour(sample.ToVector4()), 3f);
        drawList.AddRect(swatchMin, swatchMax, Colour(Theme.Text with { W = 0.6f }), 3f);
    }

    /// <summary>
    /// A small badge by the pointer, + to add or - to take away, in the colour the faces will be
    /// drawn in: the operator's own cursor badge, as a file manager puts one on a copy.
    /// </summary>
    private static void DrawCursorMark(ImDrawListPtr drawList, SelectionOperation operation)
    {
        const float Radius = 7f;
        const float Arm = 3.5f;

        Vector2 centre = ImGui.GetIO().MousePos + new Vector2(17f, 19f);
        Vector4 colour = EditorOverlays.SelectionColour(operation).ToVector4();

        drawList.AddCircleFilled(centre, Radius + 1.5f, Colour(Theme.Background with { W = 0.9f }), 16);
        drawList.AddCircleFilled(centre, Radius, Colour(colour), 16);

        uint mark = Colour(Theme.Background);
        drawList.AddLine(centre - new Vector2(Arm, 0f), centre + new Vector2(Arm, 0f), mark, 2f);
        if (operation == SelectionOperation.Add)
        {
            drawList.AddLine(centre - new Vector2(0f, Arm), centre + new Vector2(0f, Arm), mark, 2f);
        }
    }

    /// <summary>
    /// Blender's text info, top left beside the tools: which view this is, and what is focused. The
    /// view's name is what says an aligned view is not the perspective it looks like.
    /// </summary>
    private static void DrawTextInfo(ImDrawListPtr drawList, EditorSession session, FlyCamera camera, ViewportRect viewport)
    {
        string projection = camera.Orthographic ? "Orthographic" : "Perspective";
        string view = camera.CurrentAlignedView() is { } aligned ? $"{aligned} {projection}" : $"User {projection}";

        string focus = session.SelectedLight is { } light
            ? $"{light.Name}  ·  {light.Kind} light"
            : session.EditObject is { } edited
                ? $"{edited.Name}  ·  Edit Mode{(session.VoxelSelection.IsEmpty ? string.Empty : $"  ·  {session.VoxelSelection.Count:N0} voxels chosen")}"
                : session.Scene.Focus is { } o
                    ? $"{o.Name}{(o.Locked ? "  (locked)" : string.Empty)}"
                    : string.Empty;

        Vector2 at = viewport.Position + new Vector2(58f, 10f);
        Text(drawList, at, view, Theme.Text with { W = 0.9f });

        if (focus.Length > 0)
        {
            Text(drawList, at + new Vector2(0f, ImGui.GetTextLineHeightWithSpacing()), focus, Theme.Text with { W = 0.7f });
        }
    }

    /// <summary>Text with a shadow under it, readable on a light model and a dark background alike.</summary>
    private static void Text(ImDrawListPtr drawList, Vector2 at, string text, Vector4 colour)
    {
        drawList.AddText(at + Vector2.One, Colour(new Vector4(0f, 0f, 0f, colour.W * 0.6f)), text);
        drawList.AddText(at, Colour(colour), text);
    }

    /// <summary>
    /// The focused object's size in voxels, one label per axis, placed on the edge it measures.
    /// </summary>
    private static void DrawObjectDimensions(
        ImDrawListPtr drawList,
        EditorSession session,
        FlyCamera camera,
        ViewportRect viewport)
    {
        // What it shows, its modifiers' copies too: the same box its outline is drawn round.
        if (session.Scene.Focus is not { } focus || !focus.Shown.TryGetBounds(out Int3 min, out Int3 max))
        {
            return;
        }

        Int3 size = max - min + Int3.One;
        Vector3 localMin = min.ToVector3();
        Vector3 localMax = max.ToVector3() + Vector3.One;

        // Voxels stay the headline number — that is what an edit adds one of. The world size only
        // appears once it is not the same number, so a level at one unit per voxel reads as before.
        float scale = focus.VoxelSize;
        bool scaled = MathF.Abs(scale - 1f) > 1e-6f;
        string Label(int voxels) => scaled ? $"{voxels}  ({voxels * scale:0.###})" : $"{voxels}";

        // One edge per axis, all meeting at the same corner, so the three labels read as a set.
        DrawEdgeLabel(drawList, camera, viewport, focus,
            localMin,
            new Vector3(localMax.X, localMin.Y, localMin.Z),
            Label(size.X), Theme.AxisX);

        DrawEdgeLabel(drawList, camera, viewport, focus,
            localMin,
            new Vector3(localMin.X, localMax.Y, localMin.Z),
            Label(size.Y), Theme.AxisY);

        DrawEdgeLabel(drawList, camera, viewport, focus,
            localMin,
            new Vector3(localMin.X, localMin.Y, localMax.Z),
            Label(size.Z), Theme.AxisZ);
    }

    private static void DrawEdgeLabel(
        ImDrawListPtr drawList,
        FlyCamera camera,
        ViewportRect viewport,
        VoxelObject focus,
        Vector3 localFrom,
        Vector3 localTo,
        string text,
        Vector4 colour)
    {
        Vector3 midpoint = focus.Transform.TransformPoint((localFrom + localTo) * 0.5f);

        if (!camera.TryProjectToScreen(midpoint, viewport.Size, out Vector2 local))
        {
            return;
        }

        // An edge seen end-on — the depth edge in a front or top view — has no length on screen, and
        // its label would only pile up on the corner it collapses into.
        if (camera.TryProjectToScreen(focus.Transform.TransformPoint(localFrom), viewport.Size, out Vector2 from)
            && camera.TryProjectToScreen(focus.Transform.TransformPoint(localTo), viewport.Size, out Vector2 to)
            && Vector2.Distance(from, to) < MinimumEdgeOnScreen)
        {
            return;
        }

        Vector2 screen = viewport.Position + local;
        if (!viewport.Contains(screen))
        {
            return;
        }

        DrawPill(drawList, screen, text, colour);
    }

    /// <summary>A running drag's own number, next to the cursor where the eye already is.</summary>
    private static void DrawCursorReadout(ImDrawListPtr drawList, string text)
    {
        Vector2 position = ImGui.GetIO().MousePos + new Vector2(18f, 18f);
        DrawPill(drawList, position, text, Theme.Highlight, centred: false);
    }

    private static void DrawPill(ImDrawListPtr drawList, Vector2 position, string text, Vector4 colour, bool centred = true)
    {
        Vector2 size = ImGui.CalcTextSize(text);
        Vector2 topLeft = centred ? position - size * 0.5f : position;

        // A backing plate: unreadable numbers over a white model are worse than none.
        drawList.AddRectFilled(
            topLeft - new Vector2(LabelPadding),
            topLeft + size + new Vector2(LabelPadding),
            Colour(Theme.Background with { W = 0.85f }),
            4f);

        drawList.AddText(topLeft, Colour(colour), text);
    }

    private static uint Colour(Vector4 value) => ImGui.ColorConvertFloat4ToU32(value);
}
