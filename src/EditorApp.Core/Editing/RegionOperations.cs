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
/// Reads and writes both go through the command's target grid, so the two cannot end up pointing at
/// different objects.
/// </summary>
public static class RegionOperations
{
    /// <summary>Sets every cell in the box, empty ones included.</summary>
    public static int Fill(VoxelBox box, byte paletteIndex, VoxelEditCommand command)
    {
        int changed = 0;
        for (int y = box.Min.Y; y <= box.Max.Y; y++)
        {
            for (int z = box.Min.Z; z <= box.Max.Z; z++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    if (command.Apply(new Int3(x, y, z), paletteIndex))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    public static int Delete(VoxelBox box, VoxelEditCommand command) =>
        Fill(box, Palette.EmptyIndex, command);

    /// <summary>Recolors the solid cells in the box, leaving the shape alone.</summary>
    public static int Paint(VoxelBox box, byte paletteIndex, VoxelEditCommand command)
    {
        int changed = 0;
        for (int y = box.Min.Y; y <= box.Max.Y; y++)
        {
            for (int z = box.Min.Z; z <= box.Max.Z; z++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    var cell = new Int3(x, y, z);
                    if (command.Target.GetVoxel(cell) != Palette.EmptyIndex && command.Apply(cell, paletteIndex))
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
    public static int Mirror(VoxelBox box, Axis axis, VoxelEditCommand command)
    {
        VoxelClip source = VoxelClip.Copy(command.Target, box);
        return source.Mirrored(axis).Paste(box.Min, command, skipEmpty: false);
    }

    /// <summary>
    /// Moves the region by a delta: the source box is cleared and the contents written at the
    /// offset. Overlapping source and destination is handled because the read happens up front.
    /// </summary>
    public static int Move(VoxelBox box, Int3 delta, VoxelEditCommand command)
    {
        if (delta == Int3.Zero)
        {
            return 0;
        }

        VoxelClip clip = VoxelClip.Copy(command.Target, box);
        int changed = Delete(box, command);
        changed += clip.Paste(box.Min + delta, command, skipEmpty: false);
        return changed;
    }
}
