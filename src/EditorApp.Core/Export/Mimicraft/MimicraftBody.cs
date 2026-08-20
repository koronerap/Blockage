using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export.Mimicraft;

/// <summary>One named grid on its way into a Mimicraft file.</summary>
public sealed record MimicraftPiece(string Id, VoxelWorld Grid);

/// <summary>
/// The VoxelBodyCodec payload (EditorCodec.md §3) — the core both file formats wrap.
///
/// A voxel body written as runs along x, with one palette shared by every piece and a list of
/// per-face exceptions on top. The reader treats every length in the stream as hostile and rejects
/// far more than it accepts, so most of what follows is about producing exactly what it will take
/// rather than about producing something merely reasonable.
///
/// The three rules that are easy to get wrong, all of them enforced on the other side:
///
///   * A run never crosses a row. When x wraps back to zero the index still advances by one, but the
///     reader checks <c>xStart + length &lt;= sizeX</c> and refuses anything that spans two rows.
///   * The two gaps are measured differently. A run's gap counts from the END of the previous run;
///     a face's gap counts from the INDEX of the previous face. Getting the second one wrong is
///     invisible until a voxel has more than one painted face.
///   * A piece with no voxels still writes a complete header, faceCount included. Leaving it out
///     makes the reader take the next piece's position as a face entry.
/// </summary>
public static class MimicraftBody
{
    public const byte FormatRaw = 0x01;

    /// <summary>
    /// The format's own ceiling on a box axis: <c>boxSize</c> is a <c>uint16</c>, so anything larger
    /// cannot be written at all — it would wrap and describe a different, smaller model.
    ///
    /// Not the same thing as what the game will accept. That is <see cref="StockBoxExtent"/>, a
    /// number in Mimicraft's own source and therefore one its author can change; this one is
    /// arithmetic and cannot be.
    /// </summary>
    public const int MaxBoxExtent = ushort.MaxValue;

    /// <summary>
    /// What an unmodified Mimicraft build stops at (<c>VoxelBodyCodec.MaxPieceBoxExtent</c>). Nothing
    /// here enforces it — a piece larger than this is written and the reader on the other side is
    /// expected to have been raised to match. Kept so the dialog can say so.
    /// </summary>
    public const int StockBoxExtent = 64;

    /// <summary>
    /// Cells in one piece, capped by what a gap can express: run and face gaps are varints holding a
    /// <c>uint32</c>, so a box with more cells than that could not be addressed.
    /// </summary>
    public const long MaxBoxCells = uint.MaxValue;

    public const int MaxTotalVoxels = 200_000;

    /// <summary>A run cannot be longer than this, so a long row is split rather than overflowed.</summary>
    public const int MaxRunLength = 65535;

    /// <summary>
    /// The face number an editor face becomes under a given orientation.
    ///
    /// Both the numbering and the direction differ — here the order is +X, -X, +Y, -Y, +Z, -Z, and
    /// there it starts at -Z and pairs the axes the other way — and on top of that the model may
    /// have been built lying in some other direction entirely. All of it comes out of the same
    /// matrix the cells go through, so the two cannot disagree.
    /// </summary>
    public static byte ToMimicraftFace(Face face, MimicraftOrientation? orientation = null) =>
        (orientation ?? MimicraftOrientation.Default).FaceNumber(face);

    /// <summary>
    /// Writes the framed payload: one format byte, then the body.
    ///
    /// Always raw. Deflate is offered by the format and only ever saves space — the reader accepts
    /// both — and a raw file cannot trip the two-megabyte inflation ceiling that only applies to the
    /// compressed path.
    /// </summary>
    public static byte[] Encode(
        IReadOnlyList<MimicraftPiece> pieces,
        Palette palette,
        MimicraftOrientation? orientation = null,
        float voxelSize = 1f)
    {
        orientation ??= MimicraftOrientation.Default;

        var body = new List<byte> { FormatRaw };

        MimicraftBinary.WriteSingle(body, voxelSize);
        MimicraftBinary.WriteVarint(body, (uint)pieces.Count);

        // One table for the whole body, holding only the colours actually used. The editor's palette
        // has 256 slots and a model rarely touches many of them; writing all of them would cost
        // nothing in correctness and mislead anyone reading the file.
        var indexOf = new Dictionary<byte, int>();
        var colors = new List<Color32>();

        foreach (MimicraftPiece piece in pieces)
        {
            foreach (byte used in UsedColors(piece.Grid))
            {
                if (!indexOf.ContainsKey(used))
                {
                    indexOf.Add(used, colors.Count);
                    colors.Add(palette[used]);
                }
            }
        }

        MimicraftBinary.WriteVarint(body, (uint)colors.Count);
        foreach (Color32 color in colors)
        {
            // Three bytes: voxel colours are opaque and the format has no alpha.
            body.Add(color.R);
            body.Add(color.G);
            body.Add(color.B);
        }

        foreach (MimicraftPiece piece in pieces)
        {
            WritePiece(body, piece.Grid, indexOf, colors.Count, orientation);
        }

        return [.. body];
    }

