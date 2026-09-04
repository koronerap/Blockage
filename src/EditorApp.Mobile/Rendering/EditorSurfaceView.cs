using System.Numerics;
using Android.Content;
using Android.Opengl;
using Android.Views;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Mobile.Tools;
using EditorApp.Rendering;
using Javax.Microedition.Khronos.Opengles;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// The GL surface the editor draws into, the renderer that draws it, and the fingers that move it.
///
/// Two threads meet here. Everything from <see cref="OnSurfaceCreated"/> down runs on the GL thread;
/// touch events and the interface arrive on the UI thread. The session, the camera and the tools are
/// read by one and written by the other, so every path through them takes <see cref="_sceneGate"/> —
/// a voxel world being remeshed while an edit lands in it would be a very hard bug to find later.
/// </summary>
public sealed class EditorSurfaceView : GLSurfaceView, GLSurfaceView.IRenderer
{
    private readonly Lock _sceneGate = new();
    private readonly EditorSession _session = new();
    private readonly OrbitCamera _camera = new();
    private readonly TouchGestureTracker _tracker = new();
    private readonly TouchGestures _gestures;
    private readonly MobileTools _tools;

    private MobileRenderer? _renderer;
    private Vector2 _viewportSize = Vector2.One;

    /// <summary>
    /// Set when the level is replaced. Every chunk buffer belongs to the old scene and has to go,
    /// but only the GL thread may delete them, so the request is left here for the next frame.
    /// </summary>
    private bool _dropBuffers;

    private readonly Lock _reportGate = new();
    private string _report = "Waiting for the GL thread…";

    public EditorSurfaceView(Context context)
        : base(context)
    {
        SetEGLContextClientVersion(3);

        // Losing the context on every pause would mean rebuilding every chunk buffer on resume. The
        // flag is a request, not a guarantee, so the renderer still has to cope with a surface that
        // comes back empty.
        PreserveEGLContextOnPause = true;

        _gestures = new TouchGestures(_tracker);
        _tools = new MobileTools(_session);

        _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        FrameScene();

        SetRenderer(this);

        // A level does not move on its own, and a phone pays for every frame in battery. So the
        // surface only redraws when something asked it to — which puts the burden on every state
        // change to say so, including the remesh queue, which finishes over several frames.
        RenderMode = Rendermode.WhenDirty;
    }

    /// <summary>Raised on the UI thread whenever <see cref="Report"/> changes.</summary>
    public event Action? ReportChanged;

    /// <summary>Raised on the UI thread when something the tool bar shows has changed.</summary>
    public event Action? SessionChanged;

    /// <summary>
    /// What the GL thread found when it came up: the driver's own identification, or the reason
    /// nothing is on screen.
    /// </summary>
    public string Report
    {
        get
        {
            lock (_reportGate)
            {
                return _report;
            }
        }
    }

