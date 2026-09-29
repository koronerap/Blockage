using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>What a snapped move may land on — Blender's "Snap To", as far as it means anything here.</summary>
[Flags]
public enum SnapTarget
{
    None = 0,

    /// <summary>Whole voxels: steps of the moved object's own voxel size.</summary>
    Increment = 1,

    /// <summary>The corners of other objects' boxes — Blender's vertex snapping, on the boxes a voxel model is made of.</summary>
    Corner = 2,

    /// <summary>The middles of other objects' box edges.</summary>
    EdgeCentre = 4,

    /// <summary>The face under the cursor: the moved thing is set down on it.</summary>
    Surface = 8,
}

/// <summary>Which point of the moved thing is brought to the target — Blender's "Snap Base".</summary>
public enum SnapBase
{
    /// <summary>Its corner nearest the target; on a surface, the middle of its side that faces it.</summary>
    Closest,

    /// <summary>The middle of its box.</summary>
    Center,

    /// <summary>Its origin, which the rotate rings do not turn about but the file places it by.</summary>
    Origin,
}

/// <summary>
/// How snapping behaves, from the header's magnet and the popover beside it. Off by default: a drag
/// goes where the mouse goes, and Shift held during it turns snapping on — or off, when the magnet
/// is lit. Shared by the desktop and the phone.
/// </summary>
public sealed class SnapSettings
{
    public const float MinRotationIncrement = 1f;
    public const float MaxRotationIncrement = 90f;

    /// <summary>The magnet: whether a drag snaps without Shift held.</summary>
    public bool Enabled { get; set; }

    public SnapTarget Targets { get; set; } = SnapTarget.Increment;

    /// <summary>
    /// Increments land on the world's lattice rather than counting steps from where the drag began —
    /// Blender's "Absolute Grid Snap". On, as the editor has always snapped.
    /// </summary>
    public bool AbsoluteGrid { get; set; } = true;

    public SnapBase Base { get; set; } = SnapBase.Closest;

    /// <summary>Locked objects are not snapped to — Blender's "Exclude Non-Selectable".</summary>
    public bool ExcludeLocked { get; set; } = true;

    /// <summary>Set down on a surface, the moved thing turns so its up points out of the surface.</summary>
    public bool AlignToSurface { get; set; }

    public bool AffectMove { get; set; } = true;

    public bool AffectRotate { get; set; } = true;

    /// <summary>What a snapped rotation steps by, in degrees.</summary>
    public float RotationIncrement
    {
        get => _rotationIncrement;
        set => _rotationIncrement = float.IsFinite(value) ? Math.Clamp(value, MinRotationIncrement, MaxRotationIncrement) : _rotationIncrement;
    }

    private float _rotationIncrement = ObjectTransform.DefaultAngleStepDegrees;

    public bool Snaps(SnapTarget target) => (Targets & target) != 0;

    /// <summary>Switches one target on or off.</summary>
    public void Set(SnapTarget target, bool on) => Targets = on ? Targets | target : Targets & ~target;

    public void CopyFrom(SnapSettings other)
    {
        Enabled = other.Enabled;
        Targets = other.Targets;
        AbsoluteGrid = other.AbsoluteGrid;
        Base = other.Base;
        ExcludeLocked = other.ExcludeLocked;
        AlignToSurface = other.AlignToSurface;
        AffectMove = other.AffectMove;
        AffectRotate = other.AffectRotate;
        RotationIncrement = other.RotationIncrement;
    }
}

/// <summary>
/// The geometry snapping works on: the boxes things occupy, where their corners and edge middles
/// are, and the side of a box that faces a surface. Screen distances — which target the cursor is
/// nearest — are the head's business; this is only the world.
/// </summary>
public static class Snapping
{
    /// <summary>
    /// What a placeable thing occupies at a transform, in its own space: an object's box in voxels,
    /// a light's single point.
    /// </summary>
    public static (Vector3 Min, Vector3 Max) LocalBox(IPlaceable thing) =>
        thing is VoxelObject o && o.TryGetLocalBounds(out Vector3 min, out Vector3 max)
            ? (min, max)
            : (Vector3.Zero, Vector3.Zero);

