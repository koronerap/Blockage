using System.Numerics;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>A linked GLSL program plus a cache of its uniform locations.</summary>
public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniforms = new();

    public uint Handle { get; }

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;

        uint vertex = Compile(ShaderType.VertexShader, vertexSource);
        uint fragment = Compile(ShaderType.FragmentShader, fragmentSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vertex);
        _gl.AttachShader(Handle, fragment);
        _gl.LinkProgram(Handle);

        _gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = _gl.GetProgramInfoLog(Handle);
            throw new InvalidOperationException($"Shader link failed: {log}");
        }

        _gl.DetachShader(Handle, vertex);
        _gl.DetachShader(Handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    private uint Compile(ShaderType type, string source)
    {
        uint handle = _gl.CreateShader(type);
        _gl.ShaderSource(handle, source);
        _gl.CompileShader(handle);

        _gl.GetShader(handle, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(handle);
            _gl.DeleteShader(handle);
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }

        return handle;
    }

    public void Use() => _gl.UseProgram(Handle);

    private int Location(string name)
    {
        if (!_uniforms.TryGetValue(name, out int location))
        {
            location = _gl.GetUniformLocation(Handle, name);
            _uniforms[name] = location;
        }

        return location;
    }

    public unsafe void SetMatrix4(string name, Matrix4x4 value)
    {
        int location = Location(name);
        if (location < 0)
        {
            return;
        }

        _gl.UniformMatrix4(location, 1, false, (float*)&value);
    }

    public void SetVector4(string name, Vector4 value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    public void SetVector3(string name, Vector3 value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform3(location, value.X, value.Y, value.Z);
        }
    }

    public void SetFloat(string name, float value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform1(location, value);
        }
    }

    public void SetInt(string name, int value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform1(location, value);
        }
    }

    /// <summary>
    /// Fills a <c>float[n]</c> uniform. The name is the array's, without a subscript — GLSL gives
    /// an array a single location its elements follow on from.
    /// </summary>
    public unsafe void SetFloatArray(string name, ReadOnlySpan<float> values)
    {
        int location = Location(name);
        if (location < 0)
        {
            return;
        }

        fixed (float* first = values)
        {
            _gl.Uniform1(location, (uint)values.Length, first);
        }
    }

    public unsafe void SetVector4Array(string name, ReadOnlySpan<Vector4> values)
    {
        int location = Location(name);
        if (location < 0)
        {
            return;
        }

        fixed (Vector4* first = values)
        {
            _gl.Uniform4(location, (uint)values.Length, (float*)first);
        }
    }

    public unsafe void SetVector3Array(string name, ReadOnlySpan<Vector3> values)
    {
        int location = Location(name);
        if (location < 0)
        {
            return;
        }

        fixed (Vector3* first = values)
        {
            _gl.Uniform3(location, (uint)values.Length, (float*)first);
        }
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