    private static IEnumerable<byte> UsedColors(VoxelWorld grid)
    {
        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            yield break;
        }

        for (int y = min.Y; y <= max.Y; y++)
        {
            for (int z = min.Z; z <= max.Z; z++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    if (!grid.IsSolid(x, y, z))
                    {
                        continue;
                    }

                    yield return grid.GetVoxel(x, y, z);

                    for (int f = 0; f < FaceInfo.Count; f++)
                    {
                        yield return grid.GetFaceColor(x, y, z, (Face)f);
                    }
                }
            }
        }
    }

    private static void WritePiece(
        List<byte> body,
        VoxelWorld grid,
        Dictionary<byte, int> indexOf,
        int paletteCount,
        MimicraftOrientation orientation)
    {
        // Position and rotation are written and then ignored on the way back: a character part hangs
        // off a bone and a weapon off its prefab, so where the modeller left it was never carried.
        MimicraftBinary.WriteSingle(body, 0f);
        MimicraftBinary.WriteSingle(body, 0f);
        MimicraftBinary.WriteSingle(body, 0f);
        MimicraftBinary.WriteSingle(body, 0f);
        MimicraftBinary.WriteSingle(body, 0f);
        MimicraftBinary.WriteSingle(body, 0f);
        MimicraftBinary.WriteSingle(body, 1f);

        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            // An empty piece still writes a full header. Both counts have to be there or the reader
            // starts taking the next piece apart in the wrong place.
            MimicraftBinary.WriteInt16(body, 0);
            MimicraftBinary.WriteInt16(body, 0);
            MimicraftBinary.WriteInt16(body, 0);
            MimicraftBinary.WriteUInt16(body, 0);
            MimicraftBinary.WriteUInt16(body, 0);
            MimicraftBinary.WriteUInt16(body, 0);
            MimicraftBinary.WriteVarint(body, 0);
            MimicraftBinary.WriteVarint(body, 0);
            return;
        }

        Int3 sourceSize = max - min + Int3.One;

        // The written box is the model's box with its axes shuffled into Unity's.
        Int3 size = orientation.Size(sourceSize);

        // Rebased to its own box: the piece is written as though its lowest occupied cell were the
        // origin. Mimicraft expects a part's voxels inside 0..BoxSize-1 and clips what falls outside,
        // and where the model happened to sit in this editor is not something the format carries.
        MimicraftBinary.WriteInt16(body, 0);
        MimicraftBinary.WriteInt16(body, 0);
        MimicraftBinary.WriteInt16(body, 0);
        MimicraftBinary.WriteUInt16(body, (ushort)size.X);
        MimicraftBinary.WriteUInt16(body, (ushort)size.Y);
        MimicraftBinary.WriteUInt16(body, (ushort)size.Z);

        WriteRuns(body, grid, min, sourceSize, size, indexOf, paletteCount, orientation);
        WriteFaces(body, grid, min, sourceSize, size, indexOf, paletteCount, orientation);
    }

    /// <summary>
    /// Index of a cell inside the box. x varies fastest, then z, then y — the order runs are built
    /// in and the only order the reader can undo.
    /// </summary>
    private static long CellIndex(int x, int y, int z, Int3 size) =>
        (((long)y * size.Z) + z) * size.X + x;

    private static void WriteRuns(
        List<byte> body,
        VoxelWorld grid,
        Int3 min,
        Int3 sourceSize,
        Int3 size,
        Dictionary<byte, int> indexOf,
        int paletteCount,
        MimicraftOrientation orientation)
    {
        var runs = new List<(long Start, int Length, byte Color)>();

        for (int y = 0; y < size.Y; y++)
        {
            for (int z = 0; z < size.Z; z++)
            {
                // Restarted for every row rather than carried across, which is what keeps a run
                // inside one row without having to check for the wrap afterwards.
                long start = -1;
                int length = 0;
                byte color = 0;

                for (int x = 0; x < size.X; x++)
                {
                    // Written in the output's order and read from wherever that cell came from, so a
                    // run is contiguous where it counts — along the row Mimicraft will decode.
                    Int3 from = min + orientation.Source(new Int3(x, y, z), sourceSize);

                    bool solid = grid.IsSolid(from);
                    byte here = solid ? grid.GetVoxel(from) : (byte)0;

                    bool extends = solid && length > 0 && here == color && length < MaxRunLength;
                    if (extends)
                    {
                        length++;
                        continue;
                    }

                    if (length > 0)
                    {
                        runs.Add((start, length, color));
                        length = 0;
                    }

                    if (solid)
                    {
                        start = CellIndex(x, y, z, size);
                        length = 1;
                        color = here;
                    }
                }

                if (length > 0)
                {
                    runs.Add((start, length, color));
                }
            }
        }

        MimicraftBinary.WriteVarint(body, (uint)runs.Count);

        long previousEnd = 0;
        foreach ((long start, int length, byte color) in runs)
        {
            // From the END of the previous run, not from its start.
            MimicraftBinary.WriteVarint(body, (uint)(start - previousEnd));
            MimicraftBinary.WriteVarint(body, (uint)length);
            WriteColor(body, color, indexOf, paletteCount);
            previousEnd = start + length;
        }
    }

    private static void WriteFaces(
        List<byte> body,
        VoxelWorld grid,
        Int3 min,
        Int3 sourceSize,
        Int3 size,
        Dictionary<byte, int> indexOf,
        int paletteCount,
        MimicraftOrientation orientation)
    {
        var faces = new List<(long Index, byte Face, byte Color)>();

        for (int y = 0; y < size.Y; y++)
        {
            for (int z = 0; z < size.Z; z++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    Int3 from = min + orientation.Source(new Int3(x, y, z), sourceSize);

                    if (!grid.IsSolid(from))
                    {
                        continue;
                    }

                    byte baseColor = grid.GetVoxel(from);

                    for (int f = 0; f < FaceInfo.Count; f++)
                    {
                        byte painted = grid.GetFaceColor(from, (Face)f);
                        if (painted == baseColor)
                        {
                            // The section is a list of exceptions; a face the same colour as its
                            // voxel is already covered by the run.
                            continue;
                        }

                        // Turned through the same matrix as the cell, so the paint stays on the same
                        // physical side of the same block.
                        faces.Add((CellIndex(x, y, z, size), orientation.FaceNumber((Face)f), painted));
                    }
                }
            }
        }

        // Sorted by cell and then by face number, because the reader rejects an entry that does not
        // advance the pair — and the face numbers are not in this editor's order, so a voxel's six
        // faces come out shuffled if they are left in the order they were collected.
        faces.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Face.CompareTo(b.Face));

        MimicraftBinary.WriteVarint(body, (uint)faces.Count);

        long previousIndex = 0;
        foreach ((long index, byte face, byte color) in faces)
        {
            // From the INDEX of the previous entry, so several faces of one voxel are a gap of zero.
            MimicraftBinary.WriteVarint(body, (uint)(index - previousIndex));
            body.Add(face);
            WriteColor(body, color, indexOf, paletteCount);
            previousIndex = index;
        }
    }

    private static void WriteColor(List<byte> body, byte editorIndex, Dictionary<byte, int> indexOf, int paletteCount)
    {
        int index = indexOf[editorIndex];

        if (paletteCount <= 256)
        {
            body.Add((byte)index);
            return;
        }

        body.Add((byte)(index & 0xFF));
        body.Add((byte)(index >> 8));
    }
}
