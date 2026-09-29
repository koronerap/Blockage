using System.Diagnostics;
using System.Numerics;
using EditorApp.Core.Rendering;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// The GPU engine (Fullreleaseplan 5.1): <see cref="PathTraceShader"/> run a band of rows at a time
/// on the GL thread, as many each frame as a time budget allows, so the editor keeps drawing while it
/// renders. The light is summed on the card and read back to be developed as the CPU's is.
///
/// Compute shaders are OpenGL 4.3. The window asks for 3.3, which drivers answer with the newest
/// they have — except macOS, which stops at 4.1; there, and wherever else it cannot run,
/// <see cref="TryCreate"/> says why and the CPU renders instead.
/// </summary>
public sealed class GpuPathTracer : IDisposable
{
    private static uint _program;
    private static GL? _programGl;
    private static string? _programError;
    private static readonly Dictionary<string, int> Locations = [];

    private readonly GL _gl;
    private readonly RenderScene _scene;
    private readonly RenderCamera _camera;
    private readonly RenderSettings _settings;
    private readonly PackedScene _packed;
    private readonly uint[] _buffers;
    private readonly uint _sum;

    /// <summary>The next row of the sample under way.</summary>
    private int _row;

    /// <summary>Rows a dispatch does, grown or shrunk so one takes a fair share of the frame's budget.</summary>
    private int _band = 8;

