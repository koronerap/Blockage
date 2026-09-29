using System.Text;

namespace EditorApp;

/// <summary>
/// A record of anything that went badly, written beside the recent-files list.
///
/// A crash the user has to describe from memory is a crash nobody can fix. This turns "it closed
/// when I opened my level" into a stack trace and a file name.
///
/// It catches managed failures. What it cannot catch is a native abort — an ImGui assertion, or a
/// driver fault — because those never become an exception; the process is simply gone. The startup
/// banner is there for exactly that case: if the log ends at "started" with no matching exit, the
/// failure was below the runtime.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EditorApp",
        "crash.log");

    /// <summary>
    /// What the log said of a crash in the session before this one, read as the editor starts and
    /// before it adds to the log; null when that session ended well. See <see cref="CrashReport"/>.
    /// </summary>
    public static string? PreviousCrash { get; private set; }

    /// <summary>
    /// Raised once something has escaped and the process is going down — the last moment anything
    /// can still be written. Autosave uses it to put the unsaved level in the recovery folder.
    /// </summary>
    public static event Action? Crashing;

    /// <summary>Routes anything that escapes the application here, then to the console.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Record("unhandled", exception);
                Console.Error.WriteLine($"Blockage crashed. Details written to {Path}");
            }

            try
            {
                Crashing?.Invoke();
            }
            catch (Exception failure)
            {
                // Whatever goes wrong now, the original failure is already on record.
                Record("rescuing work while crashing", failure);
            }
        };

        PreviousCrash = ReadPreviousCrash();
        Note($"started, version {typeof(CrashLog).Assembly.GetName().Version}");
    }

    private static string? ReadPreviousCrash()
    {
        try
        {
            return File.Exists(Path) ? CrashReport.LastSessionCrash(File.ReadAllText(Path)) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Record(string context, Exception exception) =>
        Append($"FAILED while {context}{Environment.NewLine}{exception}");

    public static void Note(string message) => Append(message);

    private static void Append(string message)
    {
        try
        {
            lock (Gate)
            {
                string? directory = System.IO.Path.GetDirectoryName(Path);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                // Trimmed rather than rotated: this only ever has to explain the last few sessions,
                // and a log that grows without limit is its own bug.
                if (File.Exists(Path) && new FileInfo(Path).Length > 256 * 1024)
                {
                    File.Delete(Path);
                }

                File.AppendAllText(
                    Path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Failing to write the crash log must not become the crash.
        }
    }
}
