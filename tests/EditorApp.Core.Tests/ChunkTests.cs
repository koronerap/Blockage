using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class ChunkTests
{
    [Fact]
    public void LinearIndexRoundTrips()
    {
        for (int y = 0; y < Chunk.Size; y += 7)
        {
            for (int z = 0; z < Chunk.Size; z += 5)
            {
                for (int x = 0; x < Chunk.Size; x += 3)
                {
                    int linear = Chunk.LinearIndex(x, y, z);
                    Assert.Equal(new Int3(x, y, z), Chunk.FromLinearIndex(linear));
                }
            }
        }
    }

    [Fact]
    public void OccupancyTracksIndexArray()
    {
        var chunk = new Chunk();
        Assert.True(chunk.IsEmpty);

        Assert.True(chunk.Set(5, 6, 7, 42));
        Assert.True(chunk.IsSolid(5, 6, 7));
        Assert.Equal(42, chunk.Get(5, 6, 7));
        Assert.Equal(1, chunk.SolidCount);

        // Recoloring is a change but does not alter occupancy.
        Assert.True(chunk.Set(5, 6, 7, 43));
        Assert.Equal(1, chunk.SolidCount);

        // Writing the same value again is a no-op.
        Assert.False(chunk.Set(5, 6, 7, 43));

        Assert.True(chunk.Set(5, 6, 7, Palette.EmptyIndex));
        Assert.False(chunk.IsSolid(5, 6, 7));
        Assert.Equal(0, chunk.SolidCount);
        Assert.True(chunk.IsEmpty);
    }

    [Fact]
    public void LoadIndicesRebuildsOccupancy()
    {
        var source = new byte[Chunk.VoxelCount];
        source[Chunk.LinearIndex(1, 2, 3)] = 9;
        source[Chunk.LinearIndex(31, 31, 31)] = 200;

        var chunk = new Chunk();
        chunk.LoadIndices(source);

        Assert.Equal(2, chunk.SolidCount);
        Assert.True(chunk.IsSolid(1, 2, 3));
        Assert.True(chunk.IsSolid(31, 31, 31));
        Assert.False(chunk.IsSolid(0, 0, 0));
    }

    [Fact]
    public void LocalBoundsAreTight()
    {
        var chunk = new Chunk();
        Assert.False(chunk.TryGetLocalBounds(out _, out _));

        chunk.Set(4, 9, 2, 1);
        chunk.Set(20, 11, 30, 1);

        Assert.True(chunk.TryGetLocalBounds(out Int3 min, out Int3 max));
        Assert.Equal(new Int3(4, 9, 2), min);
        Assert.Equal(new Int3(20, 11, 30), max);
    }

    /// <summary>
    /// The bounds are kept between asks, so they have to follow every voxel that comes or goes —
    /// checked against every voxel, over a run of random edits and a load.
    /// </summary>
    [Fact]
    public void KeptBoundsFollowEveryEdit()
    {
        var random = new Random(9);
        var chunk = new Chunk();
        for (int step = 0; step < 400; step++)
        {
            int count = random.Next(1, 6);
            for (int i = 0; i < count; i++)
            {
                chunk.Set(random.Next(Chunk.Size), random.Next(Chunk.Size), random.Next(Chunk.Size), (byte)(random.Next(3) == 0 ? 0 : random.Next(1, 9)));
            }

            if (step == 200)
            {
                byte[] indices = new byte[Chunk.VoxelCount];
                indices[Chunk.LinearIndex(31, 0, 17)] = 4;
                indices[Chunk.LinearIndex(3, 31, 0)] = 4;
                chunk.LoadIndices(indices);
            }

            Int3 min = new(int.MaxValue, int.MaxValue, int.MaxValue);
            Int3 max = new(int.MinValue, int.MinValue, int.MinValue);
            for (int linear = 0; linear < Chunk.VoxelCount; linear++)
            {
                Int3 at = Chunk.FromLinearIndex(linear);
                if (chunk.IsSolid(at.X, at.Y, at.Z))
                {
                    min = Int3.Min(min, at);
                    max = Int3.Max(max, at);
                }
            }

            bool any = chunk.TryGetLocalBounds(out Int3 keptMin, out Int3 keptMax);
            Assert.Equal(!chunk.IsEmpty, any);
            if (any)
            {
                Assert.Equal(min, keptMin);
                Assert.Equal(max, keptMax);
            }
        }
    }

    [Fact]
    public void NegativeWorldCoordinatesMapToTheCorrectChunk()
    {
        Assert.Equal(new ChunkCoord(-1, -1, -1), ChunkCoord.FromWorld(-1, -1, -1));
        Assert.Equal(new ChunkCoord(-1, 0, 0), ChunkCoord.FromWorld(-32, 0, 0));
        Assert.Equal(new ChunkCoord(-2, 0, 0), ChunkCoord.FromWorld(-33, 0, 0));
        Assert.Equal(new Int3(-64, 0, 0), new ChunkCoord(-2, 0, 0).Origin);

        // The local coordinate must land inside the chunk for negative positions too.
        Assert.Equal(31, -33 & Chunk.SizeMask);
    }

    [Fact]
    public void WorldStoresAndReadsAcrossChunkBoundaries()
    {
        var world = new VoxelWorld();

        Assert.True(world.SetVoxel(-1, 5, 40, 12));
        Assert.True(world.SetVoxel(0, 5, 40, 13));

        Assert.Equal(12, world.GetVoxel(-1, 5, 40));
        Assert.Equal(13, world.GetVoxel(0, 5, 40));
        Assert.Equal(2, world.Chunks.Count);
        Assert.Equal(2, world.SolidCount);

        Assert.True(world.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(new Int3(-1, 5, 40), min);
        Assert.Equal(new Int3(0, 5, 40), max);
    }

    [Fact]
    public void EditingAChunkFaceDirtiesTheNeighbour()
    {
        var world = new VoxelWorld();
        world.SetVoxel(-1, 0, 0, 1);   // creates chunk (-1, 0, 0)
        world.SetVoxel(0, 0, 0, 1);    // creates chunk (0, 0, 0)
        world.ConsumeDirtyChunks();

        // Local x = 0 of chunk (0,0,0) touches chunk (-1,0,0).
        world.SetVoxel(0, 1, 0, 2);

        IReadOnlySet<ChunkCoord> dirty = world.DirtyChunks;
        Assert.Contains(new ChunkCoord(0, 0, 0), dirty);
        Assert.Contains(new ChunkCoord(-1, 0, 0), dirty);
    }

    [Fact]
    public void PruneEmptyChunksDropsClearedChunks()
    {
        var world = new VoxelWorld();
        world.SetVoxel(100, 100, 100, 5);
        Assert.Single(world.Chunks);

        world.SetVoxel(100, 100, 100, Palette.EmptyIndex);
        world.PruneEmptyChunks();
        Assert.Empty(world.Chunks);
    }
}
