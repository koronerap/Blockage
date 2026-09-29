using System.Diagnostics;
using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp;

/// <summary>
/// Runs the whole export chain on a level without opening a window: greedy mesh, the surface-area
/// check, then every format — OBJ, glTF, GLB, FBX and MagicaVoxel. Used to check the pipeline end to
/// end on a real-size level, by the script that opens the files in Blender and Unity
/// (Fullreleaseplan 8.6), and usable from a build script.
/// </summary>
public static class HeadlessExport
{
    /// <param name="outputDirectory">Where the files are written.</param>
    /// <param name="levelPath">A .vxlevel or .vox to load, or null for the sample level: a little of everything an export carries.</param>
    public static int Run(string outputDirectory, string? levelPath)
    {
        Directory.CreateDirectory(outputDirectory);

        VoxelScene scene;
        string name;

        if (levelPath is not null)
        {
            scene = levelPath.EndsWith(VoxFile.Extension, StringComparison.OrdinalIgnoreCase)
                ? VoxFile.Load(levelPath)
                : VxLevelFile.LoadScene(levelPath);
            name = Path.GetFileNameWithoutExtension(levelPath);
        }
        else
        {
            scene = Sample();
            name = "sample";

            string projectPath = Path.Combine(outputDirectory, name + VxLevelFile.Extension);
            VxLevelFile.Save(scene, projectPath, name);
            Console.WriteLine($"Saved  {projectPath}  ({new FileInfo(projectPath).Length / 1024.0:0.0} KB)");
        }

        Console.WriteLine($"Level  {scene.SolidCount:N0} voxels in {scene.Objects.Count} objects, {scene.Lights.Count} lights");

        // The §4b invariant, checked on the real level and not only in unit tests: merging must not
        // change the surface by a single unit.
        var stopwatch = Stopwatch.StartNew();
        double naiveArea = 0, greedyArea = 0;
        int naiveQuads = 0, greedyQuads = 0;
        foreach (VoxelObject o in scene.Objects.Where(o => o.IsExported && !o.IsEmpty))
        {
            var naive = new MeshBuilder();
            EditMesher.BuildWorldNaive(o.Shown, naive);
            ExportMesh greedy = GreedyMesher.Build(o.Shown, uvSelector: null, mergeAcrossColors: true);
            naiveArea += naive.TotalArea();
            greedyArea += greedy.TotalArea();
            naiveQuads += naive.QuadCount;
            greedyQuads += greedy.QuadCount;
        }

        Console.WriteLine($"Greedy {greedyQuads:N0} quads from {naiveQuads:N0} ({stopwatch.Elapsed.TotalMilliseconds:0} ms)");
        Console.WriteLine($"Area   naive {naiveArea:0.###}  greedy {greedyArea:0.###}  delta {Math.Abs(naiveArea - greedyArea):0.######}");
        if (Math.Abs(naiveArea - greedyArea) > 1e-3)
        {
            Console.Error.WriteLine("FAIL: greedy meshing changed the surface area.");
            return 1;
        }

        // Baked where it stands for OBJ; nodes, lights and collision for the formats with a scene.
        ExportMesh baked = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true);
        UvAtlas bakedAtlas = UvUnwrap.Apply(baked);
        ExportMesh nodes = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true, instanceLinked: true, lights: true, colliders: true);
        UvAtlas nodesAtlas = UvUnwrap.Apply(nodes);
        Console.WriteLine($"Atlas  {nodesAtlas.Width} x {nodesAtlas.Height}  ({nodesAtlas.Charts.Count:N0} charts, {nodesAtlas.TexelsPerVoxel} texels per voxel)");

        var written = new List<string>();
        written.AddRange(new ObjExporter().Export(baked, scene.Palette, Path.Combine(outputDirectory, name + ".obj"),
            new ExportOptions { Atlas = bakedAtlas, TextureFileName = name + "-obj.png" }).FilesWritten);

        var sceneOptions = new ExportOptions { Atlas = nodesAtlas, TextureFileName = name + ".png" };
        foreach (IMeshExporter exporter in new IMeshExporter[] { new GltfExporter(binary: true), new GltfExporter(binary: false), new FbxExporter() })
        {
            written.AddRange(exporter.Export(nodes, scene.Palette, Path.Combine(outputDirectory, name + exporter.Extension), sceneOptions).FilesWritten);
        }

        string voxPath = Path.Combine(outputDirectory, name + VoxFile.Extension);
        VoxReport vox = VoxFile.Save(scene, voxPath);
        written.Add(voxPath);
        foreach (string warning in vox.Warnings)
        {
            Console.WriteLine($"vox    {warning}");
        }

        foreach (string file in written.Distinct())
        {
            Console.WriteLine($"Wrote  {file}  ({new FileInfo(file).Length / 1024.0:0.0} KB)");
        }

        return 0;
    }

    /// <summary>
    /// A little of everything an export carries: the demo ground, a crate and a turned linked copy
    /// of it, a cup parented to a table, a crate of smaller voxels, metal and glass, a spawn point
    /// with properties for the game, the sun and a lamp.
    /// </summary>
    private static VoxelScene Sample()
    {
        var scene = new VoxelScene();

        var ground = new VoxelWorld();
        DemoScene.Fill(ground);
        scene.Add(ground, ObjectTransform.Identity with { Position = new Vector3(0f, -24f, 0f) }, "Demo");

        const byte Wood = 60, Metal = 100, Glass = 140;
        scene.Palette.SetMaterial(Metal, VoxelMaterial.Of(0f, 1f, 0.25f, 1f));
        scene.Palette.SetMaterial(Glass, VoxelMaterial.Of(0f, 0f, 0.1f, 0.4f));

        VoxelWorld crate = Box(4, 4, 4, (x, y, z) => x == 0 || x == 3 || z == 0 || z == 3 ? Wood : Metal);
        scene.Add(crate, ObjectTransform.Identity with { Position = new Vector3(-20f, 0f, 0f) }, "Crate");
        scene.Add(crate, new ObjectTransform(new Vector3(-28f, 0f, 4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f), 1f), "Crate copy");

        VoxelObject table = scene.Add(Box(6, 3, 4, (_, _, _) => Wood), ObjectTransform.Identity with { Position = new Vector3(20f, 0f, 0f) }, "Table");
        VoxelObject cup = scene.Add(Box(1, 2, 1, (_, _, _) => Glass), ObjectTransform.Identity with { Position = new Vector3(22f, 3f, 1f) }, "Cup");
        scene.SetParent(cup.Id, table.Id);

        scene.Add(Box(4, 4, 4, (_, y, _) => y < 2 ? Metal : Wood), new ObjectTransform(new Vector3(0f, 0f, -24f), Quaternion.Identity, 0.5f), "Small crate");

        VoxelObject spawn = scene.Add(new VoxelWorld(), ObjectTransform.Identity with { Position = new Vector3(0f, 1f, 20f) }, "Spawn");
        spawn.Marker = ObjectMarker.Default(MarkerKind.Spawn);
        spawn.Properties = [new CustomProperty("team", PropertyKind.Text, "red"), new CustomProperty("lives", PropertyKind.Number, "3")];

        scene.AddDefaultSun();
        SceneLight lamp = scene.AddLight(LightKind.Point, "Lamp");
        lamp.Transform = lamp.Transform with { Position = new Vector3(20f, 8f, 0f) };
        lamp.Range = 20f;
        return scene;
    }

    private static VoxelWorld Box(int width, int height, int depth, Func<int, int, int, byte> colour)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        for (int z = 0; z < depth; z++)
        {
            grid.SetVoxel(x, y, z, colour(x, y, z));
        }

        return grid;
    }
}
