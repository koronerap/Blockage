using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class SceneProjectTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-scene-tests", Guid.NewGuid().ToString("N"));

    public SceneProjectTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name + VxLevelFile.Extension);

    private static VoxelWorld Block(int side, byte index)
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

        return grid;
    }

    private static VoxelScene TwoObjectScene()
    {
        var scene = new VoxelScene();
        scene.Add(Block(3, 40), ObjectTransform.Identity, "base");
        scene.Add(
            Block(2, 137),
            new ObjectTransform(
                new Vector3(10f, 2f, -4f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f)),
            "turned");

        scene.Palette[40] = new Color32(200, 30, 60);
        scene.Palette[137] = new Color32(20, 180, 90);
        return scene;
    }

    [Fact]
    public void RoundTripPreservesObjectsTransformsAndPalette()
    {
        VoxelScene original = TwoObjectScene();
        string path = PathFor("scene");

        VxLevelFile.Save(original, path);
        VoxelScene loaded = VxLevelFile.LoadScene(path);

        Assert.Equal(original.Objects.Count, loaded.Objects.Count);
        Assert.Equal(original.SolidCount, loaded.SolidCount);
        Assert.Equal(original.ContentHash(), loaded.ContentHash());

        for (int i = 0; i < original.Objects.Count; i++)
        {
            VoxelObject before = original.Objects[i];
            VoxelObject after = loaded.Objects[i];

            Assert.Equal(before.Name, after.Name);
            Assert.True(Vector3.Distance(before.Transform.Position, after.Transform.Position) < 1e-5f);
            Assert.True(
                Math.Abs(Quaternion.Dot(before.Transform.Rotation, after.Transform.Rotation)) > 0.99999f,
                "Rotation did not survive the round trip.");
        }

        for (int i = 0; i < Palette.Size; i++)
        {
            Assert.Equal(original.Palette[i], loaded.Palette[i]);
        }
    }

    [Fact]
    public void ManifestReportsTheCurrentVersionAndItsObjects()
    {
        string path = PathFor("manifest");
        VxLevelFile.Save(TwoObjectScene(), path, name: "My Level");

        LevelManifest manifest = VxLevelFile.ReadManifest(path);

        Assert.Equal(VxLevelFile.CurrentVersion, manifest.Version);
        Assert.Equal("My Level", manifest.Name);
        Assert.NotNull(manifest.Objects);
        Assert.Equal(2, manifest.Objects!.Length);
        Assert.Equal("turned", manifest.Objects[1].Name);
        Assert.Equal(4, manifest.Objects[1].Rotation.Length);
    }

    [Fact]
    public void AVersionOneFileStillOpens()
    {
        // The version field exists so old files keep working; a v1 file is one grid at the origin.
        string path = PathFor("legacy");
        VoxelWorld grid = Block(4, 12);
        VxLevelFile.Save(grid, path);

        // Saving a bare grid writes v2, so rewrite the manifest as a genuine v1 layout instead.
        string legacyPath = WriteLegacyFile(grid, "legacy-v1");

        VoxelScene loaded = VxLevelFile.LoadScene(legacyPath);

        Assert.Single(loaded.Objects);
        Assert.Equal(grid.SolidCount, loaded.SolidCount);
        Assert.Equal(ObjectTransform.Identity, loaded.Objects[0].Transform);
    }

    private string WriteLegacyFile(VoxelWorld grid, string name)
    {
        string path = PathFor(name);

        using var stream = File.Create(path);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);

        var coordinates = grid.Chunks.Keys.ToList();
        var manifest = new LevelManifest
        {
            Version = 1,
            Name = name,
            ChunkSize = Chunk.Size,
            Palette = LevelManifest.EncodePalette(grid.Palette),
            Chunks = [.. coordinates.Select(c => new[] { c.X, c.Y, c.Z })],
            Objects = null,
        };

        using (Stream manifestStream = archive.CreateEntry("manifest.json").Open())
        {
            System.Text.Json.JsonSerializer.Serialize(manifestStream, manifest);
        }

        foreach (ChunkCoord coord in coordinates)
        {
            byte[] encoded = Rle.Encode(grid.Chunks[coord].Indices);
            using Stream chunkStream = archive.CreateEntry($"chunks/{coord.X}_{coord.Y}_{coord.Z}.bin").Open();
            chunkStream.Write(encoded, 0, encoded.Length);
        }

        return path;
    }

    [Fact]
    public void ExportBakesEachObjectsPlacementIntoOneMesh()
    {
        VoxelScene scene = TwoObjectScene();
        ExportMesh mesh = GreedyMesher.BuildScene(scene);

        // Two separate cubes: 6 merged quads each, and one material for the pair.
        Assert.Equal(12, mesh.QuadCount);
        Assert.Equal(2, mesh.UsedPaletteIndices().Count);

        (Vector3 min, Vector3 max) = mesh.Bounds();
        Assert.True(scene.TryGetWorldBounds(out Vector3 sceneMin, out Vector3 sceneMax));
        Assert.True(Vector3.Distance(min, sceneMin) < 1e-4f);
        Assert.True(Vector3.Distance(max, sceneMax) < 1e-4f);
    }

    [Fact]
    public void ExportKeepsTotalAreaAcrossObjectsAndRotations()
    {
        // A rigid transform cannot change surface area, so the §4b invariant still has to hold
        // once each object's placement is baked in.
        VoxelScene scene = TwoObjectScene();

        double naiveArea = 0;
        foreach (VoxelObject o in scene.Objects)
        {
            var naive = new MeshBuilder();
            EditMesher.BuildWorldNaive(o.Grid, naive);
            naiveArea += naive.TotalArea();
        }

        Assert.Equal(naiveArea, GreedyMesher.BuildScene(scene).TotalArea(), 3);
    }

    [Fact]
    public void RotatedQuadsStillFaceOutward()
    {
        var scene = new VoxelScene();
        scene.Add(
            Block(2, 5),
            new ObjectTransform(
                new Vector3(3f, 0f, 0f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 3f)),
            "turned");

        ExportMesh mesh = GreedyMesher.BuildScene(scene);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            Vector3 a = mesh.Positions[quad * 4];
            Vector3 b = mesh.Positions[quad * 4 + 1];
            Vector3 c = mesh.Positions[quad * 4 + 2];

            Vector3 geometric = Vector3.Normalize(Vector3.Cross(b - a, c - a));

            Assert.True(
                Vector3.Dot(geometric, mesh.Normals[quad * 4]) > 0.99f,
                $"Quad {quad} winds against its normal after rotation.");
        }
    }

    [Fact]
    public void HiddenObjectsAreNotExported()
    {
        VoxelScene scene = TwoObjectScene();
        scene.Objects[1].Visible = false;

        Assert.Equal(6, GreedyMesher.BuildScene(scene).QuadCount);
    }

    [Fact]
    public void ObjWritesTheWholeSceneAsOneMaterial()
    {
        VoxelScene scene = TwoObjectScene();
        string path = Path.Combine(_directory, "level.obj");

        ExportResult result = new ObjExporter().Export(
            GreedyMesher.BuildScene(scene), scene.Palette, path, new ExportOptions());

        Assert.Equal(12, result.QuadCount);
        Assert.Equal(1, File.ReadAllText(Path.Combine(_directory, "level.mtl")).Split("newmtl ").Length - 1);
    }
}
