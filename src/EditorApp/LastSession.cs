using System.Text.Json;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;

namespace EditorApp;

/// <summary>What the last session closed on, as <see cref="LastSession.Read"/> finds it.</summary>
/// <param name="ProjectPath">Where the level was saved, if it ever was; the recovered level saves there.</param>
/// <param name="HadUnsavedChanges">Whether it held work its file did not — work that was then thrown away, or it would have been saved.</param>
public sealed record LastSessionInfo(string LevelPath, string ProjectName, string? ProjectPath, bool HadUnsavedChanges, DateTime SavedUtc);

/// <summary>
/// Blender's quit.blend: the level as the editor last closed on it, written on every clean exit
/// whatever the answer to "discard your changes?" was — so work thrown away by a wrong click on the
/// way out can still be had back. The recovery folder is for crashes, and is emptied on a clean
/// exit; this is for the other kind of accident.
/// </summary>
public sealed class LastSession(string? directory = null)
{
    private readonly string _directory = directory ?? DefaultDirectory;

    /// <summary>Beside the preferences and the recent-files list.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EditorApp");

    public string LevelPath => Path.Combine(_directory, "last-session" + VxLevelFile.Extension);

    private string InfoPath => Path.Combine(_directory, "last-session.json");

    /// <summary>
    /// Keeps what is open. Written beside the old copy and moved over it, so a failure halfway
    /// leaves the last good one rather than half a file. Never throws: the editor is closing.
    /// </summary>
    public void Write(EditorSession session)
    {
        try
        {
            Directory.CreateDirectory(_directory);

            string temporary = LevelPath + ".tmp";
            VxLevelFile.Save(session.Scene, temporary, session.ProjectName);
            File.Move(temporary, LevelPath, overwrite: true);

            var info = new Stored(session.ProjectName, session.ProjectPath, session.HasUnsavedChanges, DateTime.UtcNow);
            File.WriteAllText(InfoPath, JsonSerializer.Serialize(info));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or VxLevelFormatException)
        {
            CrashLog.Record("keeping the last session", exception);
        }
    }

    /// <summary>What the last session left, or null when it left nothing. A missing or unreadable note is an untitled level.</summary>
    public LastSessionInfo? Read()
    {
        if (!File.Exists(LevelPath))
        {
            return null;
        }

        Stored? info = null;
        try
        {
            if (File.Exists(InfoPath))
            {
                info = JsonSerializer.Deserialize<Stored>(File.ReadAllText(InfoPath));
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // The level is what matters; without its note it comes back as an untitled one.
        }

        return new LastSessionInfo(
            LevelPath,
            info?.ProjectName ?? "Untitled",
            info?.ProjectPath,
            info?.HadUnsavedChanges ?? true,
            info?.SavedUtc ?? File.GetLastWriteTimeUtc(LevelPath));
    }

    private sealed record Stored(string ProjectName, string? ProjectPath, bool HadUnsavedChanges, DateTime SavedUtc);
}
