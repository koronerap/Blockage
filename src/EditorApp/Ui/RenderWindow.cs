using System.Diagnostics;
using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Rendering;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace EditorApp.Ui;

/// <summary>
/// Blender's F12 (Fullreleaseplan 5.2): the level rendered from the view, sample by sample in the
/// background while the editor goes on working, shown as it clears, and saved as a PNG. The settings
/// under it are the level's own, saved with it.
/// </summary>
public sealed class RenderWindow(GL gl) : IDisposable
{
    private static readonly FileBrowserDialog Browser = new();

    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();

    private bool _open;
    private PathTracer? _tracer;
    private Task? _task;
    private CancellationTokenSource? _cancel;

    /// <summary>The newest image the render has made, waiting to go to the GPU; null once it has.</summary>
    private byte[]? _pending;

    /// <summary>The newest image, kept to be saved.</summary>
    private byte[]? _latest;
    private int _width;
    private int _height;
    private uint _texture;
    private int _textureWidth;
    private int _textureHeight;

    public bool IsOpen => _open;

    public bool IsRendering => _task is { IsCompleted: false };

    public static void DrawDialogs() => Browser.Draw();

    /// <summary>Renders the level as it is now, from <paramref name="camera"/>, and shows the window.</summary>
    public void Start(EditorSession session, RenderCamera camera)
    {
        Stop();
        _open = true;

        RenderSettings settings = session.Scene.RenderSettings.Clamped();
        var tracer = new PathTracer(RenderScene.Capture(session.Scene), camera, settings);
        var cancel = new CancellationTokenSource();
        _tracer = tracer;
        _cancel = cancel;
        _width = tracer.Width;
        _height = tracer.Height;
        _clock.Restart();

        _task = Task.Run(() =>
        {
            var sinceShown = Stopwatch.StartNew();
            try
            {
                while (!tracer.IsFinished && !cancel.IsCancellationRequested)
                {
                    tracer.AddSample(cancel.Token);
                    if (sinceShown.ElapsedMilliseconds > 250 || tracer.IsFinished)
                    {
                        Publish(tracer.ToRgba());
                        sinceShown.Restart();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }

            // What was drawn when it stopped is kept, however far it got.
            Publish(tracer.ToRgba());
            _clock.Stop();
        });
    }

    public void Stop()
    {
        _cancel?.Cancel();
        _task?.Wait(TimeSpan.FromSeconds(5));
        _cancel?.Dispose();
        _cancel = null;
    }

    private void Publish(byte[] image)
    {
        lock (_gate)
        {
            _pending = image;
            _latest = image;
        }
    }

    public void Draw(EditorSession session, Func<RenderCamera> camera)
    {
        UploadPending();

        if (!_open)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 44f, ImGui.GetFontSize() * 34f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Render", ref _open))
        {
            ImGui.End();
            return;
        }

        if (!_open)
        {
            Stop();
        }

        if (IsRendering)
        {
            if (ImGui.Button("Stop"))
            {
                Stop();
            }
        }
        else if (ImGui.Button("Render Again"))
        {
            Start(session, camera());
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(_latest is null);
        if (ImGui.Button("Save PNG..."))
        {
            Browser.Show(FileBrowserMode.Save, "Save the render", ".png", null, session.ProjectName, Save);
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (_tracer is { } tracer)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled($"{tracer.SamplesDone} of {session.Scene.RenderSettings.Clamped().Samples} samples  ·  {_clock.Elapsed.TotalSeconds:0.0} s  ·  {_width} x {_height}");
        }

        DrawSettings(session);

        // The picture, as large as the window lets it be, keeping its shape.
        if (_texture != 0 && _textureWidth > 0)
        {
            Vector2 room = ImGui.GetContentRegionAvail();
            float scale = MathF.Min(room.X / _textureWidth, room.Y / _textureHeight);
            if (scale > 0f)
            {
                ImGui.Image((IntPtr)_texture, new Vector2(_textureWidth, _textureHeight) * scale);
            }
        }

        ImGui.End();
    }

    private void Save(string path)
    {
        byte[]? image;
        lock (_gate)
        {
            image = _latest;
        }

        if (image is null)
        {
            return;
        }

        try
        {
            PngWriter.WriteRgba(path, image, _width, _height);
            ReportLog.Shared.Post($"Saved the render to {Path.GetFileName(path)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportLog.Shared.Post($"Could not save {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
        }
    }

    /// <summary>The level's render settings, folded away until wanted. Changing them marks the level changed; they are not undo steps.</summary>
    private static void DrawSettings(EditorSession session)
    {
        if (!ImGui.CollapsingHeader("Settings"))
        {
            return;
        }

        RenderSettings settings = session.Scene.RenderSettings;
        RenderSettings changed = settings;

        int width = settings.Width;
        int height = settings.Height;
        if (Props.Int("Width", "render-width", ref width, 4f, 16, 8192, "%d px"))
        {
            changed = changed with { Width = width };
        }

        if (Props.Int("Height", "render-height", ref height, 4f, 16, 8192, "%d px"))
        {
            changed = changed with { Height = height };
        }

        switch (Props.Buttons(string.Empty, "render-size", "720p", "1080p", "4K", "Square"))
        {
            case 0: changed = changed with { Width = 1280, Height = 720 }; break;
            case 1: changed = changed with { Width = 1920, Height = 1080 }; break;
            case 2: changed = changed with { Width = 3840, Height = 2160 }; break;
            case 3: changed = changed with { Width = 1024, Height = 1024 }; break;
        }

        int samples = settings.Samples;
        if (Props.Int("Samples", "render-samples", ref samples, 1f, 1, 4096, "%d"))
        {
            changed = changed with { Samples = samples };
        }

        int bounces = settings.Bounces;
        if (Props.Int("Bounces", "render-bounces", ref bounces, 0.05f, 0, 16, "%d"))
        {
            changed = changed with { Bounces = bounces };
        }

        int seed = settings.Seed;
        if (Props.Int("Seed", "render-seed", ref seed, 0.2f, 0, 100_000, "%d"))
        {
            changed = changed with { Seed = seed };
        }

        float exposure = settings.Exposure;
        if (Props.Float("Exposure", "render-exposure", ref exposure, 0.02f, -8f, 8f, "%+.2f"))
        {
            changed = changed with { Exposure = exposure };
        }

        float sky = settings.SkyStrength;
        if (Props.Float("Sky", "render-sky", ref sky, 0.01f, 0f, 16f, "%.2f"))
        {
            changed = changed with { SkyStrength = sky };
        }

        Vector3 top = settings.SkyTop;
        Props.Label("Sky overhead");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.ColorEdit3("##render-sky-top", ref top))
        {
            changed = changed with { SkyTop = top };
        }

        Vector3 horizon = settings.SkyHorizon;
        Props.Label("Sky at the horizon");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.ColorEdit3("##render-sky-horizon", ref horizon))
        {
            changed = changed with { SkyHorizon = horizon };
        }

        float glow = settings.EmissionStrength;
        if (Props.Float("Glow", "render-emission", ref glow, 0.05f, 0f, 64f, "%.1f"))
        {
            changed = changed with { EmissionStrength = glow };
        }

        float fog = settings.Fog;
        if (Props.Slider("Fog", "render-fog", ref fog, 0f, 1f, "%.2f"))
        {
            changed = changed with { Fog = fog };
        }

        bool transparent = settings.TransparentBackground;
        if (Props.Check(string.Empty, "render-transparent", "See-through background", ref transparent))
        {
            changed = changed with { TransparentBackground = transparent };
        }

        if (changed != settings)
        {
            session.Scene.RenderSettings = changed.Clamped();
            session.HasUnsavedChanges = true;
        }
    }

    /// <summary>The newest image onto the GPU, made the size it is.</summary>
    private unsafe void UploadPending()
    {
        byte[]? image;
        lock (_gate)
        {
            image = _pending;
            _pending = null;
        }

        if (image is null)
        {
            return;
        }

        if (_texture == 0)
        {
            _texture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _texture);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        gl.BindTexture(TextureTarget.Texture2D, _texture);
        fixed (byte* data = image)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)_width, (uint)_height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);
        _textureWidth = _width;
        _textureHeight = _height;
    }

    public void Dispose()
    {
        Stop();
        if (_texture != 0)
        {
            gl.DeleteTexture(_texture);
        }
    }
}
