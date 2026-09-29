using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Cutting every voxel of an object into smaller ones. At a factor of 2 each becomes 2 × 2 × 2 of half
/// the size: the object holds eight times as many voxels and stands exactly where it stood, the same
/// size in the world. What it is for is detail — a shape blocked out coarse, then refined, without
/// building it again at the finer size.
///
/// Nothing is resampled. Each new voxel takes its block's colour, and a face painted on the block is
/// carried to every new face on that side of it. The faces inside the block are the block's own
/// colour, which is what they would have been had it been built this fine.
///
/// The object's origin needs no moving: cell c covers c to c + 1 in voxels of size s, and its children
/// f·c to f·c + f in voxels of size s / f — the same stretch of the world.
/// </summary>
public static class Subdivide
{
    /// <summary>How many voxels each voxel becomes along each axis.</summary>
    public const int Factor = 2;

    /// <summary>
    /// The most voxels an object may hold after a subdivide. Every subdivide multiplies the count by
    /// eight, so a few presses go from a model to something the mesher would spend minutes on.
    /// </summary>
    public const int MaxVoxels = 16_000_000;

    /// <summary>Cuts every voxel of the grid into factor³, in place.</summary>
    public static void Apply(VoxelWorld grid, int factor = Factor)
    {
        (List<(Int3 Cell, byte Colour)> cells, List<(Int3 Cell, Face Face, byte Colour)> faces) = Read(grid);

        // In place rather than into a new grid, for the same reason as a turn: the undo stack holds
        // references to the grids it edited, and a swapped grid would leave them writing into nothing.
        grid.Clear();

        foreach ((Int3 cell, byte colour) in cells)
        {
            Int3 corner = cell * factor;
            for (int x = 0; x < factor; x++)
            {
                for (int y = 0; y < factor; y++)
                {
                    for (int z = 0; z < factor; z++)
                    {
                        grid.SetVoxel(corner + new Int3(x, y, z), colour);
                    }
                }
            }
        }

        foreach ((Int3 cell, Face face, byte colour) in faces)
        {
            foreach (Int3 child in ChildrenOnSide(cell, face, factor))
            {
                grid.SetFaceColor(child, face, colour);
            }
        }
    }

    /// <summary>
    /// Puts back the grid <see cref="Apply"/> was given, from the one it made — the undo. Each block
    /// of factor³ becomes one voxel again, coloured as its corner child, with each face taken from a
    /// child on that side.
    ///
    /// Exact on anything <see cref="Apply"/> produced, which is all it is ever handed: the undo stack
    /// takes back every later edit before it reaches this one. On any other grid a block keeps the
    /// colour of one of its children, which is a coarsening, not an undo.
    /// </summary>
    public static void Revert(VoxelWorld grid, int factor = Factor)
    {
        (List<(Int3 Cell, byte Colour)> cells, List<(Int3 Cell, Face Face, byte Colour)> faces) = Read(grid);

        var blocks = new Dictionary<Int3, byte>();
        foreach ((Int3 cell, byte colour) in cells)
        {
            Int3 block = BlockOf(cell, factor);

            // The corner child speaks for the block; any other only when the corner is missing.
            if (cell == block * factor || !blocks.ContainsKey(block))
            {
                blocks[block] = colour;
            }
        }

        grid.Clear();

        foreach ((Int3 block, byte colour) in blocks)
        {
            grid.SetVoxel(block, colour);
        }

        foreach ((Int3 cell, Face face, byte colour) in faces)
        {
            Int3 block = BlockOf(cell, factor);
            if (blocks.ContainsKey(block) && cell == SideCorner(block, face, factor))
            {
                grid.SetFaceColor(block, face, colour);
            }
        }
    }

    /// <summary>The children of a cell that lie against its <paramref name="face"/> side.</summary>
    public static IEnumerable<Int3> ChildrenOnSide(Int3 cell, Face face, int factor = Factor)
    {
        int axis = FaceInfo.Axis(face);
        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;
        Int3 corner = SideCorner(cell, face, factor);

        for (int a = 0; a < factor; a++)
        {
            for (int b = 0; b < factor; b++)
            {
                Int3 child = VoxelBox.WithComponent(corner, u, VoxelBox.Component(corner, u) + a);
                yield return VoxelBox.WithComponent(child, v, VoxelBox.Component(child, v) + b);
            }
        }
    }

    /// <summary>The child with the lowest coordinates among those against a side.</summary>
    private static Int3 SideCorner(Int3 cell, Face face, int factor)
    {
        Int3 corner = cell * factor;
        if (!FaceInfo.IsPositive(face))
        {
            return corner;
        }

        int axis = FaceInfo.Axis(face);
        return VoxelBox.WithComponent(corner, axis, VoxelBox.Component(corner, axis) + factor - 1);
    }

    /// <summary>Which block a cell belongs to — rounding down, so -1 is in block -1 and not block 0.</summary>
    private static Int3 BlockOf(Int3 cell, int factor) =>
        new(FloorDivide(cell.X, factor), FloorDivide(cell.Y, factor), FloorDivide(cell.Z, factor));

    private static int FloorDivide(int value, int divisor) =>
        value >= 0 ? value / divisor : ((value + 1) / divisor) - 1;

    /// <summary>Every solid cell and every separately painted face, read out before anything is written.</summary>
    private static (List<(Int3 Cell, byte Colour)> Cells, List<(Int3 Cell, Face Face, byte Colour)> Faces) Read(VoxelWorld grid)
    {
        var cells = new List<(Int3, byte)>(grid.SolidCount);
        var faces = new List<(Int3, Face, byte)>();

        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            Int3 origin = coord.Origin;
            ReadOnlySpan<byte> indices = chunk.Indices;

            for (int linear = 0; linear < indices.Length; linear++)
            {
                if (indices[linear] != Palette.EmptyIndex)
                {
                    cells.Add((origin + Chunk.FromLinearIndex(linear), indices[linear]));
                }
            }

            foreach ((int linear, Face face, byte index) in chunk.FaceOverrides())
            {
                faces.Add((origin + Chunk.FromLinearIndex(linear), face, index));
            }
        }

        return (cells, faces);
    }
}
