using EditorApp.Core.Meshing;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// One chunk's GPU buffers (EditorApp.md §5: a VBO/IBO per chunk, so a dirty chunk re-uploads alone
/// and clean chunks are never touched).
/// </summary>
public sealed class ChunkMeshBuffer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ibo;

    private nuint _vertexCapacity;
    private nuint _indexCapacity;

    public int IndexCount { get; private set; }

    public int VertexCount { get; private set; }

    public bool IsEmpty => IndexCount == 0;

    public unsafe ChunkMeshBuffer(GL gl)
    {
        _gl = gl;
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ibo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);

        const uint stride = MeshVertex.SizeInBytes;
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)12);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, (void*)16);

        _gl.BindVertexArray(0);
    }

    public unsafe void Upload(ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<uint> indices)
    {
        VertexCount = vertices.Length;
        IndexCount = indices.Length;

        if (IndexCount == 0)
        {
            return;
        }

        _gl.BindVertexArray(_vao);

        var vertexBytes = (nuint)(vertices.Length * MeshVertex.SizeInBytes);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        if (vertexBytes > _vertexCapacity)
        {
            // Grow in steps so an edit that nudges the count does not reallocate every frame.
            _vertexCapacity = NextCapacity(vertexBytes);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, _vertexCapacity, null, BufferUsageARB.DynamicDraw);
        }

        fixed (MeshVertex* source = vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, vertexBytes, source);
        }

        var indexBytes = (nuint)(indices.Length * sizeof(uint));
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
        if (indexBytes > _indexCapacity)
        {
            _indexCapacity = NextCapacity(indexBytes);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, _indexCapacity, null, BufferUsageARB.DynamicDraw);
        }

        fixed (uint* source = indices)
        {
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, indexBytes, source);
        }

        _gl.BindVertexArray(0);
    }

    private static nuint NextCapacity(nuint required)
    {
        nuint capacity = 4096;
        while (capacity < required)
        {
            capacity *= 2;
        }

        return capacity;
    }

    public unsafe void Draw()
    {
        if (IndexCount == 0)
        {
            return;
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount, DrawElementsType.UnsignedInt, (void*)0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ibo);
        _gl.DeleteVertexArray(_vao);
    }
}
