using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// Boxes that fill an object's voxels (Fullreleaseplan 8.4), for an engine to collide with instead of
/// with every face: from each voxel not yet in one, grown along X, then Z, then Y, as far as the
/// voxels go — a chunk at a time, so a level of any size needs no more than a chunk's worth of room.
/// </summary>
public static class CollisionBoxes
{
    /// <summary>The boxes, each from its first corner up to — not including — its last, in the grid's cells.</summary>
    public static List<(Int3 Min, Int3 Max)> Of(VoxelWorld grid)
    {
        var boxes = new List<(Int3 Min, Int3 Max)>();
        var taken = new bool[Chunk.VoxelCount];
        const int S = Chunk.Size;

        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            Array.Clear(taken);
            bool Open(int x, int y, int z) => chunk.IsSolid(x, y, z) && !taken[Chunk.LinearIndex(x, y, z)];

            for (int y = 0; y < S; y++)
            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                if (!Open(x, y, z))
                {
                    continue;
                }

                int x1 = x + 1;
                while (x1 < S && Open(x1, y, z))
                {
                    x1++;
                }

                int z1 = z + 1;
                while (z1 < S && Enumerable.Range(x, x1 - x).All(xx => Open(xx, y, z1)))
                {
                    z1++;
                }

                int y1 = y + 1;
                while (y1 < S && Enumerable.Range(z, z1 - z).All(zz => Enumerable.Range(x, x1 - x).All(xx => Open(xx, y1, zz))))
                {
                    y1++;
                }

                for (int yy = y; yy < y1; yy++)
                for (int zz = z; zz < z1; zz++)
                for (int xx = x; xx < x1; xx++)
                {
                    taken[Chunk.LinearIndex(xx, yy, zz)] = true;
                }

                var origin = new Int3(coord.X * S, coord.Y * S, coord.Z * S);
                boxes.Add((new Int3(origin.X + x, origin.Y + y, origin.Z + z), new Int3(origin.X + x1, origin.Y + y1, origin.Z + z1)));
            }
        }

        return boxes;
    }
}
