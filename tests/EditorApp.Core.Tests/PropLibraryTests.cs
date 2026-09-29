using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The prop library and Append (Fullreleaseplan 6.3): objects kept to use again, and brought in from other levels.</summary>
public sealed class PropLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "blockage-props-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static VoxelWorld Column(int height, byte colour)
    {
        var grid = new VoxelWorld();
        for (int y = 0; y < height; y++)
        {
            grid.SetVoxel(0, y, 0, colour);
            grid.SetVoxel(1, y, 0, colour);
        }

        return grid;
    }

    [Fact]
    public void APropIsKeptWithAPictureAndStandsOnItsOrigin()
    {
        var scene = new VoxelScene();
        scene.Add(Column(3, Palette.WhiteIndex), ObjectTransform.At(new Vector3(10f, 4f, -6f)), "Post");

        PropEntry saved = PropLibrary.Save(_directory, "Lamp post", scene, scene.Objects);

        PropEntry listed = Assert.Single(PropLibrary.List(_directory));
        Assert.Equal("Lamp post", listed.Name);
        Assert.True(File.Exists(saved.ThumbnailPath));

        VoxelScene prop = VxLevelFile.LoadScene(listed.LevelPath);
        Assert.True(prop.Objects[0].TryGetWorldBounds(out Vector3 min, out Vector3 max));
        Assert.Equal(0f, min.Y);
        Assert.Equal(0f, (min.X + max.X) * 0.5f);

        PropLibrary.Delete(listed);
        Assert.Empty(PropLibrary.List(_directory));
    }

    [Fact]
    public void APropIsSetDownWhereItIsPutAndItsColoursFindTheirWay()
    {
        // The prop's own palette has a colour the level's does not.
        var propScene = new VoxelScene();
        propScene.Palette.SetCustomSaved(Palette.CustomStart, true);
        propScene.Palette[Palette.CustomStart] = new Color32(12, 200, 34, 255);
        propScene.Add(Column(2, (byte)Palette.CustomStart), ObjectTransform.Identity, "Bush");
        propScene.Add(Column(2, Palette.WhiteIndex), ObjectTransform.At(new Vector3(0f, 2f, 0f)), "Snow");
        VoxelScene prop = PropLibrary.Build(propScene, propScene.Objects);

        var session = new EditorSession();
        session.ReplaceWorld(Column(1, Palette.WhiteIndex), projectPath: null);
        int before = session.Scene.Objects.Count;

        IReadOnlyList<VoxelObject> placed = session.PlaceProp(prop, "Bush", new Vector3(20.2f, 0f, 5f));

        Assert.Equal(2, placed.Count);
        Assert.All(placed, o => Assert.True(session.IsSelected(o.Id)));
        VoxelObject bush = placed.Single(o => o.Name == "Bush");
        Assert.True(bush.TryGetWorldBounds(out Vector3 min, out Vector3 max));
        Assert.Equal(20f, (min.X + max.X) * 0.5f, 3);
        Assert.Equal(0f, min.Y, 3);

        byte green = bush.Grid.GetVoxel(0, 0, 0);
        Assert.Equal(new Color32(12, 200, 34, 255), session.Scene.Palette[green]);
        Assert.Equal(Palette.WhiteIndex, placed.Single(o => o.Name == "Snow").Grid.GetVoxel(0, 0, 0));

        session.Undo();
        Assert.Equal(before, session.Scene.Objects.Count);
        Assert.True(session.Scene.Palette.IsCustomSlotFree(green));
    }

    [Fact]
    public void AppendBringsTheChosenObjectsWhereTheyStoodKeepingParentsAndLinks()
    {
        var other = new VoxelScene();
        VoxelObject house = other.Add(Column(4, Palette.WhiteIndex), ObjectTransform.At(new Vector3(3f, 0f, 3f)), "House");
        VoxelObject door = other.Add(Column(2, Palette.WhiteIndex), ObjectTransform.At(new Vector3(3f, 0f, 2f)), "Door");
        VoxelObject twin = other.Add(house.Grid, ObjectTransform.At(new Vector3(9f, 0f, 3f)), "Twin");
        other.Add(Column(1, Palette.WhiteIndex), ObjectTransform.Identity, "Left behind");
        other.SetParent(door.Id, house.Id);

        var session = new EditorSession();
        session.ReplaceWorld(Column(1, Palette.WhiteIndex), projectPath: null);

        IReadOnlyList<VoxelObject> appended = session.AppendObjects(other, [house.Id, door.Id, twin.Id], "town");

        Assert.Equal(3, appended.Count);
        VoxelObject newHouse = appended.Single(o => o.Name == "House");
        VoxelObject newDoor = appended.Single(o => o.Name == "Door");
        VoxelObject newTwin = appended.Single(o => o.Name == "Twin");
        Assert.Equal(new Vector3(3f, 0f, 3f), newHouse.Transform.Position);
        Assert.Equal(newHouse.Id, session.Scene.ParentOf(newDoor)?.Id);
        Assert.Same(newHouse.Grid, newTwin.Grid);
        Assert.NotSame(house.Grid, newHouse.Grid);
        Assert.DoesNotContain(session.Scene.Objects, o => o.Name == "Left behind");
    }
}
