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

/// <summary>Immediate-mode line drawing for the ground grid and the hovered-face highlight.</summary>
public sealed class LineBatch : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly List<LineVertex> _vertices = [];

    private nuint _capacity;

    public unsafe LineBatch(GL gl)
    {
        _gl = gl;
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        const uint stride = LineVertex.SizeInBytes;
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)12);

        _gl.BindVertexArray(0);
    }

    public bool IsEmpty => _vertices.Count == 0;

    /// <summary>
    /// Applied to every point added from now on. Selections and hover highlights are expressed in
    /// the focused object's own space, so this carries them into the world without a second shader
    /// or a second draw pass.
    /// </summary>
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

    public void Clear()
    {
        _vertices.Clear();
        Transform = Matrix4x4.Identity;
    }

    public void AddLine(Vector3 from, Vector3 to, Color32 color)
    {
        uint rgba = color.Rgba;
        _vertices.Add(new LineVertex(Vector3.Transform(from, Transform), rgba));
        _vertices.Add(new LineVertex(Vector3.Transform(to, Transform), rgba));
    }

    /// <summary>Outlines one face of a voxel, pushed slightly outward so it does not z-fight.</summary>
    public void AddVoxelFace(Int3 voxel, Face face, Color32 color, float offset = 0.004f)
    {
        Vector3 push = FaceInfo.Normal(face) * offset;
        Vector3 origin = voxel.ToVector3();

        Vector3 Corner(int index) => origin + FaceInfo.Corner(face, index).ToVector3() + push;

        for (int i = 0; i < 4; i++)
        {
            AddLine(Corner(i), Corner((i + 1) & 3), color);
        }
    }

    public void AddBox(Vector3 min, Vector3 max, Color32 color)
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
            AddLine(corners[i], corners[(i + 1) & 3], color);
            AddLine(corners[i + 4], corners[((i + 1) & 3) + 4], color);
            AddLine(corners[i], corners[i + 4], color);
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
        if (_vertices.Count == 0)
        {
            return;
        }

        ReadOnlySpan<LineVertex> vertices = CollectionsMarshal.AsSpan(_vertices);
        var bytes = (nuint)(vertices.Length * LineVertex.SizeInBytes);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        if (bytes > _capacity)
        {
            _capacity = 4096;
            while (_capacity < bytes)
            {
                _capacity *= 2;
            }

            _gl.BufferData(BufferTargetARB.ArrayBuffer, _capacity, null, BufferUsageARB.DynamicDraw);
        }

        fixed (LineVertex* source = vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, bytes, source);
        }

        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)vertices.Length);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
