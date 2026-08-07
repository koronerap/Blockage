using System.Numerics;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// The regression gate from EditorApp.md §4b: merging quads must reduce the vertex count without
/// changing the surface by a single unit. Run against random volumes, these two assertions catch
/// very nearly every mistake the merge logic can make.
/// </summary>
public class GreedyMesherTests
{
    private static double NaiveArea(VoxelWorld world)
    {
        var naive = new MeshBuilder();
        EditMesher.BuildWorldNaive(world, naive);
        return naive.TotalArea();
    }

    private static int NaiveVertexCount(VoxelWorld world)
    {
        var naive = new MeshBuilder();
        EditMesher.BuildWorldNaive(world, naive);
        return naive.VertexCount;
    }

    private static void AssertMatchesNaive(VoxelWorld world)
    {
        ExportMesh greedy = GreedyMesher.Build(world);

        Assert.Equal(NaiveArea(world), greedy.TotalArea(), 4);
        Assert.True(
            greedy.VertexCount <= NaiveVertexCount(world),
            $"Greedy mesh has {greedy.VertexCount} vertices, naive has {NaiveVertexCount(world)}.");
    }

    [Fact]
    public void EmptyWorldProducesNothing()
    {
        ExportMesh mesh = GreedyMesher.Build(new VoxelWorld());
        Assert.Equal(0, mesh.QuadCount);
        Assert.Equal(0d, mesh.TotalArea());
    }

