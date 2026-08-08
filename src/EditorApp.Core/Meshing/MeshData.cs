using System.Numerics;
using System.Runtime.InteropServices;

namespace EditorApp.Core.Meshing;

/// <summary>
/// One vertex of the editor mesh: 20 bytes, interleaved exactly as the vertex shader reads it.
/// Colors live per vertex because voxel color is per voxel and faces do not share vertices (§4a).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MeshVertex(Vector3 position, uint rgba, float faceIndex)
{
    public Vector3 Position = position;
    public uint Rgba = rgba;

    /// <summary>
    /// Which of the six faces this vertex belongs to, as a float because that is what a vertex
    /// attribute is. It used to hold the flat shade for that face directly, but a brightness cannot
    /// be turned back into a direction — the two side pairs share a value — so the shader could
    /// never light the face. Carrying the index instead lets it look up both.
    /// </summary>
    public float FaceIndex = faceIndex;

    public const int SizeInBytes = 20;
}

/// <summary>Accumulates quads into vertex and index buffers. Reused across chunk rebuilds.</summary>
public sealed class MeshBuilder
{
    private readonly List<MeshVertex> _vertices = [];
    private readonly List<uint> _indices = [];

    public int VertexCount => _vertices.Count;

    public int IndexCount => _indices.Count;

    public int TriangleCount => _indices.Count / 3;

    public int QuadCount => _indices.Count / 6;

    public bool IsEmpty => _indices.Count == 0;

    public ReadOnlySpan<MeshVertex> Vertices => CollectionsMarshal.AsSpan(_vertices);

    public ReadOnlySpan<uint> Indices => CollectionsMarshal.AsSpan(_indices);

    public void Clear()
    {
        _vertices.Clear();
        _indices.Clear();
    }

    /// <summary>Appends a quad as two triangles: (0,1,2) and (0,2,3). Corners must be counter-clockwise.</summary>
    public void AddQuad(
        Vector3 corner0,
        Vector3 corner1,
        Vector3 corner2,
        Vector3 corner3,
        uint rgba,
        float faceIndex)
    {
        uint baseIndex = (uint)_vertices.Count;

        _vertices.Add(new MeshVertex(corner0, rgba, faceIndex));
        _vertices.Add(new MeshVertex(corner1, rgba, faceIndex));
        _vertices.Add(new MeshVertex(corner2, rgba, faceIndex));
        _vertices.Add(new MeshVertex(corner3, rgba, faceIndex));

        _indices.Add(baseIndex);
        _indices.Add(baseIndex + 1);
        _indices.Add(baseIndex + 2);
        _indices.Add(baseIndex);
        _indices.Add(baseIndex + 2);
        _indices.Add(baseIndex + 3);
    }

    /// <summary>Total surface area of the mesh. The invariant that validates greedy meshing (§4b).</summary>
    public double TotalArea()
    {
        double area = 0;
        ReadOnlySpan<MeshVertex> vertices = Vertices;
        ReadOnlySpan<uint> indices = Indices;

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[(int)indices[i]].Position;
            Vector3 b = vertices[(int)indices[i + 1]].Position;
            Vector3 c = vertices[(int)indices[i + 2]].Position;
            area += 0.5 * Vector3.Cross(b - a, c - a).Length();
        }

        return area;
    }
}
