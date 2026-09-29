using System.Diagnostics;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Voxels;

namespace EditorApp;

/// <summary>
/// Measures the operations that decide whether a level scale is workable: building every chunk's
/// edit mesh, greedy meshing the whole level, and the project file round trip. No window and no GL,
/// so the numbers are the CPU cost alone.
/// </summary>
public static class StressCheck
{
    public static int Run(int halfExtent)
    {
        Console.WriteLine($"Stress check - terrain half-extent {halfExtent} ({halfExtent * 2}x{halfExtent * 2} footprint)");
        Console.WriteLine();

        RunCase("Terrain", () =>
        {
            var world = new VoxelWorld();
            DemoScene.Fill(world, halfExtent);
            return world;
        });

        Console.WriteLine();

        // The level size aimed at (Fullreleaseplan 9.1): 512 x 128 x 512, hills and caves, surfaces
        // at every height — the meshing worst case a real level comes near.
        RunCase("Level 512x128x512, hills and caves", () =>
        {
            var world = new VoxelWorld();
            for (int x = 0; x < 512; x++)
            {
                for (int z = 0; z < 512; z++)
                {
                    int top = 24 + (int)(90f * EditorApp.Core.Editing.Noise.Fractal2(x / 96f, z / 96f, 7, 4, 0.5f));
                    for (int y = 0; y < Math.Min(top, 128); y++)
                    {
                        // Caves where the noise runs high, never at the surface.
                        if (y < top - 4 && EditorApp.Core.Editing.Noise.Fractal3(x / 24f, y / 16f, z / 24f, 11, 3, 0.5f) > 0.62f)
                        {
                            continue;
                        }

                        world.SetVoxel(x, y, z, (byte)(y >= top - 1 ? 78 : y >= top - 4 ? 96 : 8));
                    }
                }
            }

            return world;
        });

        Console.WriteLine();

        // A solid block is the memory worst case: every chunk fully populated.
        int side = Math.Max(halfExtent, 32);
        RunCase($"Solid {side}x{side}x{side} block", () =>
        {
            var world = new VoxelWorld();
            for (int x = 0; x < side; x++)
            {
                for (int y = 0; y < side; y++)
                {
                    for (int z = 0; z < side; z++)
                    {
                        world.SetVoxel(x, y, z, 12);
                    }
                }
            }

            return world;
        });

        return 0;
    }

    private static void RunCase(string label, Func<VoxelWorld> build)
    {
        long beforeMemory = GC.GetTotalMemory(forceFullCollection: true);

        var clock = Stopwatch.StartNew();
        VoxelWorld world = build();
        double buildMs = clock.Elapsed.TotalMilliseconds;

        long afterMemory = GC.GetTotalMemory(forceFullCollection: true);

        Console.WriteLine($"{label}");
        Console.WriteLine($"  {world.SolidCount,12:N0} voxels in {world.Chunks.Count:N0} chunks, built in {buildMs:0} ms");
        Console.WriteLine($"  {(afterMemory - beforeMemory) / (1024.0 * 1024.0),12:0.0} MB world memory");

        // Every chunk's edit mesh: the work a full reload has to do before the first frame.
        var builder = new MeshBuilder();
        clock.Restart();
        long vertices = 0;
        foreach (ChunkCoord coord in world.Chunks.Keys)
        {
            EditMesher.BuildChunk(world, coord, builder);
            vertices += builder.VertexCount;
        }

        double meshMs = clock.Elapsed.TotalMilliseconds;
        double perChunk = world.Chunks.Count > 0 ? meshMs / world.Chunks.Count : 0;
        Console.WriteLine($"  {meshMs,12:0} ms edit-meshing every chunk ({perChunk:0.00} ms per chunk, {vertices:N0} vertices)");

        // The same with a quad for every face, what merging is weighed against.
        clock.Restart();
        long faceVertices = 0;
        foreach (ChunkCoord coord in world.Chunks.Keys)
        {
            EditMesher.BuildChunk(world, coord, builder, merge: false);
            faceVertices += builder.VertexCount;
        }

        meshMs = clock.Elapsed.TotalMilliseconds;
        perChunk = world.Chunks.Count > 0 ? meshMs / world.Chunks.Count : 0;
        Console.WriteLine($"  {meshMs,12:0} ms with a quad a face ({perChunk:0.00} ms per chunk, {faceVertices:N0} vertices)");

        clock.Restart();
        ExportMesh greedy = GreedyMesher.Build(world);
        Console.WriteLine($"  {clock.Elapsed.TotalMilliseconds,12:0} ms greedy meshing "
            + $"({greedy.QuadCount:N0} quads, {greedy.VertexCount:N0} vertices, "
            + $"-{100.0 * (1.0 - greedy.VertexCount / (double)Math.Max(vertices, 1)):0.0}%)");

        string path = Path.Combine(Path.GetTempPath(), $"editorapp-stress-{Guid.NewGuid():N}.vxlevel");
        try
        {
            clock.Restart();
            VxLevelFile.Save(world, path);
            double saveMs = clock.Elapsed.TotalMilliseconds;
            double sizeMb = new FileInfo(path).Length / (1024.0 * 1024.0);

            clock.Restart();
            VoxelWorld reloaded = VxLevelFile.Load(path);
            double loadMs = clock.Elapsed.TotalMilliseconds;

            Console.WriteLine($"  {saveMs,12:0} ms save  ({sizeMb:0.00} MB on disk)");
            Console.WriteLine($"  {loadMs,12:0} ms load  (hash {(reloaded.ContentHash() == world.ContentHash() ? "matches" : "DIFFERS")})");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
