using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// A set of voxel faces that all point the same way and lie in the same plane — what Extrude
/// operates on (EditorApp.md, "Extrude"). Only faces that are actually exposed can be selected:
/// an interior face has nothing to pull.
///
/// Keeping the direction and plane on the selection itself is what lets the arrow have one
/// unambiguous axis, and what makes adding to and subtracting from a selection well defined.
/// </summary>
public sealed class FaceSelection
{
    private readonly HashSet<Int3> _voxels;

    private FaceSelection(Face direction, int plane, HashSet<Int3> voxels)
    {
        Direction = direction;
        Plane = plane;
        _voxels = voxels;
    }

    public Face Direction { get; }

    /// <summary>Coordinate along the face axis that every selected voxel shares.</summary>
    public int Plane { get; }

    public IReadOnlyCollection<Int3> Voxels => _voxels;

    public int Count => _voxels.Count;

    public bool IsEmpty => _voxels.Count == 0;

    public int Axis => FaceInfo.Axis(Direction);

    public static FaceSelection Empty(Face direction, int plane) => new(direction, plane, []);

    /// <summary>True when a voxel is solid and its face in this direction is exposed.</summary>
    public static bool IsFaceExposed(VoxelWorld world, Int3 voxel, Face direction) =>
        world.IsSolid(voxel) && !world.IsSolid(voxel + FaceInfo.Offset(direction));

    /// <summary>
    /// Every exposed face inside the rectangle spanned by two cells, flattened onto the plane of
    /// <paramref name="direction"/>. The corners come from the drag's start and current cells.
    /// </summary>
    public static FaceSelection Box(VoxelWorld world, Face direction, int plane, Int3 cornerA, Int3 cornerB)
    {
        int axis = FaceInfo.Axis(direction);
        VoxelBox box = VoxelBox.FromCorners(
            VoxelBox.WithComponent(cornerA, axis, plane),
            VoxelBox.WithComponent(cornerB, axis, plane));

        var voxels = new HashSet<Int3>();
        for (int y = box.Min.Y; y <= box.Max.Y; y++)
        {
            for (int z = box.Min.Z; z <= box.Max.Z; z++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    var cell = new Int3(x, y, z);
                    if (IsFaceExposed(world, cell, direction))
                    {
                        voxels.Add(cell);
                    }
                }
            }
        }

        return new FaceSelection(direction, plane, voxels);
    }

    /// <summary>
    /// The whole connected patch of exposed faces reachable from a seed, staying in the seed's
    /// plane. This is Extrude's Face sub-mode: one click takes a flat surface entire.
    /// </summary>
    public static FaceSelection ConnectedPatch(VoxelWorld world, Int3 seed, Face direction, int limit = 500_000)
    {
        int axis = FaceInfo.Axis(direction);
        int plane = VoxelBox.Component(seed, axis);

        var voxels = new HashSet<Int3>();
        if (!IsFaceExposed(world, seed, direction))
        {
            return new FaceSelection(direction, plane, voxels);
        }

        var queue = new Queue<Int3>();
        var visited = new HashSet<Int3> { seed };
        queue.Enqueue(seed);

        while (queue.Count > 0 && voxels.Count < limit)
        {
            Int3 cell = queue.Dequeue();
            if (!IsFaceExposed(world, cell, direction))
            {
                continue;
            }

            voxels.Add(cell);

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                var neighbourFace = (Face)f;
                if (FaceInfo.Axis(neighbourFace) == axis)
                {
                    continue;   // stay in the plane
                }

                Int3 neighbour = cell + FaceInfo.Offset(neighbourFace);
                if (visited.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        return new FaceSelection(direction, plane, voxels);
    }

    /// <summary>
    /// Combines two selections. A selection that faces a different way or sits in a different plane
    /// replaces the old one outright — there is no sensible arrow for a mixed set.
    /// </summary>
    public FaceSelection Combine(FaceSelection other, SelectionOperation operation)
    {
        if (operation == SelectionOperation.Replace || other.Direction != Direction || other.Plane != Plane)
        {
            return other;
        }

        var voxels = new HashSet<Int3>(_voxels);
        if (operation == SelectionOperation.Add)
        {
            voxels.UnionWith(other._voxels);
        }
        else
        {
            voxels.ExceptWith(other._voxels);
        }

        return new FaceSelection(Direction, Plane, voxels);
    }

    /// <summary>The same faces shifted along the face axis — where the selection lands after a step.</summary>
    public FaceSelection Translated(int steps)
    {
        if (steps == 0)
        {
            return this;
        }

        Int3 delta = FaceInfo.Offset(Direction) * steps;
        var voxels = new HashSet<Int3>(_voxels.Count);
        foreach (Int3 voxel in _voxels)
        {
            voxels.Add(voxel + delta);
        }

        return new FaceSelection(Direction, Plane + (FaceInfo.IsPositive(Direction) ? steps : -steps), voxels);
    }

    public bool Contains(Int3 voxel) => _voxels.Contains(voxel);

    public VoxelBox Bounds()
    {
        if (_voxels.Count == 0)
        {
            return VoxelBox.Single(Int3.Zero);
        }

        Int3 min = new(int.MaxValue, int.MaxValue, int.MaxValue);
        Int3 max = new(int.MinValue, int.MinValue, int.MinValue);

        foreach (Int3 voxel in _voxels)
        {
            min = Int3.Min(min, voxel);
            max = Int3.Max(max, voxel);
        }

        return new VoxelBox(min, max);
    }

    /// <summary>
    /// Where the drag arrow starts: the middle of the selected faces, on the outer surface rather
    /// than at the centre of the voxels.
    /// </summary>
    public Vector3 ArrowOrigin()
    {
        if (_voxels.Count == 0)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        foreach (Int3 voxel in _voxels)
        {
            sum += voxel.ToVector3() + new Vector3(0.5f);
        }

        Vector3 centre = sum / _voxels.Count;

        // Push from the voxel centre onto the face itself.
        return centre + FaceInfo.Normal(Direction) * 0.5f;
    }
}
