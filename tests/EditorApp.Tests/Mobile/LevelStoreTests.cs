using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Mobile.Files;

namespace EditorApp.Tests.Mobile;

/// <summary>
/// The level library a phone keeps. Nothing here is Android — it is a directory and some rules
/// about names — which is exactly why it can be tested here rather than on a device.
/// </summary>
public sealed class LevelStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "blockage-store-" + Guid.NewGuid().ToString("N"));

    private LevelStore Store => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static VoxelScene Scene(byte color = 7)
    {
        VoxelWorld world = EditorSession.CreateStarterWorld(color);
        var scene = new VoxelScene();
        scene.ReplacePalette(world.Palette);
        scene.Add(world, ObjectTransform.Identity, "Object 1");
        return scene;
    }

    [Fact]
    public void ALevelSurvivesBeingSavedAndOpened()
    {
        LevelStore store = Store;
        string path = store.Save(Scene(), "House");

        VoxelScene loaded = LevelStore.Load(path);

        Assert.Single(loaded.Objects);
        Assert.Equal(Scene().Objects[0].Grid.SolidCount, loaded.Objects[0].Grid.SolidCount);
    }

    [Fact]
    public void AnEmptyLibraryListsNothingRatherThanFailing()
    {
        Assert.Empty(Store.List());
    }

    /// <summary>Newest first: the level being worked on is the one wanted next.</summary>
    [Fact]
    public void LevelsAreListedNewestFirst()
    {
        LevelStore store = Store;
        store.Save(Scene(), "Older");

        // Filesystem timestamps are coarse enough that two saves in a row can share one.
        File.SetLastWriteTime(store.PathFor("Older"), DateTime.Now.AddMinutes(-10));
        store.Save(Scene(), "Newer");

        Assert.Equal(["Newer", "Older"], store.List().Select(level => level.Name));
    }

    [Fact]
    public void SavingTwiceReplacesRatherThanAccumulating()
    {
        LevelStore store = Store;
        store.Save(Scene(), "House");
        store.Save(Scene(), "House");

        Assert.Single(store.List());
    }

    [Theory]
    [InlineData("Level/One", "Level_One")]
    [InlineData("a:b*c?", "a_b_c_")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("", "Untitled")]
    [InlineData("   ", "Untitled")]
    public void NamesAreMadeSafeForAFilesystem(string typed, string expected)
    {
        Assert.Equal(expected, LevelStore.MakeFileSafe(typed));
    }

    /// <summary>
    /// The name the user typed is what the level calls itself, whatever had to be done to the
    /// filename. Sanitising the manifest too would quietly rename their level.
    /// </summary>
    [Fact]
    public void TheTypedNameReachesTheFileEvenWhenTheFilenameCannot()
    {
        LevelStore store = Store;
        string path = store.Save(Scene(), "Level/One");

        Assert.EndsWith("Level_One.vxlevel", path, StringComparison.Ordinal);
        Assert.Equal("Level/One", EditorApp.Core.Project.VxLevelFile.ReadManifest(path).Name);
    }

    /// <summary>
    /// Saving over another level by accident is an afternoon gone, and a phone has no folder to
    /// notice the collision in — so a fresh save is offered a name nothing is using.
    /// </summary>
    [Fact]
    public void AnUnusedNameStepsAsideFromWhatIsAlreadyThere()
    {
        LevelStore store = Store;
        store.Save(Scene(), "House");

        Assert.Equal("House 2", store.UnusedName("House"));

        store.Save(Scene(), "House 2");
        Assert.Equal("House 3", store.UnusedName("House"));
    }

    [Fact]
    public void AnUnusedNameLeavesAFreeOneAlone()
    {
        Assert.Equal("House", Store.UnusedName("House"));
    }

    [Fact]
    public void DeletingRemovesTheLevel()
    {
        LevelStore store = Store;
        string path = store.Save(Scene(), "House");

        store.Delete(path);

        Assert.Empty(store.List());
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// Delete only ever reaches inside the library. It is handed a path from a list the app built,
    /// but a path is a path, and this one is not allowed to point anywhere else.
    /// </summary>
    [Fact]
    public void DeletingWillNotReachOutsideTheLibrary()
    {
        LevelStore store = Store;
        store.Save(Scene(), "House");

        string outsider = Path.Combine(Path.GetTempPath(), "blockage-outsider-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(outsider, "not a level");

        try
        {
            store.Delete(outsider);
            Assert.True(File.Exists(outsider), "a path outside the library was deleted");
        }
        finally
        {
            File.Delete(outsider);
        }
    }
}
