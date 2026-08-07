using System.Numerics;

namespace EditorApp.Rendering;

/// <summary>
/// The six clip planes of a view-projection matrix, used to skip chunks that cannot be on screen
/// (EditorApp.md §5). Occlusion culling is deliberately out of scope.
/// </summary>
public struct Frustum
{
    private Plane _left;
    private Plane _right;
    private Plane _bottom;
    private Plane _top;
    private Plane _near;
    private Plane _far;

    public static Frustum FromViewProjection(Matrix4x4 m)
    {
        var frustum = new Frustum
        {
            _left = Plane.Normalize(new Plane(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)),
            _right = Plane.Normalize(new Plane(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)),
            _bottom = Plane.Normalize(new Plane(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)),
            _top = Plane.Normalize(new Plane(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)),
            _near = Plane.Normalize(new Plane(m.M13, m.M23, m.M33, m.M43)),
            _far = Plane.Normalize(new Plane(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43)),
        };

        return frustum;
    }

    /// <summary>False only when the box is entirely outside one plane — the cheap conservative test.</summary>
    public readonly bool Intersects(Vector3 min, Vector3 max) =>
        IsInside(_left, min, max)
        && IsInside(_right, min, max)
        && IsInside(_bottom, min, max)
        && IsInside(_top, min, max)
        && IsInside(_near, min, max)
        && IsInside(_far, min, max);

    private static bool IsInside(Plane plane, Vector3 min, Vector3 max)
    {
        // Test the box corner furthest along the plane normal; if even that is behind, all are.
        var positive = new Vector3(
            plane.Normal.X >= 0 ? max.X : min.X,
            plane.Normal.Y >= 0 ? max.Y : min.Y,
            plane.Normal.Z >= 0 ? max.Z : min.Z);

        return Plane.DotCoordinate(plane, positive) >= 0f;
    }
}
