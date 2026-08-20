using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Tests;

/// <summary>
/// Where the export dialog proposes to write. Its own suggestion, not a path it inherited from
/// whatever was open before.
/// </summary>
public class ExportPathTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-export-path", Guid.NewGuid().ToString("N"));

    public ExportPathTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private VoxelScene Cube()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "block");
        return scene;
    }

    /// <summary>Saves a level under the given name and hands back a session that has it open.</summary>
    private EditorSession Opened(string name)
    {
        string path = Path.Combine(_directory, name + VxLevelFile.Extension);
        VxLevelFile.Save(Cube(), path, name);

        var session = new EditorSession();
        session.ReplaceScene(VxLevelFile.LoadScene(path), path);
        return session;
    }

    [Fact]
    public void TheSuggestedNameFollowsTheLevel()
    {
        EditorSession session = Opened("truck");
        var export = new ExportController(session);

        export.Show();

        Assert.Equal("truck.obj", Path.GetFileName(export.OutputPath));
    }

    [Fact]
    public void OpeningASecondLevelDoesNotKeepTheFirstOnesPath()
    {
        // The reported failure: export one level, open another, press Export — and the first level's
        // mesh is quietly overwritten while the file that was asked for never appears.
        EditorSession session = Opened("spas12");
        var export = new ExportController(session);
        export.Show();

        Assert.Equal("spas12.obj", Path.GetFileName(export.OutputPath));

        string second = Path.Combine(_directory, "truck" + VxLevelFile.Extension);
        VxLevelFile.Save(Cube(), second, "truck");
        session.ReplaceScene(VxLevelFile.LoadScene(second), second);

        export.Show();

        Assert.Equal("truck.obj", Path.GetFileName(export.OutputPath));
    }

    [Fact]
    public void TheChosenFolderIsRemembered()
    {
        // Only the name follows the level. Having to browse back to the same folder for every export
        // would trade one annoyance for another.
        EditorSession session = Opened("truck");
        var export = new ExportController(session);
        export.Show();

        string elsewhere = Path.Combine(_directory, "out");
        Directory.CreateDirectory(elsewhere);
        export.SetOutputPath(Path.Combine(elsewhere, "truck.obj"));

        export.Show();

        Assert.Equal(elsewhere, Path.GetDirectoryName(export.OutputPath));
        Assert.Equal("truck.obj", Path.GetFileName(export.OutputPath));
    }
}
