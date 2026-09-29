using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Raycast;
using EditorApp.Core.Project;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
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

    private IWindow _window;

    /// <summary>Set once the window and its context exist: a failure before it is the context refused.</summary>
    private bool _loaded;
    private readonly int _smokeFrames;
    /// <summary>The level in front: what every tool, panel and shortcut works on. Changes with the tab in front.</summary>
    private EditorSession _session = new();

    /// <summary>The levels open, a tab each (Fullreleaseplan 7.8); <see cref="_level"/> is the one in front.</summary>
    private readonly List<LevelDocument> _levels = [];
    private LevelDocument? _level;
    private int _nextSlot;

    /// <summary>What was last copied: every level open shares it, so what is copied in one pastes into another.</summary>
    private readonly SharedClipboard _clipboard = new();

    /// <summary>A change of level asked for while the frame was being drawn, made once it has been.</summary>
    private Action? _afterUi;

    private readonly FlyCamera _camera = new();
    private readonly PalettePanel _palettePanel = new();
    private readonly ReferencePanel _referencePanel = new();
    private readonly StatsOverlay _stats = new();
    private readonly LayoutSettings _layout;
    private readonly EditorShell _shell;

    /// <summary>The space the shell leaves for the 3D view, in logical window pixels.</summary>
    private ViewportRect _viewport = new(Vector2.Zero, Vector2.One);

    /// <summary>The whole of the viewport's area; <see cref="_viewport"/> is the part of it the view works in — all of it, but for the quad view.</summary>
    private ViewportRect _viewportArea = new(Vector2.Zero, Vector2.One);

    /// <summary>The quad view's top, front and right views: looking along the axes at what the view looks at.</summary>
    private readonly FlyCamera[] _quadCameras = [new(), new(), new()];

    private static readonly (AlignedView View, string Name)[] QuadViews = [(AlignedView.Top, "Top"), (AlignedView.Front, "Front"), (AlignedView.Right, "Right")];

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;
    private GlRenderer? _renderer;
    private ProjectController? _project;
    private ExportController? _export;
    private MimicraftController? _mimicraft;

    /// <summary>The level as the editor last closed on it, kept at every clean exit. Null for smoke and screenshot runs.</summary>
    private LastSession? _lastSession;

    private WelcomeActions? _welcomeActions;
    private string _windowTitle = string.Empty;

    private Vector2 _previousMousePosition;
    private bool _looking;

    /// <summary>The walker, while walking (Fullreleaseplan 7.5); null otherwise.</summary>
    private WalkBody? _walk;

    /// <summary>A click that ended walking is not a click on the level: the tools wait for the button to come up.</summary>
    private bool _walkClickHeld;

    /// <summary>Where the view was before walking, to go back to.</summary>
    private ViewPose _walkStart;

    /// <summary>Tells a right click, which opens the viewport's menu, from a right drag, which looks round.</summary>
    private readonly RightClick _rightClick = new();

    /// <summary>What was under the cursor as the right button went down, and which way the view faced then.</summary>
    private (int ObjectId, int LightId, float Yaw, float Pitch) _rightPress;

    /// <summary>Seconds since the editor started, for timing a click.</summary>
    private double _clock;
    private MiddleDrag _middleDrag;
    private bool _middleWasDown;

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
    private LightAimInteraction? _aim;
    private SelectInteraction? _select;
    private RenderWindow? _renderWindow;
    private RenderOutputsWindow? _outputsWindow;
    private PropLibraryWindow? _library;

    /// <summary>
    /// The camera the view was put at by looking through it, and the view as it stood before, to go
    /// back to; null while the view is its own. Moving the view away from the camera ends it.
    /// </summary>
    private (int CameraId, Vector3 Position, float Yaw, float Pitch, float FieldOfView, bool Orthographic, float PivotDistance)? _through;
    private ViewportRender? _viewportRender;

    /// <summary>Where the next frame's viewport is to be saved, overlays and all left out; null when none is asked for.</summary>
    private string? _viewportShotPath;

    /// <summary>The object under the pointer, outlined faintly; 0 for none. Never changes what is selected.</summary>
    private int _hoverObjectId;

    /// <summary>The light whose aim line is under the pointer, drawn lit so it is seen to be grabbable.</summary>
    private SceneLight? _aimHover;

    /// <summary>The ruler end being dragged by the measure tool: which ruler, and whether its end or its start.</summary>
    private (int Index, bool End)? _rulerDrag;

    /// <summary>The ruler under the pointer, lit — what Delete takes away; −1 for none.</summary>
    private int _rulerHover = -1;

    /// <summary>Where a ruler would start from under the pointer, shown before the press.</summary>
    private Vector3? _measureCursor;

    /// <summary>Where each note's words were drawn last frame, in the viewport's pixels, nearest last: a click there is a click on the note.</summary>
    private readonly List<(int Id, Vector2 Min, Vector2 Max)> _noteLabels = [];

    /// <summary>A press would pull the extrude surface — the pointer is on its arrow or on the selection.</summary>
    private bool _extrudeWouldPull;

    /// <summary>What the cursor is over, whichever object owns it.</summary>
    private ScenePick? _pick;

    /// <summary>The same hit, but only when it belongs to the object the tools are editing.</summary>
    private RaycastHit? _hover;
    private Int3? _paintShapeStart;
    private Face _paintShapeFace;
    private bool _paintShapeIsBox;
    private readonly Preferences _preferences;

    /// <summary>What Shift+Z goes back to from Wireframe.</summary>
    private ShadingMode _shadingBeforeWireframe = ShadingMode.Lit;

    /// <summary>The viewport header's settings — gizmos, overlays, X-Ray, shading — kept with the preferences.</summary>
    private ViewportSettings View => _preferences.Viewport;

    // What is drawn, with the header's two master switches taken into account.
    private bool ShowGrid => View.Overlays && View.Grid;
    private bool ShowMeasurements => View.Overlays && View.Measurements;
    private bool ShowLightIcons => View.Overlays && View.LightIcons;
    private bool ShowMirrorPlanes => View.Overlays && View.MirrorPlanes;
    private bool ToolGizmos => View.Gizmos && View.ToolGizmos;
    private bool LightGizmos => View.Gizmos && View.LightGizmos;

    // The designed values the preferences scale.
    private const float BaseLookSensitivity = 0.0035f;
    private const float BaseMoveSpeed = 16f;
    private const float BaseLineThickness = 0.0035f;

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

        // Smoke and screenshot runs see the defaults, as they do the layout, and write nothing.
        _preferences = smokeFrames <= 0 && screenshotPath is null ? Preferences.Load(Preferences.DefaultPath) : new Preferences();
        _shell = new EditorShell(_layout);
        _startView = startView;
        _startLevel = startLevel;
        _smokeFrames = screenshotPath is not null && smokeFrames <= 0 ? 10 : smokeFrames;

        _window = CreateWindow(new APIVersion(4, 3));
    }

    /// <summary>
    /// The editor's window. 3.3 core covers everything the editor itself draws and runs on the widest
    /// range of drivers — but some drivers hand out exactly the version asked for, and the GPU render
    /// engine's compute shaders are 4.3, so 4.3 is asked for first and 3.3 only when it is refused
    /// (macOS stops at 4.1; see <see cref="Run"/>).
    /// </summary>
    private IWindow CreateWindow(APIVersion version)
    {
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1600, 900),
            Title = "Blockage - Voxel Level Editor",
            // Forward-compatible is not optional: macOS hands out a core profile above 3.1 only for
            // a forward-compatible context, and without the flag window creation there fails
            // outright. It costs nothing elsewhere — it drops functionality already removed from the
            // core profile, and the one call that would have minded, glLineWidth, is not made at all
            // (thick lines are camera-facing quads, see LineBatch).
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                version),
            VSync = true,
            PreferredDepthBufferBits = 24,
        };

        IWindow window = Window.Create(options);
        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;
        window.FramebufferResize += OnFramebufferResize;
        window.Closing += OnClosing;
        return window;
    }

    public void Run()
    {
        try
        {
            _window.Run();
        }
        catch (Exception exception) when (!_loaded)
        {
            // No 4.3 context to be had: the editor on 3.3, and renders on the CPU.
            Console.Error.WriteLine($"OpenGL 4.3 was refused ({exception.Message}); starting on 3.3.");
            _window.Dispose();
            _window = CreateWindow(new APIVersion(3, 3));
            _window.Run();
        }
    }

    private void OnLoad()
    {
        _loaded = true;
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

        // A level from somewhere else — a template, a file, a recovery — is framed: where the camera
        // was looking in the last one says nothing about this one.
        _project = new ProjectController(_session, () =>
        {
            _renderer.ResetBuffers();
            FrameLevel();
        })
        {
            MakeRoom = MakeRoomForLevel,
            ShowOpen = ShowOpenLevel,
        };
        Front(AddLevel(_session));
        _renderWindow = new RenderWindow(_gl);
        _outputsWindow = new RenderOutputsWindow(_gl);
        _library = new PropLibraryWindow(_gl);
        AddMenu.OpenPropLibrary = _library.Open;
        CameraPropertiesPanel.LookThrough = LookThrough;
        SectionViewport.SceneOf = () => _session.Scene;
        ReferencePanel.Images = _renderer.Images;
        CameraPropertiesPanel.MoveToView = camera => _session.SetCameraToView(camera.Id, ViewAsCamera());
        _viewportRender = new ViewportRender(_gl);

        // The editor opens on the same thing New gives you: an 8³ white cube to extrude from.
        _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        _session.ActiveColorIndex = Palette.WhiteIndex;

        // Blender's first tool: choosing what to work on comes before working on it.
        _session.ActiveTool = EditorTool.Select;

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
            CrashLog.Crashing += WriteBeforeDying;
            _project.OfferRecovery();

            _lastSession = new LastSession();
            _project.LastSession = _lastSession;

            // Blender's splash: when the editor starts on nothing in particular, and not over the
            // question about a crash, which comes first.
            if (_startLevel is null && _preferences.ShowWelcome && !_project.IsOfferingRecovery)
            {
                WelcomeScreen.Open();
            }
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

        // Last, once everything they reach into exists.
        ApplyPreferences();
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

        // Every text size goes into the atlas while it is being built, so changing size later is a
        // pointer swap rather than a rebuilt texture.
        return new ImGuiController(gl, _window, input, new ImGuiFontConfig(fontPath, Theme.FontSizePixels, _ => Theme.GlyphRanges), () =>
        {
            ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
            Theme.AddFonts(atlas, fontPath, atlas.Fonts[0]);
        });
    }

    /// <summary>Puts the preferences into effect — at start, and after every change in their window.</summary>
    private void ApplyPreferences()
    {
        Preferences p = _preferences;

        Keymap.Active = p.BuildKeymap();
        Theme.Apply(p.Theme, p.Accent);
        Theme.UseTextSize(p.TextSize);

        if (_renderer is not null)
        {
            _renderer.BackgroundColor = Color32.FromVector4(Theme.Viewport);
            _renderer.BackgroundTopColor = Color32.FromVector4(Theme.ViewportTop);
            _renderer.Lines.ThicknessScale = BaseLineThickness * p.LineWidth;
            _renderer.GizmoLines.ThicknessScale = BaseLineThickness * p.LineWidth;
        }

        _camera.FieldOfView = p.FieldOfView * (MathF.PI / 180f);
        _camera.LookSensitivity = BaseLookSensitivity * p.OrbitSpeed;
        _camera.MoveSpeed = BaseMoveSpeed * p.FlySpeed;

        _window.VSync = p.VSync;
        foreach (LevelDocument level in _levels)
        {
            ApplyPreferencesTo(level);
        }

        if (_project is not null)
        {
            _project.Recent.Capacity = p.RecentFilesKept;
        }
    }

    /// <summary>The preferences kept in a level's session and tools, put into one.</summary>
    private void ApplyPreferencesTo(LevelDocument level)
    {
        Preferences p = _preferences;
        level.Transform.SizeScale = p.GizmoSize;
        level.Session.Snap.CopyFrom(p.Snap);
        level.Session.History.CellBudget = p.UndoMemory * 1_000_000;

        if (level.Autosave is not null)
        {
            level.Autosave.Every = TimeSpan.FromMinutes(p.AutosaveMinutes);
        }
    }

    /// <summary>The header's shading and overlays, handed to the renderer for this frame.</summary>
    private void ApplyViewStyle()
    {
        _renderer!.Lighting.Mode = View.Shading;
        _renderer.SolidLighting = View.SolidLighting;
        _renderer.Colour = View.Colour;
        _renderer.SingleColour = View.SingleColour;
        _renderer.WireOverlay = View.Overlays && View.Wireframe ? View.WireframeOpacity : 0f;
        _renderer.XRay = View.XRay ? View.XRayAlpha : 0f;
        _renderer.AmbientOcclusion = View.AmbientOcclusion ? 1f : 0f;
        _renderer.Shadows = View.Shadows;
        _renderer.Clip = View.Clip;

        (Vector4 bottom, Vector4 top) = View.Background == BackgroundMode.Custom
            ? (new Vector4(View.BackgroundColour, 1f), new Vector4(View.BackgroundColour, 1f))
            : (Theme.Viewport, Theme.ViewportTop);
        _renderer.BackgroundColor = Color32.FromVector4(bottom);
        _renderer.BackgroundTopColor = Color32.FromVector4(top);
    }

    /// <summary>Takes in what was changed outside the window — the overlay keys, the keymap — and writes it out.</summary>
    private void SavePreferences()
    {
        if (_smokeFrames > 0 || _screenshotPath is not null)
        {
            return;
        }

        RememberViewState();
        _preferences.Remember(Keymap.Active);
        _preferences.Save(Preferences.DefaultPath);
    }

    /// <summary>The overlay switches as they are now, which the View menu and its keys change too.</summary>
    private void RememberViewState()
    {
        _preferences.Snap.CopyFrom(_session.Snap);
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

        if (_session.ActiveTool == EditorTool.Sculpt && IsControlHeld())
        {
            _session.SculptRadius += wheel.Y * 0.5f;
            return;
        }

        if (_viewport.Contains(mouse.Position))
        {
            _camera.Zoom(_preferences.InvertZoom ? -wheel.Y : wheel.Y);
        }
    }

    private void OnUpdate(double deltaSeconds)
    {
        _lastDelta = (float)deltaSeconds;
        _clock += deltaSeconds;
        foreach (LevelDocument level in _levels)
        {
            level.Autosave?.Tick(deltaSeconds);
        }

        UpdateCamera((float)deltaSeconds);

        // While walking or a pie is open, and until the click that ended one is let go, the tools stand aside.
        if (_walk is not null || WalkClickStillHeld() || PieMenu.IsOpen)
        {
            return;
        }

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

        if (_walk is not null)
        {
            UpdateWalk(mouse, keyboard, mouseDelta, deltaSeconds);
            return;
        }

        bool rightDown = mouse.IsButtonPressed(MouseButton.Right);
        bool pressedNow = false;
        if (rightDown && !_looking && !io.WantCaptureMouse)
        {
            _looking = true;
            pressedNow = true;
            PressRight(mousePosition);
            mouse.Cursor.CursorMode = CursorMode.Disabled;
        }
        else if (!rightDown && _looking)
        {
            _looking = false;
            mouse.Cursor.CursorMode = CursorMode.Normal;
            ReleaseRight();
        }

        if (_looking)
        {
            _camera.Look(mouseDelta);

            // The press's own frame carries the movement from before it.
            if (!pressedNow)
            {
                _rightClick.Moved(mouseDelta);
            }
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

        if (movement != Vector3.Zero)
        {
            _rightClick.Flew();
        }
    }

    /// <summary>
    /// The right button going down: it starts a look, and might yet be a click. What is under the
    /// cursor now is what a click's menu will be about — a light's icon first, then an object.
    /// </summary>
    private void PressRight(Vector2 at)
    {
        bool counts = _viewport.Contains(at) && !IsDragging() && !_leftButtonWasDown;
        SceneLight? light = counts && ShowLightIcons ? LightUnder(_viewport.ToLocal(at), _viewport.Size) : null;
        int objectId = counts && light is null && PickAt(_viewport.ToLocal(at), selectedOnly: false) is { } pick ? pick.Object.Id : 0;

        _rightPress = (objectId, light?.Id ?? 0, _camera.Yaw, _camera.Pitch);
        _rightClick.Press(at, _clock, counts);
    }

    /// <summary>The right button let go: after a click, the view as it was and the menu for what was clicked.</summary>
    private void ReleaseRight()
    {
        if (_rightClick.Release(_clock) is not { } at)
        {
            return;
        }

        _camera.Yaw = _rightPress.Yaw;
        _camera.Pitch = _rightPress.Pitch;

        // What the menu is for is selected, if it was not already — then the menu's commands, which
        // act on the selection, act on it. Something already selected keeps the rest selected with it.
        int clicked = _rightPress.LightId != 0 ? _rightPress.LightId : _rightPress.ObjectId;
        if (clicked != 0 && !_session.IsSelected(clicked))
        {
            _session.ClickSelect(clicked);
        }

        ViewportMenu.Open(at, _rightPress.ObjectId, _rightPress.LightId);
    }

    /// <summary>
    /// Where a spot on screen falls on the level, and which way the surface there faces: an object's
    /// face, locked ones included — a floor is often locked — else the ground, else straight ahead
    /// at the pivot's distance. The middle of the view when there is no spot, or it is off the view.
    /// </summary>
    private (Vector3 Point, Vector3 Normal) SurfaceUnder(Vector2? at)
    {
        Vector2 screen = at is { } spot && _viewport.Contains(spot) ? spot : _viewport.Position + (_viewport.Size * 0.5f);
        Ray ray = _camera.ScreenPointToRay(_viewport.ToLocal(screen), _viewport.Size);

        if (Snapping.TrySurface(_session.Scene, ray, new SnapSettings { ExcludeLocked = false }, _ => false, out Vector3 point, out Vector3 normal, out _))
        {
            return (point, normal);
        }

        if (ray.Direction.Y < -1e-4f && -ray.Origin.Y / ray.Direction.Y is > 0f and var distance)
        {
            return (ray.PointAt(distance), Vector3.UnitY);
        }

        return (ray.PointAt(_camera.PivotDistance), Vector3.UnitY);
    }

    /// <summary>
    /// Makes what the Add menu picked, set down on the level under <paramref name="at"/> — or where
    /// the view looks — in voxels the size of the object being worked on, and hands it to the
    /// Transform tool to be moved where it goes, as a duplicate is.
    /// </summary>
    private void AddChosen(AddChoice choice, Vector2? at)
    {
        if (IsDragging())
        {
            return;
        }

        if (choice.Marker is { } markerKind)
        {
            _session.ExitEditMode();
            (Vector3 markerPoint, _) = SurfaceUnder(at);
            _session.AddMarker(markerKind, markerPoint);
            return;
        }

        if (choice.Camera)
        {
            _session.ExitEditMode();
            _session.AddCamera(ViewAsCamera());
            return;
        }

        (Vector3 point, Vector3 normal) = SurfaceUnder(at);
        float voxelSize = _session.Scene.Focus?.VoxelSize ?? 1f;
        _session.ExitEditMode();

        if (choice.Light is { } kind)
        {
            LightMenu.AddAt(_session, kind, point, normal);
            return;
        }

        if (choice.Shape is { } shape)
        {
            _session.AddShape(Shapes.Defaults(shape), point, normal, voxelSize);
        }
        else if (choice.Prop is { } prop)
        {
            _session.AddObject(PropPresets.Build(prop, _session.Scene.Palette), PropPresets.NameOf(prop), point, normal, voxelSize);
        }

        if (SwitchTool(EditorTool.Transform))
        {
            _session.TransformMode = TransformMode.Move;
        }
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

    /// <summary>Whether the tool in hand writes voxels, and so reaches only what is selected.</summary>
    private bool VoxelToolInHand => _session.ActiveTool is EditorTool.Extrude or EditorTool.Paint or EditorTool.LoopCut or EditorTool.Sculpt;

    /// <summary>
    /// The nearest object under a point of the viewport. With <paramref name="selectedOnly"/>, what is
    /// not selected is looked straight through, as if it were not there.
    /// </summary>
    private ScenePick? PickAt(Vector2 local, bool selectedOnly, bool shown = false)
    {
        Ray ray = _camera.ScreenPointToRay(local, _viewport.Size);

        // Inside an object, only it is there to reach.
        Func<VoxelObject, bool>? skip = _session.EditObject is { } edited
            ? o => o.Id != edited.Id
            : selectedOnly ? o => !_session.IsSelected(o.Id) : null;

        return _session.Scene.TryPick(ray, out ScenePick pick, skip: skip, shown: shown, clip: View.Clip) ? pick : null;
    }

    /// <summary>The voxel of the object in Edit Mode under a point of the viewport, or null.</summary>
    private Int3? VoxelUnder(Vector2 local) => PickAt(local, selectedOnly: false) is { } pick ? pick.Hit.Voxel : null;

    private IEnumerable<Int3> VoxelsInBox(Vector2 min, Vector2 max) =>
        _session.EditObject is { } edited
            ? SelectInteraction.VoxelsInBox(edited, _camera, _viewport.Size, min, max, throughWalls: View.XRay)
            : [];

    /// <summary>What a selecting click at a point lands on: a light's icon first, then an object; 0 for nothing.</summary>
    private int ThingUnder(Vector2 local)
    {
        if (ShowLightIcons && LightUnder(local, _viewport.Size) is { } light)
        {
            return light.Id;
        }

        if (MarkerUnder(local) is { } marker)
        {
            return marker.Id;
        }

        // What an object shows is what is clicked on, a modifier's copies too.
        return PickAt(local, selectedOnly: false, shown: true) is { } pick ? pick.Object.Id : 0;
    }

    private IEnumerable<int> ThingsInBox(Vector2 min, Vector2 max) =>
        SelectInteraction.InBox(_session.Scene, _camera, _viewport.Size, min, max, IsLightShown);

    private void UpdateHover()
    {
        _hover = null;
        _pick = null;
        _hoverObjectId = 0;

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

        // The tools that write voxels reach only what is selected, and see through the rest.
        bool voxelTool = VoxelToolInHand;
        if (PickAt(_viewport.ToLocal(mouse), selectedOnly: voxelTool) is not { } pick)
        {
            return;
        }

        _pick = pick;
        _hoverObjectId = pick.Object.Id;

        // Among the selected, the one under the pointer is the one a voxel tool writes: focus moves
        // there, except while a gesture is running. TryFocus guards strokes, extrude previews and held
        // selections itself; the extrude box-drag that is still deciding its surface is guarded here.
        // No other tool moves focus by hovering — the gizmo stays with the selection.
        if (voxelTool && _extrude is not { IsBusy: true })
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

        // Hover marks are for a pointer over the model, not over a panel in front of it.
        bool pointing = !ImGui.GetIO().WantCaptureMouse && !_looking && _viewport.Contains(mouse.Position);
        _aimHover = null;
        _extrudeWouldPull = false;

        switch (_session.ActiveTool)
        {
            case EditorTool.Select:
                UpdateSelect(local, leftDown, pressed, released);
                break;

            case EditorTool.Transform:
                UpdateTransform(local, viewport, leftDown, pressed, released, pointing);
                break;

            case EditorTool.Extrude:
                UpdateExtrude(local, viewport, leftDown, pressed, released, pointing);
                break;

            case EditorTool.Paint when _session.PaintMode == PaintMode.Stencil:
                UpdateStencil(local, leftDown, pressed, released);
                break;

            case EditorTool.Paint:
                UpdatePaint(leftDown, pressed, released);
                break;

            case EditorTool.LoopCut:
                UpdateLoopCut(pressed);
                break;

            case EditorTool.Sculpt:
                UpdateSculpt(leftDown, pressed, released);
                break;

            case EditorTool.Measure:
                UpdateMeasure(local, leftDown, pressed, pointing);
                break;

            // Transform and Loop Cut need the multi-object scene first (R4-R6); View never edits.
            default:
                break;
        }
    }

    private void UpdateTransform(Vector2 mouse, Vector2 viewport, bool leftDown, bool pressed, bool released, bool pointing)
    {
        // With the tools' gizmos switched off there is no gizmo to hover or take hold of; the
        // lights' icons can still be picked.
        if (ToolGizmos)
        {
            _transform!.UpdateHover(mouse, viewport, _camera);
        }

        if (pointing && LightGizmos && !_aim!.IsAiming && !_transform!.IsDragging)
        {
            _aimHover = _aim.LineUnder(mouse, viewport, _camera, IsLightShown);
        }

        // A sun's or a spot's aim line first: it reaches out past the gizmo, and a press on it means
        // "point the light there".
        if (pressed && LightGizmos && _aim!.OnPress(mouse, viewport, _camera, IsLightShown))
        {
            return;
        }

        if (leftDown && _aim!.IsAiming)
        {
            _aim.OnDrag(mouse, viewport, _camera);
            return;
        }

        if (released && _aim!.IsAiming)
        {
            _aim.OnRelease();
            return;
        }

        if (pressed && !(ToolGizmos && _transform!.OnPress(mouse, viewport, _camera)))
        {
            // Not a handle: a click selects and a drag draws a box, as with the Select tool.
            _select!.OnPress(mouse, IsShiftHeld(), IsControlHeld());
        }
        else if (_select!.IsPressed)
        {
            UpdateSelect(mouse, leftDown, pressed: false, released);
        }
        else if (leftDown && _transform!.IsDragging)
        {
            // Shift turns the magnet the other way for as long as it is held: snapping on when the
            // magnet is off, as it is by default, and off when it is lit.
            _transform.OnDrag(mouse, viewport, _camera, snap: _session.Snap.Enabled != IsShiftHeld());
        }
        else if (released)
        {
            _transform!.OnRelease();
        }
    }

    /// <summary>The Select tool: a click picks, a drag draws a box.</summary>
    private void UpdateSelect(Vector2 mouse, bool leftDown, bool pressed, bool released)
    {
        if (pressed)
        {
            _select!.OnPress(mouse, IsShiftHeld(), IsControlHeld());
        }
        else if (leftDown)
        {
            _select!.OnDrag(mouse);
        }
        else if (released)
        {
            if (_session.InEditMode)
            {
                _select!.OnReleaseVoxels(VoxelUnder, VoxelsInBox);
            }
            else
            {
                _select!.OnRelease(ThingUnder, ThingsInBox);
            }
        }
    }

    private void UpdateExtrude(Vector2 mouse, Vector2 viewport, bool leftDown, bool pressed, bool released, bool pointing)
    {
        _extrude!.ArrowEnabled = ToolGizmos;
        _extrudeWouldPull = pointing
            && !_extrude.IsBusy
            && _extrude.WouldPull(_pick, mouse, viewport, _camera, IsShiftHeld(), IsAltHeld());

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

    /// <summary>Where a gradient drag began, until it is let go.</summary>
    private RaycastHit? _gradientStart;

    /// <summary>The stencil's box being dragged, in viewport pixels: where it began, and where the pointer is.</summary>
    private (Vector2 From, Vector2 To)? _stencil;

    /// <summary>
    /// The stencil: the loaded image stretched over a box dragged on the view, and painted onto the
    /// focused object's faces seen through it — each the pixel it is seen through.
    /// </summary>
    private void UpdateStencil(Vector2 mouse, bool leftDown, bool pressed, bool released)
    {
        if (pressed)
        {
            _stencil = (mouse, mouse);
        }
        else if (leftDown && _stencil is { } dragging)
        {
            _stencil = (dragging.From, mouse);
        }
        else if (released && _stencil is { } box)
        {
            _stencil = null;
            Vector2 min = Vector2.Min(box.From, box.To);
            Vector2 max = Vector2.Max(box.From, box.To);
            if (max.X - min.X < 4f || max.Y - min.Y < 4f || _session.Scene.Focus is not { } target || _session.Pattern is null)
            {
                return;
            }

            _session.ProjectPattern(StencilProjection.FacesSeenIn(target, _camera, _viewport.Size, min, max));
        }
    }

    private void UpdatePaint(bool leftDown, bool pressed, bool released)
    {
        // A gradient is a drag: from the face pressed on to the cell let go over. Drawn as a line
        // meanwhile, as a painted line is.
        if (_session.PaintMode == PaintMode.Gradient)
        {
            if (pressed && _hover is { } from)
            {
                _gradientStart = from;
                _paintShapeStart = from.Voxel;
                _paintShapeIsBox = false;
            }
            else if (released)
            {
                if (_gradientStart is { } start)
                {
                    _session.PaintGradient(start, _hover?.Voxel ?? start.Voxel);
                }

                _gradientStart = null;
                _paintShapeStart = null;
            }

            return;
        }

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

    /// <summary>Dabs at most this often while the button is held: fast enough to feel continuous, slow enough to aim.</summary>
    private const double SculptInterval = 0.07;

    private double _lastDab;

    /// <summary>What a Sculpt dab does with the keys held: Shift smooths, Ctrl turns the brush round.</summary>
    private SculptMode HeldSculptMode() =>
        IsShiftHeld() ? SculptMode.Smooth
        : IsControlHeld() ? SculptOperations.Inverse(_session.SculptMode)
        : _session.SculptMode;

    /// <summary>The Sculpt tool: a stroke of dabs while the button is held, one undo step.</summary>
    private void UpdateSculpt(bool leftDown, bool pressed, bool released)
    {
        if (released)
        {
            _session.EndStroke();
            return;
        }

        if (!leftDown || _hover is not { } hit || (!pressed && _clock - _lastDab < SculptInterval))
        {
            return;
        }

        _lastDab = _clock;
        _session.Sculpt(hit, HeldSculptMode());
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

    /// <summary>Whether a light's icon is drawn, and so whether it can be taken hold of.</summary>
    private bool IsLightShown(SceneLight light) => ShowLightIcons || light.Id == _session.SelectedLightId;

    /// <summary>What a click in Extrude would do to the selection, by the modifier held right now.</summary>
    private SelectionOperation HeldSelectionOperation() =>
        IsShiftHeld() ? SelectionOperation.Add
        : IsAltHeld() ? SelectionOperation.Subtract
        : SelectionOperation.Replace;

    private bool IsShiftHeld() =>
        _input is { Keyboards.Count: > 0 }
        && (_input.Keyboards[0].IsKeyPressed(Key.ShiftLeft) || _input.Keyboards[0].IsKeyPressed(Key.ShiftRight));

    private bool IsControlHeld() =>
        _input is { Keyboards.Count: > 0 }
        && (_input.Keyboards[0].IsKeyPressed(Key.ControlLeft) || _input.Keyboards[0].IsKeyPressed(Key.ControlRight));

    /// <summary>The key the last shortcut was pressed with: a pie watches it to know when it is let go.</summary>
    private Key _lastKey;

    private void OnKeyDown(IKeyboard keyboard, Key key, int _)
    {
        _lastKey = key;

        // Walking has the keyboard: Esc goes back, Enter stays, Tab flies; the rest are its movement.
        if (_walk is not null)
        {
            switch (key)
            {
                case Key.Escape:
                    EndWalk(keep: false);
                    break;
                case Key.Enter or Key.KeypadEnter:
                    EndWalk(keep: true);
                    break;
                case Key.Tab:
                    _walk.Flying = !_walk.Flying;
                    break;
            }

            return;
        }

        bool control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
        bool alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
        var chord = new KeyChord(key, control, shift, alt);

        // A shortcut being rebound in Preferences takes the next press whole, whatever it is.
        if (KeyCapture.Offer(chord))
        {
            return;
        }

        // Typing into the search, or about to: the keys are its.
        if (ImGui.GetIO().WantCaptureKeyboard || CommandSearch.IsOpen)
        {
            return;
        }

        // A key pressed over the welcome screen puts it away and then does what it does, as in
        // Blender. Not a modifier on its own: that is the start of a chord, or of a click.
        if (WelcomeScreen.IsOpen && key is not (Key.ControlLeft or Key.ControlRight or Key.ShiftLeft or Key.ShiftRight or Key.AltLeft or Key.AltRight or Key.SuperLeft or Key.SuperRight))
        {
            WelcomeScreen.Dismiss();
        }
        else if (ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
        {
            // Any other menu or list open over the editor has the keys: Esc closes it, and nothing
            // behind it fires meanwhile.
            return;
        }

        // While the look button is held, the letter keys are flying the camera, not picking tools.
        if (_looking && !control)
        {
            return;
        }

        if (Keymap.Active.ActionFor(chord) is not { } action)
        {
            return;
        }

        // With Preferences in front, only the keys that close it get through.
        if (PreferencesWindow.IsFocused && action is not (EditorAction.Cancel or EditorAction.Preferences))
        {
            return;
        }

        Run(action);
    }

    /// <summary>Does what a shortcut stands for. The keys themselves are the keymap's business.</summary>
    private void Run(EditorAction action)
    {
        switch (action)
        {
            case EditorAction.NewLevel: _project?.NewProject(); break;
            case EditorAction.Open: _project?.OpenProject(); break;
            case EditorAction.Save: _project?.Save(); break;
            case EditorAction.SaveAs: _project?.SaveAs(); break;
            case EditorAction.NextLevel: CycleLevel(1); break;
            case EditorAction.PreviousLevel: CycleLevel(-1); break;
            case EditorAction.CloseLevel when _level is { } closing: CloseLevel(closing); break;
            case EditorAction.Export: _export?.Show(); break;

            case EditorAction.ToolSelect: SwitchTool(EditorTool.Select); break;
            case EditorAction.ToolTransform: SwitchTool(EditorTool.Transform); break;

            // Inside an object, the selection keys choose its voxels.
            case EditorAction.SelectAll when !IsDragging() && _session.InEditMode: _session.SelectAllVoxels(); break;
            case EditorAction.DeselectAll when !IsDragging() && _session.InEditMode: _session.DeselectAllVoxels(); break;
            case EditorAction.InvertSelection when !IsDragging() && _session.InEditMode: _session.InvertVoxelSelection(); break;
            case EditorAction.SelectAll when !IsDragging(): _session.SelectAll(); break;
            case EditorAction.DeselectAll when !IsDragging(): _session.DeselectAll(); break;
            case EditorAction.InvertSelection when !IsDragging(): _session.InvertSelection(); break;
            case EditorAction.Join when !IsDragging() && !_session.InEditMode: ObjectMenu.JoinSelected(_session, ReportLog.Shared); break;

            case EditorAction.ToggleEditMode when !IsDragging(): ObjectMenu.ToggleEditMode(_session, ReportLog.Shared); break;
            case EditorAction.GrowSelection when !IsDragging(): _session.GrowVoxelSelection(); break;
            case EditorAction.ShrinkSelection when !IsDragging(): _session.ShrinkVoxelSelection(); break;
            case EditorAction.Separate when !IsDragging(): ObjectMenu.Separate(_session, ReportLog.Shared); break;
            case EditorAction.FillSelection when !IsDragging(): ObjectMenu.Fill(_session, ReportLog.Shared); break;
            case EditorAction.ToolExtrude: SwitchTool(EditorTool.Extrude); break;
            case EditorAction.ToolPaint: SwitchTool(EditorTool.Paint); break;
            case EditorAction.ToolLoopCut: SwitchTool(EditorTool.LoopCut); break;
            case EditorAction.ToolSculpt: SwitchTool(EditorTool.Sculpt); break;
            case EditorAction.ToolMeasure: SwitchTool(EditorTool.Measure); break;
            case EditorAction.ToolView: SwitchTool(EditorTool.View); break;

            // Straight to a Transform mode, where the keymap has a key for each.
            case EditorAction.ToolMove:
            case EditorAction.ToolRotate:
                if (SwitchTool(EditorTool.Transform))
                {
                    _session.TransformMode = action == EditorAction.ToolMove ? TransformMode.Move : TransformMode.Rotate;
                }

                break;

            case EditorAction.ToggleSnap:
                _session.Snap.Enabled = !_session.Snap.Enabled;
                ReportLog.Shared.Post(_session.Snap.Enabled ? "Snapping on - Shift moves freely." : "Snapping off - Shift snaps.");
                break;

            // "The other sub-mode" and "the next mode": what those are depends on the active tool.
            case EditorAction.ToolOtherMode: ToggleSubMode(); break;
            case EditorAction.ToolCycleMode: CycleMode(); break;

            case EditorAction.KeepExtrude: _extrude?.Confirm(); break;
            case EditorAction.Cancel: OnEscape(); break;

            case EditorAction.Undo: _session.Undo(); break;
            case EditorAction.Redo: _session.Redo(); break;

            case EditorAction.Copy when !IsDragging(): ClipboardActions.Copy(_session, ReportLog.Shared); break;
            case EditorAction.Cut when !IsDragging(): ClipboardActions.Cut(_session, ReportLog.Shared); break;
            case EditorAction.Paste when !IsDragging(): ClipboardActions.Paste(_session, _camera, ReportLog.Shared); break;

            // The object keys act on everything selected, lights included — or, inside an object, on
            // its chosen voxels.
            case EditorAction.Duplicate when !IsDragging(): ObjectMenu.Duplicate(_session, _camera); break;
            case EditorAction.DuplicateLinked when !IsDragging(): ObjectMenu.DuplicateLinked(_session, _camera); break;

            case EditorAction.ShowAll:
                _session.ShowAllObjects();
                foreach (SceneLight light in _session.Scene.Lights)
                {
                    _session.SetLightVisible(light.Id, true);
                }

                break;

            case EditorAction.Hide when !IsDragging() && !_session.InEditMode: _session.HideSelected(); break;
            case EditorAction.Lock when !IsDragging() && !_session.InEditMode: ObjectMenu.LockSelected(_session, ReportLog.Shared); break;

            case EditorAction.UnlockAll:
                if (_session.UnlockAll() is > 0 and var unlocked)
                {
                    ReportLog.Shared.Post($"Unlocked {unlocked} {(unlocked == 1 ? "thing" : "things")}.");
                }

                break;

            case EditorAction.Delete when !IsDragging() && _session.ActiveTool == EditorTool.Measure: DeleteRuler(); break;
            case EditorAction.Delete when !IsDragging() && _session.InEditMode: _session.DeleteSelectedVoxels(); break;
            case EditorAction.Delete when !IsDragging(): _session.DeleteSelected(); break;

            case EditorAction.Rename:
                if (_session.SelectedLight is { } renamedLight)
                {
                    ObjectListPanel.StartRename(renamedLight);
                }
                else if (_session.Scene.Focus is { } renamed)
                {
                    ObjectListPanel.StartRename(renamed);
                }

                break;

            case EditorAction.Subdivide when !IsDragging() && !_session.InEditMode: ObjectMenu.SubdivideSelected(_session, ReportLog.Shared); break;

            // Blender's Ctrl+P: with several selected, the rest go under the active one; with one, a
            // list of what it could go under.
            case EditorAction.SetParent when !IsDragging() && !_session.InEditMode:
                if (_session.SelectedCount > 1)
                {
                    ObjectMenu.ParentSelected(_session, ReportLog.Shared);
                }
                else if (_session.SelectedObjects.Cast<IPlaceable>().Concat(_session.SelectedLights).FirstOrDefault() is { } child)
                {
                    ParentMenu.Open(child.Id);
                }

                break;

            case EditorAction.Search when !IsDragging(): CommandSearch.Open(); break;

            // At the mouse, where what is picked will be set down.
            case EditorAction.MoveToCollection when !IsDragging() && _input is { Mice.Count: > 0 } mice:
                CollectionMenu.Open(mice.Mice[0].Position);
                break;

            case EditorAction.AddMenu when !IsDragging() && _input is { Mice.Count: > 0 } input:
                AddMenu.Open(input.Mice[0].Position);
                break;

            case EditorAction.ClearParent when !IsDragging() && !_session.InEditMode:
                if (_session.ClearParentOfSelected() is > 0 and var freed)
                {
                    ReportLog.Shared.Post(freed == 1 ? "Cleared the parent." : $"Cleared the parents of {freed}.");
                }

                break;

            case EditorAction.RenderImage: _renderWindow?.Start(_session, RenderImageCamera()); break;

            case EditorAction.ToggleGrid: View.Grid = !View.Grid; break;
            case EditorAction.ToggleMeasurements: View.Measurements = !View.Measurements; break;
            case EditorAction.ToggleXRay: View.XRay = !View.XRay; break;
            case EditorAction.ToggleSection: ToggleSection(); break;
            case EditorAction.ToggleQuadView: View.Quad = !View.Quad; break;
            case EditorAction.WalkMode: BeginWalk(); break;
            case EditorAction.UndoHistory: HistoryWindow.Toggle(); break;
            case EditorAction.ShadingPie when _input is { Mice.Count: > 0 } pieMouse:
                PieMenu.Open("Shading", ShadingSlices(), pieMouse.Mice[0].Position, _lastKey, _clock);
                break;
            case EditorAction.ViewPie when _input is { Mice.Count: > 0 } viewMouse:
                PieMenu.Open("View", ViewSlices(), viewMouse.Mice[0].Position, _lastKey, _clock);
                break;
            case EditorAction.QuickFavorites when _input is { Mice.Count: > 0 } favouritesMouse:
                QuickFavorites.Open(favouritesMouse.Mice[0].Position);
                break;
            case EditorAction.ToggleOverlays: View.Overlays = !View.Overlays; break;
            case EditorAction.ToggleGizmos: View.Gizmos = !View.Gizmos; break;

            // Blender's Shift+Z: into Wireframe, and back out to whatever it was before.
            case EditorAction.ToggleWireframe:
                if (View.Shading == ShadingMode.Wireframe)
                {
                    View.Shading = _shadingBeforeWireframe;
                }
                else
                {
                    _shadingBeforeWireframe = View.Shading;
                    View.Shading = ShadingMode.Wireframe;
                }

                break;
            case EditorAction.ToggleSidebar: _layout.SidebarVisible = !_layout.SidebarVisible; break;
            case EditorAction.ShortcutSheet: ShortcutSheet.Toggle(); break;
            case EditorAction.Preferences when PreferencesWindow.IsOpen:
                PreferencesWindow.Close();
                SavePreferences();
                break;

            case EditorAction.Preferences: PreferencesWindow.Open(); break;

            case EditorAction.FrameLevel: FrameLevel(); break;
            case EditorAction.FrameFocused: FrameFocused(); break;

            // Blender's numpad views, and the steps round the pivot the way the scene would move under
            // a drag that way.
            case EditorAction.ViewFront: _camera.Align(AlignedView.Front); break;
            case EditorAction.ViewBack: _camera.Align(AlignedView.Back); break;
            case EditorAction.ViewRight: _camera.Align(AlignedView.Right); break;
            case EditorAction.ViewLeft: _camera.Align(AlignedView.Left); break;
            case EditorAction.ViewTop: _camera.Align(AlignedView.Top); break;
            case EditorAction.ViewBottom: _camera.Align(AlignedView.Bottom); break;

            case EditorAction.ViewTurnRound:
                if (_camera.CurrentAlignedView() is { } aligned)
                {
                    _camera.Align(FlyCamera.Opposite(aligned));
                }

                break;

            case EditorAction.ToggleOrthographic: _camera.Orthographic = !_camera.Orthographic; break;
            case EditorAction.ViewCamera: ToggleCameraView(); break;
            case EditorAction.CameraToView: CameraToView(); break;
            case EditorAction.OrbitLeft: _camera.OrbitBy(OrbitStep, 0f); break;
            case EditorAction.OrbitRight: _camera.OrbitBy(-OrbitStep, 0f); break;
            case EditorAction.OrbitUp: _camera.OrbitBy(0f, OrbitStep); break;
            case EditorAction.OrbitDown: _camera.OrbitBy(0f, -OrbitStep); break;
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
    private bool IsDragging() =>
        _extrude!.IsBusy || _transform!.IsDragging || _aim!.IsAiming || _session.IsStrokeActive || _select!.IsPressed || _rulerDrag is not null;

    /// <summary>Changes tool, unless a drag is running. Returns whether it did.</summary>
    private bool SwitchTool(EditorTool tool)
    {
        if (_extrude!.IsBusy || _transform!.IsDragging || _aim!.IsAiming)
        {
            return false;   // never swap tools out from under a running drag
        }

        _extrude.Confirm();
        _session.EndStroke();
        _select!.Cancel();

        // Every tool's transient preview belongs to that tool. Left behind, a cut plane from Loop
        // Cut keeps drawing over the model long after Extrude has taken over.
        _session.PreviewCutPlane = null;
        _session.ActiveTool = tool;
        return true;
    }

    /// <summary>The marker whose middle — or, for a note, whose words — are under the mouse, nearest the camera; null for none.</summary>
    private VoxelObject? MarkerUnder(Vector2 mouse)
    {
        // A note's words are drawn over everything, the nearest over the rest.
        for (int i = _noteLabels.Count - 1; i >= 0; i--)
        {
            (int id, Vector2 min, Vector2 max) = _noteLabels[i];
            if (mouse.X >= min.X && mouse.Y >= min.Y && mouse.X <= max.X && mouse.Y <= max.Y
                && _session.Scene.Find(id) is { Visible: true, Locked: false } note)
            {
                return note;
            }
        }

        const float Reach = 14f;
        VoxelObject? found = null;
        float nearest = float.MaxValue;

        foreach (VoxelObject o in _session.Scene.Objects)
        {
            if (!o.IsMarker || !o.Visible || o.Locked
                || !_camera.TryProjectToScreen(o.Transform.Position, _viewport.Size, out Vector2 screen)
                || Vector2.Distance(screen, mouse) > Reach)
            {
                continue;
            }

            float distance = Vector3.Distance(_camera.Position, o.Transform.Position);
            if (distance < nearest)
            {
                nearest = distance;
                found = o;
            }
        }

        return found;
    }

    /// <summary>The light whose icon is under the cursor, nearest the camera first, if any.</summary>
    private SceneLight? LightUnder(Vector2 mouse, Vector2 viewport)
    {
        const float Reach = 14f;
        SceneLight? found = null;
        float nearest = float.MaxValue;

        foreach (SceneLight light in _session.Scene.Lights)
        {
            if (light.Locked
                || !_camera.TryProjectToScreen(light.Position, viewport, out Vector2 screen)
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

    /// <summary>Esc cancels a drag if one is running, otherwise it returns to Transform.</summary>
    private void OnEscape()
    {
        if (ShortcutSheet.IsOpen)
        {
            ShortcutSheet.Close();
            return;
        }

        if (PreferencesWindow.IsOpen)
        {
            PreferencesWindow.Close();
            SavePreferences();
            return;
        }

        if (_aim!.IsAiming)
        {
            _aim.Cancel();
            return;
        }

        if (_transform!.IsDragging)
        {
            _transform.Cancel();
            return;
        }

        if (_select!.IsPressed)
        {
            _select.Cancel();
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

        _session.ActiveTool = EditorTool.Select;
    }

    private void ToggleSubMode()
    {
        switch (_session.ActiveTool)
        {
            // Inside an object: box, wand, colour, and round again.
            case EditorTool.Select when _session.InEditMode:
                _session.VoxelSelectMode = (VoxelSelectMode)(((int)_session.VoxelSelectMode + 1) % 3);
                break;

            case EditorTool.Transform:
                _session.TransformMode = _session.TransformMode == TransformMode.Move
                    ? TransformMode.Rotate
                    : TransformMode.Move;
                break;

            case EditorTool.Extrude:
                // Box, Ellipse, Line, whole Face, and round again.
                _session.ExtrudeSelectionMode = _session.ExtrudeSelectionMode switch
                {
                    ExtrudeSelectionMode.Box => ExtrudeSelectionMode.Ellipse,
                    ExtrudeSelectionMode.Ellipse => ExtrudeSelectionMode.Line,
                    ExtrudeSelectionMode.Line => ExtrudeSelectionMode.Face,
                    _ => ExtrudeSelectionMode.Box,
                };
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
                _session.PaintMode = (PaintMode)(((int)_session.PaintMode + 1) % Enum.GetValues<PaintMode>().Length);
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

        // A viewport image is the scene alone: no grid, no overlays, no gizmos, for this one frame.
        string? shot = _viewportShotPath;
        (bool Overlays, bool Gizmos) viewBefore = (View.Overlays, View.Gizmos);
        if (shot is not null)
        {
            View.Overlays = false;
            View.Gizmos = false;
        }

        BuildOverlayLines();

        var framebuffer = new Vector2(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        var logical = new Vector2(_window.Size.X, _window.Size.Y);
        Vector2 scale = new(
            framebuffer.X / MathF.Max(logical.X, 1f),
            framebuffer.Y / MathF.Max(logical.Y, 1f));

        // Off while painting. The highlight is a lie about brightness, and it is a useful one right
        // up until the colours themselves are what is being judged — at which point a face lifted
        // towards white is not the colour that was just put on it.
        _renderer.FocusHighlight = View.Overlays && View.FocusHighlight && _session.ActiveTool != EditorTool.Paint && _session.SelectedCount > 0;
        ApplyViewStyle();

        KeepCameraViewHonest();

        // Rendered shading: the path tracer runs under the viewport while it is the shading.
        if (View.Shading == ShadingMode.Rendered)
        {
            _viewportRender!.Update(_session, RenderCameraNow(), _viewport.Size * scale, _clock);
            _renderer.RenderedImage = _viewportRender.Texture;
        }
        else
        {
            _viewportRender!.Stop();
            _renderer.RenderedImage = 0;
        }

        _renderer.Render(
            _session.Scene,
            _camera,
            framebuffer,
            _viewport.Position * scale,
            _viewport.Size * scale);

        if (View.Quad)
        {
            RenderQuadPanes(framebuffer, scale);
        }

        if (shot is not null)
        {
            CaptureViewport(shot, _viewport.Position * scale, _viewport.Size * scale, framebuffer);
            (View.Overlays, View.Gizmos) = viewBefore;
            _viewportShotPath = null;
        }

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

        float spacing = GroundGrid.WorldUnitsPerCell(_session.Scene.Focus?.VoxelSize ?? 1f);
        bool axisX = View.Overlays && View.AxisX;
        bool axisZ = View.Overlays && View.AxisZ;

        if (ShowGrid)
        {
            // Leaving out the lines the axes are drawn on, which would otherwise fight them for the pixels.
            lines.AddGroundGrid(
                GroundGrid.HalfExtentCells,
                spacing,
                EditorOverlays.GridMinor,
                EditorOverlays.GridMajor,
                skipXAxis: axisX,
                skipZAxis: axisZ);
        }

        if (View.Overlays)
        {
            EditorOverlays.AddAxes(lines, axisX, View.AxisY, axisZ, GroundGrid.HalfExtentCells * spacing);
        }

        // The lights, over everything: an icon hidden inside a wall could not be picked. A picked light
        // is drawn even with the icons switched off, so what the gizmo is on can still be seen.
        foreach (SceneLight light in _session.Scene.Lights)
        {
            if (!ShowLightIcons && light.Id != _session.SelectedLightId)
            {
                continue;
            }

            bool marked = light.Id == _session.SelectedLightId || light.Id == ObjectListPanel.HoveredId;
            Vector3? aimedAt = _aim!.Light?.Id == light.Id ? _aim.Target : null;
            EditorOverlays.AddLight(gizmos, light, _camera, marked, aimedAt, aimLit: _aimHover?.Id == light.Id, aimLine: LightGizmos);
        }

        // The section box's edges, faintly, so it is plain that something is cut away.
        if (View.Clip is { } clip && View.Overlays)
        {
            lines.Transform = Matrix4x4.Identity;
            lines.AddBox(clip.Min, clip.Max, EditorOverlays.CutPlane with { A = 150 }, EditorOverlays.SelectionWidth * 0.6f);
        }

        // Markers, in their object's colour when selected — they have no box to show it by.
        if (View.Overlays)
        {
            foreach (VoxelObject marker in _session.Scene.Objects)
            {
                if (marker.IsMarker && marker.Visible)
                {
                    Color32 colour = marker.Id == _session.ActiveId && _session.IsSelected(marker.Id) ? EditorOverlays.ObjectActive
                        : _session.IsSelected(marker.Id) ? EditorOverlays.ObjectSelected
                        : marker.Id == ObjectListPanel.HoveredId ? EditorOverlays.Highlight
                        : EditorOverlays.MarkerColour;
                    EditorOverlays.AddMarker(gizmos, marker, colour);
                }
            }
        }

        // The measure tool's rulers, over everything, while it is in hand — as Blender shows its own.
        if (_session.ActiveTool == EditorTool.Measure)
        {
            gizmos.Transform = Matrix4x4.Identity;
            for (int i = 0; i < _session.Rulers.Count; i++)
            {
                Ruler ruler = _session.Rulers[i];
                gizmos.AddThickLine(ruler.Start, ruler.End, i == _rulerHover ? EditorOverlays.RulerHover : EditorOverlays.RulerColour, EditorOverlays.RulerWidth);
            }
        }

        // The cameras, but not the one the view is looking through.
        if (View.Overlays)
        {
            SceneCamera? through = LookingThrough();
            RenderSettings frame = _session.Scene.RenderSettings.Clamped();
            foreach (SceneCamera sceneCamera in _session.Scene.Cameras)
            {
                if (sceneCamera.Id != through?.Id)
                {
                    bool picked = sceneCamera.Id == _session.PickedCameraId || sceneCamera.Id == ObjectListPanel.HoveredId;
                    EditorOverlays.AddCamera(gizmos, sceneCamera, _camera, frame.Width / (float)frame.Height, picked, sceneCamera.Id == _session.Scene.ActiveCameraId);
                }
            }
        }

        // Outlined round what shows of them, as Blender outlines — the selection in its orange, the
        // active one lighter; the object whose outliner row the mouse is over, so a name can be
        // matched to a shape; and, faintly, what the pointer is over, which a click would select.
        var outlines = new List<(int, OutlineKind)>();
        if (ObjectListPanel.HoveredId != 0 && _session.Scene.Find(ObjectListPanel.HoveredId) is { Visible: true } listed)
        {
            outlines.Add((listed.Id, OutlineKind.Listed));
        }

        if (View.Overlays && !_session.InEditMode)
        {
            foreach (VoxelObject chosen in _session.SelectedObjects)
            {
                outlines.Add((chosen.Id, chosen.Id == _session.ActiveId ? OutlineKind.Active : OutlineKind.Selected));
            }

            if (_hoverObjectId != 0
                && !VoxelToolInHand
                && _session.ActiveTool != EditorTool.View
                && !_session.IsSelected(_hoverObjectId)
                && _session.Scene.Find(_hoverObjectId) is { } pointed)
            {
                outlines.Add((pointed.Id, OutlineKind.Hovered));
            }
        }

        _renderer!.Outlines = outlines;

        // Everything from here on is expressed in the focused object's own space.
        Matrix4x4 focusMatrix = _session.Scene.Focus?.Transform.ToMatrix() ?? Matrix4x4.Identity;
        lines.Transform = focusMatrix;
        gizmos.Transform = focusMatrix;

        // A selection belongs to Extrude, so it is drawn while Extrude is what is being used. It is
        // not thrown away on the way out — coming back to the tool finds the same surface still
        // chosen — but a highlighted patch left glowing over the model while painting reads as
        // something the paint tool is about to do.
        EditorOverlays.AddExtrudeSelection(lines, _session, _extrude!);

        // Inside an object: the voxels chosen, over every tool, since they are what most of them act on.
        if (_session.EditObject is { } edited)
        {
            EditorOverlays.AddVoxelSelection(lines, edited.Grid, _session.VoxelSelection);
        }

        // Over the model, not into it: a plane through the middle of the model is mostly inside it.
        if (ShowMirrorPlanes)
        {
            EditorOverlays.AddMirrorPlanes(gizmos, _session);
        }

        if (_hover is { } hit)
        {
            switch (_session.ActiveTool)
            {
                // Choosing objects is shown by their outlines, not by the voxel under the pointer.
                case EditorTool.Select or EditorTool.Transform when !_session.InEditMode:
                    break;

                case EditorTool.Sculpt:
                    EditorOverlays.AddSculptBrush(lines, hit, _session.SculptRadius, _session.SculptShape, HeldSculptMode());
                    break;

                // A ruler lands on a corner, not a face: the point is shown, not the voxel.
                case EditorTool.Measure:
                    break;

                case EditorTool.Extrude:
                    EditorOverlays.AddExtrudeHover(lines, _session, _extrude!, hit, HeldSelectionOperation());
                    break;

                // The brush shows the faces it would paint, all of them; the fills, the one they start
                // from — and the eyedropper the one face it would read.
                case EditorTool.Paint when _session.PaintMode == PaintMode.Brush && !IsSamplingColour():
                    EditorOverlays.AddBrushPreview(lines, _session, hit);
                    break;

                default:
                    lines.AddVoxelFace(hit.Voxel, hit.Face, EditorOverlays.Highlight, width: EditorOverlays.SelectionWidth);
                    EditorOverlays.AddMirroredHover(lines, _session, hit.Voxel, hit.Face);
                    break;
            }

            if (_session.ActiveTool == EditorTool.Paint)
            {
                AddPaintShapePreview(lines, hit.Voxel);
            }
        }

        // The cut plane runs through the middle of the model, so depth testing would hide it.
        EditorOverlays.AddCutPreview(gizmos, _session);

        // Everything below is already in world space.
        lines.Transform = Matrix4x4.Identity;
        gizmos.Transform = Matrix4x4.Identity;

        if (ToolGizmos)
        {
            EditorOverlays.AddExtrudeArrow(gizmos, _session, _extrude!, _extrudeWouldPull);
            EditorOverlays.AddTransformGizmo(gizmos, _session, _transform!, _camera);
        }

        if (View.Overlays && View.Origins)
        {
            EditorOverlays.AddOrigins(gizmos, _session.Scene, _camera);
        }

        if (View.Overlays && View.RelationshipLines)
        {
            EditorOverlays.AddRelationshipLines(gizmos, _session.Scene, _camera, ShowLightIcons);
        }

        EditorOverlays.AddSnapTarget(gizmos, _transform!, _camera);
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

    private void DrawUi()
    {
        _stats.Sample(_lastDelta);

        var context = new ShellContext
        {
            Session = _session,
            Levels = _levels.Count > 1 && _level is { } front
                ? new LevelTabsContext
                {
                    Levels = _levels,
                    Active = front,
                    Show = level => _afterUi = () =>
                    {
                        if (!IsDragging())
                        {
                            ShowLevel(level);
                        }
                    },
                    Close = level => _afterUi = () => CloseLevel(level),
                    New = () => _afterUi = () => _project?.NewProject(),
                }
                : null,
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
            Preferences = _preferences,
        };

        _viewportArea = _shell.Draw(context);

        // The quad view: the view itself works in the top right quarter; the other three are drawn round it.
        _viewport = View.Quad ? QuadPane(_viewportArea, 1) : _viewportArea;
        ShortcutSheet.Draw(ImGui.GetIO().DisplaySize);


        // A key rebound between frames is put into effect here; the window's own changes as it draws.
        if (PreferencesWindow.PendingApply)
        {
            PreferencesWindow.PendingApply = false;
            ApplyPreferences();
        }

        if (PreferencesWindow.IsOpen)
        {
            RememberViewState();
        }

        if (PreferencesWindow.Draw(_preferences, ApplyPreferences, () => _project!.Recent.Clear()))
        {
            SavePreferences();
        }

        ViewportOverlay.Draw(_session, _camera, _viewport, ShowMeasurements, context.DragReadout, CursorMark(), CursorSample(), View.Overlays && View.TextInfo);
        DrawSelectBox();
        DrawEmptyHint();
        DrawCameraFrame();
        DrawNotes();
        DrawRulerLabels();
        DrawQuadLabels();
        DrawWalkHint();

        // Popups sit above the shell, not inside a panel.
        _project!.DrawDialogs();


        _export!.Draw();
        _mimicraft!.Draw();
        _referencePanel.DrawDialogs();
        PalettePanel.DrawDialogs();
        ToolOptions.DrawDialogs();

        WelcomeScreen.Draw(_welcomeActions ??= CreateWelcomeActions());

        // Before the Parent list: a search that picks "Parent to..." opens it in the same frame.
        CommandSearch.Draw(
            () => SearchCommands.Build(new SearchSources
            {
                Session = _session,
                Project = _project,
                Mimicraft = _mimicraft,
                View = context.View,
                Preferences = _preferences,
                Run = Run,
                ApplyPreferences = ApplyPreferences,
                Exit = () => _closeRequested = true,
            }),
            _preferences.RecentCommands);
        ParentMenu.DrawPopup(_session);
        CollectionMenu.DrawPopup(_session);
        ColourAdjustWindow.Draw(_session);
        _renderWindow?.Draw(_session, RenderImageCamera);
        RenderWindow.DrawDialogs();
        _outputsWindow?.Draw(_session, RenderImageCamera);
        RenderOutputsWindow.DrawDialogs();
        _library?.Draw(_session);
        ScatterWindow.Draw(_session);
        HistoryWindow.Draw(_session);
        if (_afterUi is { } afterUi)
        {
            _afterUi = null;
            afterUi();
        }
        QuickFavorites.Draw(_preferences, Run, SavePreferences);
        if (_input is { Mice.Count: > 0, Keyboards.Count: > 0 } pieInput)
        {
            bool wasOpen = PieMenu.IsOpen;
            PieMenu.Draw(pieInput.Keyboards[0], pieInput.Mice[0], _clock);

            // The click that took a choice is not a click on the level too.
            if (wasOpen && !PieMenu.IsOpen)
            {
                _walkClickHeld = true;
            }
        }
        AppendDialog.Draw(_session);
        PlaceLibraryProps();
        ViewportShotBrowser.Draw();
        AddMenu.DrawPopup();
        ViewportMenu.Draw(new ViewportMenuActions { Session = _session, Run = Run, Viewport = View });

        // What the Add menu picked, wherever it was open — Shift+A, the menu bar, the Outliner, a
        // right click — made once every menu has been drawn.
        if (AddMenu.TakePending() is { } added)
        {
            AddChosen(added.Choice, added.At);
        }

        // The asterisk in the title is the only always-visible unsaved-changes indicator.
        string title = _project.WindowTitle;
        if (title != _windowTitle)
        {
            _windowTitle = title;
            _window.Title = title;
        }
    }

    private static readonly FileBrowserDialog ViewportShotBrowser = new();

    /// <summary>
    /// A prop clicked in the library goes where the view looks, into a move as anything added does;
    /// one dragged onto the viewport stands on the surface it was let go over.
    /// </summary>
    private void PlaceLibraryProps()
    {
        if (_library is null || IsDragging())
        {
            return;
        }

        if (_library.TakeClicked() is { } clicked && LoadProp(clicked) is { } prop)
        {
            (Vector3 point, _) = SurfaceUnder(null);
            if (_session.PlaceProp(prop, clicked.Name, point).Count > 0 && SwitchTool(EditorTool.Transform))
            {
                _session.TransformMode = TransformMode.Move;
            }
        }

        if (_library.TakeDropped() is { } dropped && _viewport.Contains(dropped.At) && LoadProp(dropped.Entry) is { } droppedProp)
        {
            (Vector3 point, _) = SurfaceUnder(dropped.At);
            _session.PlaceProp(droppedProp, dropped.Entry.Name, point);
        }
    }

    private static VoxelScene? LoadProp(PropEntry entry)
    {
        try
        {
            return VxLevelFile.LoadScene(entry.LevelPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            ReportLog.Shared.Post($"Could not read the prop {entry.Name}: {exception.Message}", ReportKind.Error);
            return null;
        }
    }

    /// <summary>
    /// Alt+B: the section box on, round the level with its top two fifths taken away — the ceiling
    /// off a room, to work inside it — or off again.
    /// </summary>
    private void ToggleSection()
    {
        if (View.Clip is not null)
        {
            View.Clip = null;
            return;
        }

        View.Clip = SectionViewport.Default(_session.Scene);
    }

    /// <summary>
    /// One quarter of the viewport's area: 0 top left, 1 top right, 2 bottom left, 3 bottom right,
    /// a pixel short of the middle so a line shows between them.
    /// </summary>
    private static ViewportRect QuadPane(ViewportRect area, int pane)
    {
        Vector2 half = new(MathF.Floor(area.Size.X * 0.5f), MathF.Floor(area.Size.Y * 0.5f));
        Vector2 at = area.Position + new Vector2(pane % 2 == 1 ? half.X + 1f : 0f, pane / 2 == 1 ? half.Y + 1f : 0f);
        Vector2 size = new(pane % 2 == 1 ? area.Size.X - half.X - 1f : half.X, pane / 2 == 1 ? area.Size.Y - half.Y - 1f : half.Y);
        return new ViewportRect(at, Vector2.Max(size, Vector2.One));
    }

    /// <summary>
    /// The quad view's top, front and right views, each looking straight along its axis at the point
    /// the view looks at, near enough to see the whole level: the view itself is drawn already.
    /// </summary>
    private void RenderQuadPanes(Vector2 framebuffer, Vector2 scale)
    {
        (Vector3 min, Vector3 max) = SectionViewport.Bounds(_session.Scene);
        float span = MathF.Max((max - min).Length(), 8f);
        uint rendered = _renderer!.RenderedImage;
        _renderer.RenderedImage = 0;

        int[] panes = [0, 2, 3];
        for (int i = 0; i < QuadViews.Length; i++)
        {
            FlyCamera pane = _quadCameras[i];
            pane.FieldOfView = _camera.FieldOfView;
            pane.PivotDistance = span * 0.6f / MathF.Tan(pane.FieldOfView * 0.5f);
            pane.Position = _camera.Pivot;
            pane.Align(QuadViews[i].View);
            pane.Position = _camera.Pivot - (pane.Forward * pane.PivotDistance);
            pane.Orthographic = true;

            ViewportRect rect = QuadPane(_viewportArea, panes[i]);
            _renderer.Render(_session.Scene, pane, framebuffer, rect.Position * scale, rect.Size * scale, clearAll: false);
        }

        _renderer.RenderedImage = rendered;
    }

    /// <summary>The quad view's names in the corners of its views, and the lines between them.</summary>
    private void DrawQuadLabels()
    {
        if (!View.Quad || WelcomeScreen.IsOpen)
        {
            return;
        }

        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
        uint text = ImGui.GetColorU32(Theme.Text with { W = 0.8f });
        int[] panes = [0, 2, 3];
        for (int i = 0; i < QuadViews.Length; i++)
        {
            // Clear of the tool column, which stands over the left of the viewport.
            ViewportRect rect = QuadPane(_viewportArea, panes[i]);
            float left = panes[i] % 2 == 0 ? ImGui.GetFontSize() * 3.6f : 10f;
            draw.AddText(rect.Position + new Vector2(left, 8f), text, $"{QuadViews[i].Name} Orthographic");
        }

        uint line = ImGui.GetColorU32(Theme.Border);
        Vector2 middle = _viewportArea.Position + new Vector2(MathF.Floor(_viewportArea.Size.X * 0.5f), MathF.Floor(_viewportArea.Size.Y * 0.5f));
        draw.AddLine(new Vector2(middle.X + 0.5f, _viewportArea.Position.Y), new Vector2(middle.X + 0.5f, _viewportArea.Position.Y + _viewportArea.Size.Y), line, 1f);
        draw.AddLine(new Vector2(_viewportArea.Position.X, middle.Y + 0.5f), new Vector2(_viewportArea.Position.X + _viewportArea.Size.X, middle.Y + 0.5f), line, 1f);
    }

    /// <summary>
    /// Shift+`: walking, from where the view stands, set down on whatever is under it — at the
    /// player's size the preferences give, in the game's own metres.
    /// </summary>
    private void BeginWalk()
    {
        if (_walk is not null || _input is not { Mice.Count: > 0 } input)
        {
            return;
        }

        if (IsDragging())
        {
            return;
        }

        _walkStart = ViewPose.Of(_camera);
        _walk = new WalkBody(_session.Scene, _preferences.Walk);
        _walk.PlaceBelow(_camera.Position);
        _camera.Orthographic = false;
        _camera.Pitch = Math.Clamp(_camera.Pitch, -0.5f, 0.5f);
        _camera.Position = _walk.Eye;
        input.Mice[0].Cursor.CursorMode = CursorMode.Disabled;
    }

    /// <summary>Stops walking: the view kept where the walker stands, or put back where it was.</summary>
    private void EndWalk(bool keep)
    {
        if (_walk is null)
        {
            return;
        }

        _walk = null;
        if (!keep)
        {
            _walkStart.ApplyTo(_camera);
        }
        else
        {
            // Orbiting from here turns about a point a few steps ahead.
            _camera.PivotDistance = _preferences.Walk.Unit * 3f;
        }

        if (_input is { Mice.Count: > 0 } input)
        {
            input.Mice[0].Cursor.CursorMode = CursorMode.Normal;
        }
    }

    private void UpdateWalk(IMouse mouse, IKeyboard keyboard, Vector2 mouseDelta, float deltaSeconds)
    {
        WalkBody walk = _walk!;
        _camera.Look(mouseDelta);

        if (mouse.IsButtonPressed(MouseButton.Left))
        {
            _walkClickHeld = true;
            EndWalk(keep: true);
            return;
        }

        if (mouse.IsButtonPressed(MouseButton.Right))
        {
            _walkClickHeld = true;
            EndWalk(keep: false);
            return;
        }

        // Where the view faces, flat on the ground, and to its right.
        Vector3 ahead = _camera.Forward with { Y = 0f };
        ahead = ahead.LengthSquared() > 1e-6f ? Vector3.Normalize(ahead) : Vector3.UnitZ;
        Vector3 right = Vector3.Normalize(Vector3.Cross(ahead, Vector3.UnitY));

        Vector3 wish = Vector3.Zero;
        if (keyboard.IsKeyPressed(Key.W)) wish += ahead;
        if (keyboard.IsKeyPressed(Key.S)) wish -= ahead;
        if (keyboard.IsKeyPressed(Key.D)) wish += right;
        if (keyboard.IsKeyPressed(Key.A)) wish -= right;
        if (wish.LengthSquared() > 1e-6f)
        {
            wish = Vector3.Normalize(wish);
        }

        bool running = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
        float pace = running ? walk.Settings.Run : 1f;
        float rise = (keyboard.IsKeyPressed(Key.Space) ? 1f : 0f) - (keyboard.IsKeyPressed(Key.C) ? 1f : 0f);

        walk.Advance(new Vector2(wish.X, wish.Z) * pace, keyboard.IsKeyPressed(Key.Space), rise * pace, deltaSeconds);
        _camera.Position = walk.Eye;
    }

    private bool WalkClickStillHeld()
    {
        if (_walkClickHeld && _input is { Mice.Count: > 0 } input
            && !input.Mice[0].IsButtonPressed(MouseButton.Left) && !input.Mice[0].IsButtonPressed(MouseButton.Right))
        {
            _walkClickHeld = false;
        }

        return _walkClickHeld;
    }

    /// <summary>Blender's Z pie: the four shadings, and the switches most turned on and off.</summary>
    private PieSlice[] ShadingSlices() =>
    [
        new("Wireframe", () => View.Shading = ShadingMode.Wireframe, View.Shading == ShadingMode.Wireframe),
        new("Rendered", () => View.Shading = ShadingMode.Rendered, View.Shading == ShadingMode.Rendered),
        new("Solid", () => View.Shading = ShadingMode.Unlit, View.Shading == ShadingMode.Unlit),
        new("Lit", () => View.Shading = ShadingMode.Lit, View.Shading == ShadingMode.Lit),
        new("X-Ray", () => View.XRay = !View.XRay, View.XRay),
        new("Shadows", () => View.Shadows = !View.Shadows, View.Shadows),
        new("Overlays", () => View.Overlays = !View.Overlays, View.Overlays),
        new("Ambient Occlusion", () => View.AmbientOcclusion = !View.AmbientOcclusion, View.AmbientOcclusion),
    ];

    /// <summary>Blender's ~ pie: the six views along the axes, the camera, and the selection framed.</summary>
    private PieSlice[] ViewSlices() =>
    [
        new("Left", () => _camera.Align(AlignedView.Left)),
        new("Right", () => _camera.Align(AlignedView.Right)),
        new("Bottom", () => _camera.Align(AlignedView.Bottom)),
        new("Top", () => _camera.Align(AlignedView.Top)),
        new("Front", () => _camera.Align(AlignedView.Front)),
        new("Back", () => _camera.Align(AlignedView.Back)),
        new("Camera", ToggleCameraView),
        new("Frame Selected", FrameFocused),
    ];

    /// <summary>What walking is, and how to stop, along the bottom of the view.</summary>
    private void DrawWalkHint()
    {
        if (_walk is not { } walk)
        {
            return;
        }

        string text = $"Walking{(walk.Flying ? " (flying)" : string.Empty)}  -  W A S D  ·  Space {(walk.Flying ? "up, C down" : "jumps")}  ·  Shift runs  ·  Tab {(walk.Flying ? "walks" : "flies")}  ·  Enter or click stays here  ·  Esc goes back";
        Vector2 size = ImGui.CalcTextSize(text);
        Vector2 at = _viewport.Position + new Vector2((_viewport.Size.X - size.X) * 0.5f, _viewport.Size.Y - size.Y - 18f);
        ImDrawListPtr draw = ImGui.GetForegroundDrawList();
        draw.AddRectFilled(at - new Vector2(10f, 6f), at + size + new Vector2(10f, 6f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), 6f);
        draw.AddText(at, ImGui.GetColorU32(Theme.Text), text);

        // A small cross in the middle, where the view is facing.
        Vector2 middle = _viewport.Position + (_viewport.Size * 0.5f);
        uint cross = ImGui.GetColorU32(Theme.Text with { W = 0.7f });
        draw.AddLine(middle - new Vector2(6f, 0f), middle + new Vector2(6f, 0f), cross, 1.5f);
        draw.AddLine(middle - new Vector2(0f, 6f), middle + new Vector2(0f, 6f), cross, 1.5f);
    }

    /// <summary>What Render Image is seen from: the camera renders are seen from, or the view when there is none.</summary>
    private RenderCamera RenderImageCamera() => _session.Scene.ActiveCamera?.ToRenderCamera() ?? RenderCameraNow();

    /// <summary>The view as it stands, as a camera the level could keep.</summary>
    private SceneCamera ViewAsCamera() => new(
        0,
        "Camera",
        _camera.Position,
        _camera.Yaw,
        _camera.Pitch,
        _camera.FieldOfView * (180f / MathF.PI),
        _camera.Orthographic ? CameraKind.Orthographic : CameraKind.Perspective,
        _camera.OrthographicHeight,
        _camera.PivotDistance);

    /// <summary>
    /// The view put where a camera stands, seeing what it sees — widened a little past it, so the
    /// frame of the picture fits inside the viewport with a margin round it, as Blender's camera view.
    /// </summary>
    private void LookThrough(SceneCamera camera)
    {
        _through = (_through ?? (0, _camera.Position, _camera.Yaw, _camera.Pitch, _camera.FieldOfView, _camera.Orthographic, _camera.PivotDistance)) with { CameraId = camera.Id };

        RenderSettings settings = _session.Scene.RenderSettings.Clamped();
        float pictureAspect = settings.Width / (float)settings.Height;
        float viewAspect = _viewport.Size.Y > 0f ? _viewport.Size.X / _viewport.Size.Y : pictureAspect;
        float widen = MathF.Max(1f, pictureAspect / MathF.Max(viewAspect, 0.01f)) * 1.08f;

        _camera.Position = camera.Position;
        _camera.Yaw = camera.Yaw;
        _camera.Pitch = camera.Pitch;
        _camera.Orthographic = camera.IsOrthographic;
        if (camera.IsOrthographic)
        {
            _camera.FieldOfView = _preferences.FieldOfView * (MathF.PI / 180f);
            _camera.PivotDistance = camera.OrthographicHeight * widen / (2f * MathF.Tan(_camera.FieldOfView * 0.5f));
        }
        else
        {
            float half = MathF.Atan(MathF.Tan(camera.FieldOfView * 0.5f * (MathF.PI / 180f)) * widen);
            _camera.FieldOfView = Math.Clamp(half * 2f, 0.02f, 3.1f);
            _camera.PivotDistance = camera.PivotDistance;
        }
    }

    /// <summary>The camera the view is looking through, while it still stands where the camera does.</summary>
    private SceneCamera? LookingThrough()
    {
        if (_through is not { } through || _session.Scene.FindCamera(through.CameraId) is not { } camera)
        {
            return null;
        }

        bool still = Vector3.Distance(_camera.Position, camera.Position) < 1e-3f
            && MathF.Abs(_camera.Yaw - camera.Yaw) < 1e-4f
            && MathF.Abs(_camera.Pitch - camera.Pitch) < 1e-4f
            && _camera.Orthographic == camera.IsOrthographic;
        return still ? camera : null;
    }

    /// <summary>Once the view has moved off the camera it was looking through, it sees with its own lens again.</summary>
    private void KeepCameraViewHonest()
    {
        if (_through is { } through && LookingThrough() is null)
        {
            _through = null;
            _camera.FieldOfView = _preferences.FieldOfView * (MathF.PI / 180f);
            if (!through.Orthographic && _camera.Orthographic)
            {
                _camera.Orthographic = false;
            }
        }
    }

    /// <summary>Numpad 0: through the camera renders are seen from — or, from it, back to the view as it was.</summary>
    private void ToggleCameraView()
    {
        if (_through is { } through && LookingThrough() is not null)
        {
            _through = null;
            _camera.Position = through.Position;
            _camera.Yaw = through.Yaw;
            _camera.Pitch = through.Pitch;
            _camera.FieldOfView = through.FieldOfView;
            _camera.Orthographic = through.Orthographic;
            _camera.PivotDistance = through.PivotDistance;
            return;
        }

        SceneCamera? camera = _session.Scene.ActiveCamera ?? _session.PickedCamera ?? _session.Scene.Cameras.FirstOrDefault();
        if (camera is null)
        {
            ReportLog.Shared.Post("There is no camera to look through. Add > Camera makes one where the view stands.", ReportKind.Warning);
            return;
        }

        LookThrough(camera);
    }

    /// <summary>Ctrl+Alt+Numpad 0: the camera renders are seen from, moved to the view — a new one when there is none.</summary>
    private void CameraToView()
    {
        SceneCamera view = ViewAsCamera();
        if (_session.Scene.ActiveCamera is { } active)
        {
            _session.SetCameraToView(active.Id, view);
        }
        else
        {
            _session.AddCamera(view);
        }

        if (_session.Scene.ActiveCamera is { } moved)
        {
            LookThrough(moved);
        }
    }

    /// <summary>
    /// While the view looks through a camera: the frame of the picture a render will make, and the
    /// rest of the viewport dimmed, as Blender's camera view shows it.
    /// </summary>
    private void DrawCameraFrame()
    {
        if (LookingThrough() is not { } camera || WelcomeScreen.IsOpen)
        {
            return;
        }

        RenderSettings settings = _session.Scene.RenderSettings.Clamped();
        float pictureAspect = settings.Width / (float)settings.Height;
        float fraction = camera.IsOrthographic
            ? camera.OrthographicHeight / MathF.Max(_camera.OrthographicHeight, 1e-4f)
            : MathF.Tan(camera.FieldOfView * 0.5f * (MathF.PI / 180f)) / MathF.Tan(_camera.FieldOfView * 0.5f);

        Vector2 size = new Vector2(_viewport.Size.Y * fraction * pictureAspect, _viewport.Size.Y * fraction);
        Vector2 centre = _viewport.Position + (_viewport.Size * 0.5f);
        Vector2 min = centre - (size * 0.5f);
        Vector2 max = centre + (size * 0.5f);
        Vector2 viewMin = _viewport.Position;
        Vector2 viewMax = _viewport.Position + _viewport.Size;

        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
        uint dim = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f));
        draw.AddRectFilled(viewMin, new Vector2(viewMax.X, MathF.Max(min.Y, viewMin.Y)), dim);
        draw.AddRectFilled(new Vector2(viewMin.X, MathF.Min(max.Y, viewMax.Y)), viewMax, dim);
        draw.AddRectFilled(new Vector2(viewMin.X, MathF.Max(min.Y, viewMin.Y)), new Vector2(MathF.Max(min.X, viewMin.X), MathF.Min(max.Y, viewMax.Y)), dim);
        draw.AddRectFilled(new Vector2(MathF.Min(max.X, viewMax.X), MathF.Max(min.Y, viewMin.Y)), new Vector2(viewMax.X, MathF.Min(max.Y, viewMax.Y)), dim);
        draw.AddRect(min, max, ImGui.GetColorU32(Theme.Text with { W = 0.6f }), 0f, ImDrawFlags.None, 1f);

        string label = _session.Scene.ActiveCameraId == camera.Id ? $"{camera.Name}  -  renders are seen from it" : camera.Name;
        draw.AddText(min + new Vector2(6f, 4f), ImGui.GetColorU32(Theme.Text with { W = 0.8f }), label);
    }

    /// <summary>Where the render looks from: the viewport's own camera, as it stands.</summary>
    private RenderCamera RenderCameraNow() => new(
        _camera.Position,
        _camera.Forward,
        _camera.Up,
        _camera.FieldOfView * (180f / MathF.PI),
        _camera.Orthographic,
        _camera.OrthographicHeight);

    /// <summary>The viewport's pixels, as the scene alone drew them, to a PNG.</summary>
    private unsafe void CaptureViewport(string path, Vector2 position, Vector2 size, Vector2 framebuffer)
    {
        int width = (int)MathF.Max(size.X, 1f);
        int height = (int)MathF.Max(size.Y, 1f);
        int glY = (int)(framebuffer.Y - (position.Y + size.Y));
        var pixels = new byte[width * height * 4];

        fixed (byte* destination = pixels)
        {
            _gl!.ReadPixels((int)position.X, glY, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, destination);
        }

        var flipped = new byte[pixels.Length];
        int stride = width * 4;
        for (int row = 0; row < height; row++)
        {
            Array.Copy(pixels, (height - 1 - row) * stride, flipped, row * stride, stride);
        }

        // Opaque: the viewport has no see-through background to keep.
        for (int i = 3; i < flipped.Length; i += 4)
        {
            flipped[i] = 255;
        }

        try
        {
            PngWriter.WriteRgba(path, flipped, width, height);
            ReportLog.Shared.Post($"Saved the viewport to {Path.GetFileName(path)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportLog.Shared.Post($"Could not save {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
        }
    }

    /// <summary>An empty level says how to put something in it, in the middle of the view, under the panels.</summary>
    private void DrawEmptyHint()
    {
        if (_session.Scene.Objects.Count > 0 || WelcomeScreen.IsOpen)
        {
            return;
        }

        string key = Shortcut.Of(EditorAction.AddMenu) is { Length: > 0 } add ? add : "the Add menu";
        string[] lines = ["The level is empty.", $"{key} adds a shape, a prop or a light."];

        // Low in the view, clear of the origin, where the sun and whatever is added first will be.
        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
        float y = _viewport.Position.Y + (_viewport.Size.Y * 0.72f);
        foreach (string line in lines)
        {
            Vector2 size = ImGui.CalcTextSize(line);
            draw.AddText(new Vector2(_viewport.Position.X + ((_viewport.Size.X - size.X) * 0.5f), y), ImGui.GetColorU32(Theme.Text with { W = 0.75f }), line);
            y += ImGui.GetTextLineHeightWithSpacing();
        }
    }

    /// <summary>The box a selecting drag is drawing, in the colour of what it will do — or the stencil's.</summary>
    private void DrawSelectBox()
    {
        if (_stencil is { } stencil)
        {
            ImDrawListPtr stencilDraw = ImGui.GetForegroundDrawList();
            Vector2 from = _viewport.Position + Vector2.Min(stencil.From, stencil.To);
            Vector2 to = _viewport.Position + Vector2.Max(stencil.From, stencil.To);
            stencilDraw.AddRectFilled(from, to, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)));
            stencilDraw.AddRect(from, to, ImGui.GetColorU32(EditorOverlays.BrushOutline.ToVector4()), 0f, ImDrawFlags.None, 1.5f);
            stencilDraw.AddText(from + new Vector2(6f, 4f), ImGui.GetColorU32(Theme.Text), _session.Pattern?.Name ?? "No image loaded");
            return;
        }

        if (_select?.Box is not { } box)
        {
            return;
        }

        ImDrawListPtr draw = ImGui.GetForegroundDrawList();
        Vector2 min = _viewport.Position + box.Min;
        Vector2 max = _viewport.Position + box.Max;
        Vector4 colour = EditorOverlays.SelectionColour(_select.Operation).ToVector4();

        draw.AddRectFilled(min, max, ImGui.GetColorU32(colour with { W = 0.08f }));
        draw.AddRect(min, max, ImGui.GetColorU32(colour with { W = 0.9f }), 0f, ImDrawFlags.None, 1f);
    }

    private WelcomeActions CreateWelcomeActions() => new()
    {
        New = template => _project!.NewProject(template),
        Open = () => _project!.OpenProject(),
        Recent = () => _project!.Recent.Paths,
        OpenRecent = path => _project!.OpenRecent(path),
        LastSession = () => _project!.LastSessionFound,
        RecoverLastSession = () => _project!.RecoverLastSession(),
        CanRecoverAutoSave = () => _project!.CanRecover,
        RecoverAutoSave = () => _project!.OfferRecovery(),
        OpenUrl = OpenUrl,
        ShowShortcuts = () =>
        {
            if (!ShortcutSheet.IsOpen)
            {
                ShortcutSheet.Toggle();
            }
        },
        Preferences = _preferences,
    };

    /// <summary>Opens a web page in the default browser, saying so in the status bar if it cannot.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            using var browser = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            ReportLog.Shared.Post($"Could not open {url}: {exception.Message}", ReportKind.Error);
        }
    }

    private ViewActions CreateViewActions() => new()
    {
        FrameLevel = FrameLevel,
        CloseLevel = () =>
        {
            if (_level is { } closing)
            {
                CloseLevel(closing);
            }
        },
        FrameFocused = FrameFocused,
        RenderImage = () => _renderWindow?.Start(_session, RenderImageCamera()),
        ViewCamera = ToggleCameraView,
        Walk = BeginWalk,
        RenderOutputs = () => _outputsWindow?.Open(),
        CameraToView = CameraToView,
        SaveViewportImage = () => ViewportShotBrowser.Show(FileBrowserMode.Save, "Save the viewport as an image", ".png", null, _session.ProjectName, path => _viewportShotPath = path),
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
        GridVisible = () => View.Grid,
        ToggleGrid = () => View.Grid = !View.Grid,
        MeasurementsVisible = () => View.Measurements,
        ToggleMeasurements = () => View.Measurements = !View.Measurements,
        SidebarVisible = () => _layout.SidebarVisible,
        ToggleSidebar = () => _layout.SidebarVisible = !_layout.SidebarVisible,
        StatisticsVisible = () => _layout.StatisticsVisible,
        ToggleStatistics = () => _layout.StatisticsVisible = !_layout.StatisticsVisible,
        LightIconsVisible = () => View.LightIcons,
        ToggleLightIcons = () => View.LightIcons = !View.LightIcons,
        MirrorPlanesVisible = () => View.MirrorPlanes,
        ToggleMirrorPlanes = () => View.MirrorPlanes = !View.MirrorPlanes,
        Lighting = _renderer!.Lighting,
        Viewport = View,
    };

    private void FrameLevel()
    {
        if (_session.Scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            _camera.FrameBox(min, max);
        }
    }

    /// <summary>Frames what is selected — all of it — or, with nothing selected, the focused object.</summary>
    private void FrameFocused()
    {
        bool any = false;
        Vector3 min = Vector3.Zero, max = Vector3.Zero;

        // Inside an object, what is chosen of it.
        if (_session.EditObject is { } edited && _session.VoxelSelection.TryGetBounds(out Int3 low, out Int3 high))
        {
            foreach (Vector3 corner in Snapping.Corners((low.ToVector3(), high.ToVector3() + Vector3.One), edited.Transform))
            {
                min = any ? Vector3.Min(min, corner) : corner;
                max = any ? Vector3.Max(max, corner) : corner;
                any = true;
            }

            _camera.FrameBox(min, max);
            return;
        }

        IEnumerable<VoxelObject> framed = _session.SelectedCount > 0
            ? _session.SelectedObjects
            : _session.Scene.Focus is { } focus ? [focus] : [];

        foreach (VoxelObject o in framed)
        {
            if (o.TryGetWorldBounds(out Vector3 lo, out Vector3 hi))
            {
                min = any ? Vector3.Min(min, lo) : lo;
                max = any ? Vector3.Max(max, hi) : hi;
                any = true;
            }
        }

        foreach (SceneLight light in _session.SelectedLights)
        {
            min = any ? Vector3.Min(min, light.Position - Vector3.One) : light.Position - Vector3.One;
            max = any ? Vector3.Max(max, light.Position + Vector3.One) : light.Position + Vector3.One;
            any = true;
        }

        if (any)
        {
            _camera.FrameBox(min, max);
        }
    }

    /// <summary>Whatever number the gesture in progress is producing, or nothing.</summary>
    /// <summary>
    /// The + or - by the pointer in Extrude while Shift or Alt is held, or while a drag started with
    /// one is running: what a click will do to the selection, where the eye already is.
    /// </summary>
    private SelectionOperation? CursorMark()
    {
        if (_session.ActiveTool != EditorTool.Extrude
            || _extrude!.IsDraggingArrow
            || _looking
            || ImGui.GetIO().WantCaptureMouse
            || _input is not { Mice.Count: > 0 }
            || !_viewport.Contains(_input.Mice[0].Position))
        {
            return null;
        }

        SelectionOperation operation = _extrude.IsSelecting ? _extrude.PendingOperation : HeldSelectionOperation();
        return operation == SelectionOperation.Replace ? null : operation;
    }

    /// <summary>The colour a click would sample: Alt held in Paint, over a face. Null otherwise.</summary>
    private Color32? CursorSample()
    {
        if (_session.ActiveTool != EditorTool.Paint || !IsSamplingColour() || _hover is not { } hit)
        {
            return null;
        }

        return PaintOperations.Sample(_session.World, hit.Voxel, hit.Face) is { } index
            ? _session.Scene.Palette[index]
            : null;
    }

    /// <summary>Alt in Paint means the eyedropper, for as long as it is held and the pointer is over the model.</summary>
    private bool IsSamplingColour() =>
        IsAltHeld()
        && !_looking
        && !ImGui.GetIO().WantCaptureMouse
        && _input is { Mice.Count: > 0 }
        && _viewport.Contains(_input.Mice[0].Position);

    private string CurrentDragReadout()
    {
        if (_aim is { Readout.Length: > 0 } aim)
        {
            return aim.Readout;
        }

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
        // Each level with unsaved work in turn, brought to the front to be asked about.
        if (_project is not null
            && _levels.FirstOrDefault(level => level.Session.HasUnsavedChanges && level.AnsweredAt != level.Session.Revision) is { } unsaved)
        {
            _window.IsClosing = false;
            ShowLevel(unsaved);
            _project.RequestExit(() =>
            {
                // Answered from inside a popup, so mid-frame. Closing here would dispose the
                // renderer under the frame that is still being drawn.
                unsaved.AnsweredAt = unsaved.Session.Revision;
                _closeRequested = true;
            });

            return;
        }

        // Past the guard, so the user has answered for any unsaved work — kept or thrown away, the
        // autosave is nobody's safety net any more. A crash never reaches this line, which is what
        // leaves the copy behind to be offered next time. What was open is kept all the same, as
        // the last session, for when the answer was the wrong one.
        _lastSession?.Write(_session);
        foreach (LevelDocument level in _levels)
        {
            level.Autosave?.CloseCleanly();
        }

        SavePreferences();

        if (_smokeFrames <= 0)
        {
            _layout.Save(LayoutSettings.DefaultPath);
        }

        _imgui?.Dispose();
        _renderWindow?.Dispose();
        _outputsWindow?.Dispose();
        _library?.Dispose();
        _viewportRender?.Dispose();
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

    // ---- Measuring and notes (Fullreleaseplan 7.9) -------------------------------------------------

    /// <summary>
    /// The measure tool: a drag from one point to another lays a ruler between them; a drag that
    /// starts on a ruler's end moves that end. A click that goes nowhere leaves nothing behind.
    /// </summary>
    private void UpdateMeasure(Vector2 local, bool leftDown, bool pressed, bool pointing)
    {
        List<Ruler> rulers = _session.Rulers;
        if (_rulerDrag is { } stale && stale.Index >= rulers.Count)
        {
            _rulerDrag = null;
        }

        _measureCursor = pointing && _rulerDrag is null ? MeasurePoint(local) : null;
        _rulerHover = _rulerDrag?.Index ?? (pointing ? RulerUnder(local) : -1);

        if (pressed)
        {
            if (RulerEndUnder(local) is { } grabbed)
            {
                _rulerDrag = grabbed;
            }
            else if (MeasurePoint(local) is { } start)
            {
                rulers.Add(new Ruler(start, start));
                _rulerDrag = (rulers.Count - 1, true);
            }

            return;
        }

        if (_rulerDrag is not { } drag)
        {
            return;
        }

        if (leftDown)
        {
            if (MeasurePoint(local) is { } point)
            {
                rulers[drag.Index] = drag.End ? rulers[drag.Index] with { End = point } : rulers[drag.Index] with { Start = point };
            }

            return;
        }

        if (rulers[drag.Index].Length < 1e-4f)
        {
            rulers.RemoveAt(drag.Index);
            _rulerHover = -1;
        }

        _rulerDrag = null;
    }

    /// <summary>
    /// Where a ruler's end goes for a point of the view: the voxel corner nearest what is under it,
    /// or the ground's; with Ctrl, the very point.
    /// </summary>
    private Vector3? MeasurePoint(Vector2 local)
    {
        Ray ray = _camera.ScreenPointToRay(local, _viewport.Size);
        bool free = IsControlHeld();
        return PickAt(local, selectedOnly: false) is { } pick ? Measuring.PointOn(pick, ray, free) : Measuring.OnGround(ray, free);
    }

    /// <summary>The ruler end within reach of a point of the view, the nearest; null for none.</summary>
    private (int Index, bool End)? RulerEndUnder(Vector2 local)
    {
        const float Reach = 9f;
        (int Index, bool End)? found = null;
        float nearest = Reach;
        for (int i = 0; i < _session.Rulers.Count; i++)
        {
            Ruler ruler = _session.Rulers[i];
            foreach ((Vector3 point, bool end) in new[] { (ruler.End, true), (ruler.Start, false) })
            {
                if (_camera.TryProjectToScreen(point, _viewport.Size, out Vector2 screen) && Vector2.Distance(screen, local) <= nearest)
                {
                    nearest = Vector2.Distance(screen, local);
                    found = (i, end);
                }
            }
        }

        return found;
    }

    /// <summary>The ruler whose end or line is under a point of the view, the newest first; −1 for none.</summary>
    private int RulerUnder(Vector2 local)
    {
        if (RulerEndUnder(local) is { } end)
        {
            return end.Index;
        }

        const float Reach = 6f;
        for (int i = _session.Rulers.Count - 1; i >= 0; i--)
        {
            Ruler ruler = _session.Rulers[i];
            if (_camera.TryProjectToScreen(ruler.Start, _viewport.Size, out Vector2 a)
                && _camera.TryProjectToScreen(ruler.End, _viewport.Size, out Vector2 b)
                && DistanceToSegment(local, a, b) <= Reach)
            {
                return i;
            }
        }

        return -1;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float along = ab.LengthSquared() < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(point, a + (ab * along));
    }

    /// <summary>Delete with the measure tool: the ruler under the pointer goes — or, with none there, the newest.</summary>
    private void DeleteRuler()
    {
        List<Ruler> rulers = _session.Rulers;
        if (rulers.Count == 0)
        {
            return;
        }

        rulers.RemoveAt(_rulerHover >= 0 && _rulerHover < rulers.Count ? _rulerHover : rulers.Count - 1);
        _rulerHover = -1;
    }

    /// <summary>The rulers' ends and what each measures, over the view, while the measure tool is in hand.</summary>
    private void DrawRulerLabels()
    {
        if (_session.ActiveTool != EditorTool.Measure || WelcomeScreen.IsOpen)
        {
            return;
        }

        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
        draw.PushClipRect(_viewport.Position, _viewport.Position + _viewport.Size, true);
        uint plain = ImGui.GetColorU32(Theme.Text);
        uint lit = ImGui.GetColorU32(Theme.Accent);
        uint box = ImGui.GetColorU32(Theme.Surface with { W = 0.88f });
        var padding = new Vector2(6f, 3f);
        float perMetre = _preferences.Walk.Clamped().VoxelsPerMetre;

        for (int i = 0; i < _session.Rulers.Count; i++)
        {
            Ruler ruler = _session.Rulers[i];
            uint colour = i == _rulerHover ? lit : plain;
            foreach (Vector3 end in new[] { ruler.Start, ruler.End })
            {
                if (_camera.TryProjectToScreen(end, _viewport.Size, out Vector2 at))
                {
                    draw.AddCircleFilled(_viewport.Position + at, i == _rulerHover ? 4.5f : 3.5f, colour);
                }
            }

            if (ruler.Length > 0f && _camera.TryProjectToScreen((ruler.Start + ruler.End) * 0.5f, _viewport.Size, out Vector2 middle))
            {
                // Above the middle, clear of the line it is about.
                string text = ruler.Describe(perMetre);
                Vector2 size = ImGui.CalcTextSize(text);
                Vector2 min = _viewport.Position + middle - new Vector2(size.X * 0.5f, size.Y + 10f) - padding;
                draw.AddRectFilled(min, min + size + (padding * 2f), box, 4f);
                draw.AddText(min + padding, colour, text);
            }
        }

        // Where a press would start the next one.
        if (_measureCursor is { } cursor && _camera.TryProjectToScreen(cursor, _viewport.Size, out Vector2 next))
        {
            draw.AddCircle(_viewport.Position + next, 5f, lit, 16, 1.5f);
        }

        draw.PopClipRect();
    }

    /// <summary>Each note's words, over the view at the head of its pin, the nearest drawn over the rest.</summary>
    private void DrawNotes()
    {
        _noteLabels.Clear();
        if (!View.Overlays || WelcomeScreen.IsOpen)
        {
            return;
        }

        List<VoxelObject> notes = [.. _session.Scene.Objects
            .Where(o => o.Marker is { IsNote: true } && o.Visible)
            .OrderByDescending(o => Vector3.DistanceSquared(_camera.Position, o.Transform.Position))];
        if (notes.Count == 0)
        {
            return;
        }

        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
        draw.PushClipRect(_viewport.Position, _viewport.Position + _viewport.Size, true);
        float wrap = ImGui.GetFontSize() * 16f;
        var padding = new Vector2(7f, 5f);

        foreach (VoxelObject note in notes)
        {
            if (!_camera.TryProjectToScreen(EditorOverlays.NoteHead(note), _viewport.Size, out Vector2 head))
            {
                continue;
            }

            // Nothing written yet: its name, faintly, until there is.
            string words = note.Marker!.Text;
            string text = words.Length == 0 ? note.Name : words.Length > 400 ? words[..400] + "..." : words;
            Vector2 size = ImGui.CalcTextSize(text, wrap);
            Vector2 min = _viewport.Position + head + new Vector2(-6f, -size.Y - (padding.Y * 2f) - 4f);
            Vector2 max = min + size + (padding * 2f);
            bool chosen = _session.IsSelected(note.Id);

            draw.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Surface with { W = 0.9f }), 4f);
            draw.AddRect(min, max, ImGui.GetColorU32(chosen ? Theme.Accent : Theme.Border), 4f, ImDrawFlags.None, chosen ? 2f : 1f);
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + padding, ImGui.GetColorU32(words.Length == 0 ? Theme.TextDim : Theme.Text), text, wrap);
            _noteLabels.Add((note.Id, min - _viewport.Position, max - _viewport.Position));
        }

        draw.PopClipRect();
    }

    // ---- Levels in tabs (Fullreleaseplan 7.8) ------------------------------------------------------

    /// <summary>A level for a session, with its tools and its own autosave, at the end of the tabs.</summary>
    private LevelDocument AddLevel(EditorSession session)
    {
        session.SharedClipboard = _clipboard;
        var level = new LevelDocument(session, _nextSlot++);

        // Not in a smoke or screenshot run, which must not write the user's recovery folder.
        if (_smokeFrames <= 0)
        {
            level.Autosave = new AutosaveController(session, slot: level.Slot);
        }

        ApplyPreferencesTo(level);
        _levels.Add(level);
        return level;
    }

    /// <summary>Makes a level the one every tool, panel and shortcut works on.</summary>
    private void Front(LevelDocument level)
    {
        _level = level;
        _session = level.Session;
        _export = level.Export;
        _mimicraft = level.Mimicraft;
        _extrude = level.Extrude;
        _transform = level.Transform;
        _aim = level.Aim;
        _select = level.Select;
        ObjectListPanel.Folded = level.Folded;

        if (_project is not null)
        {
            _project.Session = level.Session;
            _project.Autosave = level.Autosave;
        }
    }

    /// <summary>
    /// Brings a level's tab to the front. The view goes back to where it was in it, its section box
    /// with it; a walk, a look through a camera, and what the pointer was over are left behind.
    /// Snapping goes along: it is the editor's, not the level's.
    /// </summary>
    private void ShowLevel(LevelDocument level)
    {
        if (ReferenceEquals(level, _level))
        {
            return;
        }

        EndWalk(keep: false);
        if (_level is { } leaving)
        {
            leaving.View = ViewPose.Of(_camera);
            leaving.Clip = View.Clip;
        }

        if (!ReferenceEquals(level.Session, _session))
        {
            level.Session.Snap.CopyFrom(_session.Snap);
        }

        ObjectListPanel.StopRenaming();
        Front(level);
        _through = null;
        _hoverObjectId = 0;
        _aimHover = null;
        _pick = null;
        _hover = null;
        _paintShapeStart = null;
        _rulerDrag = null;
        _rulerHover = -1;
        _measureCursor = null;
        _noteLabels.Clear();
        View.Clip = level.Clip;

        // The renderer let go of this level's meshes while another was in front.
        _renderer?.ResetBuffers();
        foreach (VoxelObject o in _session.Scene.Objects)
        {
            o.Shown.MarkAllDirty();
        }

        if (level.View is { } pose)
        {
            pose.ApplyTo(_camera);
        }
        else
        {
            FrameLevel();
        }
    }

    /// <summary>
    /// Where a level being opened goes: in place of the one in front when that is an untitled level
    /// nothing has been done to, else in a tab of its own, brought to the front.
    /// </summary>
    private EditorSession MakeRoomForLevel()
    {
        if (_level is { IsUntouched: true } untouched)
        {
            return untouched.Session;
        }

        EditorSession session = FreshSession();
        ShowLevel(AddLevel(session));
        return session;
    }

    /// <summary>A session as the editor starts on one: choosing comes first, and the colour in hand is white.</summary>
    private static EditorSession FreshSession() => new() { ActiveTool = EditorTool.Select, ActiveColorIndex = Palette.WhiteIndex };

    /// <summary>Brings forward the tab a file is open in already; false when no tab has it.</summary>
    private bool ShowOpenLevel(string path)
    {
        string full = Path.GetFullPath(path);
        LevelDocument? open = _levels.FirstOrDefault(level =>
            level.Session.ProjectPath is { } own && string.Equals(Path.GetFullPath(own), full, StringComparison.OrdinalIgnoreCase));

        if (open is null)
        {
            return false;
        }

        ShowLevel(open);
        return true;
    }

    /// <summary>The next tab along, or the one before — round from the last to the first.</summary>
    private void CycleLevel(int step)
    {
        if (_levels.Count < 2 || _level is null || IsDragging())
        {
            return;
        }

        int index = _levels.IndexOf(_level);
        ShowLevel(_levels[(((index + step) % _levels.Count) + _levels.Count) % _levels.Count]);
    }

    /// <summary>Closes a level's tab once its unsaved work is answered for — brought to the front to be asked about.</summary>
    private void CloseLevel(LevelDocument level)
    {
        if (_project is null || IsDragging())
        {
            return;
        }

        ShowLevel(level);
        _project.RequestClose(() => RemoveLevel(level));
    }

    /// <summary>Lets a level go. The one beside it comes to the front; the last one gives way to a new level.</summary>
    private void RemoveLevel(LevelDocument level)
    {
        int index = _levels.IndexOf(level);
        if (index < 0)
        {
            return;
        }

        level.Autosave?.CloseCleanly();
        _levels.RemoveAt(index);

        if (_levels.Count == 0)
        {
            EditorSession session = FreshSession();
            session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
            _level = null;
            ShowLevel(AddLevel(session));
            return;
        }

        if (ReferenceEquals(level, _level))
        {
            _level = null;
            ShowLevel(_levels[Math.Min(index, _levels.Count - 1)]);
        }
    }

    /// <summary>Every level's unsaved work written out as the editor goes down.</summary>
    private void WriteBeforeDying()
    {
        foreach (LevelDocument level in _levels)
        {
            level.Autosave?.WriteBeforeDying();
        }
    }

    public void Dispose() => _window.Dispose();
}
