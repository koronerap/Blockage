using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Project;

/// <summary>Work a session autosaved and never got to put away — found on the next start.</summary>
public sealed record RecoveryEntry(
    string LevelPath,
    int ProcessId,
    string? ProjectPath,
    string ProjectName,
    DateTime SavedUtc);

/// <summary>
/// Where autosaves live, one pair of files per running editor: <c>&lt;pid&gt;.vxlevel</c> with the
/// level, and <c>&lt;pid&gt;.json</c> saying which project it came from.
///
/// Named by process so two editors open at once never write over each other's copy — and so that on
/// start, a copy whose process is no longer running is known to be one nobody put away: the editor
/// that wrote it crashed, was killed, or lost its power. A clean exit deletes its own pair.
///
/// Whether a process is still running is asked of the host, since what counts as "still the editor"
/// is a platform question, and a test wants to answer it without starting processes.
/// </summary>
public sealed class RecoveryStore(string directory, int processId, Func<int, bool> isEditorRunning)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Directory { get; } = directory;

    /// <summary>This editor's own autosave.</summary>
    public string LevelPath => LevelPathFor(processId);

    private string LevelPathFor(int id) => Path.Combine(Directory, id.ToString(CultureInfo.InvariantCulture) + VxLevelFile.Extension);

    private string InfoPathFor(int id) => Path.Combine(Directory, id.ToString(CultureInfo.InvariantCulture) + ".json");

    /// <summary>
    /// Writes this editor's autosave. Safe to call away from the editing thread as long as the scene
    /// is one nobody else is touching — a <see cref="VoxelScene.Snapshot"/>.
    ///
    /// The level goes first and the note about it second, each moved into place whole. A crash
    /// between the two leaves a level with an older note, or none, which is still worth offering.
    /// </summary>
    public void Write(VoxelScene snapshot, string? projectPath, string projectName)
    {
        System.IO.Directory.CreateDirectory(Directory);

        VxLevelFile.Save(snapshot, LevelPath, projectName);

        var info = new RecoveryInfo
        {
            ProjectPath = projectPath,
            ProjectName = projectName,
            SavedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        string infoPath = InfoPathFor(processId);
        string temporary = infoPath + ".saving";
        File.WriteAllText(temporary, JsonSerializer.Serialize(info, JsonOptions));
        File.Move(temporary, infoPath, overwrite: true);
    }

    /// <summary>Removes this editor's autosave: the work was saved, discarded, or is being closed cleanly.</summary>
    public void Delete() => Delete(processId);

    public void Delete(RecoveryEntry entry) => Delete(entry.ProcessId);

    private void Delete(int id)
    {
        TryDelete(LevelPathFor(id));
        TryDelete(InfoPathFor(id));
    }

    /// <summary>
    /// Takes an abandoned autosave over as this editor's own — what recovering it means. The copy on
    /// disk then already matches what is open, and stays there until this session puts it away, so a
    /// second crash straight after recovering loses nothing either.
    /// </summary>
    public void Adopt(RecoveryEntry entry)
    {
        if (entry.ProcessId == processId)
        {
            return;
        }

        File.Move(entry.LevelPath, LevelPath, overwrite: true);

        string info = InfoPathFor(entry.ProcessId);
        if (File.Exists(info))
        {
            File.Move(info, InfoPathFor(processId), overwrite: true);
        }
    }

    /// <summary>Autosaves left behind by editors that are no longer running, newest first.</summary>
    public IReadOnlyList<RecoveryEntry> FindAbandoned()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var found = new List<RecoveryEntry>();

        foreach (string level in System.IO.Directory.EnumerateFiles(Directory, "*" + VxLevelFile.Extension))
        {
            // The pattern also matches longer extensions on some platforms; only the exact one counts.
            if (!level.EndsWith(VxLevelFile.Extension, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(Path.GetFileNameWithoutExtension(level), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || id == processId
                || isEditorRunning(id))
            {
                continue;
            }

            found.Add(Describe(level, id));
        }

        return [.. found.OrderByDescending(entry => entry.SavedUtc)];
    }

    /// <summary>What the note says about an autosave — or, without a note, what the level itself does.</summary>
    private RecoveryEntry Describe(string level, int id)
    {
        try
        {
            string infoPath = InfoPathFor(id);
            if (File.Exists(infoPath)
                && JsonSerializer.Deserialize<RecoveryInfo>(File.ReadAllText(infoPath), JsonOptions) is { } info
                && DateTime.TryParse(info.SavedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime saved))
            {
                return new RecoveryEntry(level, id, info.ProjectPath, info.ProjectName, saved.ToUniversalTime());
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to the level's own manifest: a damaged note must not hide the work.
        }

        string name = "Untitled";
        try
        {
            name = VxLevelFile.ReadManifest(level).Name;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or VxLevelFormatException)
        {
        }

        return new RecoveryEntry(level, id, null, name, File.GetLastWriteTimeUtc(level));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind, it is offered again next time — which is the safe way for this to fail.
        }
    }

    private sealed class RecoveryInfo
    {
        [JsonPropertyName("projectPath")]
        public string? ProjectPath { get; set; }

        [JsonPropertyName("projectName")]
        public string ProjectName { get; set; } = "Untitled";

        [JsonPropertyName("savedUtc")]
        public string SavedUtc { get; set; } = string.Empty;
    }
}

/// <summary>What the autosave timer wants done this frame.</summary>
public enum AutosaveStep
{
    None,

    /// <summary>Take a snapshot and write it.</summary>
    Write,

    /// <summary>The level has been saved or replaced: the autosave on disk is stale, remove it.</summary>
    Delete,
}

/// <summary>
/// When to autosave. The clock only runs while there is unsaved work, so the most that can ever be
/// lost is one interval of it; a level with nothing new since the last autosave is not written again;
/// and once the work is saved properly, the autosave is removed so it can never be offered in place
/// of something newer.
/// </summary>
public sealed class AutosaveSchedule(TimeSpan interval)
{
    private double _dirtySeconds;
    private long _writtenRevision = -1;
    private bool _onDisk;

    public TimeSpan Interval { get; } = interval;

    public AutosaveStep Tick(double seconds, bool hasUnsavedChanges, long revision)
    {
        if (!hasUnsavedChanges)
        {
            _dirtySeconds = 0;
            _writtenRevision = -1;

            if (_onDisk)
            {
                _onDisk = false;
                return AutosaveStep.Delete;
            }

            return AutosaveStep.None;
        }

        _dirtySeconds += seconds;

        return revision != _writtenRevision && _dirtySeconds >= Interval.TotalSeconds
            ? AutosaveStep.Write
            : AutosaveStep.None;
    }

    /// <summary>The write for <paramref name="revision"/> finished; the clock starts over.</summary>
    public void Written(long revision)
    {
        _writtenRevision = revision;
        _dirtySeconds = 0;
        _onDisk = true;
    }

    /// <summary>A write failed: wait a whole interval before trying again rather than every frame.</summary>
    public void Failed() => _dirtySeconds = 0;

    /// <summary>The copy on disk was removed from outside — a clean exit.</summary>
    public void Forget()
    {
        _onDisk = false;
        _writtenRevision = -1;
    }
}
