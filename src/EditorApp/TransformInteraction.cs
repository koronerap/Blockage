using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;

namespace EditorApp;

/// <summary>What part of the transform gizmo the cursor is on.</summary>
public enum GizmoKind
{
    None,

    /// <summary>One of the three move arrows.</summary>
    MoveAxis,

    /// <summary>One of the three rotate rings, at the object's own centre.</summary>
    RotateRing,

    /// <summary>An edge of the bounding box, rotating like a door hinge about the nearest corner.</summary>
    EdgeHinge,
}

/// <summary>A grabbable part of the gizmo, with the geometry the drag needs.</summary>
public readonly record struct GizmoHandle(
    GizmoKind Kind,
    int Axis,
    Vector3 Origin,
    Vector3 Direction,
    Vector3 Pivot);

/// <summary>
/// The Transform tool's gizmos (EditorApp.md, "Transform"). Everything is picked in <b>screen
/// space</b> — the gizmo has no colliders, so arrows, rings and edges are all hit-tested as
/// projected line segments, which is also what keeps them grabbable at any camera distance.
///
/// Snap is on unless Shift is held: movement lands on whole voxels, rotation on a fixed angle step.
/// </summary>
public sealed class TransformInteraction(EditorSession session)
{
    private const float GrabPixels = 12f;

    /// <summary>Gizmo size as a fraction of its distance from the camera, so it keeps a constant look.</summary>
    private const float ScreenScale = 0.16f;

    private const int RingSegments = 48;

    private VoxelObject? _target;
    private ObjectTransform _startTransform;
    private GizmoHandle _grabbed;
    private Vector2 _pressPosition;
    private float _pressAngle;

    public GizmoHandle? Hovered { get; private set; }

    public bool IsDragging => _target is not null;

    /// <summary>Live readout for the panel, e.g. "+3, 0, 0" or "45°".</summary>
    public string Readout { get; private set; } = string.Empty;

    /// <summary>Gizmo length in world units at the object's distance from the camera.</summary>
    private static float GizmoScale(FlyCamera camera, Vector3 origin) =>
        MathF.Max(Vector3.Distance(camera.Position, origin) * ScreenScale, 0.5f);

    /// <summary>
    /// The handles on offer right now. Move shows arrows plus the bounding-box edges; Rotate shows
    /// three rings. The hinge stays available in Move because it is a different motion from the
    /// rings, not a duplicate of them.
    /// </summary>
    public IEnumerable<GizmoHandle> Handles(FlyCamera camera)
    {
        if (session.Scene.Focus is not { } focus || focus.IsEmpty)
        {
            yield break;
        }

        Vector3 centre = focus.WorldCentre();

        if (session.TransformMode == TransformMode.Rotate)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                yield return new GizmoHandle(GizmoKind.RotateRing, axis, centre, AxisDirection(focus, axis, local: true), centre);
            }

