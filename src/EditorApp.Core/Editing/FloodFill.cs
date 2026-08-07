using EditorApp.Core.Commands;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Bucket fill: recolors the connected run of same-colored voxels reachable from a seed, six-way
/// connected. Bounded by a cell limit so a mistaken click on a huge uniform region cannot lock the
/// editor up.
/// </summary>
public static class FloodFill
{
    public const int DefaultLimit = 2_000_000;

    /// <summary>Returns the number of voxels recolored.</summary>
    public static int Fill(
        VoxelWorld world,
        Int3 seed,
        byte replacementIndex,
        VoxelEditCommand command,
        int limit = DefaultLimit)
    {
        byte target = world.GetVoxel(seed);
        if (target == Palette.EmptyIndex || target == replacementIndex)
        {
            return 0;
        }

        var visited = new HashSet<Int3> { seed };
        var queue = new Queue<Int3>();
        queue.Enqueue(seed);

        int filled = 0;
        while (queue.Count > 0 && filled < limit)
        {
            Int3 position = queue.Dequeue();
            if (world.GetVoxel(position) != target)
            {
                continue;
            }

            command.Apply(world, position, replacementIndex);
            filled++;

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 neighbour = position + FaceInfo.Offset((Face)f);
                if (world.GetVoxel(neighbour) == target && visited.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        return filled;
    }
}
