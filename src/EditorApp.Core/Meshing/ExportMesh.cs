using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// The mesh handed to the exporters: positions, normals and UVs in parallel arrays, four vertices
/// per quad. No vertex sharing between quads — neighbouring quads carry different normals or
/// different UVs, so sharing would be wrong even where positions coincide.
/// </summary>
/// <summary>
/// One quad's colours, a palette index per voxel cell it covers, row-major along the quad's own u
/// then v.
///
/// A quad is one colour only when merging was keyed on colour. Once the mesh has a real UV layout it
/// need not be: the quad owns a rectangle of texture big enough to hold every cell separately, so
/// geometry can merge across a painted edge and the paint goes into the sheet instead of into the
/// triangle count.
/// </summary>
public readonly record struct QuadColors(int Width, int Height, byte[] Cells)
{
    public static QuadColors Uniform(byte index) => new(1, 1, [index]);

    public byte At(int x, int y) => Cells[(y * Width) + x];
}

/// <summary>A run of quads that came from one object, kept so the export can stay as many objects.</summary>
public readonly record struct MeshPart(string Name, int FirstQuad, int QuadCount);

public sealed class ExportMesh
{
    public List<Vector3> Positions { get; } = [];

    public List<Vector3> Normals { get; } = [];

    public List<Vector2> Uvs { get; } = [];

    public List<int> Indices { get; } = [];

    /// <summary>Palette index per quad. Used for reporting, not for export — color travels via UV.</summary>
    public List<byte> QuadPaletteIndices { get; } = [];

    /// <summary>Colour per voxel cell of each quad. One entry per quad, aligned with the list above.</summary>
    public List<QuadColors> QuadCells { get; } = [];

    /// <summary>
    /// Which quads came from which object. Empty when the mesh was not built from a scene, in which
    /// case the whole thing is one part.
    /// </summary>
    public List<MeshPart> Parts { get; } = [];

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
        byte paletteIndex,
        QuadColors? cells = null)
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
        QuadCells.Add(cells ?? QuadColors.Uniform(paletteIndex));
    }

    /// <summary>Records that everything added since the last part belongs to this one.</summary>
    public void BeginPart(string name, int firstQuad) =>
        Parts.Add(new MeshPart(name, firstQuad, QuadCount - firstQuad));

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
                source.QuadPaletteIndices[quad],
                source.QuadCells[quad]);
        }
    }

    /// <summary>
    /// Scales every position about the origin. Applied once to the finished export to turn voxel
    /// units into world units. Normals and UVs are left alone: a uniform positive scale does not
    /// change a direction, and a quad's UV never depended on its size in the first place.
    /// </summary>
    /// <summary>Falls back to the whole mesh as one part, for callers that never named any.</summary>
    public IReadOnlyList<MeshPart> PartsOrWhole =>
        Parts.Count > 0 ? Parts : [new MeshPart("level", 0, QuadCount)];

    public void Scale(float factor)
    {
        if (factor == 1f)
        {
            return;
        }

        for (int i = 0; i < Positions.Count; i++)
        {
            Positions[i] *= factor;
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
