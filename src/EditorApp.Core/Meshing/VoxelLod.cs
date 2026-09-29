using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// Coarser copies of a grid, for an engine to show from further off (Fullreleaseplan 8.8): each
/// block of factor³ voxels one voxel, solid when at least half the block is, in the colour most of
/// its voxels have. The copy's cells are blocks: placed at <c>factor</c> times the voxel size, it
/// covers what the grid covers.
/// </summary>
public static class VoxelLod
{
    /// <summary>How much coarser each level of detail after the first is: half, then a quarter.</summary>
    public static readonly int[] Factors = [2, 4];

    public static VoxelWorld Downsample(VoxelWorld grid, int factor)
    {
        if (factor <= 1)
        {
            return grid.Copy();
        }

        var blocks = new Dictionary<Int3, Dictionary<byte, int>>();
        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            for (int i = 0; i < Chunk.VoxelCount; i++)
            {
                Int3 local = Chunk.FromLinearIndex(i);
                byte colour = chunk.Get(local.X, local.Y, local.Z);
                if (colour == Palette.EmptyIndex)
                {
                    continue;
                }

                var block = new Int3(
                    Down((coord.X * Chunk.Size) + local.X, factor),
                    Down((coord.Y * Chunk.Size) + local.Y, factor),
                    Down((coord.Z * Chunk.Size) + local.Z, factor));
                if (!blocks.TryGetValue(block, out Dictionary<byte, int>? colours))
                {
                    colours = [];
                    blocks[block] = colours;
                }

                colours[colour] = colours.GetValueOrDefault(colour) + 1;
            }
        }

        var coarse = new VoxelWorld();
        int volume = factor * factor * factor;
        foreach ((Int3 block, Dictionary<byte, int> colours) in blocks)
        {
            if (colours.Values.Sum() * 2 < volume)
            {
                continue;
            }

            // The most voxels' colour; the lowest index where two tie, so the same grid always gives the same copy.
            byte chosen = colours.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
            coarse.SetVoxel(block, chosen);
        }

        return coarse;
    }

    private static int Down(int value, int factor) => value >= 0 ? value / factor : ((value + 1) / factor) - 1;
}