            yield break;
        }

        bool local = session.TransformSpace == TransformSpace.Local;
        for (int axis = 0; axis < 3; axis++)
        {
            yield return new GizmoHandle(GizmoKind.MoveAxis, axis, centre, AxisDirection(focus, axis, local), centre);
        }

        foreach (GizmoHandle edge in EdgeHandles(focus))
        {
            yield return edge;
        }

        _ = camera;
    }

    private static Vector3 AxisDirection(VoxelObject focus, int axis, bool local)
    {
        Vector3 world = axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            _ => Vector3.UnitZ,
        };

        // The hinge is always local; Global/Local only changes what the move arrows mean.
        return local ? focus.Transform.TransformDirection(world) : world;
    }

    /// <summary>The twelve edges of the object's own box, in world space.</summary>
    private static IEnumerable<GizmoHandle> EdgeHandles(VoxelObject focus)
    {
        if (!focus.TryGetLocalBounds(out Vector3 min, out Vector3 max))
        {
            yield break;
        }

        for (int axis = 0; axis < 3; axis++)
        {
            int uAxis = axis == 0 ? 1 : 0;
            int vAxis = axis == 2 ? 1 : 2;

            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 start = min;
                Vector3 end = min;

                SetComponent(ref start, uAxis, (corner & 1) == 0 ? Component(min, uAxis) : Component(max, uAxis));
                SetComponent(ref end, uAxis, (corner & 1) == 0 ? Component(min, uAxis) : Component(max, uAxis));
                SetComponent(ref start, vAxis, (corner & 2) == 0 ? Component(min, vAxis) : Component(max, vAxis));
                SetComponent(ref end, vAxis, (corner & 2) == 0 ? Component(min, vAxis) : Component(max, vAxis));
                SetComponent(ref start, axis, Component(min, axis));
                SetComponent(ref end, axis, Component(max, axis));

                Vector3 worldStart = focus.Transform.TransformPoint(start);
                Vector3 worldEnd = focus.Transform.TransformPoint(end);

                yield return new GizmoHandle(
                    GizmoKind.EdgeHinge,
                    axis,
                    worldStart,
                    Vector3.Normalize(worldEnd - worldStart),
                    worldEnd);
            }
        }
    }

    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static void SetComponent(ref Vector3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }

    /// <summary>Where a handle's grabbable segment runs, in world space.</summary>
    public (Vector3 Start, Vector3 End) Segment(GizmoHandle handle, FlyCamera camera) => handle.Kind switch
    {
        GizmoKind.EdgeHinge => (handle.Origin, handle.Pivot),
        _ => (handle.Origin, handle.Origin + handle.Direction * GizmoScale(camera, handle.Origin)),
    };

    /// <summary>Points around a rotate ring, for drawing and for hit testing.</summary>
    public IEnumerable<Vector3> RingPoints(GizmoHandle handle, FlyCamera camera)
    {
        float radius = GizmoScale(camera, handle.Origin);
        Vector3 normal = handle.Direction;

        Vector3 reference = MathF.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(normal, reference)) * radius;
        Vector3 v = Vector3.Normalize(Vector3.Cross(normal, u)) * radius;

        for (int i = 0; i <= RingSegments; i++)
        {
            float angle = i / (float)RingSegments * MathF.Tau;
            yield return handle.Origin + u * MathF.Cos(angle) + v * MathF.Sin(angle);
        }
    }

    public void UpdateHover(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        if (IsDragging)
        {
            return;
        }

        Hovered = Pick(mouse, viewport, camera);
    }

    private GizmoHandle? Pick(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        // Arrows and rings are tried first and win outright. A box edge runs the length of the
        // model and passes close to the gizmo at the centre, so sharing one nearest-wins test let
        // the hinge steal grabs from the move axes almost everywhere.
        if (PickAmong(mouse, viewport, camera, edges: false) is { } primary)
        {
            return primary;
        }

        return PickAmong(mouse, viewport, camera, edges: true);
    }

    private GizmoHandle? PickAmong(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool edges)
    {
        GizmoHandle? best = null;
        float bestDistance = GrabPixels;

        foreach (GizmoHandle handle in Handles(camera))
        {
            if (handle.Kind == GizmoKind.EdgeHinge != edges)
            {
                continue;
            }

            float distance = handle.Kind == GizmoKind.RotateRing
                ? DistanceToRing(handle, mouse, viewport, camera)
                : DistanceToSegment(handle, mouse, viewport, camera);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = handle;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a handle should be drawn. Move axes and rings are always on show; the twelve box
    /// edges only appear under the cursor, because drawing all of them outlines the model in grey
    /// and buries the gizmo they surround.
    /// </summary>
    public bool ShouldDraw(GizmoHandle handle)
    {
        if (handle.Kind != GizmoKind.EdgeHinge)
        {
            return true;
        }

        if (IsDragging)
        {
            return Matches(_grabbed, handle);
        }

        return Hovered is { } hovered && Matches(hovered, handle);
    }

    /// <summary>True when the hovered or grabbed handle is highlighted.</summary>
    public bool IsHighlighted(GizmoHandle handle) =>
        IsDragging ? Matches(_grabbed, handle) : Hovered is { } hovered && Matches(hovered, handle);

    private static bool Matches(GizmoHandle a, GizmoHandle b) =>
        a.Kind == b.Kind
        && a.Axis == b.Axis
        && Vector3.DistanceSquared(a.Origin, b.Origin) < 1e-6f;

    private float DistanceToSegment(GizmoHandle handle, Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        (Vector3 start, Vector3 end) = Segment(handle, camera);

        return camera.TryProjectToScreen(start, viewport, out Vector2 a)
            && camera.TryProjectToScreen(end, viewport, out Vector2 b)
                ? PointToSegment(mouse, a, b)
                : float.MaxValue;
    }

    private float DistanceToRing(GizmoHandle handle, Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        float nearest = float.MaxValue;
        Vector2? previous = null;

        foreach (Vector3 point in RingPoints(handle, camera))
        {
            if (!camera.TryProjectToScreen(point, viewport, out Vector2 projected))
            {
                previous = null;
                continue;
            }

            if (previous is { } from)
            {
                nearest = MathF.Min(nearest, PointToSegment(mouse, from, projected));
            }

            previous = projected;
        }

        return nearest;
    }

    public bool OnPress(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        if (session.Scene.Focus is not { } focus || Pick(mouse, viewport, camera) is not { } handle)
        {
            return false;
        }

        _target = focus;
        _startTransform = focus.Transform;
        _grabbed = handle;
        _pressPosition = mouse;
        _pressAngle = ScreenAngle(handle, mouse, viewport, camera);
        Readout = string.Empty;
        return true;
    }

    public void OnDrag(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool freeform)
    {
        if (_target is null)
        {
            return;
        }

        if (_grabbed.Kind == GizmoKind.MoveAxis)
        {
            DragMove(mouse, viewport, camera, freeform);
        }
        else
        {
            DragRotate(mouse, viewport, camera, freeform);
        }
    }

    private void DragMove(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool freeform)
    {
        if (!camera.TryProjectToScreen(_grabbed.Origin, viewport, out Vector2 origin)
            || !camera.TryProjectToScreen(_grabbed.Origin + _grabbed.Direction, viewport, out Vector2 oneUnit))
        {
            return;
        }

        Vector2 screenAxis = oneUnit - origin;
        float pixelsPerUnit = screenAxis.Length();
        if (pixelsPerUnit < 1.5f)
        {
            return;   // edge on, the drag would be uncontrollable
        }

        float units = Vector2.Dot(mouse - _pressPosition, screenAxis / pixelsPerUnit) / pixelsPerUnit;

        Vector3 offset = _grabbed.Direction * units;
        ObjectTransform moved = _startTransform.Translated(offset);

        if (!freeform)
        {
            moved = moved with { Position = ObjectTransform.SnapPosition(moved.Position) };
        }

        session.ApplyTransform(moved);

        Vector3 delta = moved.Position - _startTransform.Position;
        Readout = $"{delta.X:+0.##;-0.##;0}, {delta.Y:+0.##;-0.##;0}, {delta.Z:+0.##;-0.##;0}";
    }

    private void DragRotate(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool freeform)
    {
        float angle = ScreenAngle(_grabbed, mouse, viewport, camera);
        float degrees = (angle - _pressAngle) * (180f / MathF.PI);

        // Keep the reading in (-180, 180] so a drag past the top does not read as 350 degrees.
        degrees = ((degrees + 180f) % 360f + 360f) % 360f - 180f;

        if (!freeform)
        {
            degrees = ObjectTransform.SnapAngleDegrees(degrees);
        }

        // Screen Y runs down, so a clockwise drag has to turn the object the same way it looks.
        Quaternion delta = Quaternion.CreateFromAxisAngle(_grabbed.Direction, -degrees * (MathF.PI / 180f));
        session.ApplyTransform(_startTransform.RotatedAbout(_grabbed.Pivot, delta));

        Readout = $"{degrees:0.#}°";
    }

    /// <summary>
    /// The cursor's angle around the handle's pivot on screen. Rings turn about the object centre,
    /// a hinge about the corner nearest where it was grabbed.
    /// </summary>
    private float ScreenAngle(GizmoHandle handle, Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        Vector3 pivot = handle.Kind == GizmoKind.EdgeHinge ? NearestCorner(handle, mouse, viewport, camera) : handle.Pivot;

        return camera.TryProjectToScreen(pivot, viewport, out Vector2 centre)
            ? MathF.Atan2(mouse.Y - centre.Y, mouse.X - centre.X)
            : 0f;
    }

    private static Vector3 NearestCorner(GizmoHandle handle, Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        // "The corner nearest the clicked point becomes the pivot" — measured on screen, like
        // everything else about the gizmo.
        bool haveStart = camera.TryProjectToScreen(handle.Origin, viewport, out Vector2 start);
        bool haveEnd = camera.TryProjectToScreen(handle.Pivot, viewport, out Vector2 end);

        if (!haveStart)
        {
            return handle.Pivot;
        }

        if (!haveEnd)
        {
            return handle.Origin;
        }

        return Vector2.Distance(mouse, start) <= Vector2.Distance(mouse, end) ? handle.Origin : handle.Pivot;
    }

    public void OnRelease()
    {
        if (_target is { } target)
        {
            string name = _grabbed.Kind == GizmoKind.MoveAxis ? "Move object" : "Rotate object";
            session.PushTransformEdit(target, _startTransform, name);
        }

        _target = null;
        Readout = string.Empty;
    }

    public void Cancel()
    {
        if (_target is not null)
        {
            session.ApplyTransform(_startTransform);
        }

        _target = null;
        Readout = string.Empty;
    }

    /// <summary>The pivot a hinge drag is currently turning about, for drawing.</summary>
    public Vector3? ActivePivot(Vector2 mouse, Vector2 viewport, FlyCamera camera) =>
        _target is null || _grabbed.Kind == GizmoKind.MoveAxis
            ? null
            : _grabbed.Kind == GizmoKind.EdgeHinge
                ? NearestCorner(_grabbed, mouse, viewport, camera)
                : _grabbed.Pivot;

    private static float PointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-6f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }
}
