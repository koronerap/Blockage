using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
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
    private static readonly Color32 CutPlaneColor = new(255, 130, 220);
    // Straight from the theme, so an axis is the same colour in the gizmo, the corner indicator and
    // the dimension labels.
    private static readonly Color32 GizmoXColor = Color32.FromVector4(Theme.AxisX);
    private static readonly Color32 GizmoYColor = Color32.FromVector4(Theme.AxisY);
    private static readonly Color32 GizmoZColor = Color32.FromVector4(Theme.AxisZ);
    private static readonly Color32 GizmoEdgeColor = new(150, 150, 165);
    private static readonly Color32 GizmoActiveColor = new(255, 240, 140);

    /// <summary>Drawing every selected face costs four lines each; past this, outline the bounds instead.</summary>
    private const int MaxOutlinedFaces = 3000;

    // Overlay stroke widths, as multiples of the batch's screen-constant thickness. Heavy enough to
    // grab, light enough not to become the thing you look at.
    private const float SelectionWidth = 1.1f;
    private const float GizmoWidth = 1.8f;
    private const float GizmoEdgeWidth = 1.5f;
    private const float ArrowWidth = 1.8f;

    private readonly IWindow _window;
    private readonly int _smokeFrames;
    private readonly EditorSession _session = new();
    private readonly FlyCamera _camera = new();
    private readonly PalettePanel _palettePanel = new();
    private readonly ReferencePanel _referencePanel = new();
    private readonly StatsOverlay _stats = new();
    private readonly EditorShell _shell = new();

    /// <summary>The space the shell leaves for the 3D view, in logical window pixels.</summary>
    private ViewportRect _viewport = new(Vector2.Zero, Vector2.One);

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;
    private GlRenderer? _renderer;
    private ProjectController? _project;
    private ExportController? _export;
    private string _windowTitle = string.Empty;

    private Vector2 _previousMousePosition;
    private bool _looking;
    private bool _panning;
    private bool _confirmedClose;
    private ViewActions? _viewActions;
    private bool _leftButtonWasDown;
    private ExtrudeInteraction? _extrude;
    private TransformInteraction? _transform;
    private RaycastHit? _hover;
    private Int3? _paintShapeStart;
    private Face _paintShapeFace;
    private bool _paintShapeIsBox;
    private bool _showGrid = true;
    private bool _showMeasurements = true;
    private int _frameCount;
    private float _lastDelta = 1f / 60f;

    private readonly string? _screenshotPath;

    /// <param name="smokeFrames">When positive, the window closes after this many frames (used for automated smoke runs).</param>
    /// <param name="screenshotPath">When set, the last frame is written here as a PNG before closing.</param>
    public EditorApplication(int smokeFrames = 0, string? screenshotPath = null)
    {
        _screenshotPath = screenshotPath;
        _smokeFrames = screenshotPath is not null && smokeFrames <= 0 ? 10 : smokeFrames;

        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1600, 900),
            Title = "Blockage - Voxel Level Editor",
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
        _imgui = CreateImGui(_gl, _input);
        Theme.Apply();

        _renderer = new GlRenderer(_gl)
        {
            BackgroundColor = Color32.FromVector4(Theme.Viewport),
        };

        _project = new ProjectController(_session, () => _renderer.ResetBuffers());
        _export = new ExportController(_session);
        _extrude = new ExtrudeInteraction(_session);
        _transform = new TransformInteraction(_session);

        // The editor opens on the same thing New gives you: an 8³ white cube to extrude from.
        _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        _session.ActiveColorIndex = Palette.WhiteIndex;

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

    /// <summary>
    /// Builds the ImGui layer with a real UI font when one can be found. ImGui's built-in font is a
    /// 13px bitmap face, and it is the single loudest reason a tool looks like a debug overlay — but
    /// it is also the guaranteed fallback, so a missing font file must not stop the editor opening.
    /// </summary>
    private ImGuiController CreateImGui(GL gl, IInputContext input)
    {
        if (Theme.ResolveFontPath() is not { } fontPath)
        {
            Console.WriteLine("No UI font found; falling back to the built-in bitmap font.");
            return new ImGuiController(gl, _window, input);
        }

        return new ImGuiController(gl, _window, input, new ImGuiFontConfig(fontPath, Theme.FontSizePixels));
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

        UpdatePan(mouse, mouseDelta, io);

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

    /// <summary>
    /// Middle-button drag slides the view. Panning is scaled by the distance to what is being
    /// looked at, so it feels the same whether the camera is on top of a wall or across the level.
    /// </summary>
    private void UpdatePan(IMouse mouse, Vector2 mouseDelta, ImGuiIOPtr io)
    {
        bool middleDown = mouse.IsButtonPressed(MouseButton.Middle);

        if (middleDown && !_panning && !io.WantCaptureMouse && _viewport.Contains(mouse.Position))
        {
            _panning = true;
        }
        else if (!middleDown)
        {
            _panning = false;
        }

        if (_panning)
        {
            _camera.Pan(mouseDelta, DistanceToSubject(), _viewport.Size);
        }
    }

    /// <summary>How far away the thing being worked on is — the focused object, or the whole scene.</summary>
    private float DistanceToSubject()
    {
        if (_session.Scene.Focus is { } focus && !focus.IsEmpty)
        {
            return Vector3.Distance(_camera.Position, focus.WorldCentre());
        }

        return _session.Scene.TryGetWorldBounds(out Vector3 min, out Vector3 max)
            ? Vector3.Distance(_camera.Position, (min + max) * 0.5f)
            : 20f;
    }

    private void UpdateHover()
    {
        _hover = null;

        if (_input is null || _input.Mice.Count == 0 || _looking || ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        // Picking is in viewport-local pixels, because the 3D view no longer fills the window.
        Vector2 mouse = _input.Mice[0].Position;
        if (!_viewport.Contains(mouse))
        {
            return;
        }

        Ray ray = _camera.ScreenPointToRay(_viewport.ToLocal(mouse), _viewport.Size);

        if (!_session.Scene.TryPick(ray, out ScenePick pick))
        {
            return;
        }

        // Focus follows whatever the cursor is over, except while a gesture is running. TryFocus
        // guards strokes and extrudes itself; a gizmo drag has no stroke, so it is guarded here.
        if (_transform is not { IsDragging: true })
        {
            _session.TryFocus(pick.Object.Id);
        }

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
            && !_looking
            && _viewport.Contains(mouse.Position);

        bool pressed = leftDown && !_leftButtonWasDown;
        bool released = !leftDown && _leftButtonWasDown;
        _leftButtonWasDown = leftDown;

        Vector2 local = _viewport.ToLocal(mouse.Position);
        Vector2 viewport = _viewport.Size;

        switch (_session.ActiveTool)
        {
            case EditorTool.Transform:
                UpdateTransform(local, viewport, leftDown, pressed, released);
                break;

            case EditorTool.Extrude:
                UpdateExtrude(local, viewport, leftDown, pressed, released);
                break;

            case EditorTool.Paint:
                UpdatePaint(leftDown, pressed, released);
                break;

            case EditorTool.LoopCut:
                UpdateLoopCut(pressed);
                break;

            // Transform and Loop Cut need the multi-object scene first (R4-R6); View never edits.
            default:
                break;
        }
    }

    private void UpdateTransform(Vector2 mouse, Vector2 viewport, bool leftDown, bool pressed, bool released)
    {
        _transform!.UpdateHover(mouse, viewport, _camera);

        if (pressed)
        {
            _transform.OnPress(mouse, viewport, _camera);
        }
        else if (leftDown && _transform.IsDragging)
        {
            // Shift releases snap; without it movement lands on whole voxels and rotation on a
            // fixed angle step.
            _transform.OnDrag(mouse, viewport, _camera, freeform: IsShiftHeld());
        }
        else if (released)
        {
            _transform.OnRelease();
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
        if (pressed)
        {
            // The shape modifier is read once, when the drag starts.
            _paintShapeIsBox = IsControlHeld();
            _paintShapeStart = (IsShiftHeld() || _paintShapeIsBox) && _hover is { } start ? start.Voxel : null;

            // The shape paints the direction the drag started on, so it stays on one surface.
            _paintShapeFace = _hover?.Face ?? Face.PosY;
        }

        if (_hover is not { } hit)
        {
            if (released)
            {
                _session.EndStroke();
                _paintShapeStart = null;
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

        // A line or box drag draws nothing until it is let go, so the cursor's path leaves no trail.
        if (_paintShapeStart is { } anchor)
        {
            if (released)
            {
                _session.PaintShape(anchor, hit.Voxel, _paintShapeFace, _paintShapeIsBox);
                _paintShapeStart = null;
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

    /// <summary>
    /// Previews the grid boundary nearest the cursor and cuts on click. The preview point is the
    /// exact spot on the picked face, so the plane tracks the cursor rather than snapping per voxel.
    /// </summary>
    private void UpdateLoopCut(bool pressed)
    {
        _session.PreviewCutPlane = null;

        if (_hover is not { } hit || _session.Scene.Focus is not { } focus)
        {
            return;
        }

        Vector3 surfacePoint = hit.Voxel.ToVector3() + new Vector3(0.5f) + FaceInfo.Normal(hit.Face) * 0.5f;
        CutPlane? plane = LoopCut.FindNearestPlane(focus, surfacePoint);

        if (plane is null || !LoopCut.Divides(focus.Grid, plane.Value))
        {
            return;
        }

        _session.PreviewCutPlane = plane;

        if (pressed)
        {
            _session.ApplyLoopCut(plane.Value);
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
        if (_extrude!.IsBusy || _transform!.IsDragging)
        {
            return;   // never swap tools out from under a running drag
        }

        _extrude.Confirm();
        _session.EndStroke();

        // Every tool's transient preview belongs to that tool. Left behind, a cut plane from Loop
        // Cut keeps drawing over the model long after Extrude has taken over.
        _session.PreviewCutPlane = null;
        _session.ActiveTool = tool;
    }

    /// <summary>Esc cancels a drag if one is running, otherwise it returns to Transform.</summary>
    private void OnEscape()
    {
        if (_transform!.IsDragging)
        {
            _transform.Cancel();
            return;
        }

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

        // The shell runs first so the viewport rectangle it leaves is known before the scene is
        // drawn into it; ImGui's own draw data is submitted afterwards, on top.
        DrawUi();

        _renderer.SyncDirtyChunks(_session.Scene);
        _renderer.AdvanceFocusFade(_session.Scene, _lastDelta);
        BuildOverlayLines();

        var framebuffer = new Vector2(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        var logical = new Vector2(_window.Size.X, _window.Size.Y);
        Vector2 scale = new(
            framebuffer.X / MathF.Max(logical.X, 1f),
            framebuffer.Y / MathF.Max(logical.Y, 1f));

        _renderer.Render(
            _session.Scene,
            _camera,
            framebuffer,
            _viewport.Position * scale,
            _viewport.Size * scale);

        // Put the viewport back before ImGui draws. Rendering the scene leaves GL clipped to the
        // 3D view's rectangle, and the UI would otherwise be squeezed into that same rectangle —
        // the whole shell drawn, shrunk, inside itself.
        _gl.Viewport(0, 0, (uint)MathF.Max(framebuffer.X, 1f), (uint)MathF.Max(framebuffer.Y, 1f));

        _imgui.Render();

        _frameCount++;
        if (_smokeFrames > 0 && _frameCount >= _smokeFrames)
        {
            if (_screenshotPath is not null)
            {
                CaptureScreenshot(_screenshotPath, (int)framebuffer.X, (int)framebuffer.Y);
            }

            Console.WriteLine($"Smoke run complete: {_frameCount} frames, "
                + $"{_renderer.TotalVertices:N0} vertices, {_renderer.DrawnTriangles:N0} triangles drawn.");
            _window.Close();
        }
    }

    /// <summary>
    /// Reads the finished frame back and writes it as a PNG. Lets the interface be checked without
    /// a human at the screen, which is the only way to catch a layout that renders into the wrong
    /// rectangle.
    /// </summary>
    private unsafe void CaptureScreenshot(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];

        fixed (byte* destination = pixels)
        {
            _gl!.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, destination);
        }

        // GL hands back rows bottom-up; PNG wants them top-down.
        var flipped = new byte[pixels.Length];
        int stride = width * 4;
        for (int row = 0; row < height; row++)
        {
            Array.Copy(pixels, (height - 1 - row) * stride, flipped, row * stride, stride);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        PngWriter.WriteRgba(path, flipped, width, height);
        Console.WriteLine($"Wrote {path} ({width}x{height}).");
    }

    private void BuildOverlayLines()
    {
        LineBatch lines = _renderer!.Lines;
        LineBatch gizmos = _renderer.GizmoLines;

        lines.Clear();
        gizmos.Clear();
        lines.CameraPosition = _camera.Position;
        gizmos.CameraPosition = _camera.Position;

        if (_showGrid)
        {
            lines.AddGroundGrid(64, GridMinor, GridMajor);
        }

        // Everything from here on is expressed in the focused object's own space.
        Matrix4x4 focusMatrix = _session.Scene.Focus?.Transform.ToMatrix() ?? Matrix4x4.Identity;
        lines.Transform = focusMatrix;
        gizmos.Transform = focusMatrix;

        AddSelectionOutline(lines, _session.Selection, SelectionColor);
        AddSelectionOutline(
            lines,
            _extrude!.PendingSelection,
            _extrude.PendingOperation == SelectionOperation.Subtract ? SelectionSubtractColor : SelectionAddColor);

        if (_hover is { } hit)
        {
            lines.AddVoxelFace(hit.Voxel, hit.Face, HighlightColor, width: SelectionWidth);

            if (_session.ActiveTool == EditorTool.Paint)
            {
                AddBrushOutline(lines, hit.Voxel);
                AddPaintShapePreview(lines, hit.Voxel);
            }
        }

        // The cut plane runs through the middle of the model, so depth testing would hide it.
        AddCutPreview(gizmos);

        // Everything below is already in world space.
        lines.Transform = Matrix4x4.Identity;
        gizmos.Transform = Matrix4x4.Identity;

        AddExtrudeArrow(gizmos);
        AddTransformGizmo(gizmos);
    }

    /// <summary>Draws the move arrows, the box edges and the rotate rings.</summary>
    private void AddTransformGizmo(LineBatch lines)
    {
        if (_session.ActiveTool != EditorTool.Transform || _transform is null)
        {
            return;
        }

        foreach (GizmoHandle handle in _transform.Handles(_camera))
        {
            if (!_transform.ShouldDraw(handle))
            {
                continue;
            }

            Color32 color = _transform.IsHighlighted(handle) ? GizmoActiveColor : ColorFor(handle);

            if (handle.Kind == GizmoKind.RotateRing)
            {
                Vector3? previous = null;
                foreach (Vector3 point in _transform.RingPoints(handle, _camera))
                {
                    if (previous is { } from)
                    {
                        lines.AddThickLine(from, point, color, GizmoWidth);
                    }

                    previous = point;
                }

                continue;
            }

            (Vector3 start, Vector3 end) = _transform.Segment(handle, _camera);

            if (handle.Kind == GizmoKind.MoveAxis)
            {
                AddArrow(lines, start, end, color, GizmoWidth);
                continue;
            }

            lines.AddThickLine(start, end, color, GizmoEdgeWidth);
        }

        // The pivot a rotation is turning about, so a hinge is not a mystery mid-drag.
        if (_input is { Mice.Count: > 0 }
            && _transform.ActivePivot(_viewport.ToLocal(_input.Mice[0].Position), _viewport.Size, _camera)
                is { } pivot)
        {
            var size = new Vector3(0.25f);
            lines.AddBox(pivot - size, pivot + size, GizmoActiveColor, GizmoEdgeWidth);
        }
    }

    /// <summary>
    /// A shaft that stops at the base of a solid cone. Running the shaft all the way to the tip
    /// leaves a thick stub poking out of the cone's point, which is the part of an arrow that has
    /// to look sharp.
    /// </summary>
    private static void AddArrow(LineBatch lines, Vector3 start, Vector3 end, Color32 color, float width)
    {
        Vector3 along = end - start;
        float length = along.Length();
        if (length < 1e-4f)
        {
            return;
        }

        Vector3 direction = along / length;
        float coneLength = length * 0.28f;
        Vector3 coneBase = end - direction * coneLength;

        lines.AddThickLine(start, coneBase, color, width);
        lines.AddCone(end, coneBase, coneLength * 0.42f, color);
    }

    private static Color32 ColorFor(GizmoHandle handle) => handle.Kind switch
    {
        GizmoKind.EdgeHinge => GizmoEdgeColor,
        _ => handle.Axis switch
        {
            0 => GizmoXColor,
            1 => GizmoYColor,
            _ => GizmoZColor,
        },
    };

    /// <summary>Draws the loop cut plane as a rectangle spanning the object's bounds.</summary>
    private void AddCutPreview(LineBatch lines)
    {
        // Gated on the tool as well as on the plane: a preview must never outlive its own tool.
        if (_session.ActiveTool != EditorTool.LoopCut
            || _session.PreviewCutPlane is not { } plane
            || _session.Scene.Focus is not { } focus
            || !focus.Grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return;
        }

        int axis = plane.AxisIndex;
        int uAxis = axis == 0 ? 1 : 0;
        int vAxis = axis == 2 ? 1 : 2;

        Vector3 Corner(float u, float v)
        {
            Span<float> parts = stackalloc float[3];
            parts[axis] = plane.Coordinate;
            parts[uAxis] = u;
            parts[vAxis] = v;
            return new Vector3(parts[0], parts[1], parts[2]);
        }

        float uMin = VoxelBox.Component(min, uAxis);
        float uMax = VoxelBox.Component(max, uAxis) + 1f;
        float vMin = VoxelBox.Component(min, vAxis);
        float vMax = VoxelBox.Component(max, vAxis) + 1f;

        Vector3 a = Corner(uMin, vMin);
        Vector3 b = Corner(uMax, vMin);
        Vector3 c = Corner(uMax, vMax);
        Vector3 d = Corner(uMin, vMax);

        lines.AddThickLine(a, b, CutPlaneColor, SelectionWidth);
        lines.AddThickLine(b, c, CutPlaneColor, SelectionWidth);
        lines.AddThickLine(c, d, CutPlaneColor, SelectionWidth);
        lines.AddThickLine(d, a, CutPlaneColor, SelectionWidth);

        // A diagonal makes the plane read as a surface rather than an empty frame.
        lines.AddThickLine(a, c, CutPlaneColor, SelectionWidth);
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
            lines.AddBox(min, max, color, SelectionWidth);
            return;
        }

        foreach (Int3 voxel in selection.Voxels)
        {
            lines.AddVoxelFace(voxel, selection.Direction, color, offset: 0.02f, width: SelectionWidth);
        }
    }

    private void AddExtrudeArrow(LineBatch lines)
    {
        if (_session.ActiveTool != EditorTool.Extrude || _extrude!.Arrow() is not { } arrow)
        {
            return;
        }

        AddArrow(lines, arrow.Start, arrow.End, ArrowColor, ArrowWidth);
    }

    /// <summary>Shows where a Shift or Ctrl drag would land before it is committed.</summary>
    private void AddPaintShapePreview(LineBatch lines, Int3 cursor)
    {
        if (_paintShapeStart is not { } anchor)
        {
            return;
        }

        var half = new Vector3(0.5f);

        if (_paintShapeIsBox)
        {
            (Vector3 min, Vector3 max) = VoxelBox.FromCorners(anchor, cursor).ToWorldBounds();
            lines.AddBox(min, max, BrushOutlineColor, SelectionWidth);
            return;
        }

        lines.AddThickLine(
            anchor.ToVector3() + half,
            cursor.ToVector3() + half,
            BrushOutlineColor,
            SelectionWidth);
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
        _stats.Sample(_lastDelta);

        var context = new ShellContext
        {
            Session = _session,
            Project = _project!,
            Export = _export!,
            Renderer = _renderer!,
            Camera = _camera,
            Palette = _palettePanel,
            Reference = _referencePanel,
            ReferenceRenderer = _renderer!.Reference,
            Stats = _stats,
            View = _viewActions ??= CreateViewActions(),
            OnExit = _window.Close,
            Hover = _hover,
            DragReadout = CurrentDragReadout(),
            FrameSeconds = _lastDelta,
        };

        _viewport = _shell.Draw(context);

        ViewportOverlay.Draw(_session, _camera, _viewport, _showMeasurements, context.DragReadout);

        // Popups sit above the shell, not inside a panel.
        _project!.DrawDialogs();
        _export!.Draw();
        _referencePanel.DrawDialogs();
        ToolOptions.DrawDialogs();

        // The asterisk in the title is the only always-visible unsaved-changes indicator.
        string title = _project.WindowTitle;
        if (title != _windowTitle)
        {
            _windowTitle = title;
            _window.Title = title;
        }
    }

    private ViewActions CreateViewActions() => new()
    {
        FrameLevel = FrameLevel,
        FrameFocused = FrameFocused,
        LookAtCenter = () => _camera.LookAt(Vector3.Zero),
        ResetCamera = () =>
        {
            _camera.Position = new Vector3(-24f, 24f, -24f);
            _camera.Yaw = 45f * (MathF.PI / 180f);
            _camera.Pitch = -30f * (MathF.PI / 180f);
        },
        GridVisible = () => _showGrid,
        ToggleGrid = () => _showGrid = !_showGrid,
        MeasurementsVisible = () => _showMeasurements,
        ToggleMeasurements = () => _showMeasurements = !_showMeasurements,
    };

    private void FrameLevel()
    {
        if (_session.Scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            _camera.FrameBox(min, max);
        }
    }

    private void FrameFocused()
    {
        if (_session.Scene.Focus is { } focus && focus.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            _camera.FrameBox(min, max);
        }
    }

    /// <summary>Whatever number the gesture in progress is producing, or nothing.</summary>
    private string CurrentDragReadout()
    {
        if (_transform is { Readout.Length: > 0 } transform)
        {
            return transform.Readout;
        }

        return _session.IsExtruding ? $"{_session.ExtrudeSteps:+0;-0} units" : string.Empty;
    }

    private void OnFramebufferResize(Vector2D<int> size) =>
        _gl?.Viewport(0, 0, (uint)Math.Max(size.X, 1), (uint)Math.Max(size.Y, 1));

    /// <summary>
    /// Closing is vetoable: unsaved work must not disappear because a window button was clicked.
    /// The same guard covers the Exit menu item, since that closes the window too.
    /// </summary>
    private void OnClosing()
    {
        if (!_confirmedClose && _session.HasUnsavedChanges && _project is not null)
        {
            _window.IsClosing = false;
            _project.RequestExit(() =>
            {
                _confirmedClose = true;
                _window.Close();
            });

            return;
        }

        _imgui?.Dispose();
        _renderer?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
    }

    public void Dispose() => _window.Dispose();
}
