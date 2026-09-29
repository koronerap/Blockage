using System.Numerics;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// ImGui running with no window and no GPU, driven frame by frame with a synthetic mouse.
///
/// ImGui does not need a renderer to lay out, hit-test or open popups — only a font atlas it has
/// been told is uploaded. That is enough to press a button and ask what happened, which is the one
/// thing a screenshot cannot: a screenshot shows a popup closed, and cannot say whether clicking
/// would have opened it.
/// </summary>
internal sealed class ImGuiHarness : IDisposable
{
    private readonly nint _context;
    private Vector2 _mouse = new(-1f, -1f);

    /// <summary>The window <see cref="Frame"/> draws into. Tall enough for a whole panel when a test needs one.</summary>
    public Vector2 WindowSize { get; set; } = new(600f, 200f);

    public ImGuiHarness()
    {
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);

        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280f, 720f);
        io.DeltaTime = 1f / 60f;

        // The atlas has to exist and claim to be on the GPU, or NewFrame refuses to start.
        io.Fonts.GetTexDataAsRGBA32(out nint _, out int _, out int _);
        io.Fonts.SetTexID(1);
    }

    /// <summary>
    /// Runs one frame: moves the mouse, sets the button, and calls <paramref name="draw"/> inside a
    /// window at a fixed place, the way the shell's panels are drawn — or, with
    /// <paramref name="inWindow"/> off, bare, for something that opens windows of its own.
    /// </summary>
    public void Frame(Action draw, Vector2? mouse = null, bool pressed = false, bool inWindow = true)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (mouse is { } position)
        {
            _mouse = position;
        }

        io.AddMousePosEvent(_mouse.X, _mouse.Y);
        io.AddMouseButtonEvent(0, pressed);

        ImGui.NewFrame();

        if (inWindow)
        {
            ImGui.SetNextWindowPos(new Vector2(20f, 20f));
            ImGui.SetNextWindowSize(WindowSize);
            ImGui.Begin("##harness", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove);
            draw();
            ImGui.End();
        }
        else
        {
            draw();
        }

        ImGui.Render();
    }

    /// <summary>A full click at a point: press on one frame, release on the next, as a hand would.</summary>
    public void Click(Vector2 at, Action draw, bool inWindow = true)
    {
        Frame(draw, at, pressed: false, inWindow);
        Frame(draw, at, pressed: true, inWindow);
        Frame(draw, at, pressed: false, inWindow);
    }

    /// <summary>A right click at a point: the second button down on one frame, up on the next.</summary>
    public void RightClick(Vector2 at, Action draw, bool inWindow = true)
    {
        Frame(draw, at, pressed: false, inWindow);
        ImGui.GetIO().AddMouseButtonEvent(1, true);
        Frame(draw, at, pressed: false, inWindow);
        ImGui.GetIO().AddMouseButtonEvent(1, false);
        Frame(draw, at, pressed: false, inWindow);
    }

    /// <summary>Types text into whatever has keyboard focus, in one frame.</summary>
    public void Type(string text, Action draw)
    {
        ImGui.GetIO().AddInputCharactersUTF8(text);
        Frame(draw);
    }

    /// <summary>A key pressed on one frame and released on the next.</summary>
    public void Press(ImGuiKey key, Action draw)
    {
        ImGui.GetIO().AddKeyEvent(key, true);
        Frame(draw);
        ImGui.GetIO().AddKeyEvent(key, false);
        Frame(draw);
    }

    /// <summary>Press at one point, move to another with the button held, let go there.</summary>
    public void Drag(Vector2 from, Vector2 to, Action draw, bool inWindow = true)
    {
        Frame(draw, from, pressed: false, inWindow);
        Frame(draw, from, pressed: true, inWindow);
        Frame(draw, to, pressed: true, inWindow);
        Frame(draw, to, pressed: false, inWindow);
    }

    public void Dispose() => ImGui.DestroyContext(_context);
}
