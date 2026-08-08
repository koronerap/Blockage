using EditorApp.Core.Commands;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// The only way volume is created or destroyed (EditorApp.md, "Araçlar"). Pulling a face selection
/// outward adds voxels in the source voxel's own colour; pushing it in deletes them.
/// </summary>
public static class ExtrudeOperation
{
    /// <summary>
    /// Applies a whole-number number of steps at once. Returns the number of cells changed.
    /// </summary>
    public static int Apply(FaceSelection selection, int steps, VoxelEditCommand command)
    {
        if (steps == 0 || selection.IsEmpty)
        {
            return 0;
        }

        Int3 offset = FaceInfo.Offset(selection.Direction);

        return steps > 0
            ? PullOut(selection, offset, steps, command)
            : PushIn(selection, offset, -steps, command);
    }

    private static int PullOut(FaceSelection selection, Int3 offset, int layers, VoxelEditCommand command)
    {
        // Snapshot the colours first: once the first layer is written, the source voxels are no
        // longer the only thing the later layers would read.
        var colours = new Dictionary<Int3, byte>(selection.Count);
        foreach (Int3 voxel in selection.Voxels)
        {
            byte colour = command.Target.GetVoxel(voxel);
            if (colour != Palette.EmptyIndex)
            {
                colours[voxel] = colour;
            }
        }

        int changed = 0;
        for (int layer = 1; layer <= layers; layer++)
        {
            foreach ((Int3 voxel, byte colour) in colours)
            {
                if (command.Apply(voxel + offset * layer, colour))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    private static int PushIn(FaceSelection selection, Int3 offset, int layers, VoxelEditCommand command)
    {
        int changed = 0;

        // Pushing in by one removes the selected voxels themselves, by two also the layer behind.
        for (int layer = 0; layer < layers; layer++)
        {
            foreach (Int3 voxel in selection.Voxels)
            {
                if (command.Apply(voxel - offset * layer, Palette.EmptyIndex))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Where the selection ends up after the given steps, so the next drag continues from the newly
    /// formed surface rather than from where the gesture began.
    /// </summary>
    public static FaceSelection Advance(FaceSelection selection, int steps) => selection.Translated(steps);
}
