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
            int face = (int)vertices[quad * 4].FaceIndex;
            Assert.InRange(face, 0, FaceInfo.Count - 1);
            Assert.True(seen.Add(face), $"Face {face} was emitted twice.");

            // All four corners of a quad belong to the same face.
            for (int corner = 1; corner < 4; corner++)
            {
                Assert.Equal(face, (int)vertices[(quad * 4) + corner].FaceIndex);
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

        // 12 faces total, minus the two that touch each other.
        Assert.Equal(10, mesh.QuadCount);
        Assert.Equal(10.0, mesh.TotalArea(), 4);
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

        // A 3x3x3 cube: only the 6 outer 3x3 faces survive.
        Assert.Equal(6 * 9, mesh.QuadCount);
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

        // 6 sides x 32x32 faces. Naive per-face meshing; the greedy mesher must reduce this to 6.
        Assert.Equal(6 * Chunk.Size * Chunk.Size, mesh.QuadCount);
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
}
