using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;
using Silk.NET.Core;
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

    private readonly IWindow _window;
    private readonly int _smokeFrames;
    private readonly EditorSession _session = new();
    private readonly FlyCamera _camera = new();
    private readonly PalettePanel _palettePanel = new();
    private readonly ReferencePanel _referencePanel = new();
    private readonly StatsOverlay _stats = new();
    private readonly LayoutSettings _layout;
    private readonly EditorShell _shell;

    /// <summary>The space the shell leaves for the 3D view, in logical window pixels.</summary>
    private ViewportRect _viewport = new(Vector2.Zero, Vector2.One);

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;
    private GlRenderer? _renderer;
    private ProjectController? _project;
    private ExportController? _export;
    private MimicraftController? _mimicraft;
    private AutosaveController? _autosave;
    private string _windowTitle = string.Empty;

    private Vector2 _previousMousePosition;
    private bool _looking;
    private MiddleDrag _middleDrag;
    private bool _middleWasDown;
    private bool _confirmedClose;

    /// <summary>
    /// Closing is deferred to the end of the frame. Calling Close from a button handler runs
    /// Closing — and therefore Dispose — in the middle of building the UI, and the rest of the
    /// frame then draws through disposed GL and ImGui objects.
    /// </summary>
    private bool _closeRequested;
    private ViewActions? _viewActions;
    private bool _leftButtonWasDown;
    private ExtrudeInteraction? _extrude;
    private TransformInteraction? _transform;

    /// <summary>What the cursor is over, whichever object owns it.</summary>
    private ScenePick? _pick;

    /// <summary>The same hit, but only when it belongs to the object the tools are editing.</summary>
    private RaycastHit? _hover;
    private Int3? _paintShapeStart;
    private Face _paintShapeFace;
    private bool _paintShapeIsBox;
    private bool _showGrid = true;
    private bool _showMeasurements = true;
    private int _frameCount;
    private float _lastDelta = 1f / 60f;

    private readonly string? _screenshotPath;
    private readonly bool _startUnlit;
    private readonly AlignedView? _startView;
    private readonly string? _startLevel;

    /// <param name="smokeFrames">When positive, the window closes after this many frames (used for automated smoke runs).</param>
    /// <param name="screenshotPath">When set, the last frame is written here as a PNG before closing.</param>
    /// <param name="startUnlit">Opens in unlit shading rather than lit.</param>
    public EditorApplication(
        int smokeFrames = 0,
        string? screenshotPath = null,
        bool startUnlit = false,
        AlignedView? startView = null,
        string? startLevel = null)
    {
        _screenshotPath = screenshotPath;
        _startUnlit = startUnlit;

        // A smoke or screenshot run draws the default layout, whatever the user left theirs as, and
        // leaves their file alone.
        _layout = smokeFrames <= 0 && screenshotPath is null ? LayoutSettings.Load(LayoutSettings.DefaultPath) : new LayoutSettings();
        _shell = new EditorShell(_layout);
        _startView = startView;
        _startLevel = startLevel;
        _smokeFrames = screenshotPath is not null && smokeFrames <= 0 ? 10 : smokeFrames;

        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1600, 900),
            Title = "Blockage - Voxel Level Editor",
            // 3.3 core covers everything this tool needs and runs on the widest range of drivers.
            //
            // Forward-compatible is not optional: macOS hands out a core profile above 3.1 only for
            // a forward-compatible context, and without the flag window creation there fails
            // outright. It costs nothing elsewhere — it drops functionality already removed from the
            // core profile, and the one call that would have minded, glLineWidth, is not made at all
            // (thick lines are camera-facing quads, see LineBatch).
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
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
        SetWindowIcon();

        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _imgui = CreateImGui(_gl, _input);
        Theme.Apply();

        _renderer = new GlRenderer(_gl)
        {
            BackgroundColor = Color32.FromVector4(Theme.Viewport),
            BackgroundTopColor = Color32.FromVector4(Theme.ViewportTop),
        };

        if (_startUnlit)
        {
            _renderer.Lighting.Mode = ShadingMode.Unlit;
        }

        _project = new ProjectController(_session, () => _renderer.ResetBuffers());
        _export = new ExportController(_session);
        _mimicraft = new MimicraftController(_session);
        _extrude = new ExtrudeInteraction(_session);
        _transform = new TransformInteraction(_session);

        // The editor opens on the same thing New gives you: an 8³ white cube to extrude from.
        _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        _session.ActiveColorIndex = Palette.WhiteIndex;

        // A smoke or screenshot run leaves the user's recent-files list as it found it.
        _project.RemembersRecent = _smokeFrames <= 0;

        if (_startLevel is not null)
        {
            _project.OpenRecent(_startLevel);
        }

        FrameLevel();

        if (_startView is { } view)
        {
            _camera.Align(view);
        }

        // Not in a smoke or screenshot run: those must neither write the user's recovery folder nor
        // stop at a question about what is already in it.
        if (_smokeFrames <= 0)
        {
            _autosave = new AutosaveController(_session);
            _project.Autosave = _autosave;
            CrashLog.Crashing += _autosave.WriteBeforeDying;
            _project.OfferRecovery();
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
    /// The taskbar and title-bar icon, rendered rather than loaded. The executable carries the same
    /// mark as a resource for Explorer; this is the one the window manager asks for at runtime, and
    /// it comes from the same code, so the two can never drift apart.
    /// </summary>
    private void SetWindowIcon()
    {
        try
        {
            RawImage[] images =
            [
                new(32, 32, AppIcon.RenderRgba(32)),
                new(48, 48, AppIcon.RenderRgba(48)),
                new(256, 256, AppIcon.RenderRgba(256)),
            ];

            _window.SetWindowIcon(images);
        }
        catch (PlatformNotSupportedException)
        {
            // macOS takes its icon from the bundle and refuses this outright. Not worth a message.
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

    /// <summary>
    /// The wheel zooms towards the pivot. Ctrl+Scroll in Paint resizes the brush instead, live while
    /// hovering (EditorApp.md, "Paint").
    /// </summary>
    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        if (_session.ActiveTool == EditorTool.Paint && IsControlHeld())
        {
            _session.BrushRadius = Math.Clamp(_session.BrushRadius + wheel.Y * 0.5f, 0f, 12f);
            return;
        }

        if (_viewport.Contains(mouse.Position))
        {
            _camera.Zoom(wheel.Y);
        }
    }

    private void OnUpdate(double deltaSeconds)
    {
        _lastDelta = (float)deltaSeconds;
        _autosave?.Tick(deltaSeconds);
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

        UpdateMiddleDrag(mouse, keyboard, mouseDelta, io);

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
    /// Middle-button drag, the way Blender has it: on its own it orbits the pivot, with Shift it
    /// slides the view, with Ctrl it zooms. Which one is decided when the button goes down, so letting
    /// go of Shift halfway through a pan does not suddenly start turning the model — and a press that
    /// began over a panel does nothing when the drag wanders into the viewport.
    ///
    /// Panning is scaled at the pivot, so the point being orbited around stays under the cursor.
    /// </summary>
    private void UpdateMiddleDrag(IMouse mouse, IKeyboard keyboard, Vector2 mouseDelta, ImGuiIOPtr io)
    {
        bool middleDown = mouse.IsButtonPressed(MouseButton.Middle);
        bool pressedNow = middleDown && !_middleWasDown;
        _middleWasDown = middleDown;

        if (!middleDown)
        {
            _middleDrag = MiddleDrag.None;
            return;
        }

        if (pressedNow && !io.WantCaptureMouse && _viewport.Contains(mouse.Position))
        {
            bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
            bool control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
            _middleDrag = shift ? MiddleDrag.Pan : control ? MiddleDrag.Zoom : MiddleDrag.Orbit;
        }

        switch (_middleDrag)
        {
            case MiddleDrag.Orbit:
                _camera.Orbit(mouseDelta);
                break;

            case MiddleDrag.Pan:
                _camera.Pan(mouseDelta, _camera.PivotDistance, _viewport.Size);
                break;

            case MiddleDrag.Zoom:
                _camera.Zoom(-mouseDelta.Y / NavigationGizmo.ZoomDragPixelsPerStep);
                break;
        }
    }

    private enum MiddleDrag
    {
        None,
        Orbit,
        Pan,
        Zoom,
    }

    private void UpdateHover()
    {
        _hover = null;
        _pick = null;

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

        _pick = pick;

        // Focus follows whatever the cursor is over, except while a gesture is running. TryFocus
        // guards strokes, extrude previews and held selections itself; the drags that have none of
        // those — a gizmo, and the box-drag that is still deciding what the selection will be — are
        // guarded here.
        if (_transform is not { IsDragging: true } && _extrude is not { IsBusy: true })
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

        if (pressed && !_transform.OnPress(mouse, viewport, _camera))
        {
            // Not a handle: a light's icon picks the light, and anywhere else lets go of one, so the
            // gizmo goes back to the focused object.
            if (LightUnder(mouse, viewport) is { } light)
            {
                _session.SelectLight(light.Id);
            }
            else
            {
                _session.ClearLightSelection();
            }
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
            _extrude!.OnPress(_pick, mouse, viewport, _camera, IsShiftHeld(), IsAltHeld());
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
        bool alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);

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

            case Key.C when control:
                if (!IsDragging())
                {
                    _session.Copy();
                }

                break;

            case Key.X when control:
                if (!IsDragging())
                {
                    _session.Cut();
                }

                break;

            case Key.V when control:
                if (!IsDragging())
                {
                    ObjectMenu.Paste(_session, _camera);
                }

                break;

            case Key.G:
                _showGrid = !_showGrid;
                break;

            // Blender's object keys: Shift+D duplicates, H hides, Alt+H shows everything again,
            // Delete deletes, F2 renames. Before plain D, which is the measurements.
            // With a light picked, they act on the light.
            case Key.D when shift && !control:
                if (IsDragging())
                {
                    break;
                }

                if (_session.SelectedLight is { } copied)
                {
                    LightMenu.Duplicate(_session, _camera, copied);
                }
                else
                {
                    ObjectMenu.Duplicate(_session, _camera);
                }

                break;

            case Key.H when alt:
                _session.ShowAllObjects();
                foreach (SceneLight light in _session.Scene.Lights)
                {
                    _session.SetLightVisible(light.Id, true);
                }

                break;

            case Key.H when !control:
                if (IsDragging())
                {
                    break;
                }

                if (_session.SelectedLight is { } dimmed)
                {
                    _session.SetLightVisible(dimmed.Id, !dimmed.Visible);
                }
                else
                {
                    _session.SetObjectVisible(_session.Scene.FocusId, false);
                }

                break;

            case Key.Delete:
                if (IsDragging())
                {
                    break;
                }

                if (_session.SelectedLight is { } removed)
                {
                    _session.DeleteLight(removed.Id);
                }
                else
                {
                    _session.DeleteObject(_session.Scene.FocusId);
                }

                break;

            case Key.F2:
                if (_session.SelectedLight is { } renamedLight)
                {
                    ObjectListPanel.StartRename(renamedLight);
                }
                else if (_session.Scene.Focus is { } renamed)
                {
                    ObjectListPanel.StartRename(renamed);
                }

                break;

            case Key.D when !control:
                _showMeasurements = !_showMeasurements;
                break;

            case Key.Home:
                FrameLevel();
                break;

            // Blender's numpad: 1 front, 3 right, 7 top, with Ctrl for the side opposite; 9 turns
            // an aligned view round; 5 swaps the projection; 2 4 6 8 step the view round the pivot
            // the way the scene would move under a drag that way; the decimal point frames the
            // focused object.
            case Key.Keypad1: _camera.Align(control ? AlignedView.Back : AlignedView.Front); break;
            case Key.Keypad3: _camera.Align(control ? AlignedView.Left : AlignedView.Right); break;
            case Key.Keypad7: _camera.Align(control ? AlignedView.Bottom : AlignedView.Top); break;

            case Key.Keypad9:
                if (_camera.CurrentAlignedView() is { } aligned)
                {
                    _camera.Align(FlyCamera.Opposite(aligned));
                }

                break;

            case Key.Keypad5: _camera.Orthographic = !_camera.Orthographic; break;
            case Key.Keypad4: _camera.OrbitBy(OrbitStep, 0f); break;
            case Key.Keypad6: _camera.OrbitBy(-OrbitStep, 0f); break;
            case Key.Keypad8: _camera.OrbitBy(0f, OrbitStep); break;
            case Key.Keypad2: _camera.OrbitBy(0f, -OrbitStep); break;
            case Key.KeypadDecimal: FrameFocused(); break;
        }
    }

    /// <summary>How far one press of a numpad arrow turns the view: Blender's fifteen degrees.</summary>
    private const float OrbitStep = 15f * (MathF.PI / 180f);

    /// <summary>
    /// Shows the cursor ImGui asked for — the resize arrows over the sidebar's handles, the text bar
    /// over a text field. The ImGui backend leaves the system cursor alone, so without this every
    /// handle would look like plain window.
    /// </summary>
    private void UpdateCursor()
    {
        if (_input is null || _input.Mice.Count == 0 || _looking)
        {
            return;
        }

        ICursor cursor = _input.Mice[0].Cursor;
        StandardCursor wanted = ImGui.GetMouseCursor() switch
        {
            ImGuiMouseCursor.ResizeEW => StandardCursor.HResize,
            ImGuiMouseCursor.ResizeNS => StandardCursor.VResize,
            ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
            ImGuiMouseCursor.Hand => StandardCursor.Hand,
            _ => StandardCursor.Default,
        };

        if (cursor.Type != CursorType.Standard || cursor.StandardCursor != wanted)
        {
            cursor.Type = CursorType.Standard;
            cursor.StandardCursor = wanted;
        }
    }

    /// <summary>A drag is running that an object-level key would pull the object out from under.</summary>
    private bool IsDragging() => _extrude!.IsBusy || _transform!.IsDragging || _session.IsStrokeActive;

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
    /// <summary>The light whose icon is under the cursor, nearest the camera first, if any.</summary>
    private SceneLight? LightUnder(Vector2 mouse, Vector2 viewport)
    {
        const float Reach = 14f;
        SceneLight? found = null;
        float nearest = float.MaxValue;

        foreach (SceneLight light in _session.Scene.Lights)
        {
            if (!_camera.TryProjectToScreen(light.Position, viewport, out Vector2 screen)
                || Vector2.Distance(screen, mouse) > Reach)
            {
                continue;
            }

            float distance = Vector3.Distance(_camera.Position, light.Position);
            if (distance < nearest)
            {
                nearest = distance;
                found = light;
            }
        }

        return found;
    }

    private void OnEscape()
    {
        if (_transform!.IsDragging)
        {
            _transform.Cancel();
            return;
        }

        if (_session.SelectedLightId != 0)
        {
            _session.ClearLightSelection();
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
        UpdateCursor();

        _renderer.SyncDirtyChunks(_session.Scene);
        _renderer.AdvanceFocusFade(_session.Scene, _lastDelta);
        BuildOverlayLines();

        var framebuffer = new Vector2(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        var logical = new Vector2(_window.Size.X, _window.Size.Y);
        Vector2 scale = new(
            framebuffer.X / MathF.Max(logical.X, 1f),
            framebuffer.Y / MathF.Max(logical.Y, 1f));

        // Off while painting. The highlight is a lie about brightness, and it is a useful one right
        // up until the colours themselves are what is being judged — at which point a face lifted
        // towards white is not the colour that was just put on it.
        _renderer.FocusHighlight = _session.ActiveTool != EditorTool.Paint;

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
            _closeRequested = true;
        }

        if (_closeRequested)
        {
            _closeRequested = false;
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
            lines.AddGroundGrid(
                GroundGrid.HalfExtentCells,
                GroundGrid.WorldUnitsPerCell(_session.Scene.Focus?.VoxelSize ?? 1f),
                EditorOverlays.GridMinor,
                EditorOverlays.GridMajor);
        }

        // The lights, over everything: an icon hidden inside a wall could not be picked.
        foreach (SceneLight light in _session.Scene.Lights)
        {
            bool marked = light.Id == _session.SelectedLightId || light.Id == ObjectListPanel.HoveredId;
            EditorOverlays.AddLight(gizmos, light, _camera, marked);
        }

        // The object whose row the mouse is over in the outliner, so a name can be matched to a shape.
        if (ObjectListPanel.HoveredId != 0
            && _session.Scene.Find(ObjectListPanel.HoveredId) is { Visible: true } listed
            && listed.TryGetLocalBounds(out Vector3 listedMin, out Vector3 listedMax))
        {
            lines.Transform = listed.Transform.ToMatrix();
            lines.AddBox(listedMin, listedMax, EditorOverlays.Highlight, EditorOverlays.SelectionWidth);
        }

        // Everything from here on is expressed in the focused object's own space.
        Matrix4x4 focusMatrix = _session.Scene.Focus?.Transform.ToMatrix() ?? Matrix4x4.Identity;
        lines.Transform = focusMatrix;
        gizmos.Transform = focusMatrix;

        // A selection belongs to Extrude, so it is drawn while Extrude is what is being used. It is
        // not thrown away on the way out — coming back to the tool finds the same surface still
        // chosen — but a highlighted patch left glowing over the model while painting reads as
        // something the paint tool is about to do.
        if (_session.ActiveTool == EditorTool.Extrude)
        {
            EditorOverlays.AddSelectionOutline(lines, _session.Selection, EditorOverlays.Selection);
            EditorOverlays.AddSelectionOutline(
                lines,
                _extrude!.PendingSelection,
                _extrude.PendingOperation == SelectionOperation.Subtract ? EditorOverlays.SelectionSubtract : EditorOverlays.SelectionAdd);
        }

        // Over the model, not into it: a plane through the middle of the model is mostly inside it.
        EditorOverlays.AddMirrorPlanes(gizmos, _session);

        if (_session.ActiveTool == EditorTool.Extrude)
        {
            EditorOverlays.AddMirroredSelection(lines, _session, _session.Selection);
        }

        if (_hover is { } hit)
        {
            lines.AddVoxelFace(hit.Voxel, hit.Face, EditorOverlays.Highlight, width: EditorOverlays.SelectionWidth);
            EditorOverlays.AddMirroredHover(lines, _session, hit.Voxel, hit.Face);

            if (_session.ActiveTool == EditorTool.Paint)
            {
                AddBrushOutline(lines, hit.Voxel);
                AddPaintShapePreview(lines, hit.Voxel);
            }
        }

        // The cut plane runs through the middle of the model, so depth testing would hide it.
        EditorOverlays.AddCutPreview(gizmos, _session);

        // Everything below is already in world space.
        lines.Transform = Matrix4x4.Identity;
        gizmos.Transform = Matrix4x4.Identity;

        EditorOverlays.AddExtrudeArrow(gizmos, _session, _extrude!);
        EditorOverlays.AddTransformGizmo(gizmos, _session, _transform!, _camera);
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
            lines.AddBox(min, max, EditorOverlays.BrushOutline, EditorOverlays.SelectionWidth);
            return;
        }

        lines.AddThickLine(
            anchor.ToVector3() + half,
            cursor.ToVector3() + half,
            EditorOverlays.BrushOutline,
            EditorOverlays.SelectionWidth);
    }

    private void AddBrushOutline(LineBatch lines, Int3 center)
    {
        float radius = _session.BrushRadius;
        Vector3 centre = center.ToVector3() + new Vector3(0.5f);
        var extent = new Vector3(radius + 0.5f);

        lines.AddBox(centre - extent, centre + extent, EditorOverlays.BrushOutline);
    }

    private void DrawUi()
    {
        _stats.Sample(_lastDelta);

        var context = new ShellContext
        {
            Session = _session,
            Project = _project!,
            Export = _export!,
            Mimicraft = _mimicraft!,
            Renderer = _renderer!,
            Camera = _camera,
            Palette = _palettePanel,
            Reference = _referencePanel,
            ReferenceRenderer = _renderer!.Reference,
            Stats = _stats,
            View = _viewActions ??= CreateViewActions(),
            OnExit = () => _closeRequested = true,
            Hover = _hover,
            Looking = _looking,
            DragReadout = CurrentDragReadout(),
            FrameSeconds = _lastDelta,
        };

        _viewport = _shell.Draw(context);

        ViewportOverlay.Draw(_session, _camera, _viewport, _showMeasurements, context.DragReadout);

        // Popups sit above the shell, not inside a panel.
        _project!.DrawDialogs();


        _export!.Draw();
        _mimicraft!.Draw();
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
            _camera.PivotDistance = 40f;
            _camera.Orthographic = false;
        },
        Camera = _camera,
        GridVisible = () => _showGrid,
        ToggleGrid = () => _showGrid = !_showGrid,
        MeasurementsVisible = () => _showMeasurements,
        ToggleMeasurements = () => _showMeasurements = !_showMeasurements,
        StatisticsVisible = () => _layout.StatisticsVisible,
        ToggleStatistics = () => _layout.StatisticsVisible = !_layout.StatisticsVisible,
        Lighting = _renderer!.Lighting,
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
                // Answered from inside a popup, so mid-frame. Closing here would dispose the
                // renderer under the frame that is still being drawn.
                _confirmedClose = true;
                _closeRequested = true;
            });

            return;
        }

        // Past the guard, so the user has answered for any unsaved work — kept or thrown away, the
        // autosave is nobody's safety net any more. A crash never reaches this line, which is what
        // leaves the copy behind to be offered next time.
        _autosave?.CloseCleanly();

        if (_smokeFrames <= 0)
        {
            _layout.Save(LayoutSettings.DefaultPath);
        }

        _imgui?.Dispose();
        _renderer?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();

        // Nulled so that any frame the loop still delivers after this bails out at the guard at the
        // top of OnRender instead of drawing through disposed objects.
        _imgui = null;
        _renderer = null;
        _input = null;
        _gl = null;
    }

    public void Dispose() => _window.Dispose();
}
