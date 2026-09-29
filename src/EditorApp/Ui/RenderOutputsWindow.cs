using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace EditorApp.Ui;

/// <summary>What a turntable is saved as.</summary>
public enum TurntableFormat
{
    Gif,
    PngSequence,
    Mp4,
}

/// <summary>
/// Render outputs past a single picture (Fullreleaseplan 5.5): a turntable — the level turning in
/// front of the render camera — as a GIF, a PNG sequence or, where ffmpeg is on the PATH, an MP4; a
/// sheet of sprites from all round it; and a picture from every camera at once. Each renders frame
/// after frame on the level's engine while the editor goes on working.
/// </summary>
public sealed class RenderOutputsWindow(GL gl) : IDisposable
{
    private static readonly FileBrowserDialog Browser = new();

    private bool _open;
    private RenderBatch? _batch;
    private string _running = string.Empty;
    private Action<RenderBatch>? _whenDone;
    private Task? _saving;

    private int _frames = 36;
    private int _framesPerSecond = 24;
    private TurntableFormat _format = TurntableFormat.Gif;
    private int _turntableScale = 50;
    private int _turntableSamples = 16;

    private int _angles = 8;
    private int _columns = 8;
    private int _spriteWidth = 128;
    private int _spriteHeight = 128;
    private int _spriteSamples = 16;
    private bool _spriteClear = true;

    private string? _ffmpeg;
    private bool _ffmpegLooked;

    public bool IsOpen => _open;

    public static void DrawDialogs() => Browser.Draw();

    public void Open() => _open = true;

