using System.Numerics;

namespace EditorApp.Core.Raycast;

/// <summary>A ray in world space. <paramref name="Direction"/> is expected to be normalized.</summary>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    public Vector3 PointAt(float distance) => Origin + Direction * distance;

    public static Ray Normalized(Vector3 origin, Vector3 direction) =>
        new(origin, Vector3.Normalize(direction));
}
