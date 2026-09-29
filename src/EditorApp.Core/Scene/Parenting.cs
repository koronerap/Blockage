using System.Numerics;

namespace EditorApp.Core.Scene;

/// <summary>
/// How a child is held by its parent: a rigid offset — where it sits and how it is turned, in the
/// parent's own frame. Rigid only: a child keeps its own voxel size, since a voxel's size is what an
/// object is made of rather than a scale it is drawn at.
/// </summary>
public static class Parenting
{
    /// <summary>Where <paramref name="child"/> is, seen from <paramref name="parent"/>.</summary>
    public static ObjectTransform Relative(ObjectTransform parent, ObjectTransform child)
    {
        Quaternion inverse = Quaternion.Conjugate(parent.Rotation);
        return new ObjectTransform(
            Vector3.Transform(child.Position - parent.Position, inverse),
            Quaternion.Normalize(inverse * child.Rotation),
            child.VoxelSize);
    }

    /// <summary>Where a child held at <paramref name="offset"/> is in the world, its parent being at <paramref name="parent"/>.</summary>
    public static ObjectTransform Compose(ObjectTransform parent, ObjectTransform offset, float childVoxelSize) => new(
        parent.Position + Vector3.Transform(offset.Position, parent.Rotation),
        Quaternion.Normalize(parent.Rotation * offset.Rotation),
        childVoxelSize);
}
