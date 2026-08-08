using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// The mesh you see and click on (EditorApp.md §4a): one quad per exposed voxel face, no vertex
/// sharing, no merging. Interior faces are culled, which removes roughly 95% of the triangles in a
/// solid volume. This mesher is deliberately never greedy — merged quads would destroy the
/// one-to-one mapping between geometry and voxels that selection depends on.
/// </summary>
public static class EditMesher
{
    /// <summary>
    /// Rebuilds one chunk's mesh. Positions are world space, so the renderer can draw every chunk
    /// with a single identity model matrix.
    /// </summary>
    public static void BuildChunk(VoxelWorld world, ChunkCoord coord, MeshBuilder builder)
    {
        builder.Clear();

        Chunk? chunk = world.GetChunk(coord);
        if (chunk is null || chunk.IsEmpty)
        {
            return;
        }

        Int3 origin = coord.Origin;
        Palette palette = world.Palette;

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
                        EmitFace(builder, worldPosition, face, palette[chunk.GetFace(x, y, z, face)].Rgba);
                    }
                }
            }
        }
    }

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

    private static void EmitFace(MeshBuilder builder, Int3 voxel, Face face, uint rgba)
    {
        Vector3 basePosition = voxel.ToVector3();

        builder.AddQuad(
            basePosition + FaceInfo.Corner(face, 0).ToVector3(),
            basePosition + FaceInfo.Corner(face, 1).ToVector3(),
            basePosition + FaceInfo.Corner(face, 2).ToVector3(),
            basePosition + FaceInfo.Corner(face, 3).ToVector3(),
            rgba,
            (int)face);
    }

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
            BuildChunk(world, coord, chunkBuilder);
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
