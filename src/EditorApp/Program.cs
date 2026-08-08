using EditorApp;

// Only when there is something to report. Launching the editor normally has no arguments and wants
// nothing to do with a terminal.
if (args.Length > 0)
{
    NativeConsole.AttachToParentTerminal();
}

// --smoke[=frames]        opens the window, renders a few frames and exits. Lets a build pipeline
//                         verify GL context creation, shader compilation and the first upload.
// --export-to=<dir>       runs the whole export chain with no window and exits. Add --level=<path>
//                         to export an existing .vxlevel instead of the built-in demo scene.
// --stress[=halfExtent]   measures build, mesh, greedy and file times on a large level and exits.
// --shading=lit|unlit     which shading mode to start in. Mainly so a screenshot run can capture
//                         either one, since a shading change is only ever visible in a picture.
int smokeFrames = 0;
int stressHalfExtent = 0;
string? exportDirectory = null;
string? levelPath = null;
string? screenshotPath = null;
bool startUnlit = false;

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
    else if (argument.StartsWith("--screenshot=", StringComparison.Ordinal))
    {
        screenshotPath = argument["--screenshot=".Length..];
    }
    else if (argument.StartsWith("--shading=", StringComparison.Ordinal))
    {
        startUnlit = argument["--shading=".Length..].Equals("unlit", StringComparison.OrdinalIgnoreCase);
    }
}

if (stressHalfExtent > 0)
{
    return StressCheck.Run(stressHalfExtent);
}

if (exportDirectory is not null)
{
    return HeadlessExport.Run(exportDirectory, levelPath);
}

using var application = new EditorApplication(smokeFrames, screenshotPath, startUnlit);
application.Run();
return 0;
