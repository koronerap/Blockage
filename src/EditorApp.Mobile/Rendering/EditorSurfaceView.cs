using System.Numerics;
using Android.Content;
using Android.Opengl;
using EditorApp.Core.Voxels;
using Javax.Microedition.Khronos.Opengles;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// The GL surface the editor draws into, and the renderer that draws it.
///
/// Every method below the constructor runs on the GL thread, not the UI thread. Anything that has
/// to reach a widget goes back through <see cref="Android.Views.View.Post(Java.Lang.IRunnable)"/>;
/// anything that reads editor state will need a lock once there is editor state to read.
/// </summary>
public sealed class EditorSurfaceView : GLSurfaceView, GLSurfaceView.IRenderer
{
    /// <summary>The gradient's lower colour, matching the desktop editor exactly.</summary>
    private static readonly Color32 BackgroundColor = new(38, 42, 48);

    /// <summary>The gradient's upper colour. Kept close to the lower one — this is a backdrop.</summary>
    private static readonly Color32 BackgroundTopColor = new(52, 56, 62);

    private GlProgram? _backgroundShader;

    /// <summary>
    /// What the GL thread found when it came up: the driver's own identification, or the reason
    /// nothing is on screen. Read by the UI thread, written by the GL thread, so it goes through a
    /// lock rather than being handed over on the assumption that a reference write is enough.
    /// </summary>
    private readonly Lock _reportGate = new();
    private string _report = "Waiting for the GL thread…";

    public EditorSurfaceView(Context context)
        : base(context)
    {
        SetEGLContextClientVersion(3);

        // Losing the context on every pause would mean rebuilding every chunk buffer on resume.
        // The flag is a request, not a guarantee, so the renderer still has to cope with a
        // surface that comes back empty.
        PreserveEGLContextOnPause = true;

        SetRenderer(this);
    }

    /// <summary>Raised on the UI thread whenever <see cref="Report"/> changes.</summary>
    public event Action? ReportChanged;

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

    // Qualified: Android.Opengl declares an EGLConfig of its own, and the one the renderer
    // interface asks for is the Khronos type.
    public void OnSurfaceCreated(IGL10? gl, Javax.Microedition.Khronos.Egl.EGLConfig? config)
    {
        try
        {
            _backgroundShader?.Dispose();
            _backgroundShader = new GlProgram(EsShaders.BackgroundVertex, EsShaders.BackgroundFragment);

            SetReport(string.Join(
                '\n',
                "GL ready.",
                $"Vendor:   {GLES30.GlGetString(GLES30.GlVendor)}",
                $"Renderer: {GLES30.GlGetString(GLES30.GlRenderer)}",
                $"Version:  {GLES30.GlGetString(GLES30.GlVersion)}",
                $"GLSL:     {GLES30.GlGetString(GLES30.GlShadingLanguageVersion)}",
                "Background shader compiled and linked."));
        }
        catch (Exception error)
        {
            // A shader that will not compile is the single most likely way this milestone fails,
            // and a crash would take the message down with it. Put it on the screen instead.
            _backgroundShader = null;
            SetReport($"GL setup failed.\n\n{error.Message}");
        }
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        GLES30.GlViewport(0, 0, width, height);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        Vector4 clear = BackgroundColor.ToVector4();
        GLES30.GlClearColor(clear.X, clear.Y, clear.Z, 1f);
        GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlDepthBufferBit);

        if (_backgroundShader is null)
        {
            return;
        }

        // Three vertices built from gl_VertexID alone — no buffer, no attributes. ES 3.0 allows
        // drawing from the default vertex array object, so there is nothing to bind either.
        _backgroundShader.Use();
        _backgroundShader.SetVector3("uTop", ToRgb(BackgroundTopColor));
        _backgroundShader.SetVector3("uBottom", ToRgb(BackgroundColor));

        GLES30.GlDisable(GLES30.GlDepthTest);
        GLES30.GlDepthMask(false);
        GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 3);
        GLES30.GlDepthMask(true);
        GLES30.GlEnable(GLES30.GlDepthTest);
    }

    private static Vector3 ToRgb(Color32 color)
    {
        Vector4 value = color.ToVector4();
        return new Vector3(value.X, value.Y, value.Z);
    }

    private void SetReport(string report)
    {
        lock (_reportGate)
        {
            _report = report;
        }

        Post(() => ReportChanged?.Invoke());
    }
}
