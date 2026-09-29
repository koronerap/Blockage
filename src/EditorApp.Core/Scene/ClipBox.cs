using System.Numerics;

namespace EditorApp.Core.Scene;

/// <summary>
/// The section box (Fullreleaseplan 7.2): a box in the world outside which nothing is drawn or
/// picked — the ceiling and the wall in front taken away, to work inside a room.
/// </summary>
public readonly record struct ClipBox(Vector3 Min, Vector3 Max)
{
    public bool Contains(Vector3 point) =>
        point.X >= Min.X && point.X <= Max.X
        && point.Y >= Min.Y && point.Y <= Max.Y
        && point.Z >= Min.Z && point.Z <= Max.Z;

    /// <summary>Whether a cell of an object is inside, by its middle.</summary>
    public bool Contains(ObjectTransform transform, int x, int y, int z) =>
        Contains(transform.TransformPoint(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)));
}
