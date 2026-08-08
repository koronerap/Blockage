using System.Numerics;
using EditorApp.Core.Raycast;

namespace EditorApp.Core.Scene;

/// <summary>
/// Where an object sits in the world. Rigid only — position and rotation, never scale: a scaled
/// voxel grid would stop lining up with the integer lattice everything else depends on.
///
/// Rotation is about the object's local origin. Rotating about anything else is expressed by
/// <see cref="RotatedAbout"/>, which is what both the centre rings and the edge hinge need
/// (EditorApp.md, "Transform").
/// </summary>
public readonly record struct ObjectTransform(Vector3 Position, Quaternion Rotation)
{
    public static readonly ObjectTransform Identity = new(Vector3.Zero, Quaternion.Identity);

    public static ObjectTransform At(Vector3 position) => new(position, Quaternion.Identity);

    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public Vector3 TransformPoint(Vector3 local) =>
        Vector3.Transform(local, Rotation) + Position;

    public Vector3 TransformDirection(Vector3 local) => Vector3.Transform(local, Rotation);

    public Vector3 InverseTransformPoint(Vector3 world) =>
        Vector3.Transform(world - Position, Quaternion.Conjugate(Rotation));

    public Vector3 InverseTransformDirection(Vector3 world) =>
        Vector3.Transform(world, Quaternion.Conjugate(Rotation));

    /// <summary>
    /// Moves a world-space ray into the object's own space, so picking can use the plain
    /// axis-aligned grid walk no matter how the object is turned.
    /// </summary>
    public Ray InverseTransformRay(Ray world) => new(
        InverseTransformPoint(world.Origin),
        Vector3.Normalize(InverseTransformDirection(world.Direction)));

    /// <summary>Rotates about an arbitrary world-space pivot, carrying the position with it.</summary>
    public ObjectTransform RotatedAbout(Vector3 worldPivot, Quaternion delta)
    {
        Vector3 offset = Position - worldPivot;
        return new ObjectTransform(
            worldPivot + Vector3.Transform(offset, delta),
            Quaternion.Normalize(delta * Rotation));
    }

    public ObjectTransform Translated(Vector3 delta) => this with { Position = Position + delta };

    /// <summary>
    /// Snap is on unless Shift is held (EditorApp.md, "Transform"): movement lands on whole voxels
    /// and rotation on a fixed angle step.
    /// </summary>
    public static Vector3 SnapPosition(Vector3 position) => new(
        Snap(position.X),
        Snap(position.Y),
        Snap(position.Z));

    public const float DefaultAngleStepDegrees = 15f;

    public static float SnapAngleDegrees(float degrees, float step = DefaultAngleStepDegrees) =>
        step <= 0f ? degrees : Snap(degrees / step) * step;

    // Away from zero at the midpoint, not to even: on a snap grid, 12.5 landing on 12 while 13.5
    // lands on 14 feels arbitrary from behind the cursor.
    private static float Snap(float value) => MathF.Round(value, MidpointRounding.AwayFromZero);
}
