using System.Numerics;
using Android.Content;
using Android.Opengl;
using Android.Views;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using Javax.Microedition.Khronos.Opengles;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// The GL surface the editor draws into, the renderer that draws it, and the fingers that move it.
///
/// Two threads meet here. Everything from <see cref="OnSurfaceCreated"/> down runs on the GL thread;
/// touch events arrive on the UI thread. The session and the camera are read by one and written by
/// the other, so both go through <see cref="_sceneGate"/> — a voxel world being remeshed while an
/// edit is landing in it would be a very hard bug to find later.
/// </summary>
public sealed class EditorSurfaceView : GLSurfaceView, GLSurfaceView.IRenderer
{
    /// <summary>Colour of the ground grid's ordinary lines, matching the desktop.</summary>
    private static readonly Color32 GridMinor = new(70, 76, 84);

    /// <summary>Every tenth line, so the grid can be counted rather than just seen.</summary>
    private static readonly Color32 GridMajor = new(96, 104, 114);

    private readonly Lock _sceneGate = new();
    private readonly EditorSession _session = new();
    private readonly OrbitCamera _camera = new();
    private readonly TouchGestures _gestures = new();

    private MobileRenderer? _renderer;
    private Vector2 _viewportSize = Vector2.One;

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
            _renderer.SyncDirtyChunks(_session.Scene);

            _renderer.Lines.Clear();
            _renderer.Lines.CameraPosition = _camera.Camera.Position;
            _renderer.Lines.AddGroundGrid(
                GroundGrid.HalfExtentCells,
                GroundGrid.Spacing(_session.Scene.VoxelSize),
                GridMinor,
                GridMajor);

            _renderer.Render(_session.Scene, _camera.Camera, _viewportSize);

            // A large edit is remeshed over several frames on a budget, so the queue has to ask for
            // the next one itself or the level would stop half built until something else moved.
            if (_renderer.PendingChunks > 0)
            {
                RequestRender();
            }
        }
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        TouchGesture gesture = _gestures.Consume(e);

        lock (_sceneGate)
        {
            switch (gesture.Kind)
            {
                case TouchGestureKind.Orbit:
                    _camera.Orbit(gesture.Delta);
                    break;

                case TouchGestureKind.PanAndZoom:
                    _camera.Pan(gesture.Delta, _viewportSize);
                    _camera.Zoom(gesture.Scale);
                    break;

                case TouchGestureKind.None:
                    return true;    // a finger landing or leaving moved nothing
            }
        }

        RequestRender();
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
