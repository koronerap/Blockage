using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Rendering;

/// <summary>
/// Everything the editor draws over the model: the selection, the extrude arrow, the transform
/// gizmo, the cut plane, and the colours all of them are drawn in.
///
/// It lives here rather than in the desktop shell because a phone has to draw exactly the same
/// things, and an outline that means one thing on one screen and another elsewhere would be worse
/// than no outline. Everything takes a <see cref="LineGeometry"/> and reads the session — no window,
/// no graphics API, nothing either head has that the other does not.
/// </summary>
public static class EditorOverlays
{
    public static readonly Color32 GridMinor = new(60, 66, 74);

    public static readonly Color32 GridMajor = new(92, 100, 110);

    /// <summary>Whatever the pointer is over right now.</summary>
    public static readonly Color32 Highlight = new(255, 236, 120);

    public static readonly Color32 BrushOutline = new(255, 160, 60);

    public static readonly Color32 Selection = new(120, 230, 140);

    public static readonly Color32 SelectionAdd = new(120, 230, 140);

    /// <summary>
    /// Subtract has to look different from add: until the click lands, the two gestures are
    /// otherwise indistinguishable (EditorApp.md, "Extrude").
    /// </summary>
    public static readonly Color32 SelectionSubtract = new(255, 110, 110);

    public static readonly Color32 Arrow = new(255, 210, 90);

    public static readonly Color32 CutPlane = new(255, 130, 220);

    // The axis colours the whole editor agrees on: the gizmo, the corner indicator and the dimension
    // labels all come from here, and the ImGui theme converts these rather than repeating them.
    public static readonly Color32 AxisX = new(0xEB, 0x5A, 0x5A);

    public static readonly Color32 AxisY = new(0x78, 0xDC, 0x6E);

    public static readonly Color32 AxisZ = new(0x64, 0x96, 0xFA);

    public static readonly Color32 GizmoEdge = new(150, 150, 165);

    public static readonly Color32 GizmoActive = new(255, 240, 140);

    /// <summary>Drawing every selected face costs four lines each; past this, outline the bounds instead.</summary>
    public const int MaxOutlinedFaces = 3000;

    // Overlay stroke widths, as multiples of the batch's screen-constant thickness. Heavy enough to
    // grab, light enough not to become the thing you look at.
    public const float SelectionWidth = 1.1f;

    public const float GizmoWidth = 1.8f;

    public const float GizmoEdgeWidth = 1.5f;

    public const float ArrowWidth = 1.8f;

    /// <summary>
    /// A shaft that stops at the base of a solid cone. Running the shaft all the way to the tip
    /// leaves a thick stub poking out of the cone's point, which is the part of an arrow that has
    /// to look sharp.
    /// </summary>
    public static void AddArrow(LineGeometry lines, Vector3 start, Vector3 end, Color32 color, float width)
    {
        Vector3 along = end - start;
        float length = along.Length();
        if (length < 1e-4f)
        {
            return;
        }

        Vector3 direction = along / length;
        float coneLength = length * 0.28f;
        Vector3 coneBase = end - (direction * coneLength);

        lines.AddThickLine(start, coneBase, color, width);
        lines.AddCone(end, coneBase, coneLength * 0.42f, color);
    }

    public static void AddSelectionOutline(LineGeometry lines, FaceSelection? selection, Color32 color)
    {
        if (selection is not { IsEmpty: false })
        {
            return;
        }

        // Outlining thousands of individual faces costs more than it communicates; past the cap the
        // bounding box says the same thing for four orders of magnitude fewer lines.
        if (selection.Count > MaxOutlinedFaces)
        {
            (Vector3 min, Vector3 max) = selection.Bounds().ToWorldBounds();
            lines.AddBox(min, max, color, SelectionWidth);
            return;
        }

        foreach (Int3 voxel in selection.Voxels)
        {
            lines.AddVoxelFace(voxel, selection.Direction, color, offset: 0.02f, width: SelectionWidth);
        }
    }

    /// <summary>
    /// The extrude arrow, in world space. Nothing is drawn unless Extrude is the tool in hand — a
    /// gizmo outliving its own tool is a standing invitation to grab the wrong thing.
    /// </summary>
    public static void AddExtrudeArrow(LineGeometry lines, EditorSession session, ExtrudeInteraction extrude)
    {
        if (session.ActiveTool != EditorTool.Extrude || extrude.Arrow() is not { } arrow)
        {
            return;
        }

        AddArrow(lines, arrow.Start, arrow.End, Arrow, ArrowWidth);
    }

    /// <summary>A light that is switched off: still there to be picked, but grey.</summary>
    public static readonly Color32 LightOff = new(130, 130, 130);

    private const float LightWidth = 1.2f;

