using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// The mesh you see (EditorApp.md §4a): every exposed voxel face, shaded at its corners by ambient
/// occlusion, with interior faces culled — roughly 95% of the triangles of a solid volume.
///
/// Neighbouring faces of one colour and one shading are merged, a chunk at a time (Fullreleaseplan
/// 9.1): along a row only where the shading does not change along the row, and down the rows only
/// where it does not change down them — so a merged face looks exactly like the faces it replaces.
/// The hills and caves of a 512 × 128 × 512 level come to a third fewer triangles; flat ground and
/// walls to far fewer. Nothing needs one quad per face any more: clicking is a ray through the
/// voxels, and the voxel grid drawn over the surface comes from where the pixel is, not from the
/// triangles'. What merging does ask of whoever draws it: quads of different sizes meet with a
/// corner of one partway along the other's edge, and unless the corners are pushed out a little
/// (the desktop's Shaders.GrownCorner), pinholes show along those seams.
/// </summary>
public static class EditMesher
{
    /// <summary>A copy to mesh a chunk from, one a thread, kept between chunks.</summary>
    [ThreadStatic]
    private static ChunkNeighbourhood? _around;

    /// <summary>
    /// Rebuilds one chunk's mesh. Positions are world space, so the renderer can draw every chunk
    /// with a single identity model matrix.
    /// </summary>
    /// <param name="merge">Merge faces that look alike; off, one quad for every face, the reference the others are checked against.</param>
    public static void BuildChunk(VoxelWorld world, ChunkCoord coord, MeshBuilder builder, bool merge = true)
    {
        if (merge)
        {
            ChunkNeighbourhood around = _around ??= new ChunkNeighbourhood();
            around.CopyFrom(world, coord);
            BuildChunk(around, builder);
            return;
        }

        builder.Clear();

        Chunk? chunk = world.GetChunk(coord);
        if (chunk is null || chunk.IsEmpty)
        {
            return;
        }

        Int3 origin = coord.Origin;
        Palette palette = world.Palette;
        var reader = new VoxelReader(world);


        for (int y = 0; y < Chunk.Size; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                for (int x = 0; x < Chunk.Size; x++)
                {
                    if (!chunk.IsSolid(x, y, z))
                    {
                        continue;
                    }

                    var worldPosition = new Int3(origin.X + x, origin.Y + y, origin.Z + z);

                    for (int f = 0; f < FaceInfo.Count; f++)
                    {
                        var face = (Face)f;
                        if (IsNeighbourSolid(world, chunk, x, y, z, worldPosition, face))
                        {
                            continue;
                        }

                        // Per face, not per voxel: an edge voxel can carry a different colour on
                        // each side it shows.
                        byte index = chunk.GetFace(x, y, z, face);
                        EmitFace(builder, ref reader, worldPosition, face, palette[index].Rgba, index);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Meshes a chunk from its copy, merging faces that look alike — what the renderer runs on its
    /// workers. A slice at a time, each face direction in turn: which faces show and how, then the
    /// largest rectangles of faces that look the same, each one quad.
    /// </summary>
    public static void BuildChunk(ChunkNeighbourhood around, MeshBuilder builder)
    {
        builder.Clear();
        if (around.IsEmpty)
        {
            return;
        }

        const int S = Chunk.Size;
        byte[] cells = around.Cells;
        Int3 origin = around.Coord.Origin;
        Span<uint> mask = stackalloc uint[S * S];
        Span<int> cell = stackalloc int[3];
        Span<int> stride = stackalloc int[] { 1, ChunkNeighbourhood.StrideY, ChunkNeighbourhood.StrideZ };
        Span<int> cornerU = stackalloc int[4];
        Span<int> cornerV = stackalloc int[4];
        Span<int> cornerAxis = stackalloc int[4];
        Span<int> side1 = stackalloc int[4];
        Span<int> side2 = stackalloc int[4];
        Span<int> across = stackalloc int[4];
        Span<Vector3> corners = stackalloc Vector3[4];

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            var face = (Face)f;
            int axis = FaceInfo.Axis(face);
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;
            int outward = Stride(FaceInfo.Offset(face));
            for (int k = 0; k < 4; k++)
            {
                Int3 at = FaceInfo.Corner(face, k);
                cornerU[k] = Component(at, u);
                cornerV[k] = Component(at, v);
                cornerAxis[k] = Component(at, axis);

                // The cells Occlusion looks at, as steps from the voxel through the copy.
                (Int3 sideU, Int3 sideV) = OcclusionSides(face, k);
                side1[k] = outward + Stride(sideU);
                side2[k] = outward + Stride(sideV);
                across[k] = outward + Stride(sideU + sideV);
            }

            for (int d = 0; d < S; d++)
            {
                // The faces of this slice that look out on empty space: shown bit, colour, and each
                // corner's openness in two bits.
                int slice = ChunkNeighbourhood.Index(0, 0, 0) + (d * stride[axis]);
                for (int j = 0; j < S; j++)
                {
                    int row = slice + (j * stride[v]);
                    for (int i = 0; i < S; i++)
                    {
                        int p = row + (i * stride[u]);
                        byte index = cells[p];
                        uint key = 0;
                        if (index != Palette.EmptyIndex && cells[p + outward] == Palette.EmptyIndex)
                        {
                            if (around.PaintedFaces is { } painted)
                            {
                                cell[axis] = d;
                                cell[u] = i;
                                cell[v] = j;
                                if (painted.TryGetValue((Chunk.LinearIndex(cell[0], cell[1], cell[2]) * FaceInfo.Count) + f, out byte colour))
                                {
                                    index = colour;
                                }
                            }

                            key = 0x8000_0000u | ((uint)index << 8);
                            for (int k = 0; k < 4; k++)
                            {
                                key |= (uint)Openness(cells, p, side1[k], side2[k], across[k]) << (2 * k);
                            }
                        }

                        mask[(j * S) + i] = key;
                    }
                }

                for (int j = 0; j < S; j++)
                {
                    for (int i = 0; i < S;)
                    {
                        uint key = mask[(j * S) + i];
                        if (key == 0)
                        {
                            i++;
                            continue;
                        }

                        int width = 1;
                        if (Even(key, cornerV))
                        {
                            while (i + width < S && mask[(j * S) + i + width] == key)
                            {
                                width++;
                            }
                        }

                        int height = 1;
                        if (Even(key, cornerU))
                        {
                            while (j + height < S && RowIs(mask, i, width, j + height, key))
                            {
                                height++;
                            }
                        }

                        for (int jj = j; jj < j + height; jj++)
                        {
                            mask.Slice((jj * S) + i, width).Clear();
                        }

                        // The rectangle's corners in the face's own order, so it winds as a face does.
                        for (int k = 0; k < 4; k++)
                        {
                            cell[axis] = d + cornerAxis[k];
                            cell[u] = i + (cornerU[k] * width);
                            cell[v] = j + (cornerV[k] * height);
                            corners[k] = new Vector3(origin.X + cell[0], origin.Y + cell[1], origin.Z + cell[2]);
                        }

                        byte colourIndex = (byte)(key >> 8);
                        uint rgba = around.Colours[colourIndex];
                        int a0 = (int)(key & 3), a1 = (int)((key >> 2) & 3), a2 = (int)((key >> 4) & 3), a3 = (int)((key >> 6) & 3);
                        builder.AddQuad(
                            corners[0], corners[1], corners[2], corners[3],
                            WithOcclusion(rgba, a0), WithOcclusion(rgba, a1), WithOcclusion(rgba, a2), WithOcclusion(rgba, a3),
                            MeshVertex.Pack(face, colourIndex),
                            flip: a0 + a2 < a1 + a3);
                        i += width;
                    }
                }
            }
        }
    }

    /// <summary>How far a step is through a neighbourhood's cells.</summary>
    private static int Stride(Int3 step) => step.X + (step.Y * ChunkNeighbourhood.StrideY) + (step.Z * ChunkNeighbourhood.StrideZ);

    /// <summary><see cref="Occlusion"/> over a neighbourhood's cells, the steps to them worked out once a face.</summary>
    private static int Openness(byte[] cells, int voxel, int side1, int side2, int across)
    {
        bool first = cells[voxel + side1] != Palette.EmptyIndex;
        bool second = cells[voxel + side2] != Palette.EmptyIndex;
        if (first && second)
        {
            return 0;
        }

        return 3 - (first ? 1 : 0) - (second ? 1 : 0) - (cells[voxel + across] != Palette.EmptyIndex ? 1 : 0);
    }

    /// <summary>
    /// Whether a face's shading stays the same between the corners that differ only along one way
    /// across it — given which way each corner lies along the other: corners on the same line of
    /// <paramref name="line"/> must be equally open for the face to be merged the other way.
    /// </summary>
    private static bool Even(uint key, ReadOnlySpan<int> line)
    {
        for (int a = 0; a < 4; a++)
        {
            for (int b = a + 1; b < 4; b++)
            {
                if (line[a] == line[b] && ((key >> (2 * a)) & 3) != ((key >> (2 * b)) & 3))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool RowIs(ReadOnlySpan<uint> mask, int i, int width, int j, uint key)
    {
        ReadOnlySpan<uint> row = mask.Slice((j * Chunk.Size) + i, width);
        foreach (uint value in row)
        {
            if (value != key)
            {
                return false;
            }
        }

        return true;
    }

    private static int Component(Int3 value, int axis) => axis switch { 0 => value.X, 1 => value.Y, _ => value.Z };

    // Inside the chunk this is a single bit test; only the 6 boundary planes pay for a world lookup.
    private static bool IsNeighbourSolid(
        VoxelWorld world,
        Chunk chunk,
        int x,
        int y,
        int z,
        Int3 worldPosition,
        Face face)
    {
        Int3 offset = FaceInfo.Offset(face);
        int nx = x + offset.X;
        int ny = y + offset.Y;
        int nz = z + offset.Z;

        if ((uint)nx < Chunk.Size && (uint)ny < Chunk.Size && (uint)nz < Chunk.Size)
        {
            return chunk.IsSolid(nx, ny, nz);
        }

        return world.IsSolid(worldPosition + offset);
    }

    private static void EmitFace(MeshBuilder builder, ref VoxelReader reader, Int3 voxel, Face face, uint rgba, byte paletteIndex)
    {
        Vector3 basePosition = voxel.ToVector3();

        int a0 = Occlusion(ref reader, voxel, face, 0);
        int a1 = Occlusion(ref reader, voxel, face, 1);
        int a2 = Occlusion(ref reader, voxel, face, 2);
        int a3 = Occlusion(ref reader, voxel, face, 3);

        builder.AddQuad(
            basePosition + FaceInfo.Corner(face, 0).ToVector3(),
            basePosition + FaceInfo.Corner(face, 1).ToVector3(),
            basePosition + FaceInfo.Corner(face, 2).ToVector3(),
            basePosition + FaceInfo.Corner(face, 3).ToVector3(),
            WithOcclusion(rgba, a0),
            WithOcclusion(rgba, a1),
            WithOcclusion(rgba, a2),
            WithOcclusion(rgba, a3),
            MeshVertex.Pack(face, paletteIndex),
            flip: a0 + a2 < a1 + a3);
    }

    /// <summary>
    /// How open a face's corner is, 0 to 3 — the classic voxel ambient occlusion: the two cells
    /// beside it and the one across the corner, in the layer of air the face looks into. Both sides
    /// filled closes the corner whatever the diagonal holds.
    /// </summary>
    public static int Occlusion(ref VoxelReader reader, Int3 voxel, Face face, int corner)
    {
        (Int3 u, Int3 v) = OcclusionSides(face, corner);
        Int3 outside = voxel + FaceInfo.Offset(face);

        bool side1 = reader.IsSolid(outside + u);
        bool side2 = reader.IsSolid(outside + v);
        if (side1 && side2)
        {
            return 0;
        }

        bool across = reader.IsSolid(outside + u + v);
        return 3 - (side1 ? 1 : 0) - (side2 ? 1 : 0) - (across ? 1 : 0);
    }

    /// <summary>The ways to the two cells beside a face's corner, in the layer of air it looks into.</summary>
    private static (Int3 U, Int3 V) OcclusionSides(Face face, int corner)
    {
        Int3 at = FaceInfo.Corner(face, corner);
        int axis = FaceInfo.Axis(face);
        Int3 u = axis == 0 ? new Int3(0, (at.Y * 2) - 1, 0) : new Int3((at.X * 2) - 1, 0, 0);
        Int3 v = axis == 2 ? new Int3(0, (at.Y * 2) - 1, 0) : new Int3(0, 0, (at.Z * 2) - 1);
        return (u, v);
    }

    /// <summary>The colour with a corner's openness in its alpha: 255 fully open, less the more enclosed.</summary>
    private static uint WithOcclusion(uint rgba, int openness) => (rgba & 0x00FFFFFFu) | ((uint)(openness * 85) << 24);

    /// <summary>
    /// Builds the whole world as one mesh with no merging. Only used as the reference the greedy
    /// mesher is validated against (§4b) — the renderer always meshes per chunk.
    /// </summary>
    public static void BuildWorldNaive(VoxelWorld world, MeshBuilder builder)
    {
        builder.Clear();

        var chunkBuilder = new MeshBuilder();
        foreach (ChunkCoord coord in world.Chunks.Keys)
        {
            BuildChunk(world, coord, chunkBuilder, merge: false);
            AppendTo(builder, chunkBuilder);
        }
    }

    private static void AppendTo(MeshBuilder target, MeshBuilder source)
    {
        ReadOnlySpan<MeshVertex> vertices = source.Vertices;
        for (int i = 0; i + 3 < vertices.Length; i += 4)
        {
            target.AddQuad(
                vertices[i].Position,
                vertices[i + 1].Position,
                vertices[i + 2].Position,
                vertices[i + 3].Position,
                vertices[i].Rgba,
                vertices[i].FaceIndex);
        }
    }
}
