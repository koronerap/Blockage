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

    /// <summary>
    /// The selection's extent, in voxels. The component along the selection's own axis is always 1 —
    /// every face in it lies in the same plane — so the two that vary are the ones worth reading.
    /// </summary>
    public bool TryGetBounds(out Int3 min, out Int3 max)
    {
        min = default;
        max = default;

        bool any = false;
        foreach (Int3 voxel in _voxels)
        {
            min = any ? Int3.Min(min, voxel) : voxel;
            max = any ? Int3.Max(max, voxel) : voxel;
            any = true;
        }

        return any;
    }

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
    /// The exposed faces inside the ellipse that fits the rectangle between two corners, in the
    /// plane of <paramref name="direction"/> — Extrude's Ellipse: a round shape drawn straight onto
    /// the surface, to be pulled out or pushed in.
    /// </summary>
    public static FaceSelection Ellipse(VoxelWorld world, Face direction, int plane, Int3 cornerA, Int3 cornerB)
    {
        int axis = FaceInfo.Axis(direction);
        (int u, int v) = Across(axis);
        VoxelBox box = VoxelBox.FromCorners(
            VoxelBox.WithComponent(cornerA, axis, plane),
            VoxelBox.WithComponent(cornerB, axis, plane));

        float cu = (VoxelBox.Component(box.Min, u) + VoxelBox.Component(box.Max, u) + 1) * 0.5f;
        float cv = (VoxelBox.Component(box.Min, v) + VoxelBox.Component(box.Max, v) + 1) * 0.5f;
        float ru = (VoxelBox.Component(box.Max, u) - VoxelBox.Component(box.Min, u) + 1) * 0.5f;
        float rv = (VoxelBox.Component(box.Max, v) - VoxelBox.Component(box.Min, v) + 1) * 0.5f;

        var voxels = new HashSet<Int3>();
        for (int y = box.Min.Y; y <= box.Max.Y; y++)
        {
            for (int z = box.Min.Z; z <= box.Max.Z; z++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    var cell = new Int3(x, y, z);
                    float du = (VoxelBox.Component(cell, u) + 0.5f - cu) / ru;
                    float dv = (VoxelBox.Component(cell, v) + 0.5f - cv) / rv;
                    if ((du * du) + (dv * dv) <= 1f && IsFaceExposed(world, cell, direction))
                    {
                        voxels.Add(cell);
                    }
                }
            }
        }

        return new FaceSelection(direction, plane, voxels);
    }

    /// <summary>
    /// The exposed faces along a line from one cell to another, one voxel wide, in the plane of
    /// <paramref name="direction"/> — Extrude's Line.
    /// </summary>
    public static FaceSelection Line(VoxelWorld world, Face direction, int plane, Int3 from, Int3 to)
    {
        int axis = FaceInfo.Axis(direction);
        (int u, int v) = Across(axis);
        int u0 = VoxelBox.Component(from, u), v0 = VoxelBox.Component(from, v);
        int u1 = VoxelBox.Component(to, u), v1 = VoxelBox.Component(to, v);

        var voxels = new HashSet<Int3>();

        // Bresenham: every cell the line steps through, without gaps at a corner.
        int du = Math.Abs(u1 - u0), dv = -Math.Abs(v1 - v0);
        int su = u0 < u1 ? 1 : -1, sv = v0 < v1 ? 1 : -1;
        int error = du + dv;
        int cu = u0, cv = v0;
        for (int guard = 0; guard <= du - dv + 1; guard++)
        {
            Int3 cell = VoxelBox.WithComponent(VoxelBox.WithComponent(VoxelBox.WithComponent(Int3.Zero, axis, plane), u, cu), v, cv);
            if (IsFaceExposed(world, cell, direction))
            {
                voxels.Add(cell);
            }

            if (cu == u1 && cv == v1)
            {
                break;
            }

            int twice = 2 * error;
            if (twice >= dv)
            {
                error += dv;
                cu += su;
            }

            if (twice <= du)
            {
                error += du;
                cv += sv;
            }
        }

        return new FaceSelection(direction, plane, voxels);
    }

    /// <summary>The two axes that lie across a face whose normal is along <paramref name="axis"/>.</summary>
    private static (int U, int V) Across(int axis) => axis switch
    {
        0 => (1, 2),
        1 => (0, 2),
        _ => (0, 1),
    };

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

    /// <summary>
    /// The same selection without the faces the world no longer shows. After an undo the voxels a
    /// selection sat on may be gone, or buried; the faces left are the ones still there to pull.
    /// </summary>
    public FaceSelection Retain(VoxelWorld world)
    {
        var kept = new HashSet<Int3>(_voxels.Count);
        foreach (Int3 voxel in _voxels)
        {
            if (IsFaceExposed(world, voxel, Direction))
            {
                kept.Add(voxel);
            }
        }

        return kept.Count == _voxels.Count ? this : new FaceSelection(Direction, Plane, kept);
    }

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
