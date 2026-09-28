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
    /// window at a fixed place, the way the shell's panels are drawn.
    /// </summary>
    public void Frame(Action draw, Vector2? mouse = null, bool pressed = false)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (mouse is { } position)
        {
            _mouse = position;
        }

        io.AddMousePosEvent(_mouse.X, _mouse.Y);
        io.AddMouseButtonEvent(0, pressed);

        ImGui.NewFrame();

        ImGui.SetNextWindowPos(new Vector2(20f, 20f));
        ImGui.SetNextWindowSize(new Vector2(600f, 200f));
        ImGui.Begin("##harness", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove);
        draw();
        ImGui.End();

        ImGui.Render();
    }

    /// <summary>A full click at a point: press on one frame, release on the next, as a hand would.</summary>
    public void Click(Vector2 at, Action draw)
    {
        Frame(draw, at, pressed: false);
        Frame(draw, at, pressed: true);
        Frame(draw, at, pressed: false);
    }

    public void Dispose() => ImGui.DestroyContext(_context);
}
