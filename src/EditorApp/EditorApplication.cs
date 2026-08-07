using System.Numerics;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace EditorApp;

/// <summary>
/// Owns the window, the GL context, the world and the frame loop. Silk.NET gives us a window, a GL
/// binding and input; everything above that is ours (EditorApp.md §2).
/// </summary>
public sealed class EditorApplication : IDisposable
{
    private readonly IWindow _window;
    private readonly int _smokeFrames;

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;
    private GlRenderer? _renderer;

    private readonly VoxelWorld _world = new();
    private readonly FlyCamera _camera = new();

    private Vector2 _previousMousePosition;
    private bool _looking;
    private int _frameCount;
    private float _lastDelta = 1f / 60f;

    /// <param name="smokeFrames">When positive, the window closes after this many frames (used for automated smoke runs).</param>
    public EditorApplication(int smokeFrames = 0)
    {
        _smokeFrames = smokeFrames;

        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1600, 900),
            Title = "EditorApp — Voxel Level Editor",
            // 3.3 core covers everything this tool needs and runs on the widest range of drivers.
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3)),
            VSync = true,
            PreferredDepthBufferBits = 24,
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _imgui = new ImGuiController(_gl, _window, _input);
        _renderer = new GlRenderer(_gl);

        DemoScene.Fill(_world);

        if (_world.TryGetBounds(out Int3 min, out Int3 max))
        {
            _camera.FrameBox(min.ToVector3(), max.ToVector3() + Vector3.One);
        }

        _renderer.Lines.AddGroundGrid(64, new Color32(60, 66, 74), new Color32(92, 100, 110));

        if (_input.Mice.Count > 0)
        {
            _previousMousePosition = _input.Mice[0].Position;
        }
    }

    private void OnUpdate(double deltaSeconds)
    {
        _lastDelta = (float)deltaSeconds;
        UpdateCamera((float)deltaSeconds);
    }

    private void UpdateCamera(float deltaSeconds)
    {
        if (_input is null || _input.Mice.Count == 0 || _input.Keyboards.Count == 0)
        {
            return;
        }

        IMouse mouse = _input.Mice[0];
        IKeyboard keyboard = _input.Keyboards[0];
        ImGuiIOPtr io = ImGui.GetIO();

        Vector2 mousePosition = mouse.Position;
        Vector2 mouseDelta = mousePosition - _previousMousePosition;
        _previousMousePosition = mousePosition;

        bool wantsLook = mouse.IsButtonPressed(MouseButton.Right) && !io.WantCaptureMouse;
        if (wantsLook && !_looking)
        {
            _looking = true;
            mouse.Cursor.CursorMode = CursorMode.Disabled;
        }
        else if (!mouse.IsButtonPressed(MouseButton.Right) && _looking)
        {
            _looking = false;
            mouse.Cursor.CursorMode = CursorMode.Normal;
        }

        if (_looking)
        {
            _camera.Look(mouseDelta);
        }

        if (io.WantCaptureKeyboard)
        {
            return;
        }

        var movement = Vector3.Zero;
        if (keyboard.IsKeyPressed(Key.W)) movement.Z += 1f;
        if (keyboard.IsKeyPressed(Key.S)) movement.Z -= 1f;
        if (keyboard.IsKeyPressed(Key.D)) movement.X += 1f;
        if (keyboard.IsKeyPressed(Key.A)) movement.X -= 1f;
        if (keyboard.IsKeyPressed(Key.E)) movement.Y += 1f;
        if (keyboard.IsKeyPressed(Key.Q)) movement.Y -= 1f;

        float multiplier = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight) ? 4f : 1f;
        _camera.Move(movement, deltaSeconds, multiplier);
    }

    private void OnRender(double deltaSeconds)
    {
        if (_gl is null || _renderer is null || _imgui is null)
        {
            return;
        }

        _imgui.Update((float)deltaSeconds);

        _renderer.SyncDirtyChunks(_world);
        var viewport = new Vector2(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        _renderer.Render(_camera, viewport);

        StatsOverlay.Draw(_renderer, _camera, _world.SolidCount, _world.Chunks.Count, _lastDelta);
        _imgui.Render();

        _frameCount++;
        if (_smokeFrames > 0 && _frameCount >= _smokeFrames)
        {
            Console.WriteLine($"Smoke run complete: {_frameCount} frames, "
                + $"{_renderer.TotalVertices:N0} vertices, {_renderer.DrawnTriangles:N0} triangles drawn.");
            _window.Close();
        }
    }

    private void OnFramebufferResize(Vector2D<int> size) =>
        _gl?.Viewport(0, 0, (uint)Math.Max(size.X, 1), (uint)Math.Max(size.Y, 1));

    private void OnClosing()
    {
        _imgui?.Dispose();
        _renderer?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
    }

    public void Dispose() => _window.Dispose();
}
