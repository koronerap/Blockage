using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// A chunk's voxels and the layer of cells round it, copied out of the world: everything meshing the
/// chunk reads — which faces show, how their corners are shaded, what colour each one is. Meshing
/// from the copy can run on another thread while the world goes on being edited (Fullreleaseplan
/// 9.1), and reads an array rather than the world's dictionary. Filled again for chunk after chunk
/// rather than made anew.
/// </summary>
public sealed class ChunkNeighbourhood
{
    /// <summary>Cells along each side: the chunk's, and one either side of them.</summary>
    public const int Side = Chunk.Size + 2;

    internal const int StrideZ = Side;
    internal const int StrideY = Side * Side;

    /// <summary>Palette indices, x fastest, then z, then y; the chunk's own cell (0, 0, 0) at (1, 1, 1).</summary>
    internal readonly byte[] Cells = new byte[Side * Side * Side];

    /// <summary>Each palette entry's colour.</summary>
    internal readonly uint[] Colours = new uint[Palette.Size];

    /// <summary>The chunk's faces painted another colour than their voxel, by linear index × 6 + face; null for none.</summary>
    internal Dictionary<int, byte>? PaintedFaces;

    public ChunkCoord Coord { get; private set; }

    /// <summary>Whether the chunk holds no voxels, and so meshes to nothing.</summary>
    public bool IsEmpty { get; private set; } = true;

    /// <summary>Where a cell of the chunk, or of the layer round it (−1 or <see cref="Chunk.Size"/>), is in <see cref="Cells"/>.</summary>
    internal static int Index(int x, int y, int z) => ((y + 1) * StrideY) + ((z + 1) * StrideZ) + x + 1;

    /// <summary>Copies a chunk and what borders it, across faces, edges and corners alike.</summary>
    public void CopyFrom(VoxelWorld world, ChunkCoord coord)
    {
        Coord = coord;
        PaintedFaces = null;
        Array.Clear(Cells);

        Chunk? chunk = world.GetChunk(coord);
        IsEmpty = chunk is null || chunk.IsEmpty;
        if (chunk is null || IsEmpty)
        {
            return;
        }

        Palette palette = world.Palette;
        for (int i = 0; i < Palette.Size; i++)
        {
            Colours[i] = palette[i].Rgba;
        }

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Chunk? source = dx == 0 && dy == 0 && dz == 0 ? chunk : world.GetChunk(coord.Offset(dx, dy, dz));
                    if (source is { IsEmpty: false })
                    {
                        CopyPart(source, dx, dy, dz);
                    }
                }
            }
        }

        if (chunk.FaceOverrideCount > 0)
        {
            PaintedFaces = new Dictionary<int, byte>(chunk.FaceOverrideCount);
            foreach ((int linear, Face face, byte index) in chunk.FaceOverrides())
            {
                PaintedFaces[(linear * FaceInfo.Count) + (int)face] = index;
            }
        }
    }

    /// <summary>
    /// The part of a chunk that lies in the neighbourhood, from the chunk that way of the middle one:
    /// along each axis all of it where it is level with the middle, else its last or first layer.
    /// </summary>
    private void CopyPart(Chunk source, int dx, int dy, int dz)
    {
        (int x0, int x1) = Range(dx);
        (int y0, int y1) = Range(dy);
        (int z0, int z1) = Range(dz);
        int width = x1 - x0 + 1;
        ReadOnlySpan<byte> indices = source.Indices;
        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                indices.Slice(Chunk.LinearIndex(x0, y, z), width)
                    .CopyTo(Cells.AsSpan(Index(x0 + (dx * Chunk.Size), y + (dy * Chunk.Size), z + (dz * Chunk.Size)), width));
            }
        }
    }

    private static (int From, int To) Range(int direction) =>
        direction < 0 ? (Chunk.Size - 1, Chunk.Size - 1) : direction > 0 ? (0, 0) : (0, Chunk.Size - 1);
}