    [Fact]
    public void SingleVoxelIsSixQuads()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);

        ExportMesh mesh = GreedyMesher.Build(world);

        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(24, mesh.VertexCount);
        Assert.Equal(6d, mesh.TotalArea(), 4);
    }

    [Fact]
    public void FlatWallCollapsesToOneQuadPerSide()
    {
        // A 10x10 wall one voxel thick: the two large faces must each become a single quad.
        var world = new VoxelWorld();
        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                world.SetVoxel(x, y, 0, 5);
            }
        }

        ExportMesh mesh = GreedyMesher.Build(world);

        // 2 large faces + 4 edge strips, each also a single quad.
        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(24, mesh.VertexCount);
        // Before merging: 2 faces x 100 voxels + 4 edges x 10 = 240 quads = 960 vertices.
        Assert.Equal(960, NaiveVertexCount(world));
        Assert.Equal(NaiveArea(world), mesh.TotalArea(), 4);
    }

    [Fact]
    public void SingleColorChunkCubeCollapsesToSixQuads()
    {
        // The headline case from the spec: 6144 quads become 6.
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

        ExportMesh mesh = GreedyMesher.Build(world);

        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(24, mesh.VertexCount);
        Assert.Single(mesh.UsedPaletteIndices());
        Assert.Equal(6 * Chunk.Size * Chunk.Size, mesh.TotalArea(), 4);
        Assert.Equal(NaiveArea(world), mesh.TotalArea(), 4);
    }

    [Fact]
    public void TwoColorsNeverMergeIntoOneQuad()
    {
        // A 2x1x1 bar of two different colors: the top face must stay two quads.
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 3);
        world.SetVoxel(1, 0, 0, 4);

        ExportMesh mesh = GreedyMesher.Build(world);

        Assert.Equal(10, mesh.QuadCount);
        Assert.Equal(2, mesh.UsedPaletteIndices().Count);
        Assert.Equal(NaiveArea(world), mesh.TotalArea(), 4);
    }

    [Fact]
    public void OppositeNormalsNeverMerge()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);

        ExportMesh mesh = GreedyMesher.Build(world);

        var normals = new HashSet<Vector3>(mesh.Normals);
        Assert.Equal(6, normals.Count);
    }

    [Fact]
    public void QuadWindingMatchesTheOutwardNormal()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int z = 0; z < 4; z++)
            {
                world.SetVoxel(x, 0, z, 2);
            }
        }

        ExportMesh mesh = GreedyMesher.Build(world);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            Vector3 a = mesh.Positions[quad * 4];
            Vector3 b = mesh.Positions[quad * 4 + 1];
            Vector3 c = mesh.Positions[quad * 4 + 2];

            Vector3 geometric = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            Vector3 declared = mesh.Normals[quad * 4];

            Assert.True(
                Vector3.Dot(geometric, declared) > 0.99f,
                $"Quad {quad} winds against its normal {declared}.");
        }
    }

    [Fact]
    public void AllFourCornersOfAQuadShareOneUv()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                world.SetVoxel(x, 0, z, 42);
            }
        }

        ExportMesh mesh = GreedyMesher.Build(world);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            Vector2 uv = mesh.Uvs[quad * 4];
            for (int corner = 1; corner < 4; corner++)
            {
                Assert.Equal(uv, mesh.Uvs[quad * 4 + corner]);
            }
        }
    }

    [Fact]
    public void HollowShellMatchesNaive()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 12; x++)
        {
            for (int y = 0; y < 12; y++)
            {
                for (int z = 0; z < 12; z++)
                {
                    bool onShell = x is 0 or 11 || y is 0 or 11 || z is 0 or 11;
                    if (onShell)
                    {
                        world.SetVoxel(x, y, z, 9);
                    }
                }
            }
        }

        AssertMatchesNaive(world);
    }

    [Fact]
    public void ShapeStraddlingChunkBoundariesMatchesNaive()
    {
        var world = new VoxelWorld();
        for (int x = 20; x < 45; x++)
        {
            for (int y = 28; y < 36; y++)
            {
                for (int z = -5; z < 5; z++)
                {
                    world.SetVoxel(x, y, z, 11);
                }
            }
        }

        Assert.True(world.Chunks.Count > 1);
        AssertMatchesNaive(world);

        // Uniform box across chunk seams: merging must not stop at a chunk edge.
        ExportMesh mesh = GreedyMesher.Build(world);
        Assert.Equal(6, mesh.QuadCount);
    }

    [Fact]
    public void NegativeCoordinatesMatchNaive()
    {
        var world = new VoxelWorld();
        for (int x = -37; x < -20; x++)
        {
            for (int y = -70; y < -60; y++)
            {
                world.SetVoxel(x, y, -33, (byte)(1 + (x + y & 7)));
            }
        }

        AssertMatchesNaive(world);
    }

    [Theory]
    [InlineData(1, 0.65f, 4)]
    [InlineData(2, 0.30f, 12)]
    [InlineData(3, 0.85f, 2)]
    [InlineData(4, 0.50f, 1)]
    [InlineData(5, 0.15f, 40)]
    [InlineData(6, 0.95f, 200)]
    public void RandomVolumesPreserveSurfaceArea(int seed, float density, int colorCount)
    {
        var random = new Random(seed);
        var world = new VoxelWorld();

        // Deliberately spans chunk boundaries in every axis, including negatives.
        for (int x = -18; x < 18; x++)
        {
            for (int y = -6; y < 40; y++)
            {
                for (int z = -35; z < 5; z++)
                {
                    if (random.NextSingle() < density)
                    {
                        world.SetVoxel(x, y, z, (byte)(1 + random.Next(colorCount)));
                    }
                }
            }
        }

        AssertMatchesNaive(world);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomSlabsAndStripesPreserveSurfaceArea(int seed)
    {
        var random = new Random(seed);
        var world = new VoxelWorld();

        // Large single-color slabs with stripes through them: the pattern greedy meshing gains most
        // from, and the one where a merge bug is most likely to eat or duplicate a face.
        for (int slab = 0; slab < 6; slab++)
        {
            int height = random.Next(1, 5);
            int baseY = slab * 7;
            byte color = (byte)(1 + random.Next(6));

            for (int x = -20; x < 20; x++)
            {
                for (int z = -20; z < 20; z++)
                {
                    for (int y = baseY; y < baseY + height; y++)
                    {
                        byte index = x % 5 == 0 ? (byte)(200 + (z & 3)) : color;
                        world.SetVoxel(x, y, z, index);
                    }
                }
            }
        }

        AssertMatchesNaive(world);
    }

    [Fact]
    public void MergingActuallyReducesVertexCountSubstantially()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 40; x++)
        {
            for (int z = 0; z < 40; z++)
            {
                for (int y = 0; y < 6; y++)
                {
                    world.SetVoxel(x, y, z, 3);
                }
            }
        }

        ExportMesh greedy = GreedyMesher.Build(world);
        int naive = NaiveVertexCount(world);

        // A uniform slab is the best case: it must come out as the 6 sides of a box.
        Assert.Equal(6, greedy.QuadCount);
        Assert.True(naive / (double)greedy.VertexCount > 100d, $"Only reduced {naive} to {greedy.VertexCount}.");
    }
}
