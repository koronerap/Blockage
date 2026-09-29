using System.Text.RegularExpressions;
using EditorApp.Ui;

namespace EditorApp;

/// <summary>
/// A crash the last time the editor ran, offered as a report (Fullreleaseplan 9.10): the crash log's
/// account of it, filled into GitHub's bug form. Nothing is sent by Blockage. The browser opens on
/// the form, where the user reads it over and chooses whether to send it.
/// </summary>
public static partial class CrashReport
{
    private const string Crashed = "FAILED while unhandled";

    /// <summary>
    /// What the log says of the last session's crash, or null when it ended well. A session starts
    /// at a "started" line; only the one before this run counts, so a crash is offered once. So does
    /// only what came as an exception: a native abort leaves nothing to report, and a process that was
    /// closed from outside is not a crash at all.
    /// </summary>
    public static string? LastSessionCrash(string log)
    {
        int started = log.LastIndexOf("] started, version", StringComparison.Ordinal);
        int from = started < 0 ? 0 : log.IndexOf('\n', started) is var end and >= 0 ? end + 1 : log.Length;
        int crash = log.IndexOf(Crashed, from, StringComparison.Ordinal);
        if (crash < 0)
        {
            return null;
        }

        int entry = log.LastIndexOf('\n', crash) + 1;
        Match next = NextEntry().Match(log, crash);
        return log[entry..(next.Success ? next.Index : log.Length)].Trim();
    }

    /// <summary>The bug form, filled in with the version, the platform and the log's account of the crash.</summary>
    public static string IssueUrl(string? crash, string version, string platform)
    {
        var url = new System.Text.StringBuilder($"{Links.Repository}/issues/new?template=bug_report.yml");
        url.Append("&version=").Append(Uri.EscapeDataString(version));
        url.Append("&platform=").Append(Uri.EscapeDataString(platform));
        if (crash is not null)
        {
            // The exception's own line, under the log's "FAILED while", names the crash.
            string what = crash.Split('\n').Skip(1).FirstOrDefault()?.Trim() ?? "Blockage closed unexpectedly";
            url.Append("&title=").Append(Uri.EscapeDataString("Crash: " + (what.Length > 90 ? what[..90] : what)));

            // Kept well inside what a browser takes in one address; the top of the stack is what matters.
            string text = crash.Length > 3000 ? crash[..3000] + "\n..." : crash;
            url.Append("&crash=").Append(Uri.EscapeDataString(text));
        }

        return url.ToString();
    }

    /// <summary>The platform as the bug form lists it.</summary>
    public static string Platform =>
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Android";

    /// <summary>Where the log's next entry starts: a new line and a time in brackets.</summary>
    [GeneratedRegex(@"\n\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]")]
    private static partial Regex NextEntry();
}