    /// <summary>The eight corners of a thing's box, in the world, placed by <paramref name="at"/>.</summary>
    public static Vector3[] Corners(IPlaceable thing, ObjectTransform at) => Corners(LocalBox(thing), at);

    /// <summary>The eight corners of a box in some thing's own space, in the world, placed by <paramref name="at"/>.</summary>
    public static Vector3[] Corners((Vector3 Min, Vector3 Max) box, ObjectTransform at)
    {
        (Vector3 min, Vector3 max) = box;
        var corners = new Vector3[8];

        for (int i = 0; i < 8; i++)
        {
            var local = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            corners[i] = at.TransformPoint(local);
        }

        return corners;
    }

    /// <summary>The middles of a thing's twelve box edges, in the world.</summary>
    public static Vector3[] EdgeCentres(IPlaceable thing, ObjectTransform at)
    {
        Vector3[] corners = Corners(thing, at);
        var centres = new List<Vector3>(12);

        // Corners differing in exactly one bit share an edge.
        for (int a = 0; a < 8; a++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                int b = a | bit;
                if (b != a)
                {
                    centres.Add((corners[a] + corners[b]) * 0.5f);
                }
            }
        }

        return [.. centres];
    }

    /// <summary>The middle of a thing's box, in the world.</summary>
    public static Vector3 Centre(IPlaceable thing, ObjectTransform at) => Centre(LocalBox(thing), at);

    public static Vector3 Centre((Vector3 Min, Vector3 Max) box, ObjectTransform at) =>
        at.TransformPoint((box.Min + box.Max) * 0.5f);

    /// <summary>
    /// The middle of the side of a thing's box that faces into a surface — the side it would stand
    /// on, set down on a surface whose outward normal is <paramref name="normal"/>.
    /// </summary>
    public static Vector3 ContactPoint(IPlaceable thing, ObjectTransform at, Vector3 normal) =>
        ContactPoint(LocalBox(thing), at, normal);

    public static Vector3 ContactPoint((Vector3 Min, Vector3 Max) box, ObjectTransform at, Vector3 normal)
    {
        (Vector3 min, Vector3 max) = box;
        Vector3 centre = (min + max) * 0.5f;
        Vector3 half = (max - min) * 0.5f;

        // In the thing's own space, the side most opposed to the surface's outward normal.
        Vector3 local = at.InverseTransformDirection(normal);
        Vector3 away = MathF.Abs(local.X) >= MathF.Abs(local.Y) && MathF.Abs(local.X) >= MathF.Abs(local.Z)
            ? new Vector3(-MathF.Sign(local.X) * half.X, 0f, 0f)
            : MathF.Abs(local.Y) >= MathF.Abs(local.Z)
                ? new Vector3(0f, -MathF.Sign(local.Y) * half.Y, 0f)
                : new Vector3(0f, 0f, -MathF.Sign(local.Z) * half.Z);

        return at.TransformPoint(centre + away);
    }

    /// <summary>The point of the moved thing that is brought to a target point.</summary>
    public static Vector3 BasePoint(IPlaceable thing, ObjectTransform at, SnapBase snapBase, Vector3 target) =>
        BasePoint(LocalBox(thing), at, snapBase, target);

    /// <summary>
    /// The same for a box in the moved thing's own space — the box round the whole of a selection,
    /// when several things move as one.
    /// </summary>
    public static Vector3 BasePoint((Vector3 Min, Vector3 Max) box, ObjectTransform at, SnapBase snapBase, Vector3 target) => snapBase switch
    {
        SnapBase.Center => Centre(box, at),
        SnapBase.Origin => at.Position,
        _ => Corners(box, at).MinBy(c => Vector3.DistanceSquared(c, target)),
    };

    /// <summary>The point of the moved thing that is set down on a surface.</summary>
    public static Vector3 SurfaceBase(IPlaceable thing, ObjectTransform at, SnapBase snapBase, Vector3 normal) =>
        SurfaceBase(LocalBox(thing), at, snapBase, normal);

    public static Vector3 SurfaceBase((Vector3 Min, Vector3 Max) box, ObjectTransform at, SnapBase snapBase, Vector3 normal) => snapBase switch
    {
        SnapBase.Center => Centre(box, at),
        SnapBase.Origin => at.Position,
        _ => ContactPoint(box, at, normal),
    };

    /// <summary>
    /// The box round several things, in the first one's own space as it stands at
    /// <paramref name="frame"/>: what they snap by when they are moved together.
    /// </summary>
    public static (Vector3 Min, Vector3 Max) GroupBox(IPlaceable first, ObjectTransform frame, IEnumerable<(IPlaceable Thing, ObjectTransform At)> others)
    {
        (Vector3 min, Vector3 max) = LocalBox(first);

        foreach ((IPlaceable thing, ObjectTransform at) in others)
        {
            foreach (Vector3 corner in Corners(thing, at))
            {
                Vector3 local = frame.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }

        return (min, max);
    }

    /// <summary>A position rounded to the world lattice of <paramref name="step"/>.</summary>
    public static Vector3 ToGrid(Vector3 position, float step) => ObjectTransform.SnapPosition(position, step);

    /// <summary>
    /// The turn that stands a thing up on a surface: its up carried to the surface's outward normal,
    /// by the shortest way round, on top of the turn it already had.
    /// </summary>
    public static Quaternion Standing(Quaternion rotation, Vector3 normal)
    {
        Vector3 up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rotation));
        Vector3 n = Vector3.Normalize(normal);
        float dot = Vector3.Dot(up, n);

        if (dot > 0.9999f)
        {
            return rotation;
        }

        Quaternion turn = dot < -0.9999f
            ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Transform(Vector3.UnitX, rotation)), MathF.PI)
            : Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(up, n)), MathF.Acos(Math.Clamp(dot, -1f, 1f)));

        return Quaternion.Normalize(turn * rotation);
    }

    /// <summary>
    /// Every point that could be snapped to on the other objects, by kind: the corners and edge
    /// middles of their boxes. Hidden objects are never targets; locked ones only when asked for.
    /// </summary>
    public static IEnumerable<(Vector3 Point, SnapTarget Kind, VoxelObject Owner)> TargetPoints(
        VoxelScene scene,
        SnapSettings settings,
        Func<VoxelObject, bool> skip)
    {
        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible || o.IsEmpty || skip(o) || (settings.ExcludeLocked && o.Locked))
            {
                continue;
            }

            if (settings.Snaps(SnapTarget.Corner))
            {
                foreach (Vector3 corner in Corners(o, o.Transform))
                {
                    yield return (corner, SnapTarget.Corner, o);
                }
            }

            if (settings.Snaps(SnapTarget.EdgeCentre))
            {
                foreach (Vector3 centre in EdgeCentres(o, o.Transform))
                {
                    yield return (centre, SnapTarget.EdgeCentre, o);
                }
            }
        }
    }

    /// <summary>The surface a ray meets on anything but the moved thing, with its outward normal in the world.</summary>
    public static bool TrySurface(
        VoxelScene scene,
        Ray ray,
        SnapSettings settings,
        Func<VoxelObject, bool> skip,
        out Vector3 point,
        out Vector3 normal,
        out VoxelObject? owner)
    {
        point = normal = default;
        owner = null;

        if (!scene.TryPick(ray, out ScenePick pick, includeLocked: !settings.ExcludeLocked, skip: skip))
        {
            return false;
        }

        point = ray.Origin + (ray.Direction * pick.Distance);
        normal = Vector3.Normalize(pick.Object.Transform.TransformDirection(FaceInfo.Normal(pick.Hit.Face)));
        owner = pick.Object;
        return true;
    }
}
