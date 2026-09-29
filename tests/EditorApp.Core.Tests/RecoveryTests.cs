using EditorApp.Core.Project;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Tests;

/// <summary>
/// Autosave and crash recovery: where the copies go, which ones count as abandoned, and when the
/// clock says to write.
/// </summary>
public class RecoveryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-recovery-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RecoveryStore Store(int processId, params int[] running) =>
        new(_directory, processId, id => running.Contains(id));

    [Fact]
    public void AnAutosaveFromAnEditorThatIsGoneIsFound()
    {
        VoxelScene scene = VoxelScene.CreateStarter();
        Store(100).Write(scene.Snapshot(), @"C:\levels\castle.vxlevel", "castle");

        IReadOnlyList<RecoveryEntry> found = Store(200).FindAbandoned();

        RecoveryEntry entry = Assert.Single(found);
        Assert.Equal(100, entry.ProcessId);
        Assert.Equal(@"C:\levels\castle.vxlevel", entry.ProjectPath);
        Assert.Equal("castle", entry.ProjectName);
        Assert.Equal(scene.ContentHash(), VxLevelFile.LoadScene(entry.LevelPath).ContentHash());
    }

    /// <summary>Another editor still open owns its autosave; offering it would steal work in progress.</summary>
    [Fact]
    public void AnAutosaveFromAnEditorStillRunningIsLeftAlone()
    {
        Store(100).Write(VoxelScene.CreateStarter(), null, "Untitled");

        Assert.Empty(Store(200, running: 100).FindAbandoned());
    }

    [Fact]
    public void AnEditorNeverOffersItsOwnAutosave()
    {
        RecoveryStore store = Store(100);
        store.Write(VoxelScene.CreateStarter(), null, "Untitled");

        Assert.Empty(store.FindAbandoned());
    }

    [Fact]
    public void TheNewestAbandonedAutosaveComesFirst()
    {
        Store(100).Write(VoxelScene.CreateStarter(), null, "older");
        Thread.Sleep(20);
        Store(101).Write(VoxelScene.CreateStarter(), null, "newer");

        IReadOnlyList<RecoveryEntry> found = Store(200).FindAbandoned();

        Assert.Equal(["newer", "older"], found.Select(e => e.ProjectName));
    }

    /// <summary>
    /// A crash between writing the level and writing the note about it leaves a level alone. The
    /// work is in the level, so it is still offered, under the name the level itself carries.
    /// </summary>
    [Fact]
    public void ALevelWithoutItsNoteIsStillOffered()
    {
        Store(100).Write(VoxelScene.CreateStarter(), @"C:\a.vxlevel", "castle");
        File.Delete(Path.Combine(_directory, "100.json"));

        RecoveryEntry entry = Assert.Single(Store(200).FindAbandoned());

        Assert.Null(entry.ProjectPath);
        Assert.Equal("castle", entry.ProjectName);
    }

    [Fact]
    public void DeletingRemovesBothFiles()
    {
        RecoveryStore store = Store(100);
        store.Write(VoxelScene.CreateStarter(), null, "Untitled");

        store.Delete();

        Assert.Empty(Directory.GetFiles(_directory));
    }

    /// <summary>
    /// Recovering takes the abandoned copy over rather than deleting it: until this session writes an
    /// autosave of its own, that copy is the only one there is.
    /// </summary>
    [Fact]
    public void RecoveringMakesTheCopyThisEditorsOwn()
    {
        Store(100).Write(VoxelScene.CreateStarter(), @"C:\a.vxlevel", "castle");
        RecoveryStore mine = Store(200);
        RecoveryEntry entry = Assert.Single(mine.FindAbandoned());

        mine.Adopt(entry);

        Assert.Empty(mine.FindAbandoned());
        Assert.True(File.Exists(mine.LevelPath));
        Assert.Empty(Store(300, running: 200).FindAbandoned());
        Assert.Single(Store(300).FindAbandoned());
    }

    [Fact]
    public void AnEmptyOrMissingDirectoryHasNothingToOffer() =>
        Assert.Empty(Store(1).FindAbandoned());

    // ---- The schedule -----------------------------------------------------------------------------

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(120);

    [Fact]
    public void NothingIsWrittenWithoutUnsavedWork()
    {
        var schedule = new AutosaveSchedule(Interval);

        Assert.Equal(AutosaveStep.None, schedule.Tick(500, hasUnsavedChanges: false, revision: 1));
    }

    /// <summary>The clock only runs while there is something to lose.</summary>
    [Fact]
    public void TheIntervalIsCountedFromTheFirstUnsavedChange()
    {
        var schedule = new AutosaveSchedule(Interval);
        schedule.Tick(1000, hasUnsavedChanges: false, revision: 0);

        Assert.Equal(AutosaveStep.None, schedule.Tick(119, hasUnsavedChanges: true, revision: 1));
        Assert.Equal(AutosaveStep.Write, schedule.Tick(1, hasUnsavedChanges: true, revision: 1));
    }

    [Fact]
    public void NothingNewSinceTheLastWriteIsNotWrittenAgain()
    {
        var schedule = new AutosaveSchedule(Interval);
        schedule.Tick(120, true, 5);
        schedule.Written(5);

        Assert.Equal(AutosaveStep.None, schedule.Tick(500, true, 5));
        Assert.Equal(AutosaveStep.Write, schedule.Tick(1, true, 6));
    }

    [Fact]
    public void AfterAWriteTheClockStartsOver()
    {
        var schedule = new AutosaveSchedule(Interval);
        schedule.Tick(120, true, 5);
        schedule.Written(5);

        Assert.Equal(AutosaveStep.None, schedule.Tick(60, true, 6));
        Assert.Equal(AutosaveStep.Write, schedule.Tick(60, true, 6));
    }

    /// <summary>Once the work is properly saved, the autosave is stale and must not be offered.</summary>
    [Fact]
    public void SavingRemovesTheAutosaveOnce()
    {
        var schedule = new AutosaveSchedule(Interval);
        schedule.Tick(120, true, 5);
        schedule.Written(5);

        Assert.Equal(AutosaveStep.Delete, schedule.Tick(1, false, 5));
        Assert.Equal(AutosaveStep.None, schedule.Tick(1, false, 5));
    }

    [Fact]
    public void AFailedWriteWaitsAWholeIntervalBeforeTrying()
    {
        var schedule = new AutosaveSchedule(Interval);
        schedule.Tick(120, true, 5);
        schedule.Failed();

        Assert.Equal(AutosaveStep.None, schedule.Tick(100, true, 5));
        Assert.Equal(AutosaveStep.Write, schedule.Tick(20, true, 5));
    }
    /// <summary>Off in the preferences: nothing more is written, however long the work stays unsaved.</summary>
    [Fact]
    public void AtZeroNothingIsWritten()
    {
        var schedule = new AutosaveSchedule(Interval) { Interval = TimeSpan.Zero };

        Assert.Equal(AutosaveStep.None, schedule.Tick(100_000, hasUnsavedChanges: true, revision: 1));
    }

    /// <summary>Switched off after a copy was written, the copy still goes once the work is saved.</summary>
    [Fact]
    public void SwitchedOffAfterAWriteTheCopyStillGoesWhenSaved()
    {
        var schedule = new AutosaveSchedule(Interval);
        schedule.Tick(120, true, 5);
        schedule.Written(5);
        schedule.Interval = TimeSpan.Zero;

        Assert.Equal(AutosaveStep.Delete, schedule.Tick(1, hasUnsavedChanges: false, revision: 5));
    }
}
