using EditorApp.Core.Rendering;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// Renders one after another (Fullreleaseplan 5.5): the frames of a turntable, the angles of a sprite
/// sheet, every camera of the level — each on the level's engine, from the same still copy of the
/// level, the pictures kept in order. Pumped once a frame on the GL thread, as a single render is.
/// </summary>
public sealed class RenderBatch(GL gl, RenderScene scene, IReadOnlyList<RenderCamera> cameras, RenderSettings settings) : IDisposable
{
    private readonly byte[][] _pictures = new byte[cameras.Count][];
    private RenderJob? _job;
    private int _next;

    public int Count => cameras.Count;

    /// <summary>How many pictures are finished.</summary>
    public int Done => _next;

    public bool IsFinished => _next >= cameras.Count;

    public bool IsCancelled { get; private set; }

    public RenderSettings Settings { get; } = settings.Clamped();

    /// <summary>How far through the whole run it is, 0 to 1, the picture under way counted by its samples.</summary>
    public float Progress => Count == 0 ? 1f : (_next + ((_job?.SamplesDone ?? 0) / (float)Settings.Samples)) / Count;

    /// <summary>Why the GPU was asked for and the CPU is rendering; null when it is not so.</summary>
    public string? Fallback { get; private set; }

    /// <summary>The pictures, in the order of the cameras; whole only once <see cref="IsFinished"/>.</summary>
    public IReadOnlyList<byte[]> Pictures => _pictures;

    public void Pump(double budgetMilliseconds)
    {
        if (IsFinished || IsCancelled)
        {
            return;
        }

        // A picture shown only when it is done: nothing looks at the ones in between.
        _job ??= RenderJob.Start(gl, scene, cameras[_next], Settings, TimeSpan.FromDays(1));
        Fallback ??= _job.Fallback;
        _job.Pump(budgetMilliseconds);

        if (_job.IsFinished && !_job.IsRunning && _job.Latest is { } picture)
        {
            _pictures[_next++] = picture;
            _job.Dispose();
            _job = null;
        }
    }

    public void Cancel()
    {
        IsCancelled = true;
        _job?.Dispose();
        _job = null;
    }

    public void Dispose() => Cancel();
}
