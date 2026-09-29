using System.Runtime.CompilerServices;

namespace EditorApp.Tests;

/// <summary>What every test in this assembly starts with.</summary>
internal static class TestSetup
{
    /// <summary>
    /// The crash log in a folder of the tests' own, not the user's. Tests open missing and broken
    /// levels on purpose, and the editor logs what it caught; the user's log is what goes into a bug
    /// report, and those entries would only mislead it.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepTheCrashLogToTheTests() =>
        CrashLog.Path = Path.Combine(Path.GetTempPath(), "blockage-tests", $"crash-{Environment.ProcessId}.log");
}
