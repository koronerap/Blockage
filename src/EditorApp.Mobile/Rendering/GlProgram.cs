using System.Numerics;
using Android.Opengl;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// A linked GLSL ES program plus a cache of its uniform locations. The ES counterpart of the
/// desktop's ShaderProgram, and deliberately the same shape — the two are read side by side often
/// enough that a different one would cost more than it saved.
/// </summary>
public sealed class GlProgram : IDisposable
{
    private readonly Dictionary<string, int> _uniforms = new(StringComparer.Ordinal);

    /// <summary>Scratch for matrix uploads. The Java-style bindings take arrays, not pointers.</summary>
    private readonly float[] _matrix = new float[16];

    public int Handle { get; private set; }

    public GlProgram(string vertexSource, string fragmentSource)
    {
        int vertex = Compile(GLES30.GlVertexShader, vertexSource, "vertex");
        int fragment;

        try
        {
            fragment = Compile(GLES30.GlFragmentShader, fragmentSource, "fragment");
        }
        catch
        {
            GLES30.GlDeleteShader(vertex);
            throw;
        }

        Handle = GLES30.GlCreateProgram();
        GLES30.GlAttachShader(Handle, vertex);
        GLES30.GlAttachShader(Handle, fragment);
        GLES30.GlLinkProgram(Handle);

        int[] linked = new int[1];
        GLES30.GlGetProgramiv(Handle, GLES30.GlLinkStatus, linked, 0);
        if (linked[0] == 0)
        {
            string log = GLES30.GlGetProgramInfoLog(Handle) ?? "(the driver gave no reason)";
            GLES30.GlDeleteProgram(Handle);
            GLES30.GlDeleteShader(vertex);
            GLES30.GlDeleteShader(fragment);
            throw new InvalidOperationException($"Shader link failed: {log}");
        }

        GLES30.GlDetachShader(Handle, vertex);
        GLES30.GlDetachShader(Handle, fragment);
        GLES30.GlDeleteShader(vertex);
        GLES30.GlDeleteShader(fragment);
    }

    private static int Compile(int type, string source, string what)
    {
        int handle = GLES30.GlCreateShader(type);
        GLES30.GlShaderSource(handle, source);
        GLES30.GlCompileShader(handle);

        int[] compiled = new int[1];
        GLES30.GlGetShaderiv(handle, GLES30.GlCompileStatus, compiled, 0);
        if (compiled[0] == 0)
        {
            string log = GLES30.GlGetShaderInfoLog(handle) ?? "(the driver gave no reason)";
            GLES30.GlDeleteShader(handle);
            throw new InvalidOperationException($"{what} shader compile failed: {log}");
        }

        return handle;
    }

    public void Use() => GLES30.GlUseProgram(Handle);

    private int Location(string name)
    {
        if (!_uniforms.TryGetValue(name, out int location))
        {
            location = GLES30.GlGetUniformLocation(Handle, name);
            _uniforms[name] = location;
        }

        return location;
    }

    /// <summary>
    /// Uploads the matrix exactly as the desktop does: raw memory order, no transpose. System.Numerics
    /// lays a matrix out by rows and GL reads it by columns, and that mismatch is what turns the
    /// editor's row-vector maths into the column-vector form the shader multiplies by. Passing
    /// transpose here would undo it.
    /// </summary>
    public void SetMatrix4(string name, Matrix4x4 value)
    {
        int location = Location(name);
        if (location < 0)
        {
            return;
        }

        _matrix[0] = value.M11; _matrix[1] = value.M12; _matrix[2] = value.M13; _matrix[3] = value.M14;
        _matrix[4] = value.M21; _matrix[5] = value.M22; _matrix[6] = value.M23; _matrix[7] = value.M24;
        _matrix[8] = value.M31; _matrix[9] = value.M32; _matrix[10] = value.M33; _matrix[11] = value.M34;
        _matrix[12] = value.M41; _matrix[13] = value.M42; _matrix[14] = value.M43; _matrix[15] = value.M44;

        GLES30.GlUniformMatrix4fv(location, 1, false, _matrix, 0);
    }

    public void SetVector4(string name, Vector4 value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            GLES30.GlUniform4f(location, value.X, value.Y, value.Z, value.W);
        }
    }

    public void SetVector3(string name, Vector3 value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            GLES30.GlUniform3f(location, value.X, value.Y, value.Z);
        }
    }

    public void SetFloat(string name, float value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            GLES30.GlUniform1f(location, value);
        }
    }

    public void SetInt(string name, int value)
    {
        int location = Location(name);
        if (location >= 0)
        {
            GLES30.GlUniform1i(location, value);
        }
    }

    /// <summary>
    /// Fills a <c>float[n]</c> uniform. The name is the array's, without a subscript — GLSL gives an
    /// array a single location its elements follow on from.
    /// </summary>
    public void SetFloatArray(string name, float[] values)
    {
        int location = Location(name);
        if (location >= 0)
        {
            GLES30.GlUniform1fv(location, values.Length, values, 0);
        }
    }

    /// <summary>Fills a <c>vec4[n]</c> uniform from tightly packed x, y, z, w quadruples.</summary>
    public void SetVector4Array(string name, float[] values)
    {
        int location = Location(name);
        if (location >= 0)
        {
            GLES30.GlUniform4fv(location, values.Length / 4, values, 0);
        }
    }

    /// <summary>Fills a <c>vec3[n]</c> uniform from tightly packed x, y, z triples.</summary>
    public void SetVector3Array(string name, float[] values)
    {
        int location = Location(name);
        if (location >= 0)
        {
            GLES30.GlUniform3fv(location, values.Length / 3, values, 0);
        }
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            GLES30.GlDeleteProgram(Handle);
            Handle = 0;
        }
    }
}