    public EditorTool ActiveTool
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.ActiveTool;
            }
        }
    }

    public byte ActiveColorIndex
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.ActiveColorIndex;
            }
        }
    }

    public Color32 ActiveColor
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.World.Palette[_session.ActiveColorIndex];
            }
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.History.CanUndo;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.History.CanRedo;
            }
        }
    }

    /// <summary>A copy, because the caller is on another thread and the palette can change.</summary>
    public Color32[] PaletteSnapshot()
    {
        lock (_sceneGate)
        {
            return _session.World.Palette.Colors.ToArray();
        }
    }

    public string ProjectName
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.ProjectName;
            }
        }
    }

    public string? ProjectPath
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.ProjectPath;
            }
        }
    }

    public bool HasUnsavedChanges
    {
        get
        {
            lock (_sceneGate)
            {
                return _session.HasUnsavedChanges;
            }
        }
    }

    /// <summary>
    /// Runs something against the live scene while the GL thread is held off it. Used for saving:
    /// copying the whole scene out first would double a large level in memory for no reason, and
    /// reading it unguarded could catch it mid-remesh.
    /// </summary>
    public void UseScene(Action<VoxelScene> action)
    {
        lock (_sceneGate)
        {
            action(_session.Scene);
        }
    }

    /// <summary>Records that the level now lives somewhere, and has nothing outstanding.</summary>
    public void MarkSaved(string? path)
    {
        lock (_sceneGate)
        {
            _session.ProjectPath = path;
            _session.HasUnsavedChanges = false;
        }

        Changed();
    }

    public void NewLevel()
    {
        lock (_sceneGate)
        {
            _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
            AfterLevelChanged();
        }

        Changed();
    }

    public void ReplaceScene(VoxelScene scene, string? path)
    {
        lock (_sceneGate)
        {
            _session.ReplaceScene(scene, path);
            AfterLevelChanged();
        }

        Changed();
    }

    /// <summary>Call inside the lock: nothing of the old level may survive into the new one.</summary>
    private void AfterLevelChanged()
    {
        _dropBuffers = true;
        _session.ClearSelection();
        _session.PreviewCutPlane = null;
        FrameScene();
    }

    public void SetTool(EditorTool tool)
    {
        lock (_sceneGate)
        {
            if (_session.ActiveTool == tool)
            {
                return;
            }

            _session.ActiveTool = tool;

            // A preview must never outlive its own tool: a cut plane left hanging would be armed
            // and invisible the next time Loop Cut came back.
            _session.PreviewCutPlane = null;
        }

        Changed();
    }

    public void SetColorIndex(byte index)
    {
        lock (_sceneGate)
        {
            _session.ActiveColorIndex = index;
        }

        Changed();
    }

    public void Undo()
    {
        lock (_sceneGate)
        {
            _session.Undo();
        }

        Changed();
    }

    public void Redo()
    {
        lock (_sceneGate)
        {
            _session.Redo();
        }

        Changed();
    }

    private void Changed()
    {
        RequestRender();
        Post(() => SessionChanged?.Invoke());
    }

    private void FrameScene()
    {
        if (_session.Scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            _camera.Frame(min, max);
        }
        else
        {
            // An empty level still needs somewhere to stand, or the camera sits on the origin
            // looking at nothing.
            _camera.Frame(Vector3.Zero, new Vector3(16f));
        }
    }

    // Qualified: Android.Opengl declares an EGLConfig of its own, and the one the renderer interface
    // asks for is the Khronos type.
    public void OnSurfaceCreated(IGL10? gl, Javax.Microedition.Khronos.Egl.EGLConfig? config)
    {
        try
        {
            _renderer?.Dispose();
            _renderer = new MobileRenderer();

            lock (_sceneGate)
            {
                // The surface can come back after a pause with every buffer gone, so the scene is
                // marked dirty from scratch rather than assumed to still be uploaded.
                _session.Scene.MarkAllDirty();
            }

            SetReport(string.Join(
                '\n',
                $"{GLES30.GlGetString(GLES30.GlRenderer)}",
                $"{GLES30.GlGetString(GLES30.GlVersion)}"));
        }
        catch (Exception error)
        {
            // A shader that will not compile is the most likely way this fails on an unfamiliar
            // driver, and a crash would take the message down with it. Put it on the screen instead.
            _renderer = null;
            SetReport($"GL setup failed.\n\n{error.Message}");
        }
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _viewportSize = new Vector2(MathF.Max(width, 1), MathF.Max(height, 1));
        GLES30.GlViewport(0, 0, width, height);
        RequestRender();
    }

    public void OnDrawFrame(IGL10? gl)
    {
        if (_renderer is null)
        {
            GLES30.GlClearColor(0.15f, 0.16f, 0.19f, 1f);
            GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlDepthBufferBit);
            return;
        }

        lock (_sceneGate)
        {
            if (_dropBuffers)
            {
                _renderer.ResetBuffers();
                _session.Scene.MarkAllDirty();
                _dropBuffers = false;
            }

            _renderer.SyncDirtyChunks(_session.Scene);
            BuildOverlays();
            _renderer.Render(_session.Scene, _camera.Camera, _viewportSize);

            // A large edit is remeshed over several frames on a budget, so the queue has to ask for
            // the next one itself or the level would stop half built until something else moved.
            if (_renderer.PendingChunks > 0)
            {
                RequestRender();
            }
        }
    }

    /// <summary>
    /// The same overlays the desktop draws, from the same code. Depth-tested things go in
    /// <c>Lines</c>; things that must never be swallowed by the model go in <c>GizmoLines</c>,
    /// which is drawn on a cleared depth buffer.
    /// </summary>
    private void BuildOverlays()
    {
        LineGeometry lines = _renderer!.Lines;
        LineGeometry gizmos = _renderer.GizmoLines;

        lines.Clear();
        gizmos.Clear();
        lines.CameraPosition = _camera.Camera.Position;
        gizmos.CameraPosition = _camera.Camera.Position;

        lines.AddGroundGrid(
            GroundGrid.HalfExtentCells,
            GroundGrid.Spacing(_session.Scene.VoxelSize),
            EditorOverlays.GridMinor,
            EditorOverlays.GridMajor);

        // Everything from here to the reset is expressed in the focused object's own space.
        Matrix4x4 focusMatrix = _session.Scene.Focus?.Transform.ToMatrix() ?? Matrix4x4.Identity;
        lines.Transform = focusMatrix;
        gizmos.Transform = focusMatrix;

        if (_session.ActiveTool == EditorTool.Extrude)
        {
            EditorOverlays.AddSelectionOutline(lines, _session.Selection, EditorOverlays.Selection);
        }

        // The cut plane runs through the middle of the model, so depth testing would hide it.
        EditorOverlays.AddCutPreview(gizmos, _session);

        lines.Transform = Matrix4x4.Identity;
        gizmos.Transform = Matrix4x4.Identity;

        EditorOverlays.AddExtrudeArrow(gizmos, _session, _tools.Extrude);
        EditorOverlays.AddTransformGizmo(gizmos, _session, _tools.Transform, _camera.Camera);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        TouchGesture gesture = _gestures.Consume(e);
        bool changed = false;

        lock (_sceneGate)
        {
            switch (gesture.Kind)
            {
                case TouchGestureKind.Began:
                    _tools.Press(gesture.Position, _viewportSize, _camera.Camera);
                    break;

                case TouchGestureKind.Orbit:
                    if (_tools.IsCapturing)
                    {
                        _tools.Drag(gesture.Position, _viewportSize, _camera.Camera);
                        changed = true;
                    }
                    else
                    {
                        _camera.Orbit(gesture.Delta);
                    }

                    break;

                case TouchGestureKind.PanAndZoom:
                    _camera.Pan(gesture.Delta, _viewportSize);
                    _camera.Zoom(gesture.Scale);
                    break;

                case TouchGestureKind.Ended:
                    changed = _tools.Release(
                        gesture.Position, _viewportSize, _camera.Camera, gesture.WasTap);
                    break;

                case TouchGestureKind.None:
                    return true;    // a finger landing or leaving moved nothing
            }
        }

        if (changed)
        {
            Changed();
        }
        else
        {
            RequestRender();
        }

        return true;
    }

    private void SetReport(string report)
    {
        lock (_reportGate)
        {
            _report = report;
        }

        Post(() => ReportChanged?.Invoke());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer?.Dispose();
            _renderer = null;
        }

        base.Dispose(disposing);
    }
}
