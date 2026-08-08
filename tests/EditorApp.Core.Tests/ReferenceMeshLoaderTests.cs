using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Import;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class ReferenceMeshLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-reference-tests", Guid.NewGuid().ToString("N"));

    public ReferenceMeshLoaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Write(string fileName, string content)
    {
        string path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static VoxelWorld SmallWorld()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 2; z++)
            {
                world.SetVoxel(x, 0, z, 17);
            }
        }

        return world;
    }

    [Fact]
    public void ObjTrianglesAreLoadedWithFlatNormals()
    {
        string path = Write("tri.obj", """
            # a single triangle on the XZ plane
            v 0 0 0
            v 1 0 0
            v 0 0 1
            f 1 2 3
            """);

        ReferenceMesh mesh = ReferenceMeshLoader.Load(path);

        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Positions[1]);

        // Wound clockwise seen from +Y, so the face normal points down.
        Assert.Equal(-Vector3.UnitY, mesh.Normals[0]);
    }

    [Fact]
    public void ObjQuadsAreTriangulated()
    {
        string path = Write("quad.obj", """
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """);

        Assert.Equal(2, ReferenceMeshLoader.Load(path).TriangleCount);
    }

    [Fact]
    public void ObjFaceIndexFormsAreAllAccepted()
    {
        string path = Write("forms.obj", """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vn 0 0 1
            f 1/1/1 2/1/1 3/1/1
            f 1//1 2//1 3//1
            f 1/1 2/1 3/1
            f -3 -2 -1
            """);

        Assert.Equal(4, ReferenceMeshLoader.Load(path).TriangleCount);
    }

    [Fact]
    public void ObjWithNoFacesIsRejected()
    {
        string path = Write("empty.obj", "v 0 0 0\nv 1 0 0\n");
        Assert.Throws<ReferenceImportException>(() => ReferenceMeshLoader.Load(path));
    }

    [Fact]
    public void OutOfRangeFaceIndexIsRejected()
    {
        string path = Write("bad.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 9\n");
        ReferenceImportException error = Assert.Throws<ReferenceImportException>(() => ReferenceMeshLoader.Load(path));
        Assert.Contains("out of range", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedExtensionIsRejected()
    {
        string path = Write("model.fbx", "not a model");
        Assert.Throws<ReferenceImportException>(() => ReferenceMeshLoader.Load(path));
    }

    [Fact]
    public void MissingFileIsRejected()
    {
        Assert.Throws<ReferenceImportException>(
            () => ReferenceMeshLoader.Load(Path.Combine(_directory, "nope.obj")));
    }

    [Fact]
    public void OurOwnObjExportLoadsBackAsAReference()
    {
        // The round trip that matters in practice: export a level, hand it to someone, import it
        // back as the reference to build the next area against.
        VoxelWorld world = SmallWorld();
        ExportMesh exported = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.obj");
        new ObjExporter().Export(exported, world.Palette, path, new ExportOptions());

        ReferenceMesh reference = ReferenceMeshLoader.Load(path);

        Assert.Equal(exported.TriangleCount, reference.TriangleCount);

        (Vector3 min, Vector3 max) = reference.Bounds();
        Assert.Equal(new Vector3(0, 0, 0), min);
        Assert.Equal(new Vector3(3, 1, 2), max);
    }

    [Fact]
    public void OurOwnGlbExportLoadsBackAsAReference()
    {
        VoxelWorld world = SmallWorld();
        ExportMesh exported = GreedyMesher.Build(world);
        string path = Path.Combine(_directory, "level.glb");
        new GltfExporter(binary: true).Export(exported, world.Palette, path, new ExportOptions());

        ReferenceMesh reference = ReferenceMeshLoader.Load(path);

        Assert.Equal(exported.TriangleCount, reference.TriangleCount);

        (Vector3 min, Vector3 max) = reference.Bounds();
        Assert.Equal(new Vector3(0, 0, 0), min);
        Assert.Equal(new Vector3(3, 1, 2), max);
    }
}
