using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Rendering;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// Blender's Rendered shading (Fullreleaseplan 5.3): the path tracer running under the viewport at
/// a third of its resolution, a sample at a time, clearing while the view is still and starting
/// again as soon as the camera, the level or the render settings change. The picture takes the
/// place of the voxels; gizmos and overlays still draw over it.
/// </summary>
public sealed class ViewportRender(GL gl) : IDisposable
{
    /// <summary>How much smaller than the viewport it renders, for a picture that comes quickly.</summary>
    private const int Downscale = 3;

    /// <summary>A change that comes faster than this waits: a drag would otherwise restart it every frame.</summary>
    private const double RestartSeconds = 0.08;

    private readonly object _gate = new();
    private PathTracer? _tracer;
    private Task? _task;
    private CancellationTokenSource? _cancel;
    private byte[]? _pending;
    private int _pendingWidth;
    private int _pendingHeight;
    private (RenderCamera Camera, long Revision, int Width, int Height, RenderSettings Settings)? _key;
    private double _restartedAt = double.NegativeInfinity;

    /// <summary>The picture so far, on the GPU; 0 before there is one.</summary>
    public uint Texture { get; private set; }

    public int SamplesDone => _tracer?.SamplesDone ?? 0;

    /// <summary>Keeps the render going for this frame: started again when what it shows has changed, and its newest picture uploaded.</summary>
    public void Update(EditorSession session, RenderCamera camera, Vector2 viewportPixels, double clock)
    {
        UploadPending();

        int width = Math.Max((int)viewportPixels.X / Downscale, 16);
        int height = Math.Max((int)viewportPixels.Y / Downscale, 16);
        RenderSettings settings = session.Scene.RenderSettings;
        var key = (camera, session.Revision, width, height, settings);

        if (_key == key || clock - _restartedAt < RestartSeconds)
        {
            return;
        }

        _key = key;
        _restartedAt = clock;
        Restart(session, camera, settings with { Width = width, Height = height, Samples = 4096, TransparentBackground = false });
    }

    private void Restart(EditorSession session, RenderCamera camera, RenderSettings settings)
    {
        Stop();

        var tracer = new PathTracer(RenderScene.Capture(session.Scene), camera, settings);
        var cancel = new CancellationTokenSource();
        _tracer = tracer;
        _cancel = cancel;

        _task = Task.Run(() =>
        {
            try
            {
                while (!tracer.IsFinished && !cancel.IsCancellationRequested)
                {
                    tracer.AddSample(cancel.Token);
                    byte[] image = tracer.ToRgba();
                    lock (_gate)
                    {
                        _pending = image;
                        _pendingWidth = tracer.Width;
                        _pendingHeight = tracer.Height;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>Stops rendering — the view left Rendered shading. The last picture is kept for when it comes back.</summary>
    public void Stop()
    {
        if (_cancel is null)
        {
            return;
        }

        _cancel.Cancel();
        _task?.Wait(TimeSpan.FromSeconds(2));
        _cancel.Dispose();
        _cancel = null;
        _key = null;
    }

    private unsafe void UploadPending()
    {
        byte[]? image;
        int width;
        int height;
        lock (_gate)
        {
            image = _pending;
            width = _pendingWidth;
            height = _pendingHeight;
            _pending = null;
        }

        if (image is null)
        {
            return;
        }

        if (Texture == 0)
        {
            Texture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, Texture);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        gl.BindTexture(TextureTarget.Texture2D, Texture);
        fixed (byte* data = image)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        Stop();
        if (Texture != 0)
        {
            gl.DeleteTexture(Texture);
            Texture = 0;
        }
    }
}
