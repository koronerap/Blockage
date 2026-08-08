using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
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
/// Owns the window, the GL context, the session and the frame loop. Silk.NET gives us a window, a
/// GL binding and input; everything above that is ours (EditorApp.md §2).
/// </summary>
public sealed class EditorApplication : IDisposable
{
    private static readonly Color32 GridMinor = new(60, 66, 74);
    private static readonly Color32 GridMajor = new(92, 100, 110);
    private static readonly Color32 HighlightColor = new(255, 236, 120);
    private static readonly Color32 BrushOutlineColor = new(255, 160, 60);
    private static readonly Color32 SelectionColor = new(120, 230, 140);
    private static readonly Color32 PastePreviewColor = new(120, 200, 255);

    private readonly IWindow _window;
    private readonly int _smokeFrames;
    private readonly EditorSession _session = new();
    private readonly FlyCamera _camera = new();
    private readonly PalettePanel _palettePanel = new();
    private readonly ReferencePanel _referencePanel = new();
    private readonly StatsOverlay _stats = new();

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;
    private GlRenderer? _renderer;
    private ProjectController? _project;
    private ExportController? _export;
    private string _windowTitle = string.Empty;

    private Vector2 _previousMousePosition;
    private bool _looking;
    private bool _leftButtonWasDown;
    private RaycastHit? _hover;
    private Int3? _groundHover;
    private Int3? _selectionAnchor;
    private bool _pastePending;
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
        _project = new ProjectController(_session, () => _renderer.ResetBuffers());
        _export = new ExportController(_session);

        DemoScene.Fill(_session.World);
        _session.ActiveColorIndex = 96;
        _session.HasUnsavedChanges = false;

        if (_session.World.TryGetBounds(out Int3 min, out Int3 max))
        {
            _camera.FrameBox(min.ToVector3(), max.ToVector3() + Vector3.One);
        }

