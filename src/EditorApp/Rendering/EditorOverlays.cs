using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
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

    /// <summary>A selected object's box, in Blender's orange; the active one's is lighter.</summary>
    public static readonly Color32 ObjectSelected = new(241, 118, 32);

    public static readonly Color32 ObjectActive = new(255, 186, 84);

    /// <summary>What the pointer is over and a click would select: faint, so it never reads as chosen.</summary>
    public static readonly Color32 ObjectHovered = new(150, 156, 168);

    public static readonly Color32 BrushOutline = new(255, 160, 60);

    /// <summary>The surface Extrude holds: warm, the colour of the arrow that grows out of it.</summary>
    public static readonly Color32 Selection = new(255, 178, 64);

    /// <summary>Shift: what a click or a drag would add to the selection.</summary>
    public static readonly Color32 SelectionAdd = new(86, 156, 255);

    /// <summary>
    /// Alt: what it would take away. Subtract has to look different from add: until the click
    /// lands, the two gestures are otherwise indistinguishable (EditorApp.md, "Extrude").
    /// </summary>
    public static readonly Color32 SelectionSubtract = new(176, 112, 255);

    /// <summary>
    /// The colour of a surface about to be chosen, by what choosing it would do. The same yellow as
    /// any hover when it would replace the selection; blue and purple under Shift and Alt, so which
    /// of the three a click means is on screen before the click.
    /// </summary>
    public static Color32 SelectionColour(SelectionOperation operation) => operation switch
    {
        SelectionOperation.Add => SelectionAdd,
        SelectionOperation.Subtract => SelectionSubtract,
        _ => Highlight,
    };

    public static readonly Color32 Arrow = new(255, 210, 90);

    public static readonly Color32 CutPlane = new(255, 130, 220);

    // The axis colours the whole editor agrees on: the gizmo, the corner indicator and the dimension
    // labels all come from here, and the ImGui theme converts these rather than repeating them.
    public static readonly Color32 AxisX = new(0xEB, 0x5A, 0x5A);

    public static readonly Color32 AxisY = new(0x78, 0xDC, 0x6E);

    public static readonly Color32 AxisZ = new(0x64, 0x96, 0xFA);

    public static readonly Color32 GizmoEdge = new(150, 150, 165);

    public static readonly Color32 GizmoActive = new(255, 240, 140);

    /// <summary>Past this many faces a patch is drawn as its bounds: the tint alone would be a megabyte a frame.</summary>
    public const int MaxOutlinedFaces = 10_000;

    // Overlay stroke widths, as multiples of the batch's screen-constant thickness. Heavy enough to
    // grab, light enough not to become the thing you look at — three quarters of what they were,
    // which had them reading as the subject rather than a mark on it.
    public const float SelectionWidth = 0.825f;

    public const float GizmoWidth = 1.35f;

    public const float GizmoEdgeWidth = 1.125f;

    public const float ArrowWidth = 1.35f;

    /// <summary>How much of a patch's colour its tint lets through. Enough to see which faces, not so much as to hide theirs.</summary>
    public const byte FillAlpha = 60;

    /// <summary>How far a patch floats off the faces it marks, so it never fights them for depth.</summary>
    public const float PatchOffset = 0.02f;

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

    /// <summary>Where a mirrored write lands, drawn fainter than the thing itself.</summary>
    public static readonly Color32 MirrorEcho = new(200, 200, 210);

    /// <summary>
    /// The symmetry planes, in the focused object's own space: a frame a little larger than the
    /// object, in the colour of the axis it mirrors across, with a cross through it so it reads as a
    /// surface. Only while a tool that mirrors is in hand.
    /// </summary>
    public static void AddMirrorPlanes(LineGeometry lines, EditorSession session)
    {
        if (!session.Symmetry.IsOn
            || session.ActiveTool is not (EditorTool.Paint or EditorTool.Extrude)
            || session.Scene.Focus is not { } focus
            || !focus.TryGetLocalBounds(out Vector3 min, out Vector3 max))
        {
            return;
        }

        Vector3 planes = session.Symmetry.PlanesFor(focus);
        min -= Vector3.One;
        max += Vector3.One;

        foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            if (!session.Symmetry[axis])
            {
                continue;
            }

            int a = (int)axis;
            int u = (a + 1) % 3;
            int v = (a + 2) % 3;

            Vector3 Corner(float cu, float cv)
            {
                var point = new Vector3();
                point[a] = planes[a];
                point[u] = cu;
                point[v] = cv;
                return point;
            }

            Color32 colour = axis switch
            {
                Axis.X => AxisX,
                Axis.Y => AxisY,
                _ => AxisZ,
            };

            Vector3 p00 = Corner(min[u], min[v]);
            Vector3 p10 = Corner(max[u], min[v]);
            Vector3 p11 = Corner(max[u], max[v]);
            Vector3 p01 = Corner(min[u], max[v]);

            lines.AddThickLine(p00, p10, colour, SelectionWidth);
            lines.AddThickLine(p10, p11, colour, SelectionWidth);
            lines.AddThickLine(p11, p01, colour, SelectionWidth);
            lines.AddThickLine(p01, p00, colour, SelectionWidth);
            lines.AddLine(Corner((min[u] + max[u]) * 0.5f, min[v]), Corner((min[u] + max[u]) * 0.5f, max[v]), colour);
            lines.AddLine(Corner(min[u], (min[v] + max[v]) * 0.5f), Corner(max[u], (min[v] + max[v]) * 0.5f), colour);
        }
    }

    /// <summary>The faces a selection's mirror images would extrude, in the focused object's space.</summary>
    public static void AddMirroredSelection(LineGeometry lines, EditorSession session, FaceSelection? selection)
    {
        if (selection is not { IsEmpty: false } || selection.Count > MaxOutlinedFaces)
        {
            return;
        }

        AddMirroredPatch(lines, session, selection.Voxels, selection.Direction);
    }

    /// <summary>A patch's mirror images, fainter than the patch.</summary>
    private static void AddMirroredPatch(LineGeometry lines, EditorSession session, IEnumerable<Int3> cells, Face face)
    {
        foreach (MirrorImage image in session.Symmetry.ImagesFor(session.Scene.Focus))
        {
            var mirrored = new HashSet<Int3>();
            foreach (Int3 cell in cells)
            {
                mirrored.Add(image.Cell(cell));
            }

            AddFacePatch(lines, mirrored, mirrored.Contains, image.Face(face), MirrorEcho, SelectionWidth);
        }
    }

    /// <summary>The mirror images of the face under the cursor.</summary>
    public static void AddMirroredHover(LineGeometry lines, EditorSession session, Int3 voxel, Face face)
    {
        if (session.ActiveTool is not (EditorTool.Paint or EditorTool.Extrude))
        {
            return;
        }

        foreach (MirrorImage image in session.Symmetry.ImagesFor(session.Scene.Focus))
        {
            lines.AddVoxelFace(image.Cell(voxel), image.Face(face), MirrorEcho, width: SelectionWidth);
        }
    }

    public static void AddSelectionOutline(LineGeometry lines, FaceSelection? selection, Color32 color)
    {
        if (selection is not { IsEmpty: false })
        {
            return;
        }

        AddFacePatch(lines, selection.Voxels, selection.Contains, selection.Direction, color, SelectionWidth);
    }

    /// <summary>
    /// Faces all pointing one way, drawn as one surface: a faint tint over every face and a stroke
    /// round the outside only. Outlining each face drew a grid over the selection, heavier the more
    /// was chosen, and the shape of what was chosen got lost in it.
    /// </summary>
    /// <param name="contains">Whether a cell is in the patch — how an edge is known to be on the outside.</param>
    public static void AddFacePatch(
        LineGeometry lines,
        IReadOnlyCollection<Int3> cells,
        Func<Int3, bool> contains,
        Face face,
        Color32 colour,
        float width,
        float offset = PatchOffset)
    {
        if (cells.Count == 0)
        {
            return;
        }

        // Past the cap the bounding box says the same thing for a fraction of the geometry.
        if (cells.Count > MaxOutlinedFaces)
        {
            Int3 low = new(int.MaxValue, int.MaxValue, int.MaxValue);
            Int3 high = new(int.MinValue, int.MinValue, int.MinValue);
            foreach (Int3 cell in cells)
            {
                low = Int3.Min(low, cell);
                high = Int3.Max(high, cell);
            }

            (Vector3 min, Vector3 max) = new VoxelBox(low, high).ToWorldBounds();
            lines.AddBox(min, max, colour, width);
            return;
        }

        Vector3 push = FaceInfo.Normal(face) * offset;
        Color32 tint = colour with { A = FillAlpha };

        Span<Vector3> corners = stackalloc Vector3[4];
        Span<Int3> across = stackalloc Int3[4];
        EdgeNeighbours(face, across);

        foreach (Int3 cell in cells)
        {
            Vector3 origin = cell.ToVector3() + push;
            for (int i = 0; i < 4; i++)
            {
                corners[i] = origin + FaceInfo.Corner(face, i).ToVector3();
            }

            lines.AddQuad(corners[0], corners[1], corners[2], corners[3], tint);

            // An edge shared with another face of the patch is inside it, and is not drawn.
            for (int i = 0; i < 4; i++)
            {
                if (!contains(cell + across[i]))
                {
                    lines.AddThickLine(corners[i], corners[(i + 1) & 3], colour, width);
                }
            }
        }
    }

    /// <summary>
    /// For each edge of a face — corner i to corner i + 1 — the step to the cell on its far side, in
    /// the face's own plane. Twice the edge's midpoint less the four corners' sum is twice the step.
    /// </summary>
    private static void EdgeNeighbours(Face face, Span<Int3> across)
    {
        Int3 sum = FaceInfo.Corner(face, 0) + FaceInfo.Corner(face, 1) + FaceInfo.Corner(face, 2) + FaceInfo.Corner(face, 3);

        for (int i = 0; i < 4; i++)
        {
            Int3 twice = ((FaceInfo.Corner(face, i) + FaceInfo.Corner(face, (i + 1) & 3)) * 2) - sum;
            across[i] = new Int3(twice.X / 2, twice.Y / 2, twice.Z / 2);
        }
    }

    /// <summary>
    /// Extrude's surfaces, as patches: the selection held, its mirror images, and the one being
    /// dragged out. Nothing unless Extrude is in hand — a patch left glowing while painting reads as
    /// something the brush is about to do.
    /// </summary>
    public static void AddExtrudeSelection(LineGeometry lines, EditorSession session, ExtrudeInteraction extrude)
    {
        if (session.ActiveTool != EditorTool.Extrude)
        {
            return;
        }

        // A drag with no modifier replaces the selection when it lands, so the old one is as good as
        // gone. Showing both made the new rectangle look as though it would join the old.
        if (!extrude.IsReplacing && session.Selection is { IsEmpty: false } selection)
        {
            // While the arrow is pulled, the surface rides out with it, onto the faces being made.
            FaceSelection shown = selection.Translated(session.ExtrudeSteps);
            AddSelectionOutline(lines, shown, Selection);
            AddMirroredSelection(lines, session, shown);
        }

        AddSelectionOutline(lines, extrude.PendingSelection, SelectionColour(extrude.PendingOperation));
    }

    /// <summary>
    /// The face under the pointer in Extrude, in the colour of what a click there would do — or
    /// nothing, over the selection itself, where a press pulls the surface rather than choosing it.
    /// </summary>
    public static void AddExtrudeHover(
        LineGeometry lines,
        EditorSession session,
        ExtrudeInteraction extrude,
        RaycastHit hit,
        SelectionOperation operation)
    {
        if (extrude.IsBusy || (operation == SelectionOperation.Replace && extrude.IsOnSelection(hit)))
        {
            return;
        }

        lines.AddVoxelFace(hit.Voxel, hit.Face, SelectionColour(operation), offset: PatchOffset, width: SelectionWidth);
        AddMirroredHover(lines, session, hit.Voxel, hit.Face);
    }

    /// <summary>
    /// What a brush stroke here would paint, face for face — round, because the brush is. A box the
    /// size of the radius said how far it reached but drew a cube round a disc.
    /// </summary>
    public static void AddBrushPreview(LineGeometry lines, EditorSession session, RaycastHit hit)
    {
        List<Int3> cells = PaintOperations.BrushCells(session.World, hit.Voxel, hit.Face, session.BrushRadius);
        if (cells.Count == 0)
        {
            return;
        }

        var set = new HashSet<Int3>(cells);
        AddFacePatch(lines, set, set.Contains, hit.Face, BrushOutline, SelectionWidth);
        AddMirroredPatch(lines, session, set, hit.Face);
    }

    /// <summary>
    /// The extrude arrow, in world space. Nothing is drawn unless Extrude is the tool in hand — a
    /// gizmo outliving its own tool is a standing invitation to grab the wrong thing.
    /// </summary>
    /// <param name="lit">Drawn lit when a press would pull it: over the arrow or the selection.</param>
    public static void AddExtrudeArrow(LineGeometry lines, EditorSession session, ExtrudeInteraction extrude, bool lit = false)
    {
        // Not while a new selection is dragged out to replace this one: the arrow belongs to the old.
        if (session.ActiveTool != EditorTool.Extrude || extrude.IsReplacing || extrude.Arrow() is not { } arrow)
        {
            return;
        }

        AddArrow(lines, arrow.Start, arrow.End, lit || extrude.IsDraggingArrow ? GizmoActive : Arrow, ArrowWidth);
    }

    /// <summary>A light that is switched off: still there to be picked, but grey.</summary>
    public static readonly Color32 LightOff = new(130, 130, 130);

    private const float LightWidth = 0.9f;

    /// <summary>The aim line: a thread, not a shaft, so it points without covering what it points at.</summary>
    private const float AimWidth = 0.55f;

    /// <summary>How far the sun's aim line reaches, in icon sizes.</summary>
    private const float SunAimLength = 16f;

    /// <summary>The ring at the end of an aim line, in icon sizes — the handle it is dragged by.</summary>
    private const float AimHandleRadius = 0.32f;

    /// <summary>
    /// A light icon's size at its distance from the camera, so it reads the same near and far. The
    /// icon, its aim line and the handle on the end all scale by it.
    /// </summary>
    public static float LightIconSize(SceneLight light, FlyCamera camera) => IconSizeAt(light.Position, camera);

    private static float IconSizeAt(Vector3 point, FlyCamera camera) =>
        MathF.Max(Vector3.Distance(camera.Position, point) * 0.022f, 0.02f);

    /// <summary>
    /// The end of a light's aim line, in world space: the handle that is dragged onto the model to
    /// point it there. Null for a bulb, which shines every way and has nothing to aim.
    /// </summary>
    public static Vector3? AimHandle(SceneLight light, FlyCamera camera)
    {
        float size = LightIconSize(light, camera);

        return light.Kind switch
        {
            LightKind.Directional => light.Position + (light.Direction * size * SunAimLength),
            LightKind.Spot => light.Position + (light.Direction * SpotLength(light, size)),
            _ => null,
        };
    }

    /// <summary>
    /// Where a light is and which way it shines, in world space. A bulb that faces the camera for
    /// every kind; rays for the sun, and a long thread for its direction; a cone for a spot, as wide
    /// as its beam. Sun and spot end in a small ring, the handle their aim is dragged by.
    /// </summary>
    /// <param name="aimedAt">Where the light is being pointed right now, while its line is dragged.</param>
    /// <param name="aimLit">The aim line is under the pointer: a press would take hold of it.</param>
    /// <param name="aimLine">Whether the aim line is drawn at all — off with the header's light gizmos.</param>
    public static void AddLight(
        LineGeometry lines,
        SceneLight light,
        FlyCamera camera,
        bool highlighted,
        Vector3? aimedAt = null,
        bool aimLit = false,
        bool aimLine = true)
    {
        Vector3 at = light.Position;
        float size = LightIconSize(light, camera);
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

                break;

            case LightKind.Point:
                AddBillboardCircle(lines, at, right, up, 0.45f, colour);
                break;

            default:
                AddSpotCone(lines, light, size, colour);
                break;
        }

        if (!aimLine || AimHandle(light, camera) is not { } handle)
        {
            return;
        }

        // Parallel rays have no source to point from, so for the sun the direction is the whole
        // message. No head on it: a cone at the end covered the very spot it pointed at.
        Vector3 end = aimedAt ?? handle;
        Color32 aim = aimLit || aimedAt is not null ? GizmoActive : colour;
        Vector3 start = at;
        if (light.Kind == LightKind.Directional && Vector3.DistanceSquared(at, end) > 1e-8f)
        {
            start = at + (Vector3.Normalize(end - at) * size * 1.1f);
        }

        if (Vector3.DistanceSquared(start, end) > 1e-8f)
        {
            lines.AddThickLine(start, end, aim, AimWidth);
        }

        float handleSize = IconSizeAt(end, camera);
        AddBillboardCircle(lines, end, camera.Right * handleSize, camera.Up * handleSize, AimHandleRadius, aim, AimWidth);
    }

    private static void AddBillboardCircle(
        LineGeometry lines,
        Vector3 centre,
        Vector3 right,
        Vector3 up,
        float radius,
        Color32 colour,
        float width = LightWidth)
    {
        const int Segments = 16;
        Vector3 previous = centre + (right * radius);

        for (int i = 1; i <= Segments; i++)
        {
            float angle = i * (MathF.Tau / Segments);
            Vector3 point = centre + (((right * MathF.Cos(angle)) + (up * MathF.Sin(angle))) * radius);
            lines.AddThickLine(previous, point, colour, width);
            previous = point;
        }
    }

    /// <summary>The beam drawn out to its range, or a dozen icon-sizes if that is nearer: far enough to aim by.</summary>
    private static float SpotLength(SceneLight light, float size) => MathF.Min(light.Range, size * 12f);

    private static void AddSpotCone(LineGeometry lines, SceneLight light, float size, Color32 colour)
    {
        const int Segments = 20;

        Vector3 direction = light.Direction;
        float length = SpotLength(light, size);
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

            if (handle.Kind is GizmoKind.RotateRing or GizmoKind.MoveFree)
            {
                Vector3? previous = null;
                IEnumerable<Vector3> ring = handle.Kind == GizmoKind.MoveFree
                    ? transform.FreeHandlePoints(handle, camera)
                    : transform.RingPoints(handle, camera);

                foreach (Vector3 point in ring)
                {
                    if (previous is { } from)
                    {
                        lines.AddThickLine(from, point, color, handle.Kind == GizmoKind.MoveFree ? GizmoEdgeWidth : GizmoWidth);
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

    /// <summary>
    /// Where a snapped drag has landed: a ring round the point it snapped to, drawn over everything,
    /// so it is clear the object jumped to something rather than slipped.
    /// </summary>
    public static void AddSnapTarget(LineGeometry lines, TransformInteraction transform, FlyCamera camera)
    {
        if (transform.SnapPoint is not { } point)
        {
            return;
        }

        float size = MathF.Max(Vector3.Distance(camera.Position, point) * 0.014f, 0.01f);
        Vector3 right = camera.Right * size;
        Vector3 up = camera.Up * size;

        AddBillboardCircle(lines, point, right, up, 1f, SnapMark, GizmoEdgeWidth);
        lines.AddThickLine(point - (right * 0.35f), point + (right * 0.35f), SnapMark, GizmoEdgeWidth);
        lines.AddThickLine(point - (up * 0.35f), point + (up * 0.35f), SnapMark, GizmoEdgeWidth);
    }

    /// <summary>
    /// The world's axes across the floor through the origin, each in its colour — Blender draws them
    /// over its grid, and the red and blue lines are what say which way X and Z run at a glance.
    /// </summary>
    public static void AddAxes(LineGeometry lines, bool x, bool y, bool z, float extent)
    {
        if (x)
        {
            lines.AddLine(new Vector3(-extent, 0f, 0f), new Vector3(extent, 0f, 0f), AxisX);
        }

        if (y)
        {
            lines.AddLine(new Vector3(0f, -extent, 0f), new Vector3(0f, extent, 0f), AxisY);
        }

        if (z)
        {
            lines.AddLine(new Vector3(0f, 0f, -extent), new Vector3(0f, 0f, extent), AxisZ);
        }
    }

    /// <summary>A dot at each visible object's origin — the point its position is — the focused one's lit.</summary>
    public static void AddOrigins(LineGeometry lines, VoxelScene scene, FlyCamera camera)
    {
        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible)
            {
                continue;
            }

            Vector3 at = o.Transform.Position;
            float size = MathF.Max(Vector3.Distance(camera.Position, at) * 0.005f, 0.004f);
            Vector3 right = camera.Right * size;
            Vector3 up = camera.Up * size;
            Color32 colour = o.Id == scene.FocusId ? OriginFocused : OriginOther;

            lines.AddQuad(at - right - up, at + right - up, at + right + up, at - right + up, colour);
            AddBillboardCircle(lines, at, right, up, 1.5f, colour, GizmoEdgeWidth * 0.8f);
        }
    }

    /// <summary>
    /// A dashed line from each child to its parent, Blender's relationship lines: what moves with what
    /// is otherwise seen only by moving it. Centre to centre, where the eye already is — an origin can
    /// sit well away from the voxels. Lights are left out while their icons are.
    /// </summary>
    public static void AddRelationshipLines(LineGeometry lines, VoxelScene scene, FlyCamera camera, bool lights)
    {
        foreach (VoxelObject o in scene.Objects)
        {
            if (o.Visible && scene.ParentOf(o) is { Visible: true } parent)
            {
                AddDashed(lines, o.WorldCentre(), parent.WorldCentre(), camera, RelationshipLine);
            }
        }

        if (!lights)
        {
            return;
        }

        foreach (SceneLight light in scene.Lights)
        {
            if (scene.ParentOf(light) is { Visible: true } parent)
            {
                AddDashed(lines, light.WorldCentre(), parent.WorldCentre(), camera, RelationshipLine);
            }
        }
    }

    /// <summary>Dashes of a length that looks the same near and far: a share of the distance to the camera.</summary>
    private static void AddDashed(LineGeometry lines, Vector3 from, Vector3 to, FlyCamera camera, Color32 colour)
    {
        float length = Vector3.Distance(from, to);
        if (length < 1e-4f)
        {
            return;
        }

        float dash = MathF.Max(Vector3.Distance(camera.Position, (from + to) * 0.5f) * 0.012f, 0.01f);
        int steps = Math.Min((int)MathF.Ceiling(length / dash), 400);

        for (int i = 0; i < steps; i += 2)
        {
            Vector3 a = Vector3.Lerp(from, to, i / (float)steps);
            Vector3 b = Vector3.Lerp(from, to, Math.Min(i + 1, steps) / (float)steps);
            lines.AddLine(a, b, colour);
        }
    }

    public static readonly Color32 RelationshipLine = new(150, 156, 170);

    public static readonly Color32 OriginFocused = new(255, 170, 64);

    public static readonly Color32 OriginOther = new(170, 170, 180);

    /// <summary>The ring round a snap target: Blender's snapping mark is this colour.</summary>
    public static readonly Color32 SnapMark = new(255, 196, 64);

    public static Color32 ColorFor(GizmoHandle handle) => handle.Kind switch
    {
        GizmoKind.EdgeHinge or GizmoKind.MoveFree => GizmoEdge,
        _ => handle.Axis switch
        {
            0 => AxisX,
            1 => AxisY,
            _ => AxisZ,
        },
    };

    /// <summary>
    /// Where the loop cut would go through the model: a ring round the section it cuts, with the cut
    /// face tinted inside it. A frame round the whole object's bounds said which plane, but not what
    /// of the model it would cut. Drawn in the focused object's own space.
    /// </summary>
    public static void AddCutPreview(LineGeometry lines, EditorSession session)
    {
        // Gated on the tool as well as on the plane: a preview must never outlive its own tool.
        if (session.ActiveTool != EditorTool.LoopCut
            || session.PreviewCutPlane is not { } plane
            || session.Scene.Focus is not { } focus)
        {
            return;
        }

        HashSet<Int3> section = LoopCut.CrossSection(focus.Grid, plane);

        // Exactly on the plane: the cells are the ones below it, and their upper faces are the cut.
        AddFacePatch(lines, section, section.Contains, LoopCut.Towards(plane.AxisIndex), CutPlane, SelectionWidth, offset: 0f);
    }
}
