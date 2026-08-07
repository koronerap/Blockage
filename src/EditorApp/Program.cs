using EditorApp;

// --smoke[=frames] opens the window, renders a few frames and exits. Lets a build pipeline verify
// that GL context creation, shader compilation and the first upload all work without a human.
int smokeFrames = 0;
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
}

using var application = new EditorApplication(smokeFrames);
application.Run();
