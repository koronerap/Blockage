using System.Numerics;
using System.Text.Json;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// What opening the exports in Blender and Unity turned up (Fullreleaseplan 8.6), kept from coming
/// back: a text glTF naming textures it never wrote, and an OBJ that Unity welds into one mesh.
/// </summary>
public sealed class ExportInProgramsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"programs-{Guid.NewGuid():N}");

    public ExportInProgramsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const byte Metal = 100;

    private static VoxelScene TwoCrates()
    {
        var scene = new VoxelScene();
        scene.Palette.SetMaterial(Metal, VoxelMaterial.Of(0f, 1f, 0.3f, 1f));
        foreach ((string name, float x) in new[] { ("Crate", 0f), ("Other crate", 6f) })
        {
            var grid = new VoxelWorld();
            for (int i = 0; i < 2; i++)
            {
                grid.SetVoxel(i, 0, 0, Metal);
                grid.SetVoxel(i, 1, 0, Palette.WhiteIndex);
            }

            scene.Add(grid, ObjectTransform.Identity with { Position = new Vector3(x, 0f, 0f) }, name);
        }

        return scene;
    }

    [Fact]
    public void ATextGltfWritesEveryImageItNames()
    {
        VoxelScene scene = TwoCrates();
        string path = Path.Combine(_directory, "crates.gltf");

        ExportResult result = new GltfExporter(binary: false).Export(GreedyMesher.BuildScene(scene, instanceLinked: true), scene.Palette, path, new ExportOptions());

        using JsonDocument gltf = JsonDocument.Parse(File.ReadAllText(path));
        List<string> images = [.. gltf.RootElement.GetProperty("images").EnumerateArray().Select(image => image.GetProperty("uri").GetString()!)];
        Assert.Equal(2, images.Count);
        Assert.All(images, uri => Assert.True(File.Exists(Path.Combine(_directory, uri)), $"{uri} is named but not written"));
        Assert.All(result.FilesWritten, file => Assert.True(File.Exists(file), $"{file} is reported but not written"));
        Assert.Contains(result.FilesWritten, file => file.EndsWith(".bin", StringComparison.Ordinal));
    }

    /// <summary>Unity splits an OBJ by its groups, not its objects: each object is a group as well.</summary>
    [Fact]
    public void EachObjectOfAnObjIsAGroupAsWell()
    {
        VoxelScene scene = TwoCrates();
        string path = Path.Combine(_directory, "crates.obj");

        new ObjExporter().Export(GreedyMesher.BuildScene(scene), scene.Palette, path, new ExportOptions());

        string[] lines = File.ReadAllLines(path);
        foreach (string name in new[] { "Crate", "Other_crate" })
        {
            int named = Array.IndexOf(lines, $"o {name}");
            Assert.True(named >= 0, $"no object {name}");
            Assert.Equal($"g {name}", lines[named + 1]);
        }
    }
}
