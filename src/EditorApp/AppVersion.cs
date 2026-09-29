using System.Reflection;

namespace EditorApp;

/// <summary>
/// Which build this is: the project's version, and the commit it was built from when the build knew
/// it. Read from the assembly rather than written down twice, so the label can never disagree with
/// what was built.
/// </summary>
public static class AppVersion
{
    static AppVersion()
    {
        // The SDK writes "0.1.0+<full commit hash>" when it can see the repository, "0.1.0" when not.
        string informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        Number = plus >= 0 ? informational[..plus] : informational;

        string commit = plus >= 0 ? informational[(plus + 1)..] : string.Empty;
        Commit = commit.Length >= 7 ? commit[..7] : null;
    }

    /// <summary>"0.1.0".</summary>
    public static string Number { get; }

    /// <summary>The commit, shortened as git shortens it; null when the build did not know it.</summary>
    public static string? Commit { get; }

    /// <summary>What the welcome screen shows: "v0.1.0".</summary>
    public static string Label => $"v{Number}";
}
