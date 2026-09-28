using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Export.Mimicraft;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Voxel size belongs to each object: it is the scale of the object's transform, so the object is
/// drawn, picked, moved and exported at its own size while its grid stays an integer lattice. These
/// pin down that it reaches everything measured in the world, that it reaches nothing measured in
/// voxels, and that older files — one size for the whole level, positions in voxels — come in at the
/// same size they always were.
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

    private static VoxelWorld Cube(int side = 4, byte index = 40)
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

    private static VoxelScene CubeScene(int side = 4, float voxelSize = 1f, Vector3 position = default)
    {
        var scene = new VoxelScene();
        scene.Add(Cube(side), new ObjectTransform(position, Quaternion.Identity, voxelSize), "cube");
        return scene;
    }

    private static EditorSession SessionOf(VoxelScene scene)
    {
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return session;
    }

    // ---- The transform ----------------------------------------------------------------------------

    [Fact]
    public void AnObjectIsOneUnitPerVoxelUntilSaidOtherwise() =>
        Assert.Equal(1f, CubeScene().Objects[0].VoxelSize);

    [Fact]
    public void PointsGoOutScaledAndComeBackExactly()
    {
        var transform = new ObjectTransform(
            new Vector3(3f, -2f, 5f),
            Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, -0.1f),
            0.25f);

        Vector3 local = new(4f, 8f, -2f);
        Vector3 world = transform.TransformPoint(local);

        Assert.Equal(local.Length() * 0.25f, Vector3.Distance(world, transform.Position), 4);
        Assert.True(Vector3.Distance(local, transform.InverseTransformPoint(world)) < 1e-4f);
        Assert.True(Vector3.Distance(world, Vector3.Transform(local, transform.ToMatrix())) < 1e-4f);
    }

    /// <summary>A normal is a direction: scaling it would break lighting and the export's normals.</summary>
    [Fact]
    public void DirectionsAreTurnedButNeverScaled()
    {
        var transform = new ObjectTransform(Vector3.Zero, Quaternion.CreateFromYawPitchRoll(1f, 0f, 0f), 7.5f);

        Assert.Equal(1f, transform.TransformDirection(Vector3.UnitX).Length(), 5);
        Assert.Equal(1f, transform.InverseTransformDirection(Vector3.UnitY).Length(), 5);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-3f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ASizeThatWouldCollapseOrBreakTheModelIsRefused(float bad)
    {
        EditorSession session = SessionOf(CubeScene());
        int id = session.Scene.Objects[0].Id;
        session.SetObjectVoxelSize(id, 2f);

        Assert.False(session.SetObjectVoxelSize(id, bad));

        Assert.Equal(2f, session.Scene.Objects[0].VoxelSize);
    }

    [Fact]
    public void ASizeBeyondTheRangeIsClampedToIt()
    {
        EditorSession session = SessionOf(CubeScene());

        session.SetObjectVoxelSize(session.Scene.Objects[0].Id, 1e9f);

        Assert.Equal(ObjectTransform.MaxVoxelSize, session.Scene.Objects[0].VoxelSize);
    }

    /// <summary>
    /// It moves the object against its neighbours, so it is an edit to the level and has a way back —
    /// unlike the level-wide size it replaced, which only ever changed the export.
    /// </summary>
    [Fact]
    public void ChangingItIsAnUndoStepThatKeepsTheOrigin()
    {
        EditorSession session = SessionOf(CubeScene(position: new Vector3(2f, 0f, 3f)));
        VoxelObject cube = session.Scene.Objects[0];

        Assert.True(session.SetObjectVoxelSize(cube.Id, 0.5f));
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(new Vector3(2f, 0f, 3f), cube.Transform.Position);
        Assert.False(session.SetObjectVoxelSize(cube.Id, 0.5f));

        session.Undo();

        Assert.Equal(1f, cube.VoxelSize);
    }

    // ---- The viewport -------------------------------------------------------------------------------

    [Fact]
    public void TheWorldBoundsFollowTheSize()
    {
        VoxelScene scene = CubeScene(side: 4, voxelSize: 0.5f, position: new Vector3(10f, 0f, 0f));

        Assert.True(scene.TryGetWorldBounds(out Vector3 min, out Vector3 max));

        Assert.Equal(new Vector3(10f, 0f, 0f), min);
        Assert.Equal(new Vector3(12f, 2f, 2f), max);
    }

    /// <summary>
    /// Picking walks voxels in the object's own space, so the distance it finds is in voxels. Two
    /// objects of different sizes have to be compared in the world: here a small-voxel object sits in
    /// front, and would lose to the big one behind it if their voxel counts were compared instead.
    /// </summary>
    [Fact]
    public void PickingComparesObjectsOfDifferentSizesInTheWorld()
    {
        var scene = new VoxelScene();
        VoxelObject near = scene.Add(Cube(4), new ObjectTransform(new Vector3(0f, 0f, 0f), Quaternion.Identity, 0.1f), "near");
        VoxelObject far = scene.Add(Cube(4), new ObjectTransform(new Vector3(-1f, -1f, 5f), Quaternion.Identity, 1f), "far");

        var ray = new Ray(new Vector3(0.2f, 0.2f, -5f), Vector3.UnitZ);

        Assert.True(scene.TryPick(ray, out ScenePick pick));
        Assert.Same(near, pick.Object);
        Assert.Equal(5f, pick.Distance, 3);
        Assert.NotSame(far, pick.Object);
    }

    [Fact]
    public void SnappingLandsOnWholeVoxelsOfTheObjectsOwnSize()
    {
        Assert.Equal(new Vector3(1.5f, 0f, -0.5f), ObjectTransform.SnapPosition(new Vector3(1.4f, 0.2f, -0.6f), 0.5f));
        Assert.Equal(new Vector3(1f, 0f, -1f), ObjectTransform.SnapPosition(new Vector3(1.4f, 0.2f, -0.6f)));
    }

    /// <summary>Copies, cuts and extruded pieces start at the size of what they came from.</summary>
    [Fact]
    public void ADuplicateKeepsItsOriginalsSizeAndClearsItByOneOfItsVoxels()
    {
        EditorSession session = SessionOf(CubeScene(side: 4, voxelSize: 0.5f));
        VoxelObject source = session.Scene.Objects[0];

        VoxelObject copy = session.DuplicateFocus(Vector3.UnitX)!;

        Assert.Equal(0.5f, copy.VoxelSize);

        // Two units wide, then one half-unit voxel clear.
        Assert.Equal(source.Transform.Position + new Vector3(2.5f, 0f, 0f), copy.Transform.Position);
    }

    // ---- The export ---------------------------------------------------------------------------------

    [Fact]
    public void TheExportedMeshComesOutAtEachObjectsSize()
    {
        var scene = new VoxelScene();
        scene.Add(Cube(4), new ObjectTransform(Vector3.Zero, Quaternion.Identity, 0.25f), "small");
        scene.Add(Cube(2), new ObjectTransform(new Vector3(5f, 0f, 0f), Quaternion.Identity, 2f), "big");

        ExportMesh mesh = GreedyMesher.BuildScene(scene);

        (Vector3 min, Vector3 max) = mesh.Bounds();
        Assert.Equal(new Vector3(0f), min);
        Assert.Equal(new Vector3(9f, 4f, 4f), max);

        Assert.Equal([0.25f, 2f], mesh.Parts.Select(p => p.VoxelSize));
    }

    [Fact]
    public void ScalingLeavesNormalsAndUvsAlone()
    {
        ExportMesh before = GreedyMesher.BuildScene(CubeScene());
        ExportMesh after = GreedyMesher.BuildScene(CubeScene(voxelSize: 7.5f));

        for (int i = 0; i < before.VertexCount; i++)
        {
            Assert.Equal(before.Normals[i], after.Normals[i]);
            Assert.Equal(before.Uvs[i], after.Uvs[i]);
        }
    }

    [Fact]
    public void TheWrittenObjHasTheScaledCoordinates()
    {
        // The whole chain in one go: a size set on an object ends up in the numbers a DCC tool reads.
        VoxelScene scene = CubeScene(side: 4, voxelSize: 0.25f);

        string path = Path.Combine(_directory, "scaled.obj");
        new ObjExporter().Export(GreedyMesher.BuildScene(scene), scene.Palette, path, new ExportOptions());

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

    /// <summary>Texture density is per voxel: an object at a different size keeps the same layout.</summary>
    [Fact]
    public void TheTextureLayoutDoesNotMoveWhenAnObjectIsRescaled()
    {
        ExportMesh unit = GreedyMesher.BuildScene(CubeScene(side: 5), mergeAcrossColors: true);
        UvAtlas atlasA = UvUnwrap.Apply(unit);

        ExportMesh quarter = GreedyMesher.BuildScene(CubeScene(side: 5, voxelSize: 0.25f), mergeAcrossColors: true);
        UvAtlas atlasB = UvUnwrap.Apply(quarter);

        Assert.Equal(atlasA.Islands, atlasB.Islands);
    }

    /// <summary>A weapon is one grid, and one grid has one voxel size.</summary>
    [Fact]
    public void AWeaponOfPiecesAtDifferentSizesIsRefused()
    {
        var scene = new VoxelScene();
        scene.Add(Cube(2), new ObjectTransform(Vector3.Zero, Quaternion.Identity, 1f), "blade");
        scene.Add(Cube(2), new ObjectTransform(new Vector3(2f, 0f, 0f), Quaternion.Identity, 0.5f), "grip");

        Assert.Contains(
            MimicraftValidation.Check(scene, MimicraftTarget.Weapon),
            problem => problem.Message.Contains("voxel sizes", StringComparison.Ordinal));
    }

    /// <summary>A weapon's pieces at one shared size merge in whole voxels of that size.</summary>
    [Fact]
    public void AWeaponMergesInVoxelsOfItsOwnSize()
    {
        var scene = new VoxelScene();
        scene.Add(Cube(2), new ObjectTransform(Vector3.Zero, Quaternion.Identity, 0.5f), "blade");
        scene.Add(Cube(2), new ObjectTransform(new Vector3(1f, 0f, 0f), Quaternion.Identity, 0.5f), "grip");

        Assert.Empty(MimicraftValidation.Check(scene, MimicraftTarget.Weapon));

        MimicraftPiece piece = MimicraftScene.BuildWeaponPiece(scene, "sword")!;
        Assert.True(piece.Grid.TryGetBounds(out Int3 min, out Int3 max));

        // One unit apart at half a unit per voxel is two voxels: the pieces touch, four voxels long.
        Assert.Equal(new Int3(3, 1, 1), max - min);
    }

    // ---- The file -----------------------------------------------------------------------------------

    [Fact]
    public void EachObjectsSizeSurvivesASaveAndLoad()
    {
        var scene = new VoxelScene();
        scene.Add(Cube(2), new ObjectTransform(new Vector3(1.5f, 0f, 0f), Quaternion.Identity, 0.125f), "a");
        scene.Add(Cube(2), new ObjectTransform(new Vector3(-4f, 2f, 0f), Quaternion.Identity, 3f), "b");

        string path = Path.Combine(_directory, "sized" + VxLevelFile.Extension);
        VxLevelFile.Save(scene, path);
        VoxelScene loaded = VxLevelFile.LoadScene(path);

        Assert.Equal([0.125f, 3f], loaded.Objects.Select(o => o.VoxelSize));
        Assert.Equal([new Vector3(1.5f, 0f, 0f), new Vector3(-4f, 2f, 0f)], loaded.Objects.Select(o => o.Transform.Position));
        Assert.Equal(scene.ContentHash(), loaded.ContentHash());
    }

    /// <summary>
    /// Before version 6 a level had one voxel size and positions counted its voxels. Such a file has
    /// to open looking exactly as it did: every object at that size, every position multiplied out.
    /// Built by rewriting a real file into the old shape, so this fails if the loader stops reading
    /// old files at all.
    /// </summary>
    [Fact]
    public void AVersion5FileSpreadsItsLevelSizeOverItsObjects()
    {
        VoxelScene scene = CubeScene(side: 4, voxelSize: 1f, position: new Vector3(10f, 0f, 2f));
        string path = Path.Combine(_directory, "v5" + VxLevelFile.Extension);
        VxLevelFile.Save(scene, path);

        RewriteManifest(path, node =>
        {
            node["version"] = 5;
            node["voxelSize"] = 0.5f;
            foreach (JsonNode? o in node["objects"]!.AsArray())
            {
                o!.AsObject().Remove("voxelSize");
            }
        });

        VoxelObject loaded = Assert.Single(VxLevelFile.LoadScene(path).Objects);

        Assert.Equal(0.5f, loaded.VoxelSize);
        Assert.Equal(new Vector3(5f, 0f, 1f), loaded.Transform.Position);

        // Where it used to export to: ten voxels along, at half a unit each.
        (Vector3 min, _) = GreedyMesher.BuildScene(VxLevelFile.LoadScene(path)).Bounds();
        Assert.Equal(new Vector3(5f, 0f, 1f), min);
    }

    [Fact]
    public void AVersion3FileLoadsAtOneUnitPerVoxel()
    {
        VoxelScene scene = CubeScene(side: 4, voxelSize: 0.125f);
        string path = Path.Combine(_directory, "legacy" + VxLevelFile.Extension);
        VxLevelFile.Save(scene, path);

        RewriteManifest(path, node =>
        {
            node["version"] = 3;
            node.AsObject().Remove("voxelSize");
            foreach (JsonNode? o in node["objects"]!.AsArray())
            {
                o!.AsObject().Remove("voxelSize");
            }
        });

        VoxelScene loaded = VxLevelFile.LoadScene(path);

        Assert.All(loaded.Objects, o => Assert.Equal(1f, o.VoxelSize));
        Assert.Equal(scene.SolidCount, loaded.SolidCount);
    }

    private static void RewriteManifest(string path, Action<JsonNode> change)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        ZipArchiveEntry entry = archive.GetEntry("manifest.json")!;

        JsonNode node;
        using (var reader = new StreamReader(entry.Open()))
        {
            node = JsonNode.Parse(reader.ReadToEnd())!;
        }

        change(node);

        entry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
        writer.Write(node.ToJsonString());
    }
}
