using System.Numerics;
using System.Runtime.InteropServices;
using EditorApp.Core.Voxels;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LineVertex(Vector3 position, uint rgba)
{
    public Vector3 Position = position;
    public uint Rgba = rgba;

    public const int SizeInBytes = 16;
}

/// <summary>
/// Immediate-mode overlay drawing: the ground grid, selection outlines and the gizmos.
///
/// Anything that needs to be thicker than a hairline is built as camera-facing quads rather than
/// asking for a wide <c>GL_LINE</c>. A core-profile driver is only required to support a line width
/// of 1, so <c>glLineWidth</c> is silently ignored on many of them — the one thing a gizmo cannot
/// afford. Width is scaled by distance so it stays constant on screen.
/// </summary>
public sealed class LineBatch : IDisposable
{
    private readonly GL _gl;
    private readonly uint _lineVao;
    private readonly uint _lineVbo;
    private readonly uint _quadVao;
    private readonly uint _quadVbo;

    private readonly List<LineVertex> _lines = [];
    private readonly List<LineVertex> _quads = [];

    private nuint _lineCapacity;
    private nuint _quadCapacity;

    public unsafe LineBatch(GL gl)
    {
        _gl = gl;

        (_lineVao, _lineVbo) = CreateBuffer();
        (_quadVao, _quadVbo) = CreateBuffer();
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

    public bool IsEmpty => _lines.Count == 0 && _quads.Count == 0;

    /// <summary>
    /// Applied to every point added from now on. Selections and hover highlights are expressed in
    /// the focused object's own space, so this carries them into the world without a second shader
    /// or a second draw pass.
    /// </summary>
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

    /// <summary>Where the camera is, so thick lines can be turned to face it and sized on screen.</summary>
    public Vector3 CameraPosition { get; set; }

    /// <summary>
    /// Half-width of a thick line as a fraction of its distance from the camera. Roughly a
    /// constant number of pixels at the default field of view.
    /// </summary>
    public float ThicknessScale { get; set; } = 0.0035f;

    public void Clear()
    {
        _lines.Clear();
        _quads.Clear();
        Transform = Matrix4x4.Identity;
    }

    public void AddLine(Vector3 from, Vector3 to, Color32 color)
    {
        uint rgba = color.Rgba;
        _lines.Add(new LineVertex(Vector3.Transform(from, Transform), rgba));
        _lines.Add(new LineVertex(Vector3.Transform(to, Transform), rgba));
    }

    /// <summary>
    /// A line with body. <paramref name="width"/> is a multiplier on <see cref="ThicknessScale"/>,
    /// so 1 is a normal overlay stroke and 3 is a gizmo handle you can actually grab.
    /// </summary>
    public void AddThickLine(Vector3 from, Vector3 to, Color32 color, float width = 1f)
    {
        Vector3 start = Vector3.Transform(from, Transform);
        Vector3 end = Vector3.Transform(to, Transform);

        Vector3 along = end - start;
        if (along.LengthSquared() < 1e-10f)
        {
            return;
        }

        Vector3 midpoint = (start + end) * 0.5f;
        Vector3 toCamera = CameraPosition - midpoint;
        if (toCamera.LengthSquared() < 1e-10f)
        {
            toCamera = Vector3.UnitY;
        }

        // Perpendicular to both the line and the view, which is what makes the quad read as a
        // round stroke from any angle.
        Vector3 side = Vector3.Cross(Vector3.Normalize(along), Vector3.Normalize(toCamera));
        if (side.LengthSquared() < 1e-8f)
        {
            // Looking straight down the line: any perpendicular will do.
            side = Vector3.Cross(Vector3.Normalize(along), Vector3.UnitY);
            if (side.LengthSquared() < 1e-8f)
            {
                side = Vector3.Cross(Vector3.Normalize(along), Vector3.UnitX);
            }
        }

        float halfWidth = MathF.Max(toCamera.Length() * ThicknessScale * width, 1e-4f);
        side = Vector3.Normalize(side) * halfWidth;

        uint rgba = color.Rgba;
        var a = new LineVertex(start - side, rgba);
        var b = new LineVertex(start + side, rgba);
        var c = new LineVertex(end + side, rgba);
        var d = new LineVertex(end - side, rgba);

        // Two triangles; the overlay pass draws with culling off, so winding does not matter.
        _quads.Add(a);
        _quads.Add(b);
        _quads.Add(c);
        _quads.Add(a);
        _quads.Add(c);
        _quads.Add(d);
    }

    /// <summary>Outlines one face of a voxel, pushed slightly outward so it does not z-fight.</summary>
    public void AddVoxelFace(Int3 voxel, Face face, Color32 color, float offset = 0.004f, float width = 0f)
    {
        Vector3 push = FaceInfo.Normal(face) * offset;
        Vector3 origin = voxel.ToVector3();

        Vector3 Corner(int index) => origin + FaceInfo.Corner(face, index).ToVector3() + push;

        for (int i = 0; i < 4; i++)
        {
            AddEdge(Corner(i), Corner((i + 1) & 3), color, width);
        }
    }

    public void AddBox(Vector3 min, Vector3 max, Color32 color, float width = 0f)
    {
        Span<Vector3> corners =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        ];

        for (int i = 0; i < 4; i++)
        {
            AddEdge(corners[i], corners[(i + 1) & 3], color, width);
            AddEdge(corners[i + 4], corners[((i + 1) & 3) + 4], color, width);
            AddEdge(corners[i], corners[i + 4], color, width);
        }
    }

    /// <summary>Thin when no width is asked for, so the grid stays cheap.</summary>
    private void AddEdge(Vector3 from, Vector3 to, Color32 color, float width)
    {
        if (width > 0f)
        {
            AddThickLine(from, to, color, width);
        }
        else
        {
            AddLine(from, to, color);
        }
    }

    /// <summary>A ground grid on the Y = 0 plane, with every tenth line emphasised.</summary>
    public void AddGroundGrid(int halfExtent, Color32 minor, Color32 major)
    {
        for (int i = -halfExtent; i <= halfExtent; i++)
        {
            Color32 color = i % 10 == 0 ? major : minor;
            AddLine(new Vector3(i, 0, -halfExtent), new Vector3(i, 0, halfExtent), color);
            AddLine(new Vector3(-halfExtent, 0, i), new Vector3(halfExtent, 0, i), color);
        }
    }

    public unsafe void Draw()
    {
        Upload(_lines, _lineVao, _lineVbo, ref _lineCapacity);
        if (_lines.Count > 0)
        {
            _gl.BindVertexArray(_lineVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_lines.Count);
        }

        Upload(_quads, _quadVao, _quadVbo, ref _quadCapacity);
        if (_quads.Count > 0)
        {
            _gl.BindVertexArray(_quadVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_quads.Count);
        }

        _gl.BindVertexArray(0);
    }

    private unsafe void Upload(List<LineVertex> vertices, uint vao, uint vbo, ref nuint capacity)
    {
        if (vertices.Count == 0)
        {
            return;
        }

        ReadOnlySpan<LineVertex> span = CollectionsMarshal.AsSpan(vertices);
        var bytes = (nuint)(span.Length * LineVertex.SizeInBytes);

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

        fixed (LineVertex* source = span)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, bytes, source);
        }
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_lineVbo);
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteVertexArray(_lineVao);
        _gl.DeleteVertexArray(_quadVao);
    }
}
