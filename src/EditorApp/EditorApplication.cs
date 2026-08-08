using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
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
    private static readonly Color32 SelectionAddColor = new(120, 230, 140);

    // Subtract has to look different from add: until the click lands, the two gestures are
    // otherwise indistinguishable (EditorApp.md, "Extrude").
    private static readonly Color32 SelectionSubtractColor = new(255, 110, 110);
    private static readonly Color32 ArrowColor = new(255, 210, 90);

    /// <summary>Drawing every selected face costs four lines each; past this, outline the bounds instead.</summary>
    private const int MaxOutlinedFaces = 3000;

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
    private ExtrudeInteraction? _extrude;
    private RaycastHit? _hover;
    private bool _showGrid = true;
    private bool _showMeasurements = true;
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
        _extrude = new ExtrudeInteraction(_session);

        // The editor opens on the same thing New gives you: an 8³ white cube to extrude from.
        _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        _session.ActiveColorIndex = Palette.WhiteIndex;
        _session.ActiveTool = EditorTool.Extrude;

        if (_session.World.TryGetBounds(out Int3 min, out Int3 max))
        {
            _camera.FrameBox(min.ToVector3(), max.ToVector3() + Vector3.One);
        }

        if (_input.Mice.Count > 0)
        {
            _previousMousePosition = _input.Mice[0].Position;
            _input.Mice[0].Scroll += OnScroll;
        }

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }
    }

    /// <summary>Ctrl+Scroll resizes the paint brush live while hovering (EditorApp.md, "Paint").</summary>
    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (_session.ActiveTool != EditorTool.Paint || !IsControlHeld() || ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        _session.BrushRadius = Math.Clamp(_session.BrushRadius + wheel.Y * 0.5f, 0f, 12f);
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

        // WASD/QE only fly while the look button is held. The spec gives Q W E R V to the tools, and
        // a key cannot mean both "Paint" and "go up" at the same moment.
        if (!_looking)
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

        if (_input is null || _input.Mice.Count == 0 || _looking || ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        var viewport = new Vector2(_window.Size.X, _window.Size.Y);
        Ray ray = _camera.ScreenPointToRay(_input.Mice[0].Position, viewport);

        if (!_session.Scene.TryPick(ray, out ScenePick pick))
        {
            return;
        }

        // Focus follows whatever the cursor is over, but TryFocus refuses while a gesture is
        // running — focus changing mid-drag would hand the rest of the drag to another object.
        _session.TryFocus(pick.Object.Id);

        // The hit is in the picked object's own space, which is only what the tools edit when that
        // object actually holds focus.
        if (pick.Object.Id == _session.Scene.FocusId)
        {
            _hover = pick.Hit;
        }
    }

    private void UpdateTools()
    {
        if (_input is null || _input.Mice.Count == 0)
        {
            return;
        }

        IMouse mouse = _input.Mice[0];
        bool leftDown = mouse.IsButtonPressed(MouseButton.Left)
            && !ImGui.GetIO().WantCaptureMouse
            && !_looking;

        bool pressed = leftDown && !_leftButtonWasDown;
        bool released = !leftDown && _leftButtonWasDown;
        _leftButtonWasDown = leftDown;

        var viewport = new Vector2(_window.Size.X, _window.Size.Y);

        switch (_session.ActiveTool)
        {
            case EditorTool.Extrude:
                UpdateExtrude(mouse.Position, viewport, leftDown, pressed, released);
                break;

            case EditorTool.Paint:
                UpdatePaint(leftDown, pressed, released);
                break;

            // Transform and Loop Cut need the multi-object scene first (R4-R6); View never edits.
            default:
                break;
        }
    }

    private void UpdateExtrude(Vector2 mouse, Vector2 viewport, bool leftDown, bool pressed, bool released)
    {
        if (pressed)
        {
            _extrude!.OnPress(_hover, mouse, viewport, _camera, IsShiftHeld(), IsAltHeld());
        }
        else if (leftDown && _extrude!.IsBusy)
        {
            _extrude.OnDrag(_hover, mouse, viewport, _camera);
        }
        else if (released)
        {
            _extrude!.OnRelease();
        }
    }

    private void UpdatePaint(bool leftDown, bool pressed, bool released)
    {
        if (_hover is not { } hit)
        {
            if (released)
            {
                _session.EndStroke();
            }

            return;
        }

        // Alt turns the click into a sample rather than a stroke.
        if (leftDown && IsAltHeld())
        {
            if (pressed)
            {
                _session.SampleColor(hit);
            }

            return;
        }

        if (leftDown)
        {
            // Bucket is a single click; a brush keeps painting while the button is held.
            if (pressed || _session.PaintMode == PaintMode.Brush)
            {
                _session.Paint(hit);
            }
        }
        else if (released)
        {
            _session.EndStroke();
        }
    }

    private bool IsAltHeld() =>
        _input is { Keyboards.Count: > 0 }
        && (_input.Keyboards[0].IsKeyPressed(Key.AltLeft) || _input.Keyboards[0].IsKeyPressed(Key.AltRight));

    private bool IsShiftHeld() =>
        _input is { Keyboards.Count: > 0 }
        && (_input.Keyboards[0].IsKeyPressed(Key.ShiftLeft) || _input.Keyboards[0].IsKeyPressed(Key.ShiftRight));

    private bool IsControlHeld() =>
        _input is { Keyboards.Count: > 0 }
        && (_input.Keyboards[0].IsKeyPressed(Key.ControlLeft) || _input.Keyboards[0].IsKeyPressed(Key.ControlRight));

    private void OnKeyDown(IKeyboard keyboard, Key key, int _)
    {
        if (ImGui.GetIO().WantCaptureKeyboard)
        {
            return;
        }

        bool control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);

        // While the look button is held, the letter keys are flying the camera, not picking tools.
        if (_looking && !control)
        {
            return;
        }

        switch (key)
        {
            case Key.N when control: _project?.NewProject(); break;
            case Key.O when control: _project?.OpenProject(); break;
            case Key.S when control && shift: _project?.SaveAs(); break;
            case Key.S when control: _project?.Save(); break;
            case Key.E when control: _export?.Show(); break;

            // The four tools plus View. Q W E R V, in the order the spec lists them.
            case Key.Q when !control: SwitchTool(EditorTool.Transform); break;
            case Key.W when !control: SwitchTool(EditorTool.Extrude); break;
            case Key.E when !control: SwitchTool(EditorTool.Paint); break;
            case Key.R when !control: SwitchTool(EditorTool.LoopCut); break;
            case Key.V when !control: SwitchTool(EditorTool.View); break;

            // F and X mean "the other sub-mode", and what that is depends on the active tool.
            case Key.F when !control: ToggleSubMode(); break;
            case Key.X when !control: CycleMode(); break;

            case Key.Enter or Key.KeypadEnter:
                _extrude?.Confirm();
                break;

            case Key.Escape:
                OnEscape();
                break;

            case Key.Z when control && shift:
            case Key.Y when control:
                _session.Redo();
                break;

            case Key.Z when control:
                _session.Undo();
                break;

            case Key.G:
                _showGrid = !_showGrid;
                break;

            case Key.D when !control:
                _showMeasurements = !_showMeasurements;
                break;

            case Key.Home:
                if (_session.World.TryGetBounds(out Int3 min, out Int3 max))
                {
                    _camera.FrameBox(min.ToVector3(), max.ToVector3() + Vector3.One);
                }

                break;
        }
    }

    private void SwitchTool(EditorTool tool)
    {
        if (_extrude!.IsBusy)
        {
            return;   // never swap tools out from under a running drag
        }

        _extrude.Confirm();
        _session.EndStroke();
        _session.ActiveTool = tool;
    }

    /// <summary>Esc cancels a drag if one is running, otherwise it returns to Transform.</summary>
    private void OnEscape()
    {
        if (_session.IsExtruding || _extrude!.IsBusy)
        {
            _extrude!.Cancel();
            return;
        }

        if (_session.HasSelection)
        {
            _session.ClearSelection();
            return;
        }

        _session.ActiveTool = EditorTool.Transform;
    }

    private void ToggleSubMode()
    {
        switch (_session.ActiveTool)
        {
            case EditorTool.Transform:
                _session.TransformMode = _session.TransformMode == TransformMode.Move
                    ? TransformMode.Rotate
                    : TransformMode.Move;
                break;

            case EditorTool.Extrude:
                _session.ExtrudeSelectionMode = _session.ExtrudeSelectionMode == ExtrudeSelectionMode.Box
                    ? ExtrudeSelectionMode.Face
                    : ExtrudeSelectionMode.Box;
                break;
        }
    }

    private void CycleMode()
    {
        switch (_session.ActiveTool)
        {
            case EditorTool.Transform:
                _session.TransformSpace = _session.TransformSpace == TransformSpace.Global
                    ? TransformSpace.Local
                    : TransformSpace.Global;
                break;

            case EditorTool.Extrude:
                _session.ExtrudeCreatesObject = !_session.ExtrudeCreatesObject;
                break;

            case EditorTool.Paint:
                _session.PaintMode = (PaintMode)(((int)_session.PaintMode + 1) % 3);
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

        _renderer.SyncDirtyChunks(_session.Scene);
        BuildOverlayLines();

        var viewport = new Vector2(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        _renderer.Render(_session.Scene, _camera, viewport);

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

        if (_showGrid)
        {
            lines.AddGroundGrid(64, GridMinor, GridMajor);
        }

        // Everything from here on is expressed in the focused object's own space.
        lines.Transform = _session.Scene.Focus?.Transform.ToMatrix() ?? Matrix4x4.Identity;

        AddSelectionOutline(lines, _session.Selection, SelectionColor);
        AddSelectionOutline(
            lines,
            _extrude!.PendingSelection,
            _extrude.PendingOperation == SelectionOperation.Subtract ? SelectionSubtractColor : SelectionAddColor);

        if (_hover is { } hit)
        {
            lines.AddVoxelFace(hit.Voxel, hit.Face, HighlightColor);

            if (_session.ActiveTool == EditorTool.Paint)
            {
                AddBrushOutline(lines, hit.Voxel);
            }
        }

        // The arrow already carries the object transform, so it is drawn in world space.
        lines.Transform = Matrix4x4.Identity;
        AddExtrudeArrow(lines);
    }

    private static void AddSelectionOutline(LineBatch lines, FaceSelection? selection, Color32 color)
    {
        if (selection is not { IsEmpty: false })
        {
            return;
        }

        // Outlining thousands of individual faces costs more than it communicates; past the cap the
        // bounding box says the same thing for four orders of magnitude fewer lines.
        if (selection.Count > MaxOutlinedFaces)
        {
            (Vector3 min, Vector3 max) = selection.Bounds().ToWorldBounds();
            lines.AddBox(min, max, color);
            return;
        }

        foreach (Int3 voxel in selection.Voxels)
        {
            lines.AddVoxelFace(voxel, selection.Direction, color, offset: 0.02f);
        }
    }

    private void AddExtrudeArrow(LineBatch lines)
    {
        if (_session.ActiveTool != EditorTool.Extrude || _extrude!.Arrow() is not { } arrow)
        {
            return;
        }

        lines.AddLine(arrow.Start, arrow.End, ArrowColor);

        // A simple four-barbed head, so the arrow reads as a direction from any angle.
        Vector3 direction = Vector3.Normalize(arrow.End - arrow.Start);
        Vector3 side = Vector3.Cross(direction, Vector3.UnitY);
        if (side.LengthSquared() < 1e-4f)
        {
            side = Vector3.Cross(direction, Vector3.UnitX);
        }

        side = Vector3.Normalize(side) * 0.35f;
        Vector3 other = Vector3.Normalize(Vector3.Cross(direction, side)) * 0.35f;
        Vector3 barbBase = arrow.End - direction * 0.8f;

        lines.AddLine(arrow.End, barbBase + side, ArrowColor);
        lines.AddLine(arrow.End, barbBase - side, ArrowColor);
        lines.AddLine(arrow.End, barbBase + other, ArrowColor);
        lines.AddLine(arrow.End, barbBase - other, ArrowColor);
    }

    private void AddBrushOutline(LineBatch lines, Int3 center)
    {
        float radius = _session.BrushRadius;
        Vector3 centre = center.ToVector3() + new Vector3(0.5f);
        var extent = new Vector3(radius + 0.5f);

        lines.AddBox(centre - extent, centre + extent, BrushOutlineColor);
    }

    private void DrawUi()
    {
        MainMenu.Draw(_session, _project!, _export!, _window.Close);
        _stats.Draw(_renderer!, _camera, _session.World.SolidCount, _session.World.Chunks.Count, _lastDelta);
        ToolPanel.Draw(_session, _hover);
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
