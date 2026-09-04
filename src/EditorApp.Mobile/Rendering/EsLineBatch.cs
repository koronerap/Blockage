using System.Runtime.InteropServices;
using Android.Opengl;
using EditorApp.Rendering;
using Java.Nio;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// The overlay pass on ES. All the geometry — the grid, outlines, gizmo cones — comes from
/// <see cref="LineGeometry"/>, shared with the desktop; only the upload and the two draw calls are
/// written again here.
/// </summary>
public sealed class EsLineBatch : LineGeometry, IDisposable
{
    private readonly int[] _names = new int[4];

    private byte[] _staging = [];
    private int _lineCapacity;
    private int _quadCapacity;

    public EsLineBatch()
    {
        GLES30.GlGenVertexArrays(2, _names, 0);
        GLES30.GlGenBuffers(2, _names, 2);

        Describe(_names[0], _names[2]);
        Describe(_names[1], _names[3]);
    }

    private static void Describe(int vao, int vbo)
    {
        GLES30.GlBindVertexArray(vao);
        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, vbo);

        const int stride = LineVertex.SizeInBytes;
        GLES30.GlEnableVertexAttribArray(0);
        GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, stride, 0);
        GLES30.GlEnableVertexAttribArray(1);
        GLES30.GlVertexAttribPointer(1, 4, GLES30.GlUnsignedByte, true, stride, 12);

        GLES30.GlBindVertexArray(0);
    }

    public void Draw()
    {
        if (LineVertices.Length > 0)
        {
            Upload(LineVertices, _names[0], _names[2], ref _lineCapacity);
            GLES30.GlDrawArrays(GLES30.GlLines, 0, LineVertices.Length);
        }

        if (QuadVertices.Length > 0)
        {
            Upload(QuadVertices, _names[1], _names[3], ref _quadCapacity);
            GLES30.GlDrawArrays(GLES30.GlTriangles, 0, QuadVertices.Length);
        }

        GLES30.GlBindVertexArray(0);
    }

    private void Upload(ReadOnlySpan<LineVertex> vertices, int vao, int vbo, ref int capacity)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(vertices);

        GLES30.GlBindVertexArray(vao);
        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, vbo);

        if (bytes.Length > capacity)
        {
            capacity = 4096;
            while (capacity < bytes.Length)
            {
                capacity *= 2;
            }

            GLES30.GlBufferData(GLES30.GlArrayBuffer, capacity, null, GLES30.GlDynamicDraw);
        }

        if (_staging.Length < bytes.Length)
        {
            _staging = new byte[capacity];
        }

        bytes.CopyTo(_staging);
        GLES30.GlBufferSubData(
            GLES30.GlArrayBuffer, 0, bytes.Length, ByteBuffer.Wrap(_staging, 0, bytes.Length)!);
    }

    public void Dispose()
    {
        GLES30.GlDeleteBuffers(2, _names, 2);
        GLES30.GlDeleteVertexArrays(2, _names, 0);
    }
}
