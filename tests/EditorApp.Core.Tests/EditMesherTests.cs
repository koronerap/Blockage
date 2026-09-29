using System.Numerics;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class EditMesherTests
{
    private static MeshBuilder MeshOf(VoxelWorld world, ChunkCoord coord)
    {
        var builder = new MeshBuilder();
        EditMesher.BuildChunk(world, coord, builder);
        return builder;
    }

    [Fact]
    public void SingleVoxelHasSixQuads()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);

        MeshBuilder mesh = MeshOf(world, new ChunkCoord(0, 0, 0));

        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(24, mesh.VertexCount);
        Assert.Equal(6.0, mesh.TotalArea(), 4);
    }

    [Fact]
    public void EveryVertexCarriesTheIndexOfItsOwnFace()
    {
        // The renderer looks its normal and its flat shade up from this. A vertex on the wrong face
        // is lit as though it pointed somewhere else, which no area or count assertion would catch.
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);

        MeshBuilder mesh = MeshOf(world, new ChunkCoord(0, 0, 0));
        ReadOnlySpan<MeshVertex> vertices = mesh.Vertices;

        var seen = new HashSet<int>();
        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            int face = (int)vertices[quad * 4].Face;
            Assert.InRange(face, 0, FaceInfo.Count - 1);
            Assert.True(seen.Add(face), $"Face {face} was emitted twice.");

            // All four corners of a quad belong to the same face.
            for (int corner = 1; corner < 4; corner++)
            {
                Assert.Equal(face, (int)vertices[(quad * 4) + corner].Face);
            }

            // The quad has to actually lie in the plane its index claims: a face index that merely
            // happens to be unique would still pass everything above.
            Vector3 a = vertices[quad * 4].Position;
            Vector3 b = vertices[(quad * 4) + 1].Position;
            Vector3 c = vertices[(quad * 4) + 2].Position;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));

            Assert.Equal(1f, Vector3.Dot(normal, FaceInfo.Normal((Face)face)), 4);
        }

        Assert.Equal(FaceInfo.Count, seen.Count);
    }

    [Fact]
    public void SharedFaceBetweenTwoVoxelsIsCulled()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);
        world.SetVoxel(1, 0, 0, 1);

        MeshBuilder mesh = MeshOf(world, new ChunkCoord(0, 0, 0));

        // 12 faces total, minus the two that touch each other — and the long sides, alike, one quad each.
        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(10.0, mesh.TotalArea(), 4);

        var naive = new MeshBuilder();
        EditMesher.BuildChunk(world, new ChunkCoord(0, 0, 0), naive, merge: false);
        Assert.Equal(10, naive.QuadCount);
    }

    [Fact]
    public void FullyEnclosedVoxelContributesNothing()
    {
        var world = new VoxelWorld();
        for (int x = 0; x <= 2; x++)
        {
            for (int y = 0; y <= 2; y++)
            {
                for (int z = 0; z <= 2; z++)
                {
                    world.SetVoxel(x, y, z, 1);
                }
            }
        }

        MeshBuilder mesh = MeshOf(world, new ChunkCoord(0, 0, 0));

        // A 3x3x3 cube: only the 6 outer 3x3 faces survive, each one quad.
        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(6 * 9.0, mesh.TotalArea(), 4);
    }

    [Fact]
    public void SolidChunkProducesOnlyItsShell()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < Chunk.Size; x++)
        {
            for (int y = 0; y < Chunk.Size; y++)
            {
                for (int z = 0; z < Chunk.Size; z++)
                {
                    world.SetVoxel(x, y, z, 7);
                }
            }
        }

        MeshBuilder mesh = MeshOf(world, new ChunkCoord(0, 0, 0));

        // 6 sides of 32x32 faces, each side one quad; one to a face without merging.
        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(6.0 * Chunk.Size * Chunk.Size, mesh.TotalArea(), 1);

        var naive = new MeshBuilder();
        EditMesher.BuildChunk(world, new ChunkCoord(0, 0, 0), naive, merge: false);
        Assert.Equal(6 * Chunk.Size * Chunk.Size, naive.QuadCount);
    }

    /// <summary>
    /// A merged face looks exactly like the faces it replaces: at every corner of every face, the
    /// shading the merged quad gives there is the shading that face had — on a shape of walls, steps
    /// and a pillar, where the shading changes across the floor.
    /// </summary>
    [Fact]
    public void MergedFacesShadeEveryCornerAsItsOwnFaceDid()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                world.SetVoxel(x, 0, z, 3);
            }

            world.SetVoxel(x, 1, 0, 3);
            world.SetVoxel(x, 2, 0, 3);
        }

        world.SetVoxel(4, 1, 4, 5);
        world.SetVoxel(4, 2, 4, 5);
        world.SetVoxel(6, 1, 6, 3);

        Assert.True(AssertMergedLooksLikeFaces(world, new ChunkCoord(0, 0, 0)) > 1);
    }

    /// <summary>
    /// The same over the seams between chunks, where a face and its shading come from the chunk
    /// next door, and with faces painted another colour than their voxel: a random clutter round the
    /// corner eight chunks meet at.
    /// </summary>
    [Fact]
    public void MergedFacesMatchAcrossChunkSeamsAndPaintedFaces()
    {
        var random = new Random(5);
        var world = new VoxelWorld();
        for (int x = 24; x < 40; x++)
        {
            for (int y = 24; y < 40; y++)
            {
                for (int z = 24; z < 40; z++)
                {
                    if (random.Next(10) < 4 || y < 29)
                    {
                        world.SetVoxel(x, y, z, (byte)(y < 29 ? 3 : random.Next(1, 4)));
                    }
                }
            }
        }

        for (int i = 0; i < 300; i++)
        {
            world.SetFaceColor(random.Next(24, 40), random.Next(24, 40), random.Next(24, 40), (Face)random.Next(FaceInfo.Count), (byte)random.Next(5, 8));
        }

        int merged = 0;
        foreach (ChunkCoord coord in world.Chunks.Keys)
        {
            merged += AssertMergedLooksLikeFaces(world, coord);
        }

        Assert.Equal(8, world.Chunks.Count);
        Assert.True(merged > 8);
    }

    /// <summary>
    /// Checks a chunk's merged mesh against its one quad a face: the same area, and at every corner
    /// of every face a merged quad of the same face and colour, shading it as the face did. Returns
    /// how many faces fewer the merged mesh has.
    /// </summary>
    private static int AssertMergedLooksLikeFaces(VoxelWorld world, ChunkCoord coord)
    {
        var merged = new MeshBuilder();
        var naive = new MeshBuilder();
        EditMesher.BuildChunk(world, coord, merged);
        EditMesher.BuildChunk(world, coord, naive, merge: false);

        Assert.True(merged.QuadCount <= naive.QuadCount);
        Assert.Equal(naive.TotalArea(), merged.TotalArea(), 3);

        MeshVertex[] quads = merged.Vertices.ToArray();
        MeshVertex[] faces = naive.Vertices.ToArray();
        for (int face = 0; face < faces.Length; face += 4)
        {
            Vector3 middle = (faces[face].Position + faces[face + 2].Position) * 0.5f;
            int found = Enumerable.Range(0, quads.Length / 4).First(q =>
                quads[q * 4].FaceIndex == faces[face].FaceIndex && Contains(quads, q * 4, middle));

            Assert.Equal(faces[face].Rgba & 0x00FFFFFFu, quads[found * 4].Rgba & 0x00FFFFFFu);
            for (int corner = 0; corner < 4; corner++)
            {
                MeshVertex expected = faces[face + corner];
                Assert.Equal(expected.Rgba >> 24, (uint)MathF.Round(ShadeAt(quads, found * 4, expected.Position)));
            }
        }

        return naive.QuadCount - merged.QuadCount;
    }

    /// <summary>Whether a point lies on a quad, its corners p0, p0 + e1, p0 + e1 + e2, p0 + e2.</summary>
    private static bool Contains(MeshVertex[] quads, int first, Vector3 point)
    {
        (float s, float t, float off) = Parameters(quads, first, point);
        return off < 1e-3f && s >= -1e-3f && s <= 1f + 1e-3f && t >= -1e-3f && t <= 1f + 1e-3f;
    }

    /// <summary>The corners' openness, in the colours' alpha, blended to a point of the quad.</summary>
    private static float ShadeAt(MeshVertex[] quads, int first, Vector3 point)
    {
        (float s, float t, _) = Parameters(quads, first, point);
        float a0 = quads[first].Rgba >> 24, a1 = quads[first + 1].Rgba >> 24, a2 = quads[first + 2].Rgba >> 24, a3 = quads[first + 3].Rgba >> 24;
        return ((1f - s) * (1f - t) * a0) + (s * (1f - t) * a1) + (s * t * a2) + ((1f - s) * t * a3);
    }

    private static (float S, float T, float Off) Parameters(MeshVertex[] quads, int first, Vector3 point)
    {
        Vector3 origin = quads[first].Position;
        Vector3 e1 = quads[first + 1].Position - origin;
        Vector3 e2 = quads[first + 3].Position - origin;
        Vector3 offset = point - origin;
        Vector3 normal = Vector3.Normalize(Vector3.Cross(e1, e2));
        return (Vector3.Dot(offset, e1) / e1.LengthSquared(), Vector3.Dot(offset, e2) / e2.LengthSquared(), MathF.Abs(Vector3.Dot(offset, normal)));
    }

    [Fact]
    public void NeighbouringChunkCullsTheSeamFace()
    {
        var world = new VoxelWorld();
        world.SetVoxel(31, 0, 0, 1);   // last voxel of chunk (0,0,0)
        world.SetVoxel(32, 0, 0, 1);   // first voxel of chunk (1,0,0)

        MeshBuilder left = MeshOf(world, new ChunkCoord(0, 0, 0));
        MeshBuilder right = MeshOf(world, new ChunkCoord(1, 0, 0));

        Assert.Equal(5, left.QuadCount);
        Assert.Equal(5, right.QuadCount);
    }

    [Fact]
    public void QuadWindingMatchesTheOutwardNormal()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);

        MeshBuilder mesh = MeshOf(world, new ChunkCoord(0, 0, 0));
        ReadOnlySpan<MeshVertex> vertices = mesh.Vertices;

        var center = new Vector3(0.5f, 0.5f, 0.5f);
        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            Vector3 a = vertices[quad * 4].Position;
            Vector3 b = vertices[quad * 4 + 1].Position;
            Vector3 c = vertices[quad * 4 + 2].Position;

            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            Vector3 outward = Vector3.Normalize((a + c) * 0.5f - center);

            // Counter-clockwise from outside means the geometric normal points away from the voxel.
            Assert.True(Vector3.Dot(normal, outward) > 0.9f, $"Quad {quad} is wound inwards.");
        }
    }

    [Fact]
    public void EmptyChunkProducesEmptyMesh()
    {
        var world = new VoxelWorld();
        MeshBuilder mesh = MeshOf(world, new ChunkCoord(3, 3, 3));
        Assert.True(mesh.IsEmpty);
    }

    [Fact]
    public void ALoneVoxelIsOpenAtEveryCornerAndAWallDarkensTheCornersBesideIt()
    {
        var alone = new VoxelWorld();
        alone.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        var builder = new MeshBuilder();
        EditMesher.BuildChunk(alone, new ChunkCoord(0, 0, 0), builder);
        Assert.All(builder.Vertices.ToArray(), v => Assert.Equal(255u, v.Rgba >> 24));

        // A floor with a wall standing on it: where they meet, the floor's corners are closed in.
        var corner = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        {
            corner.SetVoxel(x, 0, 0, Palette.WhiteIndex);
        }

        corner.SetVoxel(0, 1, 0, Palette.WhiteIndex);
        EditMesher.BuildChunk(corner, new ChunkCoord(0, 0, 0), builder);

        var reader = new VoxelReader(corner);
        int[] openness = [.. Enumerable.Range(0, 4).Select(c => EditMesher.Occlusion(ref reader, new Int3(1, 0, 0), Face.PosY, c))];
        Assert.Contains(openness, o => o < 3);
        Assert.Contains(builder.Vertices.ToArray(), v => (v.Rgba >> 24) < 255u);
    }
}
