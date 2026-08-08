using EditorApp.Core.Commands;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Whole-region edits, used as machinery by the tools rather than exposed as tools of their own.
/// Loop Cut and Extrude's Create sub-mode both need to lift and clear a region wholesale.
///
/// Extrusion deliberately does not live here — <see cref="ExtrudeOperation"/> is the one
/// implementation. A second one would be exactly the duplicate mental model the spec rules out for
/// Place and Erase.
///
/// Every operation writes through a <see cref="VoxelEditCommand"/>, so a region edit of any size is
/// a single undo step, which is the case the cell-based undo budget exists for.
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
}
