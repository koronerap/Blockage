using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using SharpGLTF.Schema2;

namespace EditorApp.Core.Tests;

/// <summary>
/// The glTF as a scene (Fullreleaseplan 8.4): each object a node where it stands, under its parent's;
/// the lights as KHR_lights_punctual; boxes to collide with, where asked.
/// </summary>
public sealed class GltfNodesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nodes-{Guid.NewGuid():N}.glb");

    public void Dispose() => File.Delete(_path);

    private static VoxelWorld Block(int size = 2)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        for (int z = 0; z < size; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        return grid;
    }

    private ModelRoot Export(VoxelScene scene, bool lights = true, bool colliders = false)
    {
        ExportMesh mesh = GreedyMesher.BuildScene(scene, instanceLinked: true, lights: lights, colliders: colliders);
        new GltfExporter(binary: true).Export(mesh, scene.Palette, _path, new ExportOptions());
        return ModelRoot.Load(_path);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");

    // ---- Nodes --------------------------------------------------------------------------------------

    /// <summary>The object's own place is on its node, so it arrives with its pivot where it was.</summary>
    [Fact]
    public void AnObjectsNodeStandsWhereTheObjectDoes()
    {
        var scene = new VoxelScene();
        scene.Add(Block(), new ObjectTransform(new Vector3(10f, 2f, -4f), Quaternion.Identity, 0.5f), "Crate");

        Node node = Export(scene, lights: false).LogicalNodes.Single(n => n.Name == "Crate");

        AssertNear(new Vector3(10f, 2f, -4f), node.LocalTransform.Translation);
        AssertNear(new Vector3(0.5f), node.LocalTransform.Scale);
        Assert.NotNull(node.Mesh);
    }

    [Fact]
    public void AChildIsUnderItsParentWhereItStands()
    {
        var scene = new VoxelScene();
        VoxelObject table = scene.Add(Block(), new ObjectTransform(new Vector3(5f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f), 1f), "Table");
        VoxelObject cup = scene.Add(Block(1), ObjectTransform.Identity with { Position = new Vector3(5f, 3f, 1f) }, "Cup");
        Assert.True(scene.SetParent(cup.Id, table.Id));

        ModelRoot model = Export(scene, lights: false);
        Node tableNode = model.LogicalNodes.Single(n => n.Name == "Table");
        Node cupNode = model.LogicalNodes.Single(n => n.Name == "Cup");

        Assert.Same(tableNode, cupNode.VisualParent);
        AssertNear(cup.Transform.Position, cupNode.WorldMatrix.Translation);
    }

    [Fact]
    public void TheExportIsMeasuredWhereItsNodesPutIt()
    {
        var scene = new VoxelScene();
        scene.Add(Block(), ObjectTransform.Identity with { Position = new Vector3(10f, 0f, 0f) }, "Crate");

        (Vector3 min, Vector3 max) = GreedyMesher.BuildScene(scene, instanceLinked: true).Bounds();

        AssertNear(new Vector3(10f, 0f, 0f), min);
        AssertNear(new Vector3(12f, 2f, 2f), max);
    }

    // ---- Lights -------------------------------------------------------------------------------------

    [Fact]
    public void TheLightsGoAsKhrLightsPunctual()
    {
        var scene = new VoxelScene();
        scene.Add(Block(), ObjectTransform.Identity, "Crate");
        SceneLight sun = scene.AddLight(LightKind.Directional, "Sun");
        sun.Transform = sun.Transform with { Rotation = SceneLight.Aiming(Vector3.Normalize(new Vector3(1f, -2f, 0.5f))) };
        SceneLight lamp = scene.AddLight(LightKind.Point, "Lamp");
        lamp.Range = 12f;
        SceneLight torch = scene.AddLight(LightKind.Spot, "Torch");
        torch.SpotAngle = 60f;

        ModelRoot model = Export(scene);

        Assert.Contains("KHR_lights_punctual", model.ExtensionsUsed);
        Assert.Equal(3, model.LogicalPunctualLights.Count);
        Node sunNode = model.LogicalNodes.Single(n => n.PunctualLight?.LightType == PunctualLightType.Directional);
        Vector3 shines = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, sunNode.WorldMatrix));
        AssertNear(sun.Direction, shines);
        Assert.Equal(12f, model.LogicalPunctualLights.Single(l => l.LightType == PunctualLightType.Point).Range, 3);
        Assert.Equal(MathF.PI / 6f, model.LogicalPunctualLights.Single(l => l.LightType == PunctualLightType.Spot).OuterConeAngle, 3);
    }

    [Fact]
    public void WithoutLightsNoneGo()
    {
        var scene = new VoxelScene();
        scene.Add(Block(), ObjectTransform.Identity, "Crate");
        scene.AddLight(LightKind.Point, "Lamp");

        Assert.Empty(Export(scene, lights: false).LogicalPunctualLights);
    }

    // ---- Collision ----------------------------------------------------------------------------------

    [Fact]
    public void ASolidBlockIsOneBox() =>
        Assert.Equal([(new Int3(0, 0, 0), new Int3(4, 4, 4))], CollisionBoxes.Of(Block(4)));

    /// <summary>However the voxels lie, the boxes fill them exactly: every voxel in one, none in two, none outside.</summary>
    [Fact]
    public void TheBoxesFillTheVoxelsExactly()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 40; x++)
        {
            grid.SetVoxel(x, 0, 0, Palette.WhiteIndex);
        }

        for (int y = 1; y < 5; y++)
        {
            grid.SetVoxel(3, y, 0, Palette.WhiteIndex);
            grid.SetVoxel(3, y, 1, Palette.WhiteIndex);
        }

        grid.SetVoxel(-2, 7, -9, Palette.WhiteIndex);

        List<(Int3 Min, Int3 Max)> boxes = CollisionBoxes.Of(grid);

        var covered = new HashSet<Int3>();
        foreach ((Int3 min, Int3 max) in boxes)
        {
            for (int x = min.X; x < max.X; x++)
            for (int y = min.Y; y < max.Y; y++)
            for (int z = min.Z; z < max.Z; z++)
            {
                Assert.True(grid.IsSolid(new Int3(x, y, z)));
                Assert.True(covered.Add(new Int3(x, y, z)));
            }
        }

        Assert.Equal(grid.SolidCount, covered.Count);
        Assert.True(boxes.Count <= 6);
    }

    [Fact]
    public void CollisionGoesUnderTheObjectAsAColOnlyNode()
    {
        var scene = new VoxelScene();
        scene.Add(Block(3), ObjectTransform.Identity with { Position = new Vector3(1f, 0f, 0f) }, "Crate");

        ModelRoot model = Export(scene, lights: false, colliders: true);

        Node crate = model.LogicalNodes.Single(n => n.Name == "Crate");
        Node collider = Assert.Single(crate.VisualChildren);
        Assert.Equal("Crate-colonly", collider.Name);
        Assert.NotNull(collider.Mesh);
        Assert.Equal("boxes", collider.Extras!["collider"]!.GetValue<string>());
        Assert.Equal(12, collider.Mesh!.Primitives.Sum(p => p.GetTriangleIndices().Count()));
    }

    [Fact]
    public void WithoutCollisionThereIsNone()
    {
        var scene = new VoxelScene();
        scene.Add(Block(), ObjectTransform.Identity, "Crate");

        Assert.DoesNotContain(Export(scene, lights: false).LogicalNodes, n => n.Name.EndsWith("-colonly", StringComparison.Ordinal));
    }
}
