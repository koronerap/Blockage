using EditorApp.Core.Commands;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Edits that act on a whole selection at once (EditorApp.md §9, M5). Every one of them writes
/// through a <see cref="VoxelEditCommand"/>, so a region edit of any size is a single undo step —
/// which is exactly the case the cell-based undo budget exists for (§8).
/// </summary>
public static class RegionOperations
{
    /// <summary>Sets every cell in the box, empty ones included.</summary>
    public static int Fill(VoxelWorld world, VoxelBox box, byte paletteIndex, VoxelEditCommand command)
    {
        int changed = 0;
        for (int y = box.Min.Y; y <= box.Max.Y; y++)
        {
            for (int z = box.Min.Z; z <= box.Max.Z; z++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    if (command.Apply(world, new Int3(x, y, z), paletteIndex))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    public static int Delete(VoxelWorld world, VoxelBox box, VoxelEditCommand command) =>
        Fill(world, box, Palette.EmptyIndex, command);

    /// <summary>Recolors the solid cells in the box, leaving the shape alone.</summary>
    public static int Paint(VoxelWorld world, VoxelBox box, byte paletteIndex, VoxelEditCommand command)
    {
        int changed = 0;
        for (int y = box.Min.Y; y <= box.Max.Y; y++)
        {
            for (int z = box.Min.Z; z <= box.Max.Z; z++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    var cell = new Int3(x, y, z);
                    if (world.GetVoxel(cell) != Palette.EmptyIndex && command.Apply(world, cell, paletteIndex))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Mirrors the contents of the box in place across its own centre. Reads the whole region first,
    /// so a mirror never reads cells it has already overwritten.
    /// </summary>
    public static int Mirror(VoxelWorld world, VoxelBox box, Axis axis, VoxelEditCommand command)
    {
        VoxelClip source = VoxelClip.Copy(world, box);
        VoxelClip mirrored = source.Mirrored(axis);
        return mirrored.Paste(world, box.Min, command, skipEmpty: false);
    }

    /// <summary>
    /// Moves the region by a delta: the source box is cleared and the contents written at the
    /// offset. Overlapping source and destination is handled because the read happens up front.
    /// </summary>
    public static int Move(VoxelWorld world, VoxelBox box, Int3 delta, VoxelEditCommand command)
    {
        if (delta == Int3.Zero)
        {
            return 0;
        }

        VoxelClip clip = VoxelClip.Copy(world, box);
        int changed = Delete(world, box, command);
        changed += clip.Paste(world, box.Min + delta, command, skipEmpty: false);
        return changed;
    }

    /// <summary>
    /// Pulls the box's face slab outward by <paramref name="layers"/>, copying it each step. A
    /// negative count intrudes instead, deleting that many layers inward.
    /// </summary>
    public static int Extrude(VoxelWorld world, VoxelBox box, Face face, int layers, VoxelEditCommand command)
    {
        if (layers == 0)
        {
            return 0;
        }

        Int3 step = FaceInfo.Offset(face);

        if (layers < 0)
        {
            int removed = 0;
            for (int layer = 0; layer < -layers; layer++)
            {
                // Peel from the outside in: the outermost remaining slab each time.
                VoxelBox slab = box.FaceSlab(face).Translate(step * -layer);
                removed += Delete(world, slab, command);
            }

            return removed;
        }

        VoxelClip slabClip = VoxelClip.Copy(world, box.FaceSlab(face));
        VoxelBox sourceSlab = box.FaceSlab(face);

        int changed = 0;
        for (int layer = 1; layer <= layers; layer++)
        {
            changed += slabClip.Paste(world, sourceSlab.Min + step * layer, command, skipEmpty: true);
        }

        return changed;
    }

    /// <summary>
    /// Extrudes the connected run of same-colored voxels whose <paramref name="face"/> is exposed
    /// and coplanar with the seed — the "pull this surface out one step" gesture. Returns the number
    /// of cells written.
    /// </summary>
    public static int ExtrudeSurface(
        VoxelWorld world,
        Int3 seed,
        Face face,
        int layers,
        VoxelEditCommand command,
        int limit = 200_000)
    {
        byte color = world.GetVoxel(seed);
        if (color == Palette.EmptyIndex || layers == 0)
        {
            return 0;
        }

        List<Int3> surface = CollectSurface(world, seed, face, color, limit);
        Int3 step = FaceInfo.Offset(face);

        int changed = 0;
        if (layers > 0)
        {
            for (int layer = 1; layer <= layers; layer++)
            {
                foreach (Int3 cell in surface)
                {
                    if (command.Apply(world, cell + step * layer, color))
                    {
                        changed++;
                    }
                }
            }
        }
        else
        {
            for (int layer = 0; layer < -layers; layer++)
            {
                foreach (Int3 cell in surface)
                {
                    if (command.Apply(world, cell - step * layer, Palette.EmptyIndex))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Flood fills across the plane of the seed's face, keeping only cells of the same color whose
    /// face in that direction is also exposed. Four-way connected within the plane.
    /// </summary>
    private static List<Int3> CollectSurface(VoxelWorld world, Int3 seed, Face face, byte color, int limit)
    {
        int axis = FaceInfo.Axis(face);
        Int3 step = FaceInfo.Offset(face);

        var surface = new List<Int3>();
        var visited = new HashSet<Int3> { seed };
        var queue = new Queue<Int3>();
        queue.Enqueue(seed);

        while (queue.Count > 0 && surface.Count < limit)
        {
            Int3 cell = queue.Dequeue();
            if (world.GetVoxel(cell) != color || world.IsSolid(cell + step))
            {
                continue;
            }

            surface.Add(cell);

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

        return surface;
    }
}
