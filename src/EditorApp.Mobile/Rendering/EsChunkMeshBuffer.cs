using System.Runtime.InteropServices;
using Android.Opengl;
using EditorApp.Core.Meshing;
using Java.Nio;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// One chunk's GPU buffers, the ES counterpart of the desktop's ChunkMeshBuffer: a VBO and IBO per
/// chunk, so a dirty chunk re-uploads alone and clean ones are never touched.
///
/// The one real difference is how the data crosses over. The Java-style GLES bindings take a
/// <c>java.nio.Buffer</c> rather than a pointer, so a mesh reaches the driver by way of a byte array
/// wrapped in one. The array is kept and grown rather than allocated per upload — an edit dirties
/// the same chunk over and over while a stroke is being drawn.
/// </summary>
public sealed class EsChunkMeshBuffer : IDisposable
{
    private readonly int[] _names = new int[3];

    private byte[] _staging = [];
    private int _vertexCapacity;
    private int _indexCapacity;

    public int IndexCount { get; private set; }

    public int VertexCount { get; private set; }

    private int Vao => _names[0];

    private int Vbo => _names[1];

    private int Ibo => _names[2];

    public EsChunkMeshBuffer()
    {
        int[] arrays = new int[1];
        GLES30.GlGenVertexArrays(1, arrays, 0);
        _names[0] = arrays[0];

        int[] buffers = new int[2];
        GLES30.GlGenBuffers(2, buffers, 0);
        _names[1] = buffers[0];
        _names[2] = buffers[1];

        GLES30.GlBindVertexArray(Vao);
        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, Vbo);
        GLES30.GlBindBuffer(GLES30.GlElementArrayBuffer, Ibo);

        const int stride = MeshVertex.SizeInBytes;
        GLES30.GlEnableVertexAttribArray(0);
        GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, stride, 0);
        GLES30.GlEnableVertexAttribArray(1);
        GLES30.GlVertexAttribPointer(1, 4, GLES30.GlUnsignedByte, true, stride, 12);
        GLES30.GlEnableVertexAttribArray(2);
        GLES30.GlVertexAttribPointer(2, 1, GLES30.GlFloat, false, stride, 16);

        GLES30.GlBindVertexArray(0);
    }

    public void Upload(ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<uint> indices)
    {
        VertexCount = vertices.Length;
        IndexCount = indices.Length;

        if (IndexCount == 0)
        {
            return;
        }

        GLES30.GlBindVertexArray(Vao);

        int vertexBytes = vertices.Length * MeshVertex.SizeInBytes;
        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, Vbo);
        if (vertexBytes > _vertexCapacity)
        {
            // Grow in steps so an edit that nudges the count does not reallocate every frame.
            _vertexCapacity = NextCapacity(vertexBytes);
            GLES30.GlBufferData(GLES30.GlArrayBuffer, _vertexCapacity, null, GLES30.GlDynamicDraw);
        }

        GLES30.GlBufferSubData(GLES30.GlArrayBuffer, 0, vertexBytes, Stage(MemoryMarshal.AsBytes(vertices)));

        int indexBytes = indices.Length * sizeof(uint);
        GLES30.GlBindBuffer(GLES30.GlElementArrayBuffer, Ibo);
        if (indexBytes > _indexCapacity)
        {
            _indexCapacity = NextCapacity(indexBytes);
            GLES30.GlBufferData(GLES30.GlElementArrayBuffer, _indexCapacity, null, GLES30.GlDynamicDraw);
        }

        GLES30.GlBufferSubData(
            GLES30.GlElementArrayBuffer, 0, indexBytes, Stage(MemoryMarshal.AsBytes(indices)));

        GLES30.GlBindVertexArray(0);
    }

    /// <summary>
    /// Copies a span into the reusable staging array and hands back a buffer over it. The wrapper is
    /// a thin Java object around the same bytes — no second copy, and the array outlives the call so
    /// nothing is pinned across it.
    /// </summary>
    private ByteBuffer Stage(ReadOnlySpan<byte> bytes)
    {
        if (_staging.Length < bytes.Length)
        {
            _staging = new byte[NextCapacity(bytes.Length)];
        }

        bytes.CopyTo(_staging);
        return ByteBuffer.Wrap(_staging, 0, bytes.Length)!;
    }

    private static int NextCapacity(int required)
    {
        int capacity = 4096;
        while (capacity < required)
        {
            capacity *= 2;
        }

        return capacity;
    }

    public void Draw()
    {
        if (IndexCount == 0)
        {
            return;
        }

        GLES30.GlBindVertexArray(Vao);
        GLES30.GlDrawElements(GLES30.GlTriangles, IndexCount, GLES30.GlUnsignedInt, 0);
    }

    public void Dispose()
    {
        GLES30.GlDeleteBuffers(2, _names, 1);
        GLES30.GlDeleteVertexArrays(1, _names, 0);
    }
}
