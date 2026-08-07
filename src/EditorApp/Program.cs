using EditorApp;

// --smoke[=frames]        opens the window, renders a few frames and exits. Lets a build pipeline
//                         verify GL context creation, shader compilation and the first upload.
// --export-to=<dir>       runs the whole export chain with no window and exits. Add --level=<path>
//                         to export an existing .vxlevel instead of the built-in demo scene.
int smokeFrames = 0;
string? exportDirectory = null;
string? levelPath = null;

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
    else if (argument.StartsWith("--export-to=", StringComparison.Ordinal))
    {
        exportDirectory = argument["--export-to=".Length..];
    }
    else if (argument.StartsWith("--level=", StringComparison.Ordinal))
    {
        levelPath = argument["--level=".Length..];
    }
}

if (exportDirectory is not null)
{
    return HeadlessExport.Run(exportDirectory, levelPath);
}

using var application = new EditorApplication(smokeFrames);
application.Run();
return 0;
