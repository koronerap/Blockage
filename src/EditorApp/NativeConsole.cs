using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EditorApp;

/// <summary>
/// Borrows the terminal that started the process, when there is one.
///
/// The editor is a GUI application, so Windows gives it no console — which is the point, since a
/// black window opening behind the editor every time is noise. But the same executable also has
/// headless modes (<c>--export-to</c>, <c>--stress</c>, <c>--smoke</c>) whose entire output is
/// console text, and a GUI process writes that into a handle nobody is holding.
///
/// Attaching to the parent's console fixes those without ever creating a window: if there is no
/// console to attach to — launched from Explorer, or from a shortcut — the call fails and nothing
/// happens, which is exactly the wanted behaviour.
/// </summary>
public static partial class NativeConsole
{
    private const int AttachParentProcess = -1;

    /// <summary>
    /// Hooks console output up to the terminal that launched this process. Does nothing when the
    /// output is already going somewhere — a pipe or a file redirect is a handle that works fine on
    /// its own, and replacing it would send the output to the wrong place.
    /// </summary>
    public static void AttachToParentTerminal()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected || !TryAttach())
        {
            return;
        }

        // The streams were bound to the invalid handle the process started with, so they have to be
        // rebuilt against the console that now exists.
        var standardOutput = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(standardOutput);
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    [SupportedOSPlatform("windows")]
    private static bool TryAttach()
    {
        try
        {
            return AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
