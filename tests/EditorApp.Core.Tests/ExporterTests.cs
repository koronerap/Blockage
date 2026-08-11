using System.Globalization;
using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;
using SharpGLTF.Schema2;

namespace EditorApp.Core.Tests;

public class ExporterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-export-tests", Guid.NewGuid().ToString("N"));

    public ExporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Two adjacent blocks of different colors, so both the mesh and the UVs have to be right.</summary>
    private static VoxelWorld BuildWorld()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 6; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    world.SetVoxel(x, y, z, x < 3 ? (byte)40 : (byte)137);
                }
            }
        }

        world.Palette[40] = new Color32(200, 30, 60);
        world.Palette[137] = new Color32(20, 180, 90);
        return world;
    }

    [Fact]
    public void ObjExportWritesMeshMaterialAndTexture()
    {
        VoxelWorld world = BuildWorld();
        ExportMesh mesh = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.obj");

        ExportResult result = new ObjExporter().Export(mesh, world.Palette, path, new ExportOptions());

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_directory, "level.mtl")));
        Assert.True(File.Exists(Path.Combine(_directory, "palette.png")));
        Assert.True(File.Exists(Path.Combine(_directory, "level-texture-notes.txt")));
        Assert.Equal(mesh.VertexCount, result.VertexCount);
        Assert.Equal(mesh.QuadCount, result.QuadCount);
    }

    [Fact]
    public void MtlReferencesTheTextureByRelativePath()
    {
        VoxelWorld world = BuildWorld();
        string path = Path.Combine(_directory, "level.obj");
        new ObjExporter().Export(GreedyMesher.Build(world), world.Palette, path, new ExportOptions());

        string mtl = File.ReadAllText(Path.Combine(_directory, "level.mtl"));

        Assert.Contains("map_Kd palette.png", mtl);
        Assert.DoesNotContain(_directory, mtl);           // never an absolute path

        // One material for the whole level, not one per color.
        Assert.Equal(1, mtl.Split("newmtl ").Length - 1);
    }

    [Fact]
    public void ObjGeometryIsSelfConsistent()
    {
        VoxelWorld world = BuildWorld();
        ExportMesh mesh = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.obj");
        new ObjExporter().Export(mesh, world.Palette, path, new ExportOptions());

        string[] lines = File.ReadAllLines(path);
        int positions = lines.Count(l => l.StartsWith("v ", StringComparison.Ordinal));
        int uvs = lines.Count(l => l.StartsWith("vt ", StringComparison.Ordinal));
        int normals = lines.Count(l => l.StartsWith("vn ", StringComparison.Ordinal));
        string[] faces = [.. lines.Where(l => l.StartsWith("f ", StringComparison.Ordinal))];

        // Each of the three is deduplicated on its own, which is the point of OBJ's separate
        // indices — so none of them matches the mesh's corner count any more, and the file is a
        // connected surface rather than a pile of loose quads. See ObjWeldingTests.
        Assert.True(positions < mesh.VertexCount, "Positions were not welded.");
        Assert.True(uvs <= positions, "One UV per colour is expected, not one per corner.");
        Assert.Equal(FaceInfo.Count, normals);
        Assert.Equal(mesh.QuadCount, faces.Length);

        foreach (string face in faces)
        {
            string[] parts = face.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(5, parts.Length);   // "f" plus four corners

            foreach (string corner in parts[1..])
            {
                string[] triple = corner.Split('/');
                Assert.Equal(3, triple.Length);

                Assert.InRange(int.Parse(triple[0], CultureInfo.InvariantCulture), 1, positions);
                Assert.InRange(int.Parse(triple[1], CultureInfo.InvariantCulture), 1, uvs);
                Assert.InRange(int.Parse(triple[2], CultureInfo.InvariantCulture), 1, normals);
            }
        }
    }

    [Fact]
    public void ObjUvsSampleTheCorrectPaletteColor()
    {
        // The end-to-end check for §6: follow a quad's UV into the generated PNG (flipping V back
        // from OBJ's bottom-up convention) and confirm it lands on that quad's own color.
        VoxelWorld world = BuildWorld();
        ExportMesh mesh = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.obj");
        new ObjExporter().Export(mesh, world.Palette, path, new ExportOptions());

        TestPngReader.Image image = TestPngReader.Decode(File.ReadAllBytes(Path.Combine(_directory, "palette.png")));

        Vector2[] objUvs = [.. File.ReadAllLines(path)
            .Where(l => l.StartsWith("vt ", StringComparison.Ordinal))
            .Select(l =>
            {
                string[] parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return new Vector2(
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture));
            })];

        // UVs are shared between every quad of the same colour now, so the quad's own UV has to be
        // reached through its face indices rather than by position in the list.
        int[] uvOfQuad = [.. File.ReadAllLines(path)
            .Where(l => l.StartsWith("f ", StringComparison.Ordinal))
            .Select(l => int.Parse(
                l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].Split('/')[1],
                CultureInfo.InvariantCulture) - 1)];

        Assert.Equal(mesh.QuadCount, uvOfQuad.Length);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            Vector2 uv = objUvs[uvOfQuad[quad]];
            (byte r, byte g, byte b, byte _) = image.Sample(uv.X, 1f - uv.Y);

            Color32 expected = world.Palette[mesh.QuadPaletteIndices[quad]];
            Assert.Equal((expected.R, expected.G, expected.B), (r, g, b));
        }
    }

    [Fact]
    public void GlbIsASingleFileWithTheTextureEmbedded()
    {
        VoxelWorld world = BuildWorld();
        ExportMesh mesh = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.glb");

        ExportResult result = new GltfExporter(binary: true)
            .Export(mesh, world.Palette, path, new ExportOptions());

        Assert.Single(result.FilesWritten);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(_directory, "palette.png")));

        ModelRoot model = ModelRoot.Load(path);
        Assert.Single(model.LogicalMaterials);
        Assert.Single(model.LogicalImages);
        Assert.Equal("image/png", model.LogicalImages[0].Content.MimeType);

        MaterialChannel? baseColor = model.LogicalMaterials[0].FindChannel("BaseColor");
        Assert.NotNull(baseColor);
        Assert.NotNull(baseColor!.Value.Texture);
    }

    [Fact]
    public void GlbCarriesEveryTriangleWithNormalsAndUvs()
    {
        VoxelWorld world = BuildWorld();
        ExportMesh mesh = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.glb");
        new GltfExporter(binary: true).Export(mesh, world.Palette, path, new ExportOptions());

        ModelRoot model = ModelRoot.Load(path);
        MeshPrimitive primitive = Assert.Single(model.LogicalMeshes.SelectMany(m => m.Primitives));

        Assert.Equal(mesh.TriangleCount, primitive.GetTriangleIndices().Count());
        Assert.NotNull(primitive.GetVertexAccessor("POSITION"));
        Assert.NotNull(primitive.GetVertexAccessor("NORMAL"));
        Assert.NotNull(primitive.GetVertexAccessor("TEXCOORD_0"));
    }

    [Fact]
    public void GltfWritesTheTextureBesideTheMeshUnderTheAgreedName()
    {
        VoxelWorld world = BuildWorld();
        ExportMesh mesh = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.gltf");

        new GltfExporter(binary: false).Export(mesh, world.Palette, path, new ExportOptions());

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_directory, "palette.png")));

        string json = File.ReadAllText(path);
        Assert.Contains("palette.png", json);
        Assert.DoesNotContain(_directory.Replace('\\', '/'), json);   // relative, not absolute
    }

    [Fact]
    public void ExportedMeshKeepsTheSameSurfaceAsTheEditorMesh()
    {
        // Ties the whole chain together: what leaves the editor covers exactly the surface the
        // editor was showing, just with far fewer vertices.
        VoxelWorld world = BuildWorld();

        var naive = new MeshBuilder();
        EditMesher.BuildWorldNaive(world, naive);
        ExportMesh greedy = GreedyMesher.Build(world);

        Assert.Equal(naive.TotalArea(), greedy.TotalArea(), 4);
        Assert.True(greedy.VertexCount < naive.VertexCount);
    }

    [Fact]
    public void ExportingAnEmptyLevelWritesAValidButEmptyFile()
    {
        var world = new VoxelWorld();
        ExportMesh mesh = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "empty.obj");

        ExportResult result = new ObjExporter().Export(mesh, world.Palette, path, new ExportOptions());

        Assert.Equal(0, result.VertexCount);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_directory, "palette.png")));
    }

    [Fact]
    public void ImportNotesCanBeTurnedOff()
    {
        VoxelWorld world = BuildWorld();
        string path = Path.Combine(_directory, "level.obj");

        new ObjExporter().Export(
            GreedyMesher.Build(world),
            world.Palette,
            path,
            new ExportOptions { WriteImportNotes = false });

        Assert.False(File.Exists(Path.Combine(_directory, "level-texture-notes.txt")));
    }
}
