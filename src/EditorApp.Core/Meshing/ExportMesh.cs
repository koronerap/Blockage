using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// The mesh handed to the exporters: positions, normals and UVs in parallel arrays, four vertices
/// per quad. No vertex sharing between quads — neighbouring quads carry different normals or
/// different UVs, so sharing would be wrong even where positions coincide.
/// </summary>
public sealed class ExportMesh
{
    public List<Vector3> Positions { get; } = [];

    public List<Vector3> Normals { get; } = [];

    public List<Vector2> Uvs { get; } = [];

    public List<int> Indices { get; } = [];

    /// <summary>Palette index per quad. Used for reporting, not for export — color travels via UV.</summary>
    public List<byte> QuadPaletteIndices { get; } = [];

    public int VertexCount => Positions.Count;

    public int TriangleCount => Indices.Count / 3;

    public int QuadCount => QuadPaletteIndices.Count;

    public void AddQuad(
        Vector3 corner0,
        Vector3 corner1,
        Vector3 corner2,
        Vector3 corner3,
        Vector3 normal,
        Vector2 uv,
        byte paletteIndex)
    {
        int baseIndex = Positions.Count;

        Positions.Add(corner0);
        Positions.Add(corner1);
        Positions.Add(corner2);
        Positions.Add(corner3);

        for (int i = 0; i < 4; i++)
        {
            Normals.Add(normal);
            Uvs.Add(uv);
        }

        Indices.Add(baseIndex);
        Indices.Add(baseIndex + 1);
        Indices.Add(baseIndex + 2);
        Indices.Add(baseIndex);
        Indices.Add(baseIndex + 2);
        Indices.Add(baseIndex + 3);

        QuadPaletteIndices.Add(paletteIndex);
    }

    /// <summary>
    /// Appends another mesh with a rigid transform applied. Used to bake each object's placement
    /// into the exported geometry: a rotation without reflection preserves winding, so quads stay
    /// facing outward without any correction.
    /// </summary>
    public void Append(ExportMesh source, Scene.ObjectTransform transform)
    {
        for (int quad = 0; quad < source.QuadCount; quad++)
        {
            int baseIndex = quad * 4;

            AddQuad(
                transform.TransformPoint(source.Positions[baseIndex]),
                transform.TransformPoint(source.Positions[baseIndex + 1]),
                transform.TransformPoint(source.Positions[baseIndex + 2]),
                transform.TransformPoint(source.Positions[baseIndex + 3]),
                Vector3.Normalize(transform.TransformDirection(source.Normals[baseIndex])),
                source.Uvs[baseIndex],
                source.QuadPaletteIndices[quad]);
        }
    }

    /// <summary>
    /// Total surface area. Merging quads must not change this by a single unit — it is the
    /// invariant the greedy mesher is validated against (EditorApp.md §4b).
    /// </summary>
    public double TotalArea()
    {
        double area = 0;
        for (int i = 0; i < Indices.Count; i += 3)
        {
            Vector3 a = Positions[Indices[i]];
            Vector3 b = Positions[Indices[i + 1]];
            Vector3 c = Positions[Indices[i + 2]];
            area += 0.5 * Vector3.Cross(b - a, c - a).Length();
        }

        return area;
    }

    /// <summary>Bounding box of the mesh, or a zero box when it is empty.</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (Positions.Count == 0)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        Vector3 min = Positions[0];
        Vector3 max = Positions[0];

        foreach (Vector3 position in Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return (min, max);
    }

    /// <summary>Distinct palette indices present, in ascending order.</summary>
    public IReadOnlyList<byte> UsedPaletteIndices()
    {
        var used = new SortedSet<byte>(QuadPaletteIndices);
        used.Remove(Palette.EmptyIndex);
        return [.. used];
    }
}
