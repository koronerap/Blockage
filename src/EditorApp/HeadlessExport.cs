using System.Diagnostics;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Voxels;

namespace EditorApp;

/// <summary>
/// Runs the whole export chain on a level without opening a window: save, greedy mesh, write OBJ
/// and GLB, report the reduction. Used to check the pipeline end to end on a real-size level, and
/// usable from a build script.
/// </summary>
public static class HeadlessExport
{
    /// <param name="outputDirectory">Where the files are written.</param>
    /// <param name="levelPath">A .vxlevel to load, or null to use the built-in demo scene.</param>
    public static int Run(string outputDirectory, string? levelPath)
    {
        Directory.CreateDirectory(outputDirectory);

        VoxelWorld world;
        string name;

        if (levelPath is not null)
        {
            world = VxLevelFile.Load(levelPath);
            name = Path.GetFileNameWithoutExtension(levelPath);
        }
        else
        {
            world = new VoxelWorld();
            DemoScene.Fill(world);
            name = "demo";

            string projectPath = Path.Combine(outputDirectory, name + VxLevelFile.Extension);
            VxLevelFile.Save(world, projectPath, name);
            Console.WriteLine($"Saved  {projectPath}  ({new FileInfo(projectPath).Length / 1024.0:0.0} KB)");
        }

        Console.WriteLine($"Level  {world.SolidCount:N0} voxels in {world.Chunks.Count} chunks");

        var stopwatch = Stopwatch.StartNew();
        var naive = new MeshBuilder();
        EditMesher.BuildWorldNaive(world, naive);
        double naiveMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        ExportMesh greedy = GreedyMesher.Build(world);
        double greedyMs = stopwatch.Elapsed.TotalMilliseconds;

        double naiveArea = naive.TotalArea();
        double greedyArea = greedy.TotalArea();

        Console.WriteLine($"Naive  {naive.QuadCount,9:N0} quads  {naive.VertexCount,10:N0} vertices  ({naiveMs:0} ms)");
        Console.WriteLine($"Greedy {greedy.QuadCount,9:N0} quads  {greedy.VertexCount,10:N0} vertices  ({greedyMs:0} ms)");
        Console.WriteLine($"       -{100.0 * (1.0 - greedy.VertexCount / (double)naive.VertexCount):0.0}% vertices");
        Console.WriteLine($"Area   naive {naiveArea:0.###}  greedy {greedyArea:0.###}  delta {Math.Abs(naiveArea - greedyArea):0.######}");

        // The §4b invariant, checked on the real level and not only in unit tests.
        if (Math.Abs(naiveArea - greedyArea) > 1e-3)
        {
            Console.Error.WriteLine("FAIL: greedy meshing changed the surface area.");
            return 1;
        }

        // The same default the dialog offers: a real sheet the model can be textured on.
        UvAtlas atlas = UvUnwrap.Apply(greedy);
        Console.WriteLine(
            $"Atlas  {atlas.Width} x {atlas.Height}  ({atlas.Charts.Count:N0} charts from "
            + $"{atlas.Islands.Count:N0} faces, {atlas.TexelsPerVoxel} texels per voxel, "
            + $"{atlas.Coverage:P0} covered)");

        long sheetArea = (long)atlas.Width * atlas.Height;
        long chartArea = atlas.Charts.Sum(c => (long)c.Width * c.Height);
        long faceArea = atlas.Islands.Sum(i => (long)i.Width * i.Height);
        long packedArea = (long)atlas.Width
            * atlas.Charts.Max(c => c.Y + c.Height + atlas.Padding);

        Console.WriteLine(
            $"       charts occupy {chartArea / (double)sheetArea:P0} of the sheet "
            + $"({chartArea / (double)packedArea:P0} of what the packer laid out, before rounding), "
            + $"and faces fill {faceArea / (double)chartArea:P0} of the charts");

        var options = new ExportOptions { Atlas = atlas };
        foreach (IMeshExporter exporter in new IMeshExporter[] { new ObjExporter(), new GltfExporter(binary: true) })
        {
            string path = Path.Combine(outputDirectory, name + exporter.Extension);
            ExportResult result = exporter.Export(greedy, world.Palette, path, options);

            foreach (string file in result.FilesWritten)
            {
                Console.WriteLine($"Wrote  {file}  ({new FileInfo(file).Length / 1024.0:0.0} KB)");
            }
        }

        return 0;
    }
}