    private GpuPathTracer(GL gl, RenderScene scene, PackedScene packed, RenderCamera camera, RenderSettings settings)
    {
        _gl = gl;
        _scene = scene;
        _packed = packed;
        _camera = camera;
        _settings = settings;
        Width = settings.Width;
        Height = settings.Height;

        _buffers =
        [
            Upload(packed.Models),
            Upload(packed.ChunkTable),
            Upload(packed.Voxels),
            Upload(packed.ChunkFaces),
            Upload(packed.Faces),
            Upload(packed.Lights),
            Upload(packed.Palette),
        ];

        _sum = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _sum);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        unsafe
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.Float, null);
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public int Width { get; }

    public int Height { get; }

    public int SamplesDone { get; private set; }

    public bool IsFinished => SamplesDone >= _settings.Samples;

    /// <summary>Why this GL cannot run the GPU engine; null when it can.</summary>
    public static string? Unsupported(GL gl)
    {
        int major = gl.GetInteger(GetPName.MajorVersion);
        int minor = gl.GetInteger(GetPName.MinorVersion);
        if (major < 4 || (major == 4 && minor < 3))
        {
            return $"the GPU engine needs OpenGL 4.3, and this computer has {major}.{minor}";
        }

        return _programError;
    }

    /// <summary>A GPU render of <paramref name="scene"/>, or null with the reason it cannot be one.</summary>
    public static GpuPathTracer? TryCreate(GL gl, RenderScene scene, RenderCamera camera, RenderSettings settings, out string? why)
    {
        settings = settings.Clamped();
        why = Unsupported(gl);
        if (why is not null || !EnsureProgram(gl, out why))
        {
            return null;
        }

        PackedScene packed = PackedScene.Pack(scene);
        gl.GetInteger64(GLEnum.MaxShaderStorageBlockSize, out long limit);
        if (limit > 0 && packed.LargestBuffer > limit)
        {
            why = $"the level needs {packed.LargestBuffer / (1024 * 1024)} MB in one buffer, and the graphics card takes {limit / (1024 * 1024)} MB";
            return null;
        }

        return new GpuPathTracer(gl, scene, packed, camera, settings);
    }

    /// <summary>
    /// Renders for about <paramref name="budgetMilliseconds"/>: band after band of rows, each a
    /// dispatch waited on, so no single one runs long enough for the driver to give up on it.
    /// </summary>
    public void Step(double budgetMilliseconds)
    {
        if (IsFinished)
        {
            return;
        }

        Bind();
        var clock = Stopwatch.StartNew();
        while (!IsFinished && clock.Elapsed.TotalMilliseconds < budgetMilliseconds)
        {
            int end = Math.Min(_row + _band, Height);
            SetInt("uRowStart", _row);
            SetInt("uRowEnd", end);
            SetInt("uSampleIndex", SamplesDone);

            double before = clock.Elapsed.TotalMilliseconds;
            _gl.DispatchCompute((uint)((Width + 7) / 8), (uint)((end - _row + 7) / 8), 1);
            _gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit);
            _gl.Finish();
            double took = clock.Elapsed.TotalMilliseconds - before;

            if (took < budgetMilliseconds / 8 && _band < Height)
            {
                _band = Math.Min(_band * 2, Height);
            }
            else if (took > budgetMilliseconds / 2 && _band > 8)
            {
                _band = Math.Max(_band / 2, 8);
            }

            _row = end;
            if (_row >= Height)
            {
                _row = 0;
                SamplesDone++;
            }
        }

        _gl.UseProgram(0);
    }

    /// <summary>The light summed so far, read back, every row as if it had exactly <see cref="SamplesDone"/> samples.</summary>
    public unsafe Vector4[] ReadSum()
    {
        var sum = new Vector4[Width * Height];
        _gl.MemoryBarrier(MemoryBarrierMask.TextureUpdateBarrierBit);
        _gl.BindTexture(TextureTarget.Texture2D, _sum);
        fixed (Vector4* pixels = sum)
        {
            _gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);

        // Rows the sample under way has reached are one sample ahead of the rest.
        if (_row > 0 && SamplesDone > 0)
        {
            float scale = SamplesDone / (SamplesDone + 1f);
            for (int i = 0; i < _row * Width; i++)
            {
                sum[i] *= scale;
            }
        }

        return sum;
    }

    private void Bind()
    {
        _gl.UseProgram(_program);
        for (int i = 0; i < _buffers.Length; i++)
        {
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, (uint)(i + 1), _buffers[i]);
        }

        _gl.BindImageTexture(0, _sum, 0, false, 0, BufferAccessARB.ReadWrite, InternalFormat.Rgba32f);

        Vector3 forward = Vector3.Normalize(_camera.Forward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, _camera.Up));
        Vector3 up = Vector3.Cross(right, forward);
        (Vector3 top, Vector3 horizon, float skyScale) = PathTracer.SkyOf(_scene, _settings);

        SetInt("uModelCount", _packed.ModelCount);
        SetInt("uLightCount", _packed.LightCount);
        _gl.Uniform2(Location("uSize"), Width, Height);
        SetInt("uSeed", _settings.Seed);
        SetInt("uBounces", _settings.Bounces);

        SetVector("uCameraPosition", _camera.Position);
        SetVector("uCameraForward", forward);
        SetVector("uCameraRight", right);
        SetVector("uCameraUp", up);
        SetFloat("uTanHalfFov", MathF.Tan(_camera.VerticalFov * 0.5f * (MathF.PI / 180f)));
        SetInt("uOrthographic", _camera.Orthographic ? 1 : 0);
        SetFloat("uOrthographicHalf", _camera.OrthographicHeight * 0.5f);
        SetFloat("uAperture", _settings.Aperture);
        SetFloat("uFocus", _settings.FocusDistance);

        SetVector("uSkyTop", top);
        SetVector("uSkyHorizon", horizon);
        SetFloat("uSkyScale", skyScale);
        SetFloat("uFog", _settings.Fog);
        SetFloat("uEmissionStrength", _settings.EmissionStrength);
        SetInt("uTransparentBackground", _settings.TransparentBackground ? 1 : 0);
        SetInt("uColourBackground", _settings.ColourBackground ? 1 : 0);
        SetVector("uBackdrop", PathTracer.BackdropOf(_settings));
        SetFloat("uSunSpread", _settings.SunSize > 0f ? MathF.Tan(_settings.SunSize * 0.5f * (MathF.PI / 180f)) : 0f);
    }

    private static bool EnsureProgram(GL gl, out string? why)
    {
        why = null;
        if (_program != 0 && ReferenceEquals(gl, _programGl))
        {
            return true;
        }

        uint shader = gl.CreateShader(ShaderType.ComputeShader);
        gl.ShaderSource(shader, PathTraceShader.Compute);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            _programError = why = $"its shader did not compile: {gl.GetShaderInfoLog(shader).Trim()}";
            gl.DeleteShader(shader);
            return false;
        }

        uint program = gl.CreateProgram();
        gl.AttachShader(program, shader);
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        gl.DetachShader(program, shader);
        gl.DeleteShader(shader);
        if (linked == 0)
        {
            _programError = why = $"its shader did not link: {gl.GetProgramInfoLog(program).Trim()}";
            gl.DeleteProgram(program);
            return false;
        }

        _program = program;
        _programGl = gl;
        Locations.Clear();
        return true;
    }

    private unsafe uint Upload<T>(T[] data)
        where T : unmanaged
    {
        uint buffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, buffer);
        fixed (T* first = data)
        {
            _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)(data.Length * sizeof(T)), first, BufferUsageARB.StaticDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        return buffer;
    }

    private int Location(string name)
    {
        if (!Locations.TryGetValue(name, out int location))
        {
            location = _gl.GetUniformLocation(_program, name);
            Locations[name] = location;
        }

        return location;
    }

    private void SetInt(string name, int value) => _gl.Uniform1(Location(name), value);

    private void SetFloat(string name, float value) => _gl.Uniform1(Location(name), value);

    private void SetVector(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);

    public void Dispose()
    {
        _gl.DeleteBuffers(_buffers);
        _gl.DeleteTexture(_sum);
    }
}
