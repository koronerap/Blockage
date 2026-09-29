using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Markers and custom properties (Fullreleaseplan 6.4, 6.5): for the game, kept with the level and sent in the glTF.</summary>
public class MarkerTests
{
    private static EditorSession Session()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        var session = new EditorSession();
        session.ReplaceWorld(grid, projectPath: null);
        return session;
    }

    [Fact]
    public void AMarkerIsAnObjectWithNoVoxelsThatIsNotTidiedAway()
    {
        EditorSession session = Session();

        VoxelObject spawn = session.AddMarker(MarkerKind.Spawn, new Vector3(3.4f, 0f, -2.2f));

        Assert.True(spawn.IsMarker);
        Assert.True(spawn.IsEmpty);
        Assert.Equal(new Vector3(3f, 0f, -2f), spawn.Transform.Position);
        Assert.True(session.IsSelected(spawn.Id));
        Assert.Equal(0, session.Scene.RemoveEmptyObjects());

        session.Undo();
        Assert.DoesNotContain(spawn, session.Scene.Objects);
    }

    [Fact]
    public void ACopyOfAMarkerIsAMarkerWithTheSameProperties()
    {
        EditorSession session = Session();
        VoxelObject trigger = session.AddMarker(MarkerKind.Trigger, Vector3.Zero);
        session.SetProperties(trigger.Id, [new CustomProperty("event", PropertyKind.Text, "boss")], "Add property");

        VoxelObject copy = (VoxelObject)Assert.Single(session.DuplicateSelected(Vector3.UnitX));

        Assert.Equal(trigger.Marker, copy.Marker);
        Assert.Equal("boss", Assert.Single(copy.Properties).Value);
    }

    [Fact]
    public void EditingPropertiesIsUndone()
    {
        EditorSession session = Session();
        VoxelObject o = session.Scene.Objects[0];

        session.SetProperties(o.Id, [new CustomProperty("health", PropertyKind.Number, "40")], "Add property");
        IReadOnlyList<CustomProperty> before = o.Properties;
        session.SetPropertiesLive(o, [before[0] with { Value = "45" }]);
        session.SetPropertiesLive(o, [before[0] with { Value = "50" }]);
        Assert.True(session.PushObjectDataEdit(o, o.Marker, before, "Edit property"));

        Assert.Equal(50d, o.Properties[0].Number);
        session.Undo();
        Assert.Equal(40d, o.Properties[0].Number);
        session.Undo();
        Assert.Empty(o.Properties);
    }

    [Fact]
    public void MarkersAndPropertiesAreSavedWithTheLevel()
    {
        EditorSession session = Session();
        VoxelObject sound = session.AddMarker(MarkerKind.Sound, new Vector3(1f, 2f, 3f));
        session.SetMarkerLive(sound, sound.Marker! with { Size = new Vector3(12f) });
        session.SetProperties(sound.Id,
        [
            new CustomProperty("clip", PropertyKind.Text, "wind.ogg"),
            new CustomProperty("volume", PropertyKind.Number, "0.5"),
            new CustomProperty("loop", PropertyKind.Toggle, "true"),
        ], "Add properties");

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "markers");
        stream.Position = 0;
        VoxelObject loaded = VxLevelFile.LoadScene(stream).Objects.Single(o => o.Name == sound.Name);

        Assert.Equal(new ObjectMarker(MarkerKind.Sound, new Vector3(12f)), loaded.Marker);
        Assert.Equal(sound.Properties, loaded.Properties);
    }

    [Fact]
    public void TheGltfHasTheMarkerAsANodeAndThePropertiesAsExtras()
    {
        EditorSession session = Session();
        VoxelObject block = session.Scene.Objects[0];
        session.SetProperties(block.Id,
        [
            new CustomProperty("breakable", PropertyKind.Toggle, "true"),
            new CustomProperty("hits", PropertyKind.Number, "3"),
        ], "Add properties");
        VoxelObject spawn = session.AddMarker(MarkerKind.Spawn, new Vector3(5f, 0f, 0f));

        string path = Path.Combine(Path.GetTempPath(), $"markers-{Guid.NewGuid():N}.glb");
        try
        {
            ExportMesh mesh = GreedyMesher.BuildScene(session.Scene, instanceLinked: true);
            new GltfExporter(binary: true).Export(mesh, session.Scene.Palette, path, new ExportOptions());
            SharpGLTF.Schema2.ModelRoot model = SharpGLTF.Schema2.ModelRoot.Load(path);

            SharpGLTF.Schema2.Node marker = model.LogicalNodes.Single(n => n.Name == spawn.Name);
            Assert.Null(marker.Mesh);
            Assert.Equal("spawn", marker.Extras!["marker"]!.GetValue<string>());
            Assert.Equal(5f, marker.LocalTransform.Translation.X, 3);

            SharpGLTF.Schema2.Node node = model.LogicalNodes.Single(n => n.Name == block.Name);
            Assert.NotNull(node.Mesh);
            Assert.True(node.Extras!["breakable"]!.GetValue<bool>());
            Assert.Equal(3d, node.Extras["hits"]!.GetValue<double>());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
