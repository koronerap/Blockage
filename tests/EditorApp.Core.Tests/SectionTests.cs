using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The section box (Fullreleaseplan 7.2): what it cuts away is looked straight through.</summary>
public class SectionTests
{
    [Fact]
    public void APickGoesThroughTheCeilingTheBoxCutsAway()
    {
        var scene = new VoxelScene();
        var room = new VoxelWorld();
        for (int x = 0; x < 6; x++)
        for (int z = 0; z < 6; z++)
        {
            room.SetVoxel(x, 0, z, 1);
            room.SetVoxel(x, 5, z, 2);
        }

        scene.Add(room, ObjectTransform.Identity, "Room");
        var down = new Ray(new Vector3(2.5f, 20f, 2.5f), -Vector3.UnitY);

        Assert.True(scene.TryPick(down, out ScenePick roof));
        Assert.Equal(5, roof.Hit.Voxel.Y);

        var box = new ClipBox(new Vector3(-10f), new Vector3(10f, 4f, 10f));
        Assert.True(scene.TryPick(down, out ScenePick floor, clip: box));
        Assert.Equal(0, floor.Hit.Voxel.Y);

        Assert.True(box.Contains(new Vector3(0f, 4f, 0f)));
        Assert.False(box.Contains(new Vector3(0f, 4.5f, 0f)));
    }

    [Fact]
    public void ReferenceImagesAreSavedWithTheLevelAsWhereThePictureIs()
    {
        var scene = VoxelScene.CreateStarter();
        scene.AddReferenceImage(new ReferenceImage(@"C:\drawings\house front.png")
        {
            Plane = ImagePlane.Side,
            Centre = new Vector3(1f, 2f, 3f),
            Width = 40f,
            Opacity = 0.3f,
            OnlyAligned = true,
            Behind = false,
        });

        using var stream = new MemoryStream();
        Project.VxLevelFile.Save(scene, stream, "images");
        stream.Position = 0;
        ReferenceImage loaded = Assert.Single(Project.VxLevelFile.LoadScene(stream).ReferenceImages);

        Assert.Equal(@"C:\drawings\house front.png", loaded.Path);
        Assert.Equal(ImagePlane.Side, loaded.Plane);
        Assert.Equal(new Vector3(1f, 2f, 3f), loaded.Centre);
        Assert.Equal(40f, loaded.Width);
        Assert.Equal(0.3f, loaded.Opacity, 3);
        Assert.True(loaded.OnlyAligned);
        Assert.False(loaded.Behind);
        Assert.Equal("house front", loaded.Name);
    }
}
