using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Text and 2D marks drawn over the 3D view: dimensions, the drag readout under the cursor, and a
/// corner axis indicator.
///
/// These are ImGui draw-list calls rather than 3D geometry because they must stay upright and
/// legible at any camera angle — a label billboarded in the scene shrinks with distance and rotates
/// with the view, which is exactly what a measurement must not do.
/// </summary>
public static class ViewportOverlay
{
    private const float AxisIndicatorRadius = 34f;
    private const float LabelPadding = 5f;

    public static void Draw(
        EditorSession session,
        FlyCamera camera,
        ViewportRect viewport,
        bool showMeasurements,
        string dragReadout)
    {
        // The background list, not the foreground one. Both draw over the 3D scene, since all of
        // ImGui is composited on top of GL — but the foreground list is submitted after every
        // window, so labels punched through panels and dialogs alike. The background list is
        // submitted first, which puts these marks over the model and under the interface.
        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();

        DrawAxisIndicator(drawList, camera, viewport);

        if (showMeasurements)
        {
            DrawObjectDimensions(drawList, session, camera, viewport);
        }

        if (dragReadout.Length > 0)
        {
            DrawCursorReadout(drawList, dragReadout);
        }
    }

    /// <summary>
    /// Which way the world axes point from here. Cheap orientation feedback that a grid alone does
    /// not give once the camera is turned away from the origin.
    /// </summary>
    private static void DrawAxisIndicator(ImDrawListPtr drawList, FlyCamera camera, ViewportRect viewport)
    {
        Vector2 centre = viewport.Position + new Vector2(
            AxisIndicatorRadius + 24f,
            viewport.Size.Y - AxisIndicatorRadius - 24f);

        // Screen direction of each world axis: project the axis into view space and drop the depth.
        Vector3 right = camera.Right;
        Vector3 up = camera.Up;

        (Vector3 Axis, string Label, uint Colour)[] axes =
        [
            (Vector3.UnitX, "X", Colour(Theme.AxisX)),
            (Vector3.UnitY, "Y", Colour(Theme.AxisY)),
            (Vector3.UnitZ, "Z", Colour(Theme.AxisZ)),
        ];

        foreach ((Vector3 axis, string label, uint colour) in axes)
        {
            var direction = new Vector2(Vector3.Dot(axis, right), -Vector3.Dot(axis, up));
            Vector2 tip = centre + direction * AxisIndicatorRadius;

            drawList.AddLine(centre, tip, colour, 2f);

            Vector2 textSize = ImGui.CalcTextSize(label);
            drawList.AddText(tip - textSize * 0.5f, colour, label);
        }
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
        if (session.Scene.Focus is not { } focus || !focus.Grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return;
        }

        Int3 size = max - min + Int3.One;
        Vector3 localMin = min.ToVector3();
        Vector3 localMax = max.ToVector3() + Vector3.One;

        // One edge per axis, all meeting at the same corner, so the three labels read as a set.
        DrawEdgeLabel(drawList, camera, viewport, focus,
            localMin,
            new Vector3(localMax.X, localMin.Y, localMin.Z),
            $"{size.X}", Theme.AxisX);

        DrawEdgeLabel(drawList, camera, viewport, focus,
            localMin,
            new Vector3(localMin.X, localMax.Y, localMin.Z),
            $"{size.Y}", Theme.AxisY);

        DrawEdgeLabel(drawList, camera, viewport, focus,
            localMin,
            new Vector3(localMin.X, localMin.Y, localMax.Z),
            $"{size.Z}", Theme.AxisZ);
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
