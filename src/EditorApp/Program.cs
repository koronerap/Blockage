using EditorApp;

// Only when there is something to report. Launching the editor normally has no arguments and wants
// nothing to do with a terminal.
if (args.Length > 0)
{
    NativeConsole.AttachToParentTerminal();
}

CrashLog.Install();

// A Mac's Finder starts an app in /, where nothing can be written and nobody keeps their levels —
// and a level not yet saved is exported, and browsed for, from the working directory.
if (!OperatingSystem.IsWindows() && Environment.CurrentDirectory == "/")
{
    Environment.CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

// --smoke[=frames]        opens the window, renders a few frames and exits. Lets a build pipeline
//                         verify GL context creation, shader compilation and the first upload.
// --export-to=<dir>       runs the whole export chain with no window and exits: every format, for
//                         tools/validate-exports.ps1 to open in Blender and Unity. Add --level=<path>
//                         to export an existing .vxlevel or .vox instead of the sample level.
// --stress[=halfExtent]   measures build, mesh, greedy and file times on a large level and exits.
// --shading=lit|unlit     which shading mode to start in. Mainly so a screenshot run can capture
//                         either one, since a shading change is only ever visible in a picture.
// <path>.vxlevel          opens that level instead of the starter cube — what double-clicking a
//                         level in Explorer passes. --level=<path> does the same.
// --view=front|back|right|left|top|bottom
//                         start looking along an axis, in orthographic — for the same reason.
int smokeFrames = 0;
int stressHalfExtent = 0;
string? exportDirectory = null;
string? levelPath = null;
string? screenshotPath = null;
string? iconPath = null;
bool startUnlit = false;
EditorApp.Rendering.AlignedView? startView = null;

foreach (string argument in args)
{
    if (argument == "--smoke")
    {
        smokeFrames = 60;
    }
    else if (argument.StartsWith("--smoke=", StringComparison.Ordinal)
        && int.TryParse(argument.AsSpan("--smoke=".Length), out int frames))
    {
        smokeFrames = Math.Max(frames, 1);
    }
    else if (argument == "--stress")
    {
        stressHalfExtent = 128;
    }
    else if (argument.StartsWith("--stress=", StringComparison.Ordinal)
        && int.TryParse(argument.AsSpan("--stress=".Length), out int extent))
    {
        stressHalfExtent = Math.Max(extent, 8);
    }
    else if (argument.StartsWith("--export-to=", StringComparison.Ordinal))
    {
        exportDirectory = argument["--export-to=".Length..];
    }
    else if (argument.StartsWith("--level=", StringComparison.Ordinal))
    {
        levelPath = argument["--level=".Length..];
    }
    else if (!argument.StartsWith("--", StringComparison.Ordinal)
        && argument.EndsWith(EditorApp.Core.Project.VxLevelFile.Extension, StringComparison.OrdinalIgnoreCase))
    {
        levelPath = argument;
    }
    else if (argument.StartsWith("--screenshot=", StringComparison.Ordinal))
    {
        screenshotPath = argument["--screenshot=".Length..];
    }
    else if (argument.StartsWith("--shading=", StringComparison.Ordinal))
    {
        startUnlit = argument["--shading=".Length..].Equals("unlit", StringComparison.OrdinalIgnoreCase);
    }
    else if (argument.StartsWith("--view=", StringComparison.Ordinal)
        && Enum.TryParse(argument["--view=".Length..], ignoreCase: true, out EditorApp.Rendering.AlignedView view))
    {
        startView = view;
    }
    else if (argument.StartsWith("--write-icon=", StringComparison.Ordinal))
    {
        iconPath = argument["--write-icon=".Length..];
    }
}

if (iconPath is not null)
{
    // The committed .ico and .icns are generated, not drawn by hand, so they can be regenerated
    // whenever the mark or the shading constants behind it change.
    bool mac = iconPath.EndsWith(".icns", StringComparison.OrdinalIgnoreCase);
    File.WriteAllBytes(iconPath, mac ? EditorApp.Core.Export.AppIcon.EncodeIcns() : EditorApp.Core.Export.AppIcon.EncodeIco());
    Console.WriteLine($"Wrote {iconPath} ({(mac ? EditorApp.Core.Export.AppIcon.IcnsEntries.Length : EditorApp.Core.Export.AppIcon.Sizes.Length)} sizes).");
    return 0;
}

if (stressHalfExtent > 0)
{
    return StressCheck.Run(stressHalfExtent);
}

if (exportDirectory is not null)
{
    return HeadlessExport.Run(exportDirectory, levelPath);
}

using var application = new EditorApplication(smokeFrames, screenshotPath, startUnlit, startView, levelPath);
application.Run();
return 0;
