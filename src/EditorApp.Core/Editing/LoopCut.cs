using System.Numerics;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// A grid boundary plane, in an object's local space. <see cref="Coordinate"/> is the first cell on
/// the far side, so the plane sits between <c>Coordinate - 1</c> and <c>Coordinate</c>.
/// </summary>
public readonly record struct CutPlane(Axis Axis, int Coordinate)
{
    public int AxisIndex => (int)Axis;
}

/// <summary>
/// Splits an object in two at an axis-aligned grid plane (EditorApp.md, "Loop Cut"). This is not
/// Blender's loop cut: no new topology is produced, the voxels are simply divided.
///
/// Both halves keep their original local coordinates and inherit the same world transform, so
/// nothing moves on screen at the moment of the cut — only afterwards, once each half is dragged
/// somewhere by the Transform tool.
/// </summary>
public static class LoopCut
{
    /// <summary>
    /// The grid boundary nearest a point in the object's own space, restricted to planes that
    /// genuinely divide it. Returns null for an object too thin to cut on any axis.
    /// </summary>
    public static CutPlane? FindNearestPlane(VoxelObject target, Vector3 localPoint)
    {
        if (!target.Grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return null;
        }

        CutPlane? best = null;
        float bestDistance = float.MaxValue;

        for (int axis = 0; axis < 3; axis++)
        {
            // A plane at the object's own outer edge would put nothing on one side of it.
            int lowest = VoxelBox.Component(min, axis) + 1;
            int highest = VoxelBox.Component(max, axis);
            if (lowest > highest)
            {
                continue;
            }

            float along = axis switch
            {
                0 => localPoint.X,
                1 => localPoint.Y,
                _ => localPoint.Z,
            };

            int candidate = Math.Clamp((int)MathF.Round(along), lowest, highest);
            float distance = MathF.Abs(along - candidate);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new CutPlane((Axis)axis, candidate);
            }
        }

        return best;
    }

    /// <summary>
    /// True when the plane actually has voxels on both sides. A plane through a hollow gap would
    /// otherwise produce an empty object.
    /// </summary>
    public static bool Divides(VoxelWorld grid, CutPlane plane)
    {
        (VoxelWorld low, VoxelWorld high) = Split(grid, plane);
        return low.SolidCount > 0 && high.SolidCount > 0;
    }

    /// <summary>
    /// Where the plane goes through the object: the cells just below it that have solid across the
    /// plane from them. This is the face the cut will open up, and the outline the preview draws — a
    /// frame round the whole object's bounds said which plane, but not what of the model it would cut.
    ///
    /// A plane can also run between parts that never touch, and then it cuts nothing but still
    /// divides them. For that the footprint of both sides is returned instead, which is where the
    /// object will come apart.
    /// </summary>
    public static HashSet<Int3> CrossSection(VoxelWorld grid, CutPlane plane)
    {
        int axis = plane.AxisIndex;
        int below = plane.Coordinate - 1;
        Int3 across = FaceInfo.Offset(Towards(axis));

        var touching = new HashSet<Int3>();
        var footprint = new HashSet<Int3>();

        // Chunk by chunk, so the cost follows what is there rather than the bounds' whole area.
        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            Int3 origin = coord.Origin;
            int start = VoxelBox.Component(origin, axis);

            // The chunks holding the layer below the plane, and the ones holding the layer above it.
            if (chunk.IsEmpty || below < start - 1 || below >= start + Chunk.Size)
            {
                continue;
            }

            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;

            for (int a = 0; a < Chunk.Size; a++)
            {
                for (int b = 0; b < Chunk.Size; b++)
                {
                    Int3 cell = VoxelBox.WithComponent(origin, axis, below);
                    cell = VoxelBox.WithComponent(cell, u, VoxelBox.Component(origin, u) + a);
                    cell = VoxelBox.WithComponent(cell, v, VoxelBox.Component(origin, v) + b);

                    bool near = grid.IsSolid(cell);
                    bool far = grid.IsSolid(cell + across);

                    if (near && far)
                    {
                        touching.Add(cell);
                    }
                    else if (near || far)
                    {
                        footprint.Add(cell);
                    }
                }
            }
        }

        if (touching.Count > 0)
        {
            return touching;
        }

        return footprint;
    }

    /// <summary>The face pointing up an axis.</summary>
    public static Face Towards(int axis) => (Face)(axis * 2);

    /// <summary>
    /// Splits the grid into the part below the plane and the part at or above it. Both keep the
    /// original coordinates, which is what lets the two halves share one transform.
    /// </summary>
    public static (VoxelWorld Low, VoxelWorld High) Split(VoxelWorld grid, CutPlane plane)
    {
        var low = new VoxelWorld();
        var high = new VoxelWorld();
        low.ReplacePalette(grid.Palette);
        high.ReplacePalette(grid.Palette);

        int axis = plane.AxisIndex;

        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            Int3 origin = coord.Origin;
            ReadOnlySpan<byte> indices = chunk.Indices;

            for (int linear = 0; linear < Chunk.VoxelCount; linear++)
            {
                byte index = indices[linear];
                if (index == Palette.EmptyIndex)
                {
                    continue;
                }

                Int3 position = origin + Chunk.FromLinearIndex(linear);
                VoxelWorld destination = VoxelBox.Component(position, axis) < plane.Coordinate ? low : high;
                destination.SetVoxel(position, index);
            }
        }

        return (low, high);
    }
}
