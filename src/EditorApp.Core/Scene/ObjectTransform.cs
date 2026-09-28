using System.Numerics;
using EditorApp.Core.Raycast;

namespace EditorApp.Core.Scene;

/// <summary>
/// Where an object sits in the world, and how big its voxels are there: a position, a rotation, and
/// the world size of one voxel.
///
/// The voxel size is a uniform scale and the only kind there is. The grid stays an integer lattice
/// in the object's own space — picking, meshing and editing all walk whole cells — and the size only
/// says how large a cell comes out in the world. A non-uniform scale would not be a lattice any more.
///
/// Rotation is about the object's local origin. Rotating about anything else is expressed by
/// <see cref="RotatedAbout"/>, which is what both the centre rings and the edge hinge need
/// (EditorApp.md, "Transform").
/// </summary>
/// <param name="VoxelSize">World units one voxel of this object measures. Always positive.</param>
public readonly record struct ObjectTransform(Vector3 Position, Quaternion Rotation, float VoxelSize = 1f)
{
    /// <summary>Smallest and largest world size a voxel may be given.</summary>
    public const float MinVoxelSize = 0.001f;

    public const float MaxVoxelSize = 1000f;

    public static readonly ObjectTransform Identity = new(Vector3.Zero, Quaternion.Identity);

    public static ObjectTransform At(Vector3 position) => new(position, Quaternion.Identity);

    /// <summary>A voxel size clamped to the allowed range, or null for one that is not a size at all.</summary>
    public static float? ValidVoxelSize(float size) =>
        float.IsFinite(size) && size > 0f ? Math.Clamp(size, MinVoxelSize, MaxVoxelSize) : null;

    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(VoxelSize)
        * Matrix4x4.CreateFromQuaternion(Rotation)
        * Matrix4x4.CreateTranslation(Position);

    /// <summary>A cell coordinate in the object's own space to a point in the world.</summary>
    public Vector3 TransformPoint(Vector3 local) =>
        Vector3.Transform(local * VoxelSize, Rotation) + Position;

    /// <summary>
    /// A direction, turned with the object — not a length, so the voxel size does not touch it. A
    /// face normal comes out a unit vector whatever size the voxels are.
    /// </summary>
    public Vector3 TransformDirection(Vector3 local) => Vector3.Transform(local, Rotation);

    public Vector3 InverseTransformPoint(Vector3 world) =>
        Vector3.Transform(world - Position, Quaternion.Conjugate(Rotation)) / VoxelSize;

    /// <summary>The counterpart of <see cref="TransformDirection"/>: turned only, never scaled.</summary>
    public Vector3 InverseTransformDirection(Vector3 world) =>
        Vector3.Transform(world, Quaternion.Conjugate(Rotation));

    /// <summary>
    /// Moves a world-space ray into the object's own space, so picking can use the plain
    /// axis-aligned grid walk no matter how the object is turned or how big its voxels are.
    ///
    /// The direction stays a unit vector, so distances along the local ray are in voxels: multiply
    /// by <see cref="VoxelSize"/> to compare them with anything measured in the world.
    /// </summary>
    public Ray InverseTransformRay(Ray world) => new(
        InverseTransformPoint(world.Origin),
        Vector3.Normalize(InverseTransformDirection(world.Direction)));

    /// <summary>Rotates about an arbitrary world-space pivot, carrying the position with it.</summary>
    public ObjectTransform RotatedAbout(Vector3 worldPivot, Quaternion delta)
    {
        Vector3 offset = Position - worldPivot;
        return this with
        {
            Position = worldPivot + Vector3.Transform(offset, delta),
            Rotation = Quaternion.Normalize(delta * Rotation),
        };
    }

    public ObjectTransform Translated(Vector3 delta) => this with { Position = Position + delta };

    /// <summary>
    /// Snap is on unless Shift is held (EditorApp.md, "Transform"): movement lands on whole voxels
    /// and rotation on a fixed angle step. Whole voxels of the object being moved, so an object with
    /// half-unit voxels moves in half units and stays on a lattice of its own size.
    /// </summary>
    public static Vector3 SnapPosition(Vector3 position, float step = 1f)
    {
        if (!(step > 0f))
        {
            step = 1f;
        }

        return new Vector3(
            Snap(position.X / step) * step,
            Snap(position.Y / step) * step,
            Snap(position.Z / step) * step);
    }

    public const float DefaultAngleStepDegrees = 15f;

    public static float SnapAngleDegrees(float degrees, float step = DefaultAngleStepDegrees) =>
        step <= 0f ? degrees : Snap(degrees / step) * step;

    // Away from zero at the midpoint, not to even: on a snap grid, 12.5 landing on 12 while 13.5
    // lands on 14 feels arbitrary from behind the cursor.
    private static float Snap(float value) => MathF.Round(value, MidpointRounding.AwayFromZero);
}
