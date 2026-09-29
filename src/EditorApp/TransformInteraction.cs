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

    /// <summary>The small ring at the middle: moves in the plane facing the camera, and follows the cursor onto surfaces.</summary>
    MoveFree,

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
/// A drag goes where the mouse goes. It snaps when the session's magnet is lit, or while Shift is
/// held when it is not — onto whole voxels, other objects' corners and edge middles, or the surface
/// under the cursor, as <see cref="SnapSettings"/> says. An arrow's drag stays on its arrow even
/// then: the target is met as nearly as the axis allows, as Blender does with a constraint.
/// </summary>
public sealed class TransformInteraction(EditorSession session)
{
    /// <summary>
    /// How near a handle a press has to land to grab it, in pixels. Settable for the same reason as
    /// the extrude arrow: a fingertip is not a cursor.
    /// </summary>
    public float GrabPixels { get; set; } = 12f;

    /// <summary>Gizmo size as a fraction of its distance from the camera, so it keeps a constant look.</summary>
    private const float ScreenScale = 0.16f;

    private const int RingSegments = 48;

    /// <summary>How near a corner or an edge middle the cursor has to come to snap to it, in pixels.</summary>
    public float SnapPixels { get; set; } = 16f;

    private IPlaceable? _target;
    private ObjectTransform _startTransform;
    private GizmoHandle _grabbed;
    private Vector2 _pressPosition;
    private float _pressAngle;
    private Vector3 _pressWorld;
    private Vector3 _planeNormal;

    /// <summary>Where the drag last snapped to a point, and to what; null when it did not.</summary>
    public Vector3? SnapPoint { get; private set; }

    public SnapTarget SnapKind { get; private set; }

    public GizmoHandle? Hovered { get; private set; }

    public bool IsDragging => _target is not null;

    /// <summary>Live readout for the panel, e.g. "+3, 0, 0" or "45°".</summary>
    public string Readout { get; private set; } = string.Empty;

    /// <summary>How big the gizmo is drawn, and grabbed, against the size it was designed at. A preference.</summary>
    public float SizeScale
    {
        get => _sizeScale;
        set => _sizeScale = float.IsFinite(value) ? Math.Clamp(value, 0.25f, 4f) : _sizeScale;
    }

    private float _sizeScale = 1f;

    /// <summary>Gizmo length in world units at the object's distance from the camera.</summary>
    private float GizmoScale(FlyCamera camera, Vector3 origin) =>
        MathF.Max(Vector3.Distance(camera.Position, origin) * ScreenScale * _sizeScale, 0.5f);

    /// <summary>
    /// The handles on offer right now. Move shows arrows plus the bounding-box edges; Rotate shows
    /// three rings. The hinge stays available in Move because it is a different motion from the
    /// rings, not a duplicate of them.
    ///
    /// On a light there is no box and so no hinge: arrows to move it and rings to aim it.
    /// </summary>
    public IEnumerable<GizmoHandle> Handles(FlyCamera camera)
    {
        if (session.TransformTarget is not { } focus || focus is VoxelObject { IsEmpty: true })
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

        yield return new GizmoHandle(GizmoKind.MoveFree, -1, centre, Vector3.Zero, centre);

        bool local = session.TransformSpace == TransformSpace.Local;
        for (int axis = 0; axis < 3; axis++)
        {
            yield return new GizmoHandle(GizmoKind.MoveAxis, axis, centre, AxisDirection(focus, axis, local), centre);
        }

        if (focus is VoxelObject voxels)
        {
            foreach (GizmoHandle edge in EdgeHandles(voxels))
            {
                yield return edge;
            }
        }

        _ = camera;
    }

    private static Vector3 AxisDirection(IPlaceable focus, int axis, bool local)
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