    /// <summary>Called every frame: renders on, and draws the window when it is open.</summary>
    public void Draw(EditorSession session, Func<RenderCamera> camera)
    {
        if (_batch is { } batch)
        {
            batch.Pump(10);
            if (batch.IsFinished)
            {
                _batch = null;
                Action<RenderBatch>? done = _whenDone;
                _whenDone = null;
                _saving = Task.Run(() => done?.Invoke(batch));
            }
        }

        if (!_open)
        {
            return;
        }

        if (!_ffmpegLooked)
        {
            _ffmpegLooked = true;
            _ffmpeg = RenderOutputs.FindFfmpeg();
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 24f, 0f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Render Outputs", ref _open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        string from = session.Scene.ActiveCamera is { } active ? active.Name : "the view";
        ImGui.TextDisabled($"Seen from {from}, on the {(session.Scene.RenderSettings.Engine == RenderEngine.Gpu ? "GPU" : "CPU")}.");

        if (_batch is { } running)
        {
            DrawProgress(running);
        }
        else if (_saving is { IsCompleted: false })
        {
            ImGui.TextDisabled("Saving...");
        }

        ImGui.BeginDisabled(_batch is not null || _saving is { IsCompleted: false });
        DrawTurntable(session, camera);
        DrawSpriteSheet(session, camera);
        DrawCameras(session);
        ImGui.EndDisabled();

        ImGui.End();
    }

    private void DrawProgress(RenderBatch batch)
    {
        ImGui.ProgressBar(batch.Progress, new Vector2(-1f, 0f), $"{_running}: {batch.Done} of {batch.Count}");
        if (batch.Fallback is { } why)
        {
            ImGui.TextColored(Theme.Highlight, $"Rendering on the CPU: {why}.");
        }

        if (ImGui.Button("Cancel"))
        {
            batch.Cancel();
            _batch = null;
            _whenDone = null;
            ReportLog.Shared.Post($"{_running} cancelled.");
        }
    }

    private void DrawTurntable(EditorSession session, Func<RenderCamera> camera)
    {
        if (!Props.Section("Turntable"))
        {
            return;
        }

        Props.Int("Frames", "turntable-frames", ref _frames, 0.2f, 2, 720, "%d");
        Props.Int("Per second", "turntable-fps", ref _framesPerSecond, 0.1f, 1, 60, "%d");
        Props.Int("Samples", "turntable-samples", ref _turntableSamples, 0.2f, 1, 4096, "%d");
        Props.Int("Size", "turntable-scale", ref _turntableScale, 0.5f, 5, 100, "%d%%");

        RenderSettings settings = TurntableSettings(session);
        Props.Value(string.Empty, $"{settings.Width} x {settings.Height} px, {_frames / (float)_framesPerSecond:0.0} s");

        int format = Props.Choice("Save as", "turntable-format", [(null, "GIF"), (null, "PNGs"), (null, "MP4")], (int)_format);
        _format = (TurntableFormat)format;
        if (_format == TurntableFormat.Mp4 && _ffmpeg is null)
        {
            ImGui.TextColored(Theme.Highlight, "MP4 needs ffmpeg on the PATH; it was not found.");
        }

        ImGui.BeginDisabled(_format == TurntableFormat.Mp4 && _ffmpeg is null);
        if (Props.Buttons(string.Empty, "turntable-start", "Render Turntable...") == 0)
        {
            string extension = _format switch
            {
                TurntableFormat.Gif => ".gif",
                TurntableFormat.Mp4 => ".mp4",
                _ => ".png",
            };

            Browser.Show(FileBrowserMode.Save, "Save the turntable", extension, null, session.ProjectName, path =>
            {
                RenderCamera[] frames = RenderOutputs.Orbit(camera(), RenderOutputs.LevelCentre(session.Scene), _frames);
                Start(session, "Turntable", frames, settings, batch => SaveTurntable(batch, path));
            });
        }

        ImGui.EndDisabled();
    }

    private RenderSettings TurntableSettings(EditorSession session)
    {
        RenderSettings settings = session.Scene.RenderSettings.Clamped();
        int width = Math.Max(settings.Width * _turntableScale / 100, 16);
        int height = Math.Max(settings.Height * _turntableScale / 100, 16);
        return settings with { Width = width, Height = height, Samples = _turntableSamples };
    }

    private void SaveTurntable(RenderBatch batch, string path)
    {
        int width = batch.Settings.Width;
        int height = batch.Settings.Height;
        try
        {
            switch (_format)
            {
                case TurntableFormat.Gif:
                    using (FileStream stream = File.Create(path))
                    {
                        GifWriter.Write(stream, batch.Pictures, width, height, Math.Max(100 / _framesPerSecond, 2));
                    }

                    break;

                case TurntableFormat.Mp4:
                    if (RenderOutputs.WriteMp4(_ffmpeg!, path, batch.Pictures, width, height, _framesPerSecond) is { } problem)
                    {
                        ReportLog.Shared.Post($"Could not make {Path.GetFileName(path)}: {problem}", ReportKind.Error);
                        return;
                    }

                    break;

                default:
                    string stem = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path));
                    for (int i = 0; i < batch.Pictures.Count; i++)
                    {
                        PngWriter.WriteRgba($"{stem}-{i + 1:0000}.png", batch.Pictures[i], width, height);
                    }

                    break;
            }

            ReportLog.Shared.Post($"Saved the turntable to {Path.GetFileName(path)}{(_format == TurntableFormat.PngSequence ? $" and {batch.Pictures.Count - 1} more" : string.Empty)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportLog.Shared.Post($"Could not save {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
        }
    }

    private void DrawSpriteSheet(EditorSession session, Func<RenderCamera> camera)
    {
        if (!Props.Section("Sprite Sheet", openByDefault: false))
        {
            return;
        }

        int angles = _angles;
        if (Props.Int("Angles", "sprite-angles", ref angles, 0.1f, 1, 64, "%d"))
        {
            _columns = _columns == _angles ? angles : _columns;
            _angles = angles;
        }

        Props.Int("Across", "sprite-columns", ref _columns, 0.1f, 1, 64, "%d");
        Props.Int("Width", "sprite-width", ref _spriteWidth, 1f, 8, 2048, "%d px");
        Props.Int("Height", "sprite-height", ref _spriteHeight, 1f, 8, 2048, "%d px");
        Props.Int("Samples", "sprite-samples", ref _spriteSamples, 0.2f, 1, 4096, "%d");
        Props.Check(string.Empty, "sprite-clear", "See-through background", ref _spriteClear);

        if (Props.Buttons(string.Empty, "sprite-start", "Render Sprite Sheet...") == 0)
        {
            Browser.Show(FileBrowserMode.Save, "Save the sprite sheet", ".png", null, session.ProjectName, path =>
            {
                RenderSettings settings = session.Scene.RenderSettings.Clamped() with
                {
                    Width = _spriteWidth,
                    Height = _spriteHeight,
                    Samples = _spriteSamples,
                    TransparentBackground = _spriteClear || session.Scene.RenderSettings.TransparentBackground,
                };

                RenderCamera[] frames = RenderOutputs.Orbit(camera(), RenderOutputs.LevelCentre(session.Scene), _angles);
                int columns = _columns;
                Start(session, "Sprite sheet", frames, settings, batch =>
                {
                    (byte[] sheet, int width, int height) = RenderOutputs.Sheet(batch.Pictures, batch.Settings.Width, batch.Settings.Height, columns);
                    Save(path, sheet, width, height, "the sprite sheet");
                });
            });
        }
    }

    private void DrawCameras(EditorSession session)
    {
        if (!Props.Section("Every Camera", openByDefault: false))
        {
            return;
        }

        IReadOnlyList<SceneCamera> cameras = session.Scene.Cameras;
        Props.Value(string.Empty, cameras.Count == 0 ? "The level has no cameras." : $"{cameras.Count} camera{(cameras.Count == 1 ? string.Empty : "s")}, one picture each.");

        ImGui.BeginDisabled(cameras.Count == 0);
        if (Props.Buttons(string.Empty, "cameras-start", "Render Every Camera...") == 0)
        {
            Browser.Show(FileBrowserMode.Save, "Name the pictures: each camera's name is added", ".png", null, session.ProjectName, path =>
            {
                SceneCamera[] shot = [.. session.Scene.Cameras];
                Start(session, "Cameras", [.. shot.Select(c => c.ToRenderCamera())], session.Scene.RenderSettings, batch =>
                {
                    string stem = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path));
                    for (int i = 0; i < shot.Length; i++)
                    {
                        Save($"{stem}-{Safe(shot[i].Name)}.png", batch.Pictures[i], batch.Settings.Width, batch.Settings.Height, shot[i].Name);
                    }
                });
            });
        }

        ImGui.EndDisabled();
    }

    private static string Safe(string name) => Core.Project.FileNames.Safe(name);

    private void Start(EditorSession session, string name, IReadOnlyList<RenderCamera> cameras, RenderSettings settings, Action<RenderBatch> whenDone)
    {
        _batch?.Cancel();
        _batch = new RenderBatch(gl, RenderScene.Capture(session.Scene), cameras, settings);
        _running = name;
        _whenDone = whenDone;
    }

    private static void Save(string path, byte[] rgba, int width, int height, string what)
    {
        try
        {
            PngWriter.WriteRgba(path, rgba, width, height);
            ReportLog.Shared.Post($"Saved {what} to {Path.GetFileName(path)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportLog.Shared.Post($"Could not save {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
        }
    }

    public void Dispose()
    {
        _batch?.Cancel();
        _saving?.Wait(TimeSpan.FromSeconds(10));
    }
}
