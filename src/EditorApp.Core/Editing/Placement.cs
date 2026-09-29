using System.Numerics;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>Where a new object goes: against the surface it was added at, on a lattice.</summary>
public static class Placement
{
    /// <summary>
    /// The placement that sets <paramref name="grid"/> down at <paramref name="point"/> on a surface
    /// facing <paramref name="normal"/>: on it when the surface faces up, against it when it faces
    /// sideways, under it when it faces down — centred on the point across the surface either way.
    /// Then onto the lattice of its own voxels, so it lines up with the blocks it is put beside.
    /// </summary>
    public static ObjectTransform Against(VoxelWorld grid, Vector3 point, Vector3 normal, float voxelSize)
    {
        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return new ObjectTransform(Snap(point, voxelSize), Quaternion.Identity, voxelSize);
        }

        Vector3 low = min.ToVector3() * voxelSize;
        Vector3 high = (max + Int3.One).ToVector3() * voxelSize;
        Vector3 position = point - ((low + high) * 0.5f);

        Vector3 along = Vector3.Abs(normal);
        if (along.Y >= along.X && along.Y >= along.Z)
        {
            position.Y = normal.Y >= 0f ? point.Y - low.Y : point.Y - high.Y;
        }
        else if (along.X >= along.Z)
        {
            position.X = normal.X >= 0f ? point.X - low.X : point.X - high.X;
        }
        else
        {
            position.Z = normal.Z >= 0f ? point.Z - low.Z : point.Z - high.Z;
        }

        return new ObjectTransform(Snap(position, voxelSize), Quaternion.Identity, voxelSize);
    }

    /// <summary>Each coordinate to the nearest whole voxel — and never to -0, which a field would show as "-0".</summary>
    private static Vector3 Snap(Vector3 position, float size) => new(
        (MathF.Round(position.X / size) * size) + 0f,
        (MathF.Round(position.Y / size) * size) + 0f,
        (MathF.Round(position.Z / size) * size) + 0f);
}