    /// <summary>The ring round the middle that moves freely, facing the camera, for drawing.</summary>
    public IEnumerable<Vector3> FreeHandlePoints(GizmoHandle handle, FlyCamera camera)
    {
        float radius = GizmoScale(camera, handle.Origin) * FreeRingShare;
        Vector3 right = camera.Right * radius;
        Vector3 up = camera.Up * radius;

        for (int i = 0; i <= RingSegments / 2; i++)
        {
            float angle = i / (float)(RingSegments / 2) * MathF.Tau;
            yield return handle.Origin + (right * MathF.Cos(angle)) + (up * MathF.Sin(angle));
        }
    }

    /// <summary>The free-move ring's size against the arrows' length.</summary>
    private const float FreeRingShare = 0.14f;

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
        // The ring in the middle first: the arrows start where it is, and a press on the middle of
        // the gizmo means the ring rather than whichever arrow happens to be nearest.
        foreach (GizmoHandle handle in Handles(camera))
        {
            if (handle.Kind == GizmoKind.MoveFree
                && camera.TryProjectToScreen(handle.Origin, viewport, out Vector2 centre)
                && camera.TryProjectToScreen(handle.Origin + (camera.Right * GizmoScale(camera, handle.Origin) * FreeRingShare), viewport, out Vector2 rim)
                && Vector2.Distance(mouse, centre) <= MathF.Max(Vector2.Distance(centre, rim) + 4f, GrabPixels * 0.75f))
            {
                return handle;
            }
        }

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
            if (handle.Kind == GizmoKind.EdgeHinge != edges || handle.Kind == GizmoKind.MoveFree)
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
        if (session.TransformTarget is not { } focus || Pick(mouse, viewport, camera) is not { } handle)
        {
            return false;
        }