        if (_input.Mice.Count > 0)
        {
            _previousMousePosition = _input.Mice[0].Position;
        }

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }
    }

    private void OnUpdate(double deltaSeconds)
    {
        _lastDelta = (float)deltaSeconds;
        UpdateCamera((float)deltaSeconds);
        UpdateHover();
        UpdateTools();
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

        bool rightDown = mouse.IsButtonPressed(MouseButton.Right);
        if (rightDown && !_looking && !io.WantCaptureMouse)
        {
            _looking = true;
            mouse.Cursor.CursorMode = CursorMode.Disabled;
        }
        else if (!rightDown && _looking)
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

        // Ctrl is a menu modifier (Ctrl+S, Ctrl+Z...), never a movement key.
        if (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight))
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

    private void UpdateHover()
    {
        _hover = null;
        _groundHover = null;

        if (_input is null || _input.Mice.Count == 0 || _looking)
        {
            return;
        }

        if (ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        var viewport = new Vector2(_window.Size.X, _window.Size.Y);
        Ray ray = _camera.ScreenPointToRay(_input.Mice[0].Position, viewport);

        if (VoxelRaycaster.TryCast(_session.World, ray, out RaycastHit hit))
        {
            _hover = hit;
        }
        else if (VoxelRaycaster.TryHitGroundPlane(ray, 0, out Int3 cell))
        {
            // Nothing hit: fall back to the ground plane so an empty level can be started.
            _groundHover = cell;
        }
    }

    private void UpdateTools()
    {
        if (_input is null || _input.Mice.Count == 0)
        {
            return;
        }

        bool leftDown = _input.Mice[0].IsButtonPressed(MouseButton.Left)
            && !ImGui.GetIO().WantCaptureMouse
            && !_looking;

        bool pressed = leftDown && !_leftButtonWasDown;
        bool released = !leftDown && _leftButtonWasDown;
        _leftButtonWasDown = leftDown;

        // Placing a pasted block takes priority: the click commits it rather than running a tool.
        if (_pastePending)
        {
            UpdatePastePlacement(pressed);
            return;
        }

        if (_session.ActiveTool == EditorTool.BoxSelect)
        {
            UpdateBoxSelection(leftDown, pressed, released);
            return;
        }

        // Extrude fires once per click, never per frame: held down it would run away from the user.
        if (_session.ActiveTool == EditorTool.Extrude)
        {
            if (pressed && _hover is { } surface)
            {
                _session.ExtrudeSurfaceAt(surface, IsAltHeld() ? -1 : 1);
            }

            return;
        }

        if (leftDown)
        {
            if (pressed)
            {
                _session.BeginStroke();
            }

            if (_hover is { } hit)
            {
                _session.ApplyTool(hit);
            }
            else if (_groundHover is { } cell && _session.ActiveTool == EditorTool.Place)
            {
                _session.PlaceAt(cell);
            }
        }
        else if (released)
        {
            _session.EndStroke();
        }
    }

    /// <summary>Drag from the first picked cell to the current one; the box follows the cursor live.</summary>
    private void UpdateBoxSelection(bool leftDown, bool pressed, bool released)
    {
        Int3? cursorCell = _hover?.Voxel ?? _groundHover;

        if (pressed && cursorCell is { } anchor)
        {
            _selectionAnchor = anchor;
            _session.Selection = VoxelBox.Single(anchor);
        }
        else if (leftDown && _selectionAnchor is { } start && cursorCell is { } current)
        {
            _session.Selection = VoxelBox.FromCorners(start, current);
        }
        else if (released)
        {
            _selectionAnchor = null;
        }
    }

    /// <summary>The clipboard follows the cursor until a click commits it (or Esc cancels).</summary>
    private void UpdatePastePlacement(bool pressed)
    {
        if (!pressed || PasteOrigin() is not { } origin)
        {
            return;
        }

        _session.PasteAt(origin);
        _pastePending = false;
    }

    private bool IsAltHeld() =>
        _input is { Keyboards.Count: > 0 }
        && (_input.Keyboards[0].IsKeyPressed(Key.AltLeft) || _input.Keyboards[0].IsKeyPressed(Key.AltRight));

    /// <summary>Where the clipboard's minimum corner would land right now, or null if not aiming anywhere.</summary>
    private Int3? PasteOrigin()
    {
        if (_hover is { } hit)
        {
            return hit.Placement;
        }

        return _groundHover;
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int _)
    {
        if (ImGui.GetIO().WantCaptureKeyboard)
        {
            return;
        }

        bool control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);

        switch (key)
        {
            case Key.N when control: _project?.NewProject(); break;
            case Key.O when control: _project?.OpenProject(); break;
            case Key.S when control && shift: _project?.SaveAs(); break;
            case Key.S when control: _project?.Save(); break;
            case Key.E when control: _export?.Show(); break;

            case Key.Number1: _session.ActiveTool = EditorTool.Place; break;
            case Key.Number2: _session.ActiveTool = EditorTool.Erase; break;
            case Key.Number3: _session.ActiveTool = EditorTool.Paint; break;
            case Key.Number4: _session.ActiveTool = EditorTool.Fill; break;
            case Key.Number5: _session.ActiveTool = EditorTool.Pick; break;
            case Key.Number6: _session.ActiveTool = EditorTool.BoxSelect; break;
            case Key.Number7: _session.ActiveTool = EditorTool.Extrude; break;

            case Key.A when control: _session.SelectAll(); break;
            case Key.C when control: _session.CopySelection(); break;
            case Key.X when control: _session.CutSelection(); break;

            case Key.V when control:
                _pastePending = _session.HasClipboard;
                break;

            case Key.Delete:
                _session.DeleteSelection();
                break;

            case Key.Escape:
                if (_pastePending)
                {
                    _pastePending = false;
                }
                else
                {
                    _session.ClearSelection();
                }

                break;

            case Key.Z when control && shift:
            case Key.Y when control:
                _session.Redo();
                break;

            case Key.Z when control:
                _session.Undo();
                break;

            case Key.F:
                if (_session.World.TryGetBounds(out Int3 min, out Int3 max))
                {
                    _camera.FrameBox(min.ToVector3(), max.ToVector3() + Vector3.One);
                }

                break;
        }
    }

    private void OnRender(double deltaSeconds)
    {
        if (_gl is null || _renderer is null || _imgui is null)
        {
            return;
        }

        _imgui.Update((float)deltaSeconds);

        _renderer.SyncDirtyChunks(_session.World);
        BuildOverlayLines();

        var viewport = new Vector2(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        _renderer.Render(_camera, viewport);

        DrawUi();
        _imgui.Render();

        _frameCount++;
        if (_smokeFrames > 0 && _frameCount >= _smokeFrames)
        {
            Console.WriteLine($"Smoke run complete: {_frameCount} frames, "
                + $"{_renderer.TotalVertices:N0} vertices, {_renderer.DrawnTriangles:N0} triangles drawn.");
            _window.Close();
        }
    }

    private void BuildOverlayLines()
    {
        LineBatch lines = _renderer!.Lines;
        lines.Clear();
        lines.AddGroundGrid(64, GridMinor, GridMajor);

        if (_session.Selection is { } selection)
        {
            (Vector3 min, Vector3 max) = selection.ToWorldBounds();
            lines.AddBox(min, max, SelectionColor);
        }

        if (_pastePending)
        {
            AddPastePreview(lines);
            return;   // the cursor is placing a block, not pointing a brush
        }

        if (_hover is { } hit)
        {
            lines.AddVoxelFace(hit.Voxel, hit.Face, HighlightColor);

            if (_session.ActiveTool is not (EditorTool.BoxSelect or EditorTool.Extrude))
            {
                Int3 center = _session.ActiveTool == EditorTool.Place ? hit.Placement : hit.Voxel;
                AddBrushOutline(lines, center);
            }
        }
        else if (_groundHover is { } cell && _session.ActiveTool != EditorTool.BoxSelect)
        {
            AddBrushOutline(lines, cell);
        }
    }

    private void AddPastePreview(LineBatch lines)
    {
        if (_session.Clipboard is not { } clip || PasteOrigin() is not { } origin)
        {
            return;
        }

        (Vector3 min, Vector3 max) = clip.BoxAt(origin).ToWorldBounds();
        lines.AddBox(min, max, PastePreviewColor);
    }

    private void AddBrushOutline(LineBatch lines, Int3 center)
    {
        int radius = _session.BrushRadius;
        Vector3 min = (center - new Int3(radius, radius, radius)).ToVector3();
        Vector3 max = (center + new Int3(radius + 1, radius + 1, radius + 1)).ToVector3();

        lines.AddBox(min - new Vector3(0.01f), max + new Vector3(0.01f), BrushOutlineColor);
    }

    private void DrawUi()
    {
        MainMenu.Draw(_session, _project!, _export!, _window.Close);
        _stats.Draw(_renderer!, _camera, _session.World.SolidCount, _session.World.Chunks.Count, _lastDelta);
        ToolPanel.Draw(_session, _hover);
        SelectionPanel.Draw(_session);
        _palettePanel.Draw(_session);
        _referencePanel.Draw(_session, _renderer!.Reference);
        _project!.DrawDialogs();
        _export!.Draw();

        // The asterisk in the title is the only always-visible unsaved-changes indicator.
        string title = _project.WindowTitle;
        if (title != _windowTitle)
        {
            _windowTitle = title;
            _window.Title = title;
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
