using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// The desktop's overlay drawing: <see cref="LineGeometry"/> plus the buffers to put it on screen.
///
/// Only the upload and the draw live here. Everything about what an outline or a gizmo is shaped
/// like is in the base class, shared with the Android head.
/// </summary>
public sealed class LineBatch : LineGeometry, IDisposable
{
    private readonly GL _gl;
    private readonly uint _lineVao;
    private readonly uint _lineVbo;
    private readonly uint _quadVao;
    private readonly uint _quadVbo;
    private readonly uint _fillVao;
    private readonly uint _fillVbo;

    private nuint _lineCapacity;
    private nuint _quadCapacity;
    private nuint _fillCapacity;

    public LineBatch(GL gl)
    {
        _gl = gl;

        (_lineVao, _lineVbo) = CreateBuffer();
        (_quadVao, _quadVbo) = CreateBuffer();
        (_fillVao, _fillVbo) = CreateBuffer();
    }

    private unsafe (uint Vao, uint Vbo) CreateBuffer()
    {
        uint vao = _gl.GenVertexArray();
        uint vbo = _gl.GenBuffer();

        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        const uint stride = LineVertex.SizeInBytes;
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)12);

        _gl.BindVertexArray(0);
        return (vao, vbo);
    }

    public void Draw()
    {
        // Tints first, blended, writing no depth: the strokes that follow then land on top of them
        // rather than being hidden behind a tint that happens to sit a hair nearer the camera.
        Upload(FillVertices, _fillVao, _fillVbo, ref _fillCapacity);
        if (FillVertices.Length > 0)
        {
            // The colour blends; the alpha already there is kept, or a tint would leave the
            // framebuffer part transparent wherever a compositor or a screenshot reads it.
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFuncSeparate(
                BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.One);
            _gl.DepthMask(false);

            _gl.BindVertexArray(_fillVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)FillVertices.Length);

            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        Upload(LineVertices, _lineVao, _lineVbo, ref _lineCapacity);
        if (LineVertices.Length > 0)
        {
            _gl.BindVertexArray(_lineVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)LineVertices.Length);
        }

        Upload(QuadVertices, _quadVao, _quadVbo, ref _quadCapacity);
        if (QuadVertices.Length > 0)
        {
            _gl.BindVertexArray(_quadVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)QuadVertices.Length);
        }

        _gl.BindVertexArray(0);
    }

    private unsafe void Upload(ReadOnlySpan<LineVertex> vertices, uint vao, uint vbo, ref nuint capacity)
    {
        if (vertices.Length == 0)
        {
            return;
        }

        var bytes = (nuint)(vertices.Length * LineVertex.SizeInBytes);

        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        if (bytes > capacity)
        {
            capacity = 4096;
            while (capacity < bytes)
            {
                capacity *= 2;
            }

            _gl.BufferData(BufferTargetARB.ArrayBuffer, capacity, null, BufferUsageARB.DynamicDraw);
        }

        fixed (LineVertex* source = vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, bytes, source);
        }
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_lineVbo);
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteBuffer(_fillVbo);
        _gl.DeleteVertexArray(_lineVao);
        _gl.DeleteVertexArray(_quadVao);
        _gl.DeleteVertexArray(_fillVao);
    }
}