        _target = focus;
        _startTransform = focus.Transform;
        _grabbed = handle;
        _pressPosition = mouse;
        _pressAngle = ScreenAngle(handle, mouse, viewport, camera);
        _planeNormal = camera.Forward;
        _pressWorld = PlaneHit(mouse, viewport, camera) ?? handle.Origin;
        SnapPoint = null;
        Readout = string.Empty;
        return true;
    }

    /// <param name="snap">
    /// Whether this frame's drag snaps: the magnet, turned the other way while Shift is held — the
    /// host's to work out, since only it knows what is held.
    /// </param>
    public void OnDrag(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool snap)
    {
        if (_target is null)
        {
            return;
        }

        SnapPoint = null;

        switch (_grabbed.Kind)
        {
            case GizmoKind.MoveAxis:
                DragMove(mouse, viewport, camera, snap && session.Snap.AffectMove);
                break;

            case GizmoKind.MoveFree:
                DragFree(mouse, viewport, camera, snap && session.Snap.AffectMove);
                break;

            default:
                DragRotate(mouse, viewport, camera, snap && session.Snap.AffectRotate);
                break;
        }
    }

    private void DragMove(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool snap)
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

        Vector3 axis = _grabbed.Direction;
        float units = Vector2.Dot(mouse - _pressPosition, screenAxis / pixelsPerUnit) / pixelsPerUnit;
        ObjectTransform moved = _startTransform.Translated(axis * units);

        if (snap)
        {
            if (FindTarget(mouse, viewport, camera) is { } target)
            {
                // Along the arrow only: as near the target as the axis lets the moved point come.
                ObjectTransform start = Standing(_startTransform, target);
                Vector3 basePoint = BaseFor(start, target);
                moved = start.Translated(axis * Vector3.Dot(target.Point - basePoint, axis));
                Landed(target);
            }
            else if (session.Snap.Snaps(SnapTarget.Increment))
            {
                moved = IncrementAlong(moved, axis, units);
            }
        }

        Apply(moved);
    }

    private void DragFree(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool snap)
    {
        if (PlaneHit(mouse, viewport, camera) is not { } hit)
        {
            return;
        }

        Vector3 delta = hit - _pressWorld;
        ObjectTransform moved = _startTransform.Translated(delta);

        if (snap)
        {
            if (FindTarget(mouse, viewport, camera) is { } target)
            {
                // Free to go wherever the target is: the moved point lands on it.
                moved = Standing(moved, target);
                moved = moved.Translated(target.Point - BaseFor(moved, target));

                // Set down on a surface with whole voxels on as well: flush against the surface, and
                // on the lattice along it — blocks stacked on blocks line up.
                if (target.Kind == SnapTarget.Surface && session.Snap.Snaps(SnapTarget.Increment) && AxisOf(target.Normal) is { } normalAxis)
                {
                    Vector3 onGrid = Snapping.ToGrid(moved.Position, _startTransform.VoxelSize);
                    Vector3 position = moved.Position;
                    for (int a = 0; a < 3; a++)
                    {
                        if (a != normalAxis)
                        {
                            position[a] = onGrid[a];
                        }
                    }

                    moved = moved with { Position = position };
                }

                Landed(target);
            }
            else if (session.Snap.Snaps(SnapTarget.Increment))
            {
                float step = _startTransform.VoxelSize;
                moved = session.Snap.AbsoluteGrid
                    ? moved with { Position = Snapping.ToGrid(moved.Position, step) }
                    : _startTransform.Translated(Snapping.ToGrid(delta, step));
            }
        }

        Apply(moved);
    }

    /// <summary>
    /// Whole voxels along an arrow. On the world's lattice when the arrow lies along a world axis and
    /// the grid is absolute — only that one coordinate, so the drag stays on its arrow; in whole
    /// steps from where the drag began otherwise.
    /// </summary>
    private ObjectTransform IncrementAlong(ObjectTransform moved, Vector3 axis, float units)
    {
        float step = _startTransform.VoxelSize;

        if (session.Snap.AbsoluteGrid && AxisOf(axis) is { } world)
        {
            Vector3 position = moved.Position;
            position[world] = MathF.Round(position[world] / step, MidpointRounding.AwayFromZero) * step;
            return moved with { Position = position };
        }

        return _startTransform.Translated(axis * (MathF.Round(units / step, MidpointRounding.AwayFromZero) * step));
    }

    /// <summary>Which world axis a direction lies along, if it lies along one.</summary>
    private static int? AxisOf(Vector3 direction)
    {
        for (int a = 0; a < 3; a++)
        {
            if (MathF.Abs(direction[a]) > 0.999f)
            {
                return a;
            }
        }

        return null;
    }

    private void Apply(ObjectTransform moved)
    {
        session.ApplyTransform(_target!, moved);

        Vector3 delta = moved.Position - _startTransform.Position;
        string landed = SnapPoint is null ? string.Empty : $"   snapped to {NameOf(SnapKind)}";
        Readout = $"{delta.X:+0.##;-0.##;0}, {delta.Y:+0.##;-0.##;0}, {delta.Z:+0.##;-0.##;0}{landed}";
    }

    private static string NameOf(SnapTarget kind) => kind switch
    {
        SnapTarget.Corner => "a corner",
        SnapTarget.EdgeCentre => "an edge middle",
        _ => "a surface",
    };

    private void Landed((Vector3 Point, SnapTarget Kind, Vector3 Normal) target)
    {
        SnapPoint = target.Point;
        SnapKind = target.Kind;
    }

    /// <summary>A surface with its up asked to follow it turns the moved thing to stand on it; anything else leaves it as it was.</summary>
    private ObjectTransform Standing(ObjectTransform at, (Vector3 Point, SnapTarget Kind, Vector3 Normal) target) =>
        target.Kind == SnapTarget.Surface && session.Snap.AlignToSurface
            ? at with { Rotation = Snapping.Standing(_startTransform.Rotation, target.Normal) }
            : at;

    private Vector3 BaseFor(ObjectTransform at, (Vector3 Point, SnapTarget Kind, Vector3 Normal) target) =>
        target.Kind == SnapTarget.Surface
            ? Snapping.SurfaceBase(_target!, at, session.Snap.Base, target.Normal)
            : Snapping.BasePoint(_target!, at, session.Snap.Base, target.Point);

    /// <summary>
    /// What the cursor is on to snap to: the nearest corner or edge middle within reach on screen, and
    /// failing that the surface under the cursor. The moved thing, and anything that moves with it,
    /// are seen through.
    /// </summary>
    private (Vector3 Point, SnapTarget Kind, Vector3 Normal)? FindTarget(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        SnapSettings settings = session.Snap;
        Func<VoxelObject, bool> skip = MovesWithTarget;

        (Vector3 Point, SnapTarget Kind)? best = null;
        float nearest = SnapPixels;

        foreach ((Vector3 point, SnapTarget kind, _) in Snapping.TargetPoints(session.Scene, settings, skip))
        {
            if (camera.TryProjectToScreen(point, viewport, out Vector2 screen) && Vector2.Distance(screen, mouse) < nearest)
            {
                nearest = Vector2.Distance(screen, mouse);
                best = (point, kind);
            }
        }

        if (best is { } found)
        {
            return (found.Point, found.Kind, Vector3.Zero);
        }

        if (settings.Snaps(SnapTarget.Surface)
            && Snapping.TrySurface(session.Scene, camera.ScreenPointToRay(mouse, viewport), settings, skip, out Vector3 surface, out Vector3 normal, out _))
        {
            return (surface, SnapTarget.Surface, normal);
        }

        return null;
    }

    /// <summary>The moved thing itself, which a snap must see through.</summary>
    private bool MovesWithTarget(VoxelObject o) => ReferenceEquals(o, _target);

    /// <summary>Where the cursor's ray meets the plane the free ring moves in: through the gizmo, facing the camera as it was at the press.</summary>
    private Vector3? PlaneHit(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        Core.Raycast.Ray ray = camera.ScreenPointToRay(mouse, viewport);
        float facing = Vector3.Dot(ray.Direction, _planeNormal);
        if (MathF.Abs(facing) < 1e-5f)
        {
            return null;
        }

        float along = Vector3.Dot(_grabbed.Origin - ray.Origin, _planeNormal) / facing;
        return along > 0f ? ray.Origin + (ray.Direction * along) : null;
    }

    private void DragRotate(Vector2 mouse, Vector2 viewport, FlyCamera camera, bool snap)
    {
        float angle = ScreenAngle(_grabbed, mouse, viewport, camera);
        float degrees = (angle - _pressAngle) * (180f / MathF.PI);

        // Keep the reading in (-180, 180] so a drag past the top does not read as 350 degrees.
        degrees = ((degrees + 180f) % 360f + 360f) % 360f - 180f;

        if (snap)
        {
            degrees = ObjectTransform.SnapAngleDegrees(degrees, session.Snap.RotationIncrement);
        }

        // Screen Y runs down, so a clockwise drag has to turn the object the same way it looks.
        Quaternion delta = Quaternion.CreateFromAxisAngle(_grabbed.Direction, -degrees * (MathF.PI / 180f));
        session.ApplyTransform(_target!, _startTransform.RotatedAbout(_grabbed.Pivot, delta));

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
            string what = target is SceneLight ? "light" : "object";
            string name = _grabbed.Kind is GizmoKind.MoveAxis or GizmoKind.MoveFree ? $"Move {what}" : $"Rotate {what}";
            session.PushTransformEdit(target, _startTransform, name);
        }

        _target = null;
        SnapPoint = null;
        Readout = string.Empty;
    }

    public void Cancel()
    {
        if (_target is { } target)
        {
            session.ApplyTransform(target, _startTransform);
        }

        _target = null;
        SnapPoint = null;
        Readout = string.Empty;
    }

    /// <summary>The pivot a hinge drag is currently turning about, for drawing.</summary>
    public Vector3? ActivePivot(Vector2 mouse, Vector2 viewport, FlyCamera camera) =>
        _target is null || _grabbed.Kind is GizmoKind.MoveAxis or GizmoKind.MoveFree
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
