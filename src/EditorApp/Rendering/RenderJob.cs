using System.Diagnostics;
using EditorApp.Core.Rendering;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// A render under way, on whichever engine the level's settings choose — the Render window's and
/// Rendered shading's alike. The CPU's runs on its own threads; the GPU's a little every frame, on
/// the GL thread, in <see cref="Pump"/>. Either way its newest picture is taken when there is one.
/// </summary>
public abstract class RenderJob : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _pending;
    private byte[]? _latest;

    protected RenderJob(RenderSettings settings, TimeSpan showEvery)
    {
        Settings = settings;
        ShowEvery = showEvery;
    }

    public RenderSettings Settings { get; }

    public abstract int Width { get; }

    public abstract int Height { get; }

    public abstract int SamplesDone { get; }

    public bool IsFinished => SamplesDone >= Settings.Samples;

    public abstract bool IsRunning { get; }

    /// <summary>The engine rendering it — the CPU's when the GPU was asked for and could not.</summary>
    public abstract RenderEngine Engine { get; }

    /// <summary>Why the GPU was asked for and the CPU is rendering; null when it is not so.</summary>
    public string? Fallback { get; private init; }

    /// <summary>How often a new picture is made: developing one costs a little, so not after every sample.</summary>
    protected TimeSpan ShowEvery { get; }

    /// <summary>The newest picture there is, taken or not; null before the first.</summary>
    public byte[]? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public static RenderJob Start(GL gl, RenderScene scene, RenderCamera camera, RenderSettings settings, TimeSpan showEvery)
    {
        settings = settings.Clamped();
        if (settings.Engine != RenderEngine.Gpu)
        {
            return new CpuRenderJob(scene, camera, settings, showEvery);
        }

        GpuPathTracer? gpu = GpuPathTracer.TryCreate(gl, scene, camera, settings, out string? why);
        return gpu is not null
            ? new GpuRenderJob(gpu, settings, showEvery)
            : new CpuRenderJob(scene, camera, settings, showEvery) { Fallback = why };
    }

    /// <summary>Called once a frame on the GL thread: where the GPU does its share.</summary>
    public virtual void Pump(double budgetMilliseconds)
    {
    }

    /// <summary>The newest picture since the last one taken; null when there is none newer.</summary>
    public byte[]? TakePicture()
    {
        lock (_gate)
        {
            byte[]? picture = _pending;
            _pending = null;
            return picture;
        }
    }

    /// <summary>Stops rendering, keeping the picture as far as it got.</summary>
    public abstract void Stop();

    public virtual void Dispose() => Stop();

    protected void Publish(byte[] picture)
    {
        lock (_gate)
        {
            _pending = picture;
            _latest = picture;
        }
    }
}

/// <summary>The CPU's render: <see cref="PathTracer"/> on its own task, a sample over every core at a time.</summary>
internal sealed class CpuRenderJob : RenderJob
{
    private readonly PathTracer _tracer;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _task;

    public CpuRenderJob(RenderScene scene, RenderCamera camera, RenderSettings settings, TimeSpan showEvery)
        : base(settings, showEvery)
    {
        _tracer = new PathTracer(scene, camera, settings);
        PathTracer tracer = _tracer;
        CancellationToken cancel = _cancel.Token;

        _task = Task.Run(() =>
        {
            var sinceShown = Stopwatch.StartNew();
            try
            {
                while (!tracer.IsFinished && !cancel.IsCancellationRequested)
                {
                    tracer.AddSample(cancel);
                    if (sinceShown.Elapsed >= ShowEvery || tracer.IsFinished)
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
        });
    }

    public override int Width => _tracer.Width;

    public override int Height => _tracer.Height;

    public override int SamplesDone => _tracer.SamplesDone;

    public override bool IsRunning => !_task.IsCompleted;

    public override RenderEngine Engine => RenderEngine.Cpu;

    public override void Stop()
    {
        if (_task.IsCompleted)
        {
            return;
        }

        _cancel.Cancel();
        _task.Wait(TimeSpan.FromSeconds(5));
    }
}

/// <summary>
/// The GPU's render: <see cref="GpuPathTracer"/> given a slice of every frame, its sum read back
/// now and then and developed off the GL thread.
/// </summary>
internal sealed class GpuRenderJob(GpuPathTracer tracer, RenderSettings settings, TimeSpan showEvery) : RenderJob(settings, showEvery)
{
    private readonly Stopwatch _sinceShown = Stopwatch.StartNew();
    private Task? _developing;
    private bool _stopped;
    private bool _shownLast;

    public override int Width => tracer.Width;

    public override int Height => tracer.Height;

    public override int SamplesDone => tracer.SamplesDone;

    public override bool IsRunning => !_stopped && (!tracer.IsFinished || !_shownLast);

    public override RenderEngine Engine => RenderEngine.Gpu;

    public override void Pump(double budgetMilliseconds)
    {
        if (_stopped || _shownLast)
        {
            return;
        }

        tracer.Step(budgetMilliseconds);

        bool finished = tracer.IsFinished;
        if ((_sinceShown.Elapsed >= ShowEvery || finished) && _developing is not { IsCompleted: false })
        {
            Develop();
            _shownLast = finished;
        }
    }

    private void Develop()
    {
        int samples = tracer.SamplesDone;
        if (samples == 0)
        {
            return;
        }

        var sum = tracer.ReadSum();
        int width = tracer.Width;
        int height = tracer.Height;
        _developing = Task.Run(() => Publish(PathTracer.Develop(sum, width, height, samples, Settings)));
        _sinceShown.Restart();
    }

    public override void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _developing?.Wait(TimeSpan.FromSeconds(5));
        if (!_shownLast)
        {
            Develop();
            _developing?.Wait(TimeSpan.FromSeconds(5));
        }
    }

    public override void Dispose()
    {
        Stop();
        tracer.Dispose();
    }
}