    /// <summary>
    /// Where a light is and which way it shines, in world space, sized by its distance from the
    /// camera so its icon reads the same near and far. A bulb that faces the camera for every kind;
    /// rays for the sun, with its direction; a cone for a spot, as wide as its beam.
    /// </summary>
    public static void AddLight(LineGeometry lines, SceneLight light, FlyCamera camera, bool highlighted)
    {
        Vector3 at = light.Position;
        float size = MathF.Max(Vector3.Distance(camera.Position, at) * 0.022f, 0.02f);
        Vector3 right = camera.Right * size;
        Vector3 up = camera.Up * size;

        // The light's own colour, lifted towards white so a deep blue one can still be seen.
        Color32 colour = highlighted
            ? GizmoActive
            : light.Visible
                ? Color32.FromVector4(new Vector4(Vector3.Lerp(light.Colour, Vector3.One, 0.35f), 1f))
                : LightOff;

        AddBillboardCircle(lines, at, right, up, 1f, colour);

        switch (light.Kind)
        {
            case LightKind.Directional:
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * (MathF.PI / 4f);
                    Vector3 ray = (right * MathF.Cos(angle)) + (up * MathF.Sin(angle));
                    lines.AddThickLine(at + (ray * 1.5f), at + (ray * 2.2f), colour, LightWidth);
                }

                // Parallel rays have no source to point from, so the direction is the whole message.
                AddArrow(lines, at, at + (light.Direction * size * 9f), colour, LightWidth);
                break;

            case LightKind.Point:
                AddBillboardCircle(lines, at, right, up, 0.45f, colour);
                break;

            default:
                AddSpotCone(lines, light, size, colour);
                break;
        }
    }

    private static void AddBillboardCircle(LineGeometry lines, Vector3 centre, Vector3 right, Vector3 up, float radius, Color32 colour)
    {
        const int Segments = 16;
        Vector3 previous = centre + (right * radius);

        for (int i = 1; i <= Segments; i++)
        {
            float angle = i * (MathF.Tau / Segments);
            Vector3 point = centre + (((right * MathF.Cos(angle)) + (up * MathF.Sin(angle))) * radius);
            lines.AddThickLine(previous, point, colour, LightWidth);
            previous = point;
        }
    }

    /// <summary>The beam drawn out to its range, or a dozen icon-sizes if that is nearer: far enough to aim by.</summary>
    private static void AddSpotCone(LineGeometry lines, SceneLight light, float size, Color32 colour)
    {
        const int Segments = 20;

        Vector3 direction = light.Direction;
        float length = MathF.Min(light.Range, size * 12f);
        float radius = length * MathF.Tan(light.SpotAngle * 0.5f * (MathF.PI / 180f));
        Vector3 mouth = light.Position + (direction * length);

        Vector3 reference = MathF.Abs(direction.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(direction, reference)) * radius;
        Vector3 v = Vector3.Normalize(Vector3.Cross(direction, u)) * radius;

        Vector3 previous = mouth + u;
        for (int i = 1; i <= Segments; i++)
        {
            float angle = i * (MathF.Tau / Segments);
            Vector3 point = mouth + (u * MathF.Cos(angle)) + (v * MathF.Sin(angle));
            lines.AddThickLine(previous, point, colour, LightWidth);

            if (i % 5 == 0)
            {
                lines.AddThickLine(light.Position, point, colour, LightWidth);
            }

            previous = point;
        }
    }

    /// <summary>Draws the move arrows, the box edges and the rotate rings, in world space.</summary>
    public static void AddTransformGizmo(
        LineGeometry lines,
        EditorSession session,
        TransformInteraction transform,
        FlyCamera camera)
    {
        if (session.ActiveTool != EditorTool.Transform)
        {
            return;
        }

        foreach (GizmoHandle handle in transform.Handles(camera))
        {
            if (!transform.ShouldDraw(handle))
            {
                continue;
            }

            Color32 color = transform.IsHighlighted(handle) ? GizmoActive : ColorFor(handle);

            if (handle.Kind == GizmoKind.RotateRing)
            {
                Vector3? previous = null;
                foreach (Vector3 point in transform.RingPoints(handle, camera))
                {
                    if (previous is { } from)
                    {
                        lines.AddThickLine(from, point, color, GizmoWidth);
                    }

                    previous = point;
                }

                continue;
            }

            (Vector3 start, Vector3 end) = transform.Segment(handle, camera);

            if (handle.Kind == GizmoKind.MoveAxis)
            {
                AddArrow(lines, start, end, color, GizmoWidth);
                continue;
            }

            lines.AddThickLine(start, end, color, GizmoEdgeWidth);
        }
    }

    public static Color32 ColorFor(GizmoHandle handle) => handle.Kind switch
    {
        GizmoKind.EdgeHinge => GizmoEdge,
        _ => handle.Axis switch
        {
            0 => AxisX,
            1 => AxisY,
            _ => AxisZ,
        },
    };

    /// <summary>
    /// The loop cut plane, as a rectangle spanning the object's bounds with one diagonal so it reads
    /// as a surface rather than an empty frame. Drawn in the focused object's own space.
    /// </summary>
    public static void AddCutPreview(LineGeometry lines, EditorSession session)
    {
        // Gated on the tool as well as on the plane: a preview must never outlive its own tool.
        if (session.ActiveTool != EditorTool.LoopCut
            || session.PreviewCutPlane is not { } plane
            || session.Scene.Focus is not { } focus
            || !focus.Grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return;
        }

        int axis = plane.AxisIndex;
        int uAxis = axis == 0 ? 1 : 0;
        int vAxis = axis == 2 ? 1 : 2;

        Vector3 Corner(float u, float v)
        {
            Span<float> parts = stackalloc float[3];
            parts[axis] = plane.Coordinate;
            parts[uAxis] = u;
            parts[vAxis] = v;
            return new Vector3(parts[0], parts[1], parts[2]);
        }

        float uMin = VoxelBox.Component(min, uAxis);
        float uMax = VoxelBox.Component(max, uAxis) + 1f;
        float vMin = VoxelBox.Component(min, vAxis);
        float vMax = VoxelBox.Component(max, vAxis) + 1f;

        Vector3 a = Corner(uMin, vMin);
        Vector3 b = Corner(uMax, vMin);
        Vector3 c = Corner(uMax, vMax);
        Vector3 d = Corner(uMin, vMax);

        lines.AddThickLine(a, b, CutPlane, SelectionWidth);
        lines.AddThickLine(b, c, CutPlane, SelectionWidth);
        lines.AddThickLine(c, d, CutPlane, SelectionWidth);
        lines.AddThickLine(d, a, CutPlane, SelectionWidth);
        lines.AddThickLine(a, c, CutPlane, SelectionWidth);
    }
}
