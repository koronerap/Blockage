using System.Diagnostics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;

namespace EditorApp;

/// <summary>
/// Keeps a copy of unsaved work in the recovery folder, and finds what an earlier session left
/// there when it did not close properly.
///
/// Every two minutes of unsaved work the level is written out. The copy is taken on the editing
/// thread — copying chunks is a memory copy — and compressed and written on another, since that is
/// the part that would stall a frame on a large level. Saving properly removes the copy; so does
/// closing cleanly, whichever answer the user gave to the unsaved-changes prompt. A copy still there
/// on the next start therefore means the editor that wrote it crashed, was killed, or lost power.
/// </summary>
public sealed class AutosaveController
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly EditorSession _session;
    private readonly RecoveryStore _store;
    private readonly AutosaveSchedule _schedule;

    private Task? _writing;
    private long _writingRevision;

    public AutosaveController(EditorSession session, string? directory = null, TimeSpan? interval = null)
    {
        _session = session;
        _store = new RecoveryStore(directory ?? DefaultDirectory, Environment.ProcessId, IsEditorRunning);
        _schedule = new AutosaveSchedule(interval ?? Interval);
    }

    /// <summary>Beside the crash log and the recent-files list.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EditorApp",
        "recovery");

    /// <summary>How often unsaved work is copied out. Zero switches it off. A preference.</summary>
    public TimeSpan Every
    {
        get => _schedule.Interval;
        set => _schedule.Interval = value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    /// <summary>When the last autosave finished, local time, or null when there is none on disk.</summary>
    public DateTime? LastWritten { get; private set; }

    public string OwnPath => _store.LevelPath;

    public void Tick(double seconds)
    {
        if (_writing is { IsCompleted: false })
        {
            return;
        }

        if (_writing is { } finished)
        {
            _writing = null;

            if (finished.IsCompletedSuccessfully)
            {
                _schedule.Written(_writingRevision);
                LastWritten = DateTime.Now;
            }
            else
            {
                _schedule.Failed();
                CrashLog.Record("autosaving", finished.Exception?.GetBaseException() ?? new IOException("Autosave failed."));
            }
        }

        switch (_schedule.Tick(seconds, _session.HasUnsavedChanges, _session.Revision))
        {
            case AutosaveStep.Write:
                Start();
                break;

            case AutosaveStep.Delete:
                _store.Delete();
                LastWritten = null;
                break;
        }
    }

    private void Start()
    {
        VoxelScene snapshot = _session.Scene.Snapshot();
        string? projectPath = _session.ProjectPath;
        string projectName = _session.ProjectName;

        _writingRevision = _session.Revision;
        _writing = Task.Run(() => _store.Write(snapshot, projectPath, projectName));
    }

    /// <summary>Waits for a write in progress, for tests and for the paths that must not race one.</summary>
    public void Flush()
    {
        try
        {
            _writing?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // Reported by the next Tick, like any other failed write.
        }
    }

    /// <summary>
    /// The editor is closing and the user has answered for the unsaved work, one way or the other:
    /// the copy is no longer anybody's safety net.
    /// </summary>
    public void CloseCleanly()
    {
        Flush();
        _store.Delete();
        _schedule.Forget();
        LastWritten = null;
    }

    /// <summary>
    /// The last chance, from the crash handler: writes what is open as it stands, on this thread.
    /// Nothing else is running any more, so there is nothing for it to race.
    /// </summary>
    public void WriteBeforeDying()
    {
        if (!_session.HasUnsavedChanges)
        {
            return;
        }

        try
        {
            Flush();
            _store.Write(_session.Scene, _session.ProjectPath, _session.ProjectName);
        }
        catch (Exception exception)
        {
            CrashLog.Record("writing the recovery copy while crashing", exception);
        }
    }

    public IReadOnlyList<RecoveryEntry> FindAbandoned()
    {
        try
        {
            return _store.FindAbandoned();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CrashLog.Record("looking for autosaves", exception);
            return [];
        }
    }

    /// <summary>
    /// The abandoned copy has just been loaded: it becomes this session's own autosave, already up to
    /// date with what is open.
    /// </summary>
    public void Adopt(RecoveryEntry entry)
    {
        Flush();
        _store.Adopt(entry);
        _schedule.Written(_session.Revision);
        LastWritten = DateTime.Now;
    }

    public void Discard(RecoveryEntry entry) => _store.Delete(entry);

    /// <summary>
    /// Whether a process is still this editor. The name is checked as well as the id, since an id is
    /// handed out again once its process has gone, and a copy must not be kept from its owner's heir
    /// just because some unrelated program happens to have the number now.
    /// </summary>
    private static bool IsEditorRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using Process self = Process.GetCurrentProcess();
            return !process.HasExited
                && string.Equals(process.ProcessName, self.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
