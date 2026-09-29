using EditorApp;

namespace EditorApp.Tests;

/// <summary>
/// The recent list is read while it is being written: the Open Recent menu draws an entry and acts
/// on the click in the same loop, and acting on it promotes that entry to the front.
/// </summary>
public class RecentFilesTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-recent", Guid.NewGuid().ToString("N"));

    /// <summary>Its own store, so the suite never touches the real one.</summary>
    private string Store() => Path.Combine(_directory, "recent.txt");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void PromotingAnEntryWhileWalkingTheListDoesNotThrow()
    {
        // The reported crash, reduced: InvalidOperationException out of the menu's foreach, which
        // was unhandled and took the editor down with whatever was unsaved.
        var recent = new RecentFiles(Store());
        foreach (int i in Enumerable.Range(0, 5))
        {
            recent.Add(Path.Combine(Path.GetTempPath(), $"level{i}.vxlevel"));
        }

        string? clicked = null;

        foreach (string path in recent.Paths)
        {
            if (clicked is null && path.EndsWith("level3.vxlevel", StringComparison.Ordinal))
            {
                clicked = path;
                recent.Add(path);
            }
        }

        Assert.NotNull(clicked);
        Assert.Equal(clicked, recent.Paths[0]);
    }

    [Fact]
    public void TheListHandedOutIsNotTheOneKept()
    {
        var recent = new RecentFiles(Store());
        recent.Add(Path.Combine(Path.GetTempPath(), "a.vxlevel"));

        IReadOnlyList<string> first = recent.Paths;
        recent.Add(Path.Combine(Path.GetTempPath(), "b.vxlevel"));

        Assert.Single(first);
        Assert.Equal(2, recent.Paths.Count);
    }

    [Fact]
    public void RemovingWhileWalkingIsSafeToo()
    {
        // The other direction: a file that fails to open is dropped from the list, and that happens
        // from inside the same loop.
        var recent = new RecentFiles(Store());
        foreach (int i in Enumerable.Range(0, 4))
        {
            recent.Add(Path.Combine(Path.GetTempPath(), $"gone{i}.vxlevel"));
        }

        int walked = 0;
        foreach (string path in recent.Paths)
        {
            walked++;
            recent.Remove(path);
        }

        Assert.Equal(4, walked);
        Assert.Empty(recent.Paths);
    }
    /// <summary>Kept to the number the preferences ask for; asking for fewer drops the oldest at once.</summary>
    [Fact]
    public void TheListKeepsAsManyAsItIsSetTo()
    {
        var recent = new RecentFiles(Store()) { Capacity = 3 };
        for (int i = 0; i < 5; i++)
        {
            recent.Add(Path.Combine(_directory, $"level{i}.vxlevel"));
        }

        Assert.Equal(3, recent.Paths.Count);
        Assert.EndsWith("level4.vxlevel", recent.Paths[0], StringComparison.Ordinal);

        recent.Capacity = 1;
        Assert.Single(recent.Paths);
        Assert.Single(new RecentFiles(Store()).Paths);
    }

    [Fact]
    public void ClearingForgetsEverything()
    {
        var recent = new RecentFiles(Store());
        recent.Add(Path.Combine(_directory, "a.vxlevel"));

        recent.Clear();

        Assert.Empty(recent.Paths);
        Assert.Empty(new RecentFiles(Store()).Paths);
    }
}
