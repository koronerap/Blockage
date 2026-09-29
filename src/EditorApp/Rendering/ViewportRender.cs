using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;
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

    /// <summary>How long of each frame the GPU engine may render for, in milliseconds.</summary>
    private const double GpuBudget = 6;

    private RenderJob? _job;
    private (RenderCamera Camera, VoxelScene Scene, long Revision, int Width, int Height, RenderSettings Settings)? _key;
    private double _restartedAt = double.NegativeInfinity;

    /// <summary>The picture so far, on the GPU; 0 before there is one.</summary>
    public uint Texture { get; private set; }

    public int SamplesDone => _job?.SamplesDone ?? 0;

    /// <summary>Why the GPU was asked for and the CPU is rendering; null when it is not so.</summary>
    public string? Fallback => _job?.Fallback;

    /// <summary>Keeps the render going for this frame: started again when what it shows has changed, and its newest picture uploaded.</summary>
    public void Update(EditorSession session, RenderCamera camera, Vector2 viewportPixels, double clock)
    {
        if (_job is { } job)
        {
            job.Pump(GpuBudget);
            UploadPending(job);
        }

        int width = Math.Max((int)viewportPixels.X / Downscale, 16);
        int height = Math.Max((int)viewportPixels.Y / Downscale, 16);
        RenderSettings settings = session.Scene.RenderSettings;
        // The scene as well as its revision: another level's, brought to the front, can be at the same one.
        var key = (camera, session.Scene, session.Revision, width, height, settings);

        if (_key == key || clock - _restartedAt < RestartSeconds)
        {
            return;
        }

        _key = key;
        _restartedAt = clock;
        _job?.Dispose();

        // Every sample shown as it comes: the picture here is small, and quick to develop.
        _job = RenderJob.Start(
            gl,
            RenderScene.Capture(session.Scene),
            camera,
            settings with { Width = width, Height = height, Samples = 4096, TransparentBackground = false },
            settings.Engine == RenderEngine.Gpu ? TimeSpan.FromMilliseconds(50) : TimeSpan.Zero);
    }

    /// <summary>Stops rendering — the view left Rendered shading. The last picture is kept for when it comes back.</summary>
    public void Stop()
    {
        if (_job is null)
        {
            return;
        }

        _job.Dispose();
        _job = null;
        _key = null;
    }

    private unsafe void UploadPending(RenderJob job)
    {
        byte[]? image = job.TakePicture();
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
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)job.Width, (uint)job.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
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
