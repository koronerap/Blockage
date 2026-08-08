using System.Numerics;
using System.Runtime.InteropServices;
using EditorApp.Core.Import;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ReferenceVertex(Vector3 position, Vector3 normal)
{
    public Vector3 Position = position;
    public Vector3 Normal = normal;

    public const int SizeInBytes = 24;
}

/// <summary>
/// Draws the imported reference model as a translucent overlay: depth tested so it sits correctly
/// among the voxels, but without writing depth, so it can never hide the geometry being built.
/// </summary>
public sealed class ReferenceModelRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _shader;
    private readonly uint _vao;
    private readonly uint _vbo;

    private int _vertexCount;

    public ReferenceMesh? Mesh { get; private set; }

    public bool Visible { get; set; } = true;

    public bool Wireframe { get; set; }

    public float Opacity { get; set; } = 0.35f;

    public Vector3 Offset { get; set; } = Vector3.Zero;

    public float Scale { get; set; } = 1f;

    public Vector4 Tint { get; set; } = new(0.55f, 0.75f, 1f, 1f);

    public unsafe ReferenceModelRenderer(GL gl)
    {
        _gl = gl;
        _shader = new ShaderProgram(gl, Shaders.ReferenceVertex, Shaders.ReferenceFragment);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        const uint stride = ReferenceVertex.SizeInBytes;
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);

        _gl.BindVertexArray(0);
    }

    public Matrix4x4 ModelMatrix => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateTranslation(Offset);

    public unsafe void SetMesh(ReferenceMesh? mesh)
    {
        Mesh = mesh;
        _vertexCount = 0;

        if (mesh is null || mesh.IsEmpty)
        {
            return;
        }

        var vertices = new ReferenceVertex[mesh.Positions.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new ReferenceVertex(mesh.Positions[i], mesh.Normals[i]);
        }

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        fixed (ReferenceVertex* data = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * ReferenceVertex.SizeInBytes),
                data,
                BufferUsageARB.StaticDraw);
        }

        _gl.BindVertexArray(0);
        _vertexCount = vertices.Length;
    }

    /// <summary>Places the model so its own bounds sit inside the given world box.</summary>
    public void FitTo(Vector3 targetMin, Vector3 targetMax)
    {
        if (Mesh is null || Mesh.IsEmpty)
        {
            return;
        }

        (Vector3 min, Vector3 max) = Mesh.Bounds();
        Vector3 size = max - min;
        Vector3 targetSize = targetMax - targetMin;

        float scale = MathF.Min(
            MathF.Min(SafeRatio(targetSize.X, size.X), SafeRatio(targetSize.Y, size.Y)),
            SafeRatio(targetSize.Z, size.Z));

        Scale = scale > 0f ? scale : 1f;
        Offset = targetMin - min * Scale;
    }

    private static float SafeRatio(float target, float source) =>
        source > 1e-5f ? target / source : float.MaxValue;

    public void Draw(Matrix4x4 viewProjection)
    {
        if (!Visible || _vertexCount == 0)
        {
            return;
        }

        _shader.Use();
        _shader.SetMatrix4("uViewProjection", viewProjection);
        _shader.SetMatrix4("uModel", ModelMatrix);
        _shader.SetVector4("uColor", Tint with { W = Math.Clamp(Opacity, 0.02f, 1f) });

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);   // reference models are often single-sided or inverted

        if (Wireframe)
        {
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);

        if (Wireframe)
        {
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
