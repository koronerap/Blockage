using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;

namespace EditorApp.Tests;

/// <summary>
/// Autosave as the editor runs it: written in the background while there is unsaved work, removed
/// once there is none, and offered back by the project controller after a session that did not close.
/// </summary>
public sealed class AutosaveTests : IDisposable
{
    /// <summary>A process id nothing on the machine will have: the editor that "crashed".</summary>
    private const int GoneProcess = 2_147_483_644;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-autosave-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static EditorSession Session()
    {
        var session = new EditorSession();
        session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);
        return session;
    }

    private AutosaveController Autosave(EditorSession session) =>
        new(session, _directory, TimeSpan.FromSeconds(60));

    [Fact]
    public void UnsavedWorkIsWrittenOnceTheIntervalHasPassed()
    {
        EditorSession session = Session();
        AutosaveController autosave = Autosave(session);
        session.RenameObject(session.Scene.Objects[0].Id, "Edited");

        autosave.Tick(30);
        autosave.Flush();
        Assert.False(File.Exists(autosave.OwnPath));

        autosave.Tick(30);
        autosave.Flush();
        autosave.Tick(0);

        Assert.True(File.Exists(autosave.OwnPath));
        Assert.Equal(session.Scene.ContentHash(), VxLevelFile.LoadScene(autosave.OwnPath).ContentHash());
        Assert.NotNull(autosave.LastWritten);
    }

    /// <summary>
    /// Once the work is saved properly, the copy is stale — offered after a crash, it would stand in
    /// for something older than what is on disk.
    /// </summary>
    [Fact]
    public void SavingRemovesTheAutosave()
    {
        EditorSession session = Session();
        AutosaveController autosave = Autosave(session);
        session.RenameObject(session.Scene.Objects[0].Id, "Edited");
        autosave.Tick(60);
        autosave.Flush();
        autosave.Tick(0);
        Assert.True(File.Exists(autosave.OwnPath));

        session.HasUnsavedChanges = false;
        autosave.Tick(0);

        Assert.False(File.Exists(autosave.OwnPath));
    }

    [Fact]
    public void ClosingCleanlyRemovesTheAutosave()
    {
        EditorSession session = Session();
        AutosaveController autosave = Autosave(session);
        session.RenameObject(session.Scene.Objects[0].Id, "Edited");
        autosave.Tick(60);

        autosave.CloseCleanly();

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void TheCrashHandlerWritesOnlyWhenThereIsSomethingToLose()
    {
        EditorSession session = Session();
        AutosaveController autosave = Autosave(session);

        autosave.WriteBeforeDying();
        Assert.False(File.Exists(autosave.OwnPath));

        session.RenameObject(session.Scene.Objects[0].Id, "Edited");
        autosave.WriteBeforeDying();
        Assert.True(File.Exists(autosave.OwnPath));
    }

    /// <summary>The whole way back: a crashed session's copy, offered and recovered into a fresh one.</summary>
    [Fact]
    public void RecoveringOpensTheCopyUnsavedAndTakesItOver()
    {
        VoxelScene lost = VoxelScene.CreateStarter();
        lost.Objects[0].Name = "Lost work";
        new RecoveryStore(_directory, GoneProcess, _ => false).Write(lost.Snapshot(), @"C:\levels\castle.vxlevel", "castle");

        EditorSession session = Session();
        int replaced = 0;
        var project = new ProjectController(session, () => replaced++) { Autosave = Autosave(session) };

        project.OfferRecovery();
        Assert.True(project.CanRecover);

        RecoveryEntry entry = Assert.Single(project.Autosave!.FindAbandoned());
        project.Recover(entry);

        Assert.Equal(lost.ContentHash(), session.Scene.ContentHash());
        Assert.Equal("Lost work", session.Scene.Objects[0].Name);
        Assert.Equal(@"C:\levels\castle.vxlevel", session.ProjectPath);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(1, replaced);

        Assert.False(project.CanRecover);
        Assert.True(File.Exists(project.Autosave.OwnPath));
        Assert.False(File.Exists(entry.LevelPath));
    }

    [Fact]
    public void WithNothingLeftBehindThereIsNothingToRecover()
    {
        EditorSession session = Session();
        var project = new ProjectController(session, () => { }) { Autosave = Autosave(session) };

        project.OfferRecovery();

        Assert.False(project.CanRecover);
    }
}
