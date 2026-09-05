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

    /// <summary>
    /// Owned here rather than by the renderer, which is built and destroyed with the GL context. A
    /// light angle the user chose must not go with it.
    /// </summary>
    private readonly SceneLighting _lighting = new();

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

    /// <summary>Whether the ground grid is drawn. Purely a way of looking, never part of a level.</summary>
    public bool GridVisible { get; private set; } = true;

    /// <summary>Everything the level page shows, taken in one go while the lock is held.</summary>
    public SceneState SceneState
    {
        get
        {
            lock (_sceneGate)
            {
                var objects = new List<ObjectState>(_session.Scene.Objects.Count);

                foreach (VoxelObject o in _session.Scene.Objects)
                {
                    objects.Add(new ObjectState(
                        o.Id, o.Name, o.Visible, o.Id == _session.Scene.FocusId, o.Grid.SolidCount));
                }

                Vector3? extent = null;
                if (_session.Scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
                {
                    extent = (max - min) / _session.Scene.VoxelSize;
                }

                return new SceneState(
                    GridVisible,
                    LightingState.From(_lighting),
                    _session.Scene.VoxelSize,
                    extent,
                    _session.Scene.SolidCount,
                    objects);
            }
        }
    }

    public void SetGridVisible(bool visible)
    {
        GridVisible = visible;
        Changed();
    }

    /// <summary>Changes the light. Held under the lock because the GL thread reads it every frame.</summary>
    public void ConfigureLighting(Action<SceneLighting> change)
    {
        lock (_sceneGate)
        {
            change(_lighting);
        }

        Changed();
    }

    /// <summary>
    /// Hiding an object is a way of looking, not an edit, so it goes around the undo stack — and the
    /// chunks it owns have to be dropped or a hidden object would keep its buffers forever.
    /// </summary>
    public void ToggleObjectVisible(int id)
    {
        lock (_sceneGate)
        {
            foreach (VoxelObject o in _session.Scene.Objects)
            {
                if (o.Id == id)
                {
                    o.Visible = !o.Visible;
                    break;
                }
            }
        }

        Changed();
    }

    /// <summary>
    /// Renames an object. Not an undo step, and deliberately: on the desktop it is not one either,
    /// and a name is not part of what the voxels are.
    /// </summary>
    public void RenameObject(int id, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        lock (_sceneGate)
        {
            foreach (VoxelObject o in _session.Scene.Objects)
            {
                if (o.Id == id)
                {
                    o.Name = trimmed;
                    _session.HasUnsavedChanges = true;
                    break;
                }
            }
        }

        Changed();
    }

    public void FrameLevel()
    {
        lock (_sceneGate)
        {
            FrameScene();
        }

        Changed();
    }

    /// <summary>Everything the options bar needs, taken in one go while the lock is held.</summary>
    public ToolState State
    {
        get
        {
            lock (_sceneGate)
            {
                return new ToolState(
                    _session.ActiveTool,
                    _session.TransformMode,
                    _session.TransformSpace,
                    _session.ExtrudeSelectionMode,
                    _session.ExtrudeCreatesObject,
                    _session.PaintMode,
                    _session.BrushRadius,
                    _session.BucketThreshold,
                    _session.BucketWholeObject,
                    _session.Selection?.Count ?? 0,
                    _session.PreviewCutPlane is not null,
                    _tools.SamplerArmed);
            }
        }
    }

    /// <summary>
    /// Applies a change to the session and tells everyone. One entry point for every option, so a
    /// new one cannot be added without the lock and the redraw coming with it.
    /// </summary>
    public void Configure(Action<EditorSession> change)
    {
        lock (_sceneGate)
        {
            change(_session);
        }

        Changed();
    }

    /// <summary>
    /// Extrudes the current selection by whole steps and commits.
    ///
    /// Dragging the arrow is the gesture, but a fingertip covering the surface it is judging cannot
    /// place it to the voxel. This is the same operation asked for exactly rather than approximately,
    /// and each tap is its own undo step.
    /// </summary>
    public void StepExtrude(int steps)
    {
        lock (_sceneGate)
        {
            if (_session.Selection is not { IsEmpty: false })
            {
                return;
            }

            _session.PreviewExtrude(steps);
            _session.ConfirmExtrude();
        }

        Changed();
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

    /// <summary>
    /// Makes an arbitrary colour the active one, live.
    ///
    /// Not the same thing as recolouring a slot, and the difference is the whole point: if the
    /// palette already holds this colour that entry is chosen, and otherwise it goes into a working
    /// slot that is nobody's swatch until it is saved. Writing it over the active entry instead
    /// would repaint every voxel that shared the index.
    /// </summary>
    public byte PickColor(Color32 colour)
    {
        byte index;

        lock (_sceneGate)
        {
            index = _session.SelectColor(colour);
        }

        Changed();
        return index;
    }

    /// <summary>Keeps the working colour as a swatch, so it survives the next pick.</summary>
    public bool SaveSwatch()
    {
        bool saved;

        lock (_sceneGate)
        {
            saved = _session.SaveActiveColor();
        }

        Changed();
        return saved;
    }

    /// <summary>
    /// Changes what a slot holds, recolouring every voxel already using it. One undo step for the
    /// whole change rather than one per drag of a slider.
    /// </summary>
    public bool RecolourSlot(int index, Color32 colour)
    {
        lock (_sceneGate)
        {
            Color32 before = _session.World.Palette[index];

            if (before == colour)
            {
                return false;
            }

            _session.ApplyPaletteColor(index, colour);
            _session.PushPaletteEdit(index, before, colour);
        }

        Changed();
        return true;
    }

    /// <summary>Gives a saved custom slot back. Only a custom slot can be given back.</summary>
    public bool ForgetSwatch()
    {
        bool cleared;

        lock (_sceneGate)
        {
            cleared = _session.ClearCustomColor(_session.ActiveColorIndex);
        }

        Changed();
        return cleared;
    }

    /// <summary>Everything the palette page shows, taken in one go while the lock is held.</summary>
    public PaletteState PaletteState
    {
        get
        {
            lock (_sceneGate)
            {
                Palette palette = _session.World.Palette;
                byte active = _session.ActiveColorIndex;

                return new PaletteState(
                    palette.Colors.ToArray(),
                    active,
                    [.. palette.SavedCustomSlots()],
                    Palette.IsCustomIndex(active) && palette.IsCustomSaved(active),
                    _session.WorkingSlot == active,
                    palette.FreeCustomSlots);
            }
        }
    }

    /// <summary>Arms the colour sampler for exactly one tap.</summary>
    public void ArmSampler(bool armed)
    {
        lock (_sceneGate)
        {
            _tools.SamplerArmed = armed;
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
            _renderer = new MobileRenderer { Lighting = _lighting };

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

        if (GridVisible)
        {
            lines.AddGroundGrid(
                GroundGrid.HalfExtentCells,
                GroundGrid.Spacing(_session.Scene.VoxelSize),
                EditorOverlays.GridMinor,
                EditorOverlays.GridMajor);
        }

        // Everything from here to the reset is expressed in the focused object's own space.
        Matrix4x4 focusMatrix = _session.Scene.Focus?.Transform.ToMatrix() ?? Matrix4x4.Identity;
        lines.Transform = focusMatrix;
        gizmos.Transform = focusMatrix;

        if (_session.ActiveTool == EditorTool.Extrude)
        {
            EditorOverlays.AddSelectionOutline(lines, _session.Selection, EditorOverlays.Selection);

            // The rectangle being dragged out right now, before it is committed. Without it a box
            // selection is invisible until the finger leaves, which is too late to aim it.
            EditorOverlays.AddSelectionOutline(
                lines,
                _tools.Extrude.PendingSelection,
                _tools.Extrude.PendingOperation == SelectionOperation.Subtract
                    ? EditorOverlays.SelectionSubtract
                    : EditorOverlays.SelectionAdd);
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
                    // Whatever the press landed on is still holding the finger. Only the camera is
                    // left when nothing was.
                    if (_tools.IsCapturing)
                    {
                        _tools.Drag(gesture.Position, _viewportSize, _camera.Camera);
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
