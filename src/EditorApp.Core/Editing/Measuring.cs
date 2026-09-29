using System.Globalization;
using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Editing;

/// <summary>A measurement between two points of the world (Fullreleaseplan 7.9), as Blender's ruler draws one.</summary>
public readonly record struct Ruler(Vector3 Start, Vector3 End)
{
    private const float Epsilon = 1e-3f;

    public float Length => Vector3.Distance(Start, End);

    /// <summary>How far it runs along each axis.</summary>
    public Vector3 Delta => Vector3.Abs(End - Start);

    /// <summary>
    /// What its label says: its length in world units — voxels of size 1 — and in metres at
    /// <paramref name="voxelsPerMetre"/>; and, when it runs along more than one axis, how far along each.
    /// </summary>
    public string Describe(float voxelsPerMetre)
    {
        string length = $"{Units(Length)} vx  -  {Metres(Length / MathF.Max(voxelsPerMetre, 0.0001f))}";
        Vector3 d = Delta;
        int axes = (d.X > Epsilon ? 1 : 0) + (d.Y > Epsilon ? 1 : 0) + (d.Z > Epsilon ? 1 : 0);
        return axes > 1 ? $"{length}\nX {Units(d.X)}   Y {Units(d.Y)}   Z {Units(d.Z)}" : length;
    }

    private static string Units(float value) =>
        value.ToString(MathF.Abs(value - MathF.Round(value)) < 0.005f ? "0" : "0.##", CultureInfo.InvariantCulture);

    private static string Metres(float metres) => metres switch
    {
        >= 1000f => (metres / 1000f).ToString("0.## 'km'", CultureInfo.InvariantCulture),
        < 1f => (metres * 100f).ToString("0.# 'cm'", CultureInfo.InvariantCulture),
        _ => metres.ToString("0.## 'm'", CultureInfo.InvariantCulture),
    };
}

/// <summary>Where the measure tool's rulers start and end.</summary>
public static class Measuring
{
    /// <summary>
    /// Where a ruler's end goes for what a ray met: the corner of a voxel nearest the point on its
    /// face, so a measurement runs between whole voxels — or, <paramref name="free"/>, that very point.
    /// </summary>
    public static Vector3 PointOn(ScenePick pick, Ray worldRay, bool free)
    {
        Vector3 met = worldRay.Origin + (Vector3.Normalize(worldRay.Direction) * pick.Distance);
        if (free)
        {
            return met;
        }

        ObjectTransform placed = pick.Object.Transform;
        Vector3 local = placed.InverseTransformPoint(met);
        return placed.TransformPoint(new Vector3(MathF.Round(local.X), MathF.Round(local.Y), MathF.Round(local.Z)));
    }

    /// <summary>
    /// With nothing under the pointer, where the ray meets the ground, on whole units — or, <paramref name="free"/>,
    /// just where it meets it. Null when it looks away from the ground.
    /// </summary>
    public static Vector3? OnGround(Ray worldRay, bool free)
    {
        if (MathF.Abs(worldRay.Direction.Y) < 1e-6f)
        {
            return null;
        }

        float along = -worldRay.Origin.Y / worldRay.Direction.Y;
        if (along <= 0f)
        {
            return null;
        }

        Vector3 met = worldRay.Origin + (worldRay.Direction * along);
        return free ? met with { Y = 0f } : new Vector3(MathF.Round(met.X), 0f, MathF.Round(met.Z));
    }
}
