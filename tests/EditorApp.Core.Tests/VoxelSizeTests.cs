using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Voxel size is a unit conversion applied once, to the finished export. These pin down that it
/// reaches the exported geometry, that it reaches nothing else, and that it survives a file.
/// </summary>
public class VoxelSizeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-voxel-size", Guid.NewGuid().ToString("N"));

    public VoxelSizeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static VoxelScene CubeScene(int side = 4, byte index = 40)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, index);
                }
            }
        }

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "cube");
        return scene;
    }

    [Fact]
    public void ALevelIsOneUnitPerVoxelUntilSaidOtherwise()
    {
        var scene = new VoxelScene();

        Assert.Equal(1f, scene.VoxelSize);
        Assert.False(scene.HasCustomVoxelSize);
    }

    [Fact]
    public void TheExportedMeshIsScaledByIt()
    {
        VoxelScene scene = CubeScene(side: 4);
        ExportMesh unscaled = GreedyMesher.BuildScene(scene);

        scene.VoxelSize = 0.25f;
        ExportMesh scaled = GreedyMesher.BuildScene(scene);

        (Vector3 min, Vector3 max) = scaled.Bounds();
        Assert.Equal(new Vector3(0f), min);
        Assert.Equal(new Vector3(1f), max);

        // Merging is unaffected: the same quads, just smaller.
        Assert.Equal(unscaled.QuadCount, scaled.QuadCount);
        Assert.Equal(unscaled.VertexCount, scaled.VertexCount);

        // Area is a squared measure, so it follows the scale squared.
        Assert.Equal(unscaled.TotalArea() * 0.0625, scaled.TotalArea(), 4);
    }

    [Fact]
    public void ScalingLeavesNormalsAndUvsAlone()
    {
        // Both are directions or lookups, not lengths. A scaled normal would break lighting in the
        // target engine, and a scaled UV would sample the wrong palette block.
        VoxelScene scene = CubeScene();
        ExportMesh before = GreedyMesher.BuildScene(scene);

        scene.VoxelSize = 7.5f;
        ExportMesh after = GreedyMesher.BuildScene(scene);

        for (int i = 0; i < before.VertexCount; i++)
        {
            Assert.Equal(before.Normals[i], after.Normals[i]);
            Assert.Equal(before.Uvs[i], after.Uvs[i]);
        }
    }

    [Fact]
    public void ObjectPlacementIsScaledTogetherWithTheGeometry()
    {
        // Placements are in voxel units too. Scaling only the shapes would leave a level whose
        // pieces had drifted apart from each other.
        VoxelScene scene = CubeScene(side: 2);
        scene.Objects[0].Transform = new ObjectTransform(new Vector3(10f, 0f, 0f), Quaternion.Identity);
        scene.VoxelSize = 0.5f;

        (Vector3 min, Vector3 max) = GreedyMesher.BuildScene(scene).Bounds();

        Assert.Equal(5f, min.X, 4);
        Assert.Equal(6f, max.X, 4);
    }

    [Fact]
    public void TheWrittenObjHasTheScaledCoordinates()
    {
        // The whole chain in one go: a size set on the level ends up in the numbers a DCC tool reads.
        VoxelScene scene = CubeScene(side: 4);
        scene.VoxelSize = 0.25f;

        string path = Path.Combine(_directory, "scaled.obj");
        new ObjExporter().Export(
            GreedyMesher.BuildScene(scene),
            scene.Palette,
            path,
            new ExportOptions());

        float largest = 0f;
        foreach (string line in File.ReadLines(path))
        {
            if (!line.StartsWith("v ", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1..])
            {
                largest = MathF.Max(largest, float.Parse(part, CultureInfo.InvariantCulture));
            }
        }

        // Four voxels at a quarter of a unit each.
        Assert.Equal(1f, largest, 4);
    }

    [Fact]
    public void ItSurvivesASaveAndLoad()
    {
        VoxelScene scene = CubeScene();
        scene.VoxelSize = 0.125f;

        string path = Path.Combine(_directory, "sized" + VxLevelFile.Extension);
        VxLevelFile.Save(scene, path);

        Assert.Equal(0.125f, VxLevelFile.LoadScene(path).VoxelSize);
    }

    [Fact]
    public void AVersion3FileLoadsAtOneUnitPerVoxel()
    {
        // Every level written before the field existed meant one voxel, one unit. Built by taking a
        // real file back to version 3 rather than by asserting on the defaulting expression, so this
        // fails if the loader stops reading old files at all.
        VoxelScene scene = CubeScene();
        scene.VoxelSize = 0.125f;

        string path = Path.Combine(_directory, "legacy" + VxLevelFile.Extension);
        VxLevelFile.Save(scene, path);
        DowngradeToVersion3(path);

        VoxelScene loaded = VxLevelFile.LoadScene(path);

        Assert.Equal(1f, loaded.VoxelSize);
        Assert.False(loaded.HasCustomVoxelSize);
        Assert.Equal(scene.SolidCount, loaded.SolidCount);
    }

    /// <summary>Strips the voxel size out of a saved file and calls it version 3, as one would be.</summary>
    private static void DowngradeToVersion3(string path)
    {
        string manifest;
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("manifest.json")!;
            using (var reader = new StreamReader(entry.Open()))
            {
                manifest = reader.ReadToEnd();
            }

            JsonNode node = JsonNode.Parse(manifest)!;
            node["version"] = 3;
            node.AsObject().Remove("voxelSize");

            entry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(replacement.Open());
            writer.Write(node.ToJsonString());
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-3f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ASizeThatWouldCollapseOrBreakTheModelIsRefused(float bad)
    {
        var session = new EditorSession();
        session.ReplaceScene(CubeScene(), projectPath: null);
        session.SetVoxelSize(2f);

        session.SetVoxelSize(bad);

        Assert.InRange(session.Scene.VoxelSize, VoxelScene.MinVoxelSize, VoxelScene.MaxVoxelSize);
        Assert.NotEqual(0f, session.Scene.VoxelSize);
    }

    [Fact]
    public void ChangingItMarksTheProjectUnsavedButIsNotAnUndoStep()
    {
        var session = new EditorSession();
        session.ReplaceScene(CubeScene(), projectPath: null);
        Assert.False(session.HasUnsavedChanges);

        Assert.True(session.SetVoxelSize(0.5f));

        Assert.True(session.HasUnsavedChanges);
        Assert.False(session.History.CanUndo);

        // Setting the same value again is not a change.
        Assert.False(session.SetVoxelSize(0.5f));
    }
}
