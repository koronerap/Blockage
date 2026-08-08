using EditorApp.Core.Commands;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Paint only ever recolors (EditorApp.md, "Paint"): it never creates a voxel and never removes
/// one, and it only touches voxels that can actually be seen from outside. Painting a buried voxel
/// would change nothing on screen and nothing in the export, so reaching them is not a feature.
/// </summary>
public static class PaintOperations
{
    /// <summary>Solid, and with at least one face exposed.</summary>
    public static bool IsVisible(VoxelWorld world, Int3 cell)
    {
        if (!world.IsSolid(cell))
        {
            return false;
        }

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            if (!world.IsSolid(cell + FaceInfo.Offset((Face)f)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Paints every visible voxel within a 3D euclidean radius of the centre. Radius 0 is exactly
    /// one voxel — the distance test is inclusive, so the centre always qualifies.
    /// </summary>
    public static int Brush(Int3 centre, float radius, byte paletteIndex, VoxelEditCommand command)
    {
        VoxelWorld world = command.Target;
        int extent = (int)MathF.Floor(MathF.Max(radius, 0f));
        float radiusSquared = radius * radius;

        int changed = 0;
        for (int dy = -extent; dy <= extent; dy++)
        {
            for (int dz = -extent; dz <= extent; dz++)
            {
                for (int dx = -extent; dx <= extent; dx++)
                {
                    // Euclidean, not a cube: a cube brush at radius 3 would reach 5.2 voxels into
                    // the corners and paint a shape the cursor never suggested.
                    if (dx * dx + dy * dy + dz * dz > radiusSquared)
                    {
                        continue;
                    }

                    var cell = centre + new Int3(dx, dy, dz);
                    if (IsVisible(world, cell) && command.Apply(cell, paletteIndex))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Bucket fill: the connected run of visible voxels whose colour is within
    /// <paramref name="threshold"/> of the seed's. A threshold of 0 means an exact match.
    /// </summary>
    public static int Bucket(
        Int3 seed,
        byte paletteIndex,
        int threshold,
        VoxelEditCommand command,
        int limit = 2_000_000)
    {
        VoxelWorld world = command.Target;

        if (!IsVisible(world, seed))
        {
            return 0;
        }

        Color32 target = world.Palette[world.GetVoxel(seed)];

        var visited = new HashSet<Int3> { seed };
        var queue = new Queue<Int3>();
        queue.Enqueue(seed);

        int changed = 0;
        while (queue.Count > 0 && changed < limit)
        {
            Int3 cell = queue.Dequeue();
            if (!IsVisible(world, cell) || !IsWithinThreshold(world.Palette[world.GetVoxel(cell)], target, threshold))
            {
                continue;
            }

            if (command.Apply(cell, paletteIndex))
            {
                changed++;
            }

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 neighbour = cell + FaceInfo.Offset((Face)f);
                if (visited.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        return changed;
    }

    /// <summary>Chebyshev distance in RGB — cheap, and predictable to reason about on a slider.</summary>
    private static bool IsWithinThreshold(Color32 candidate, Color32 target, int threshold) =>
        Math.Abs(candidate.R - target.R) <= threshold
        && Math.Abs(candidate.G - target.G) <= threshold
        && Math.Abs(candidate.B - target.B) <= threshold;

    /// <summary>The eyedropper: the colour under the cursor, or null when there is nothing to sample.</summary>
    public static byte? Sample(VoxelWorld world, Int3 cell)
    {
        byte index = world.GetVoxel(cell);
        return index == Palette.EmptyIndex ? null : index;
    }
}
