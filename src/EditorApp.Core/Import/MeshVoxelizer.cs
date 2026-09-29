using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Import;

/// <summary>
/// A mesh made into voxels (Fullreleaseplan 8.3): every voxel its surface passes through, each the
/// colour of the surface nearest its middle — and, if asked, everything the surface closes in,
/// coloured from the surface nearest it. It comes as a small level with its own palette, as a prop
/// does, standing on the ground with its middle on the spot it is put.
/// </summary>
public static class MeshVoxelizer
{
    public const int MaxResolution = 256;

    /// <param name="resolution">How many voxels along the model's longest side.</param>
    /// <param name="solid">Fill what the surface closes in; a surface with holes in it closes in nothing.</param>
    public static VoxelScene Voxelize(ColouredMesh mesh, int resolution, bool solid, string name)
    {
        resolution = Math.Clamp(resolution, 1, MaxResolution);
        (Vector3 min, Vector3 max) = mesh.Bounds();
        Vector3 extent = max - min;
        float longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        if (mesh.Triangles.Count == 0 || !(longest > 1e-6f))
        {
            throw new InvalidDataException("The model has no size to make voxels of.");
        }

        float scale = resolution / longest;
        int nx = Math.Clamp((int)MathF.Ceiling(extent.X * scale), 1, resolution);
        int ny = Math.Clamp((int)MathF.Ceiling(extent.Y * scale), 1, resolution);
        int nz = Math.Clamp((int)MathF.Ceiling(extent.Z * scale), 1, resolution);
        int Index(int x, int y, int z) => (((y * nz) + z) * nx) + x;

        // The surface: for each voxel, the colour of the nearest point of any triangle through it.
        var surface = new Dictionary<int, (Color32 Colour, float Distance)>();
        foreach (MeshTriangle triangle in mesh.Triangles)
        {
            Vector3 a = (triangle.A - min) * scale;
            Vector3 b = (triangle.B - min) * scale;
            Vector3 c = (triangle.C - min) * scale;
            Vector3 low = Vector3.Min(a, Vector3.Min(b, c));
            Vector3 high = Vector3.Max(a, Vector3.Max(b, c));

            int x0 = Math.Clamp((int)MathF.Floor(low.X), 0, nx - 1), x1 = Math.Clamp((int)MathF.Floor(high.X), 0, nx - 1);
            int y0 = Math.Clamp((int)MathF.Floor(low.Y), 0, ny - 1), y1 = Math.Clamp((int)MathF.Floor(high.Y), 0, ny - 1);
            int z0 = Math.Clamp((int)MathF.Floor(low.Z), 0, nz - 1), z1 = Math.Clamp((int)MathF.Floor(high.Z), 0, nz - 1);

            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                var middle = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                if (!Overlaps(middle, 0.5f, a, b, c))
                {
                    continue;
                }

                (float wa, float wb, float wc, float distance) = Closest(middle, a, b, c);
                int index = Index(x, y, z);
                if (!surface.TryGetValue(index, out (Color32 Colour, float Distance) had) || distance < had.Distance)
                {
                    surface[index] = (triangle.At(wa, wb, wc), distance);
                }
            }
        }

        (Palette palette, Func<Color32, byte> slotOf) = PaletteFor([.. surface.Values.Select(v => v.Colour)]);
        var fill = new byte[nx * ny * nz];
        foreach ((int index, (Color32 colour, _)) in surface)
        {
            fill[index] = slotOf(colour);
        }

        if (solid)
        {
            FillInside(fill, nx, ny, nz);
        }

        var grid = new VoxelWorld();
        int left = nx / 2;
        int back = nz / 2;
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            byte colour = fill[Index(x, y, z)];
            if (colour != Palette.EmptyIndex)
            {
                grid.SetVoxel(x - left, y, z - back, colour);
            }
        }

        var scene = new VoxelScene();
        scene.ReplacePalette(palette);
        scene.Add(grid, ObjectTransform.Identity, name);
        return scene;
    }

    /// <summary>
    /// The colours as a palette of their own: each its own slot while there are slots, else the
    /// palette median cut makes of them, each colour then taking the nearest.
    /// </summary>
    private static (Palette Palette, Func<Color32, byte> SlotOf) PaletteFor(IReadOnlyList<Color32> colours)
    {
        var palette = new Palette();
        List<Color32> distinct = [.. colours.Distinct()];
        var slots = new Dictionary<Color32, byte>();

        if (distinct.Count < Palette.Size)
        {
            for (int i = 0; i < distinct.Count; i++)
            {
                palette[i + 1] = distinct[i];
                slots[distinct[i]] = (byte)(i + 1);
            }

            return (palette, colour => slots[colour]);
        }

        var pixels = new byte[colours.Count * 4];
        for (int i = 0; i < colours.Count; i++)
        {
            pixels[i * 4] = colours[i].R;
            pixels[(i * 4) + 1] = colours[i].G;
            pixels[(i * 4) + 2] = colours[i].B;
            pixels[(i * 4) + 3] = 255;
        }

        (byte[] cut, int count) = GifWriter.Quantize([pixels]);
        for (int i = 0; i < count; i++)
        {
            palette[i + 1] = new Color32(cut[i * 3], cut[(i * 3) + 1], cut[(i * 3) + 2]);
        }

        return (palette, colour =>
        {
            if (!slots.TryGetValue(colour, out byte slot))
            {
                slot = palette.Nearest(colour);
                slots[colour] = slot;
            }

            return slot;
        });
    }

    /// <summary>
    /// What the surface closes in, filled: whatever the outside cannot reach round a border of empty
    /// space is inside, and takes the colour of the surface nearest it, spreading in from there.
    /// </summary>
    private static void FillInside(byte[] fill, int nx, int ny, int nz)
    {
        int px = nx + 2, py = ny + 2, pz = nz + 2;
        var outside = new bool[px * py * pz];
        int Padded(int x, int y, int z) => (((y * pz) + z) * px) + x;
        bool Solid(int x, int y, int z) =>
            x >= 1 && y >= 1 && z >= 1 && x <= nx && y <= ny && z <= nz && fill[((((y - 1) * nz) + (z - 1)) * nx) + (x - 1)] != Palette.EmptyIndex;

        var queue = new Queue<(int X, int Y, int Z)>();
        outside[0] = true;
        queue.Enqueue((0, 0, 0));
        while (queue.Count > 0)
        {
            (int x, int y, int z) = queue.Dequeue();
            foreach ((int dx, int dy, int dz) in Neighbours)
            {
                int ax = x + dx, ay = y + dy, az = z + dz;
                if (ax < 0 || ay < 0 || az < 0 || ax >= px || ay >= py || az >= pz)
                {
                    continue;
                }

                int at = Padded(ax, ay, az);
                if (!outside[at] && !Solid(ax, ay, az))
                {
                    outside[at] = true;
                    queue.Enqueue((ax, ay, az));
                }
            }
        }

        // In from the surface: each inside cell the colour of the first surface to reach it.
        var spread = new Queue<(int X, int Y, int Z)>();
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            if (fill[(((y * nz) + z) * nx) + x] != Palette.EmptyIndex)
            {
                spread.Enqueue((x, y, z));
            }
        }

        while (spread.Count > 0)
        {
            (int x, int y, int z) = spread.Dequeue();
            byte colour = fill[(((y * nz) + z) * nx) + x];
            foreach ((int dx, int dy, int dz) in Neighbours)
            {
                int ax = x + dx, ay = y + dy, az = z + dz;
                if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz)
                {
                    continue;
                }

                int at = (((ay * nz) + az) * nx) + ax;
                if (fill[at] == Palette.EmptyIndex && !outside[Padded(ax + 1, ay + 1, az + 1)])
                {
                    fill[at] = colour;
                    spread.Enqueue((ax, ay, az));
                }
            }
        }
    }

    private static readonly (int, int, int)[] Neighbours = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    // ---- Geometry -----------------------------------------------------------------------------------

    /// <summary>
    /// Whether a triangle passes through a cube — Akenine-Möller's test, by separating axes: the
    /// cube's three, the triangle's plane, and the nine across their edges.
    /// </summary>
    public static bool Overlaps(Vector3 centre, float half, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 v0 = a - centre, v1 = b - centre, v2 = c - centre;

        if (Separated(v0.X, v1.X, v2.X, half) || Separated(v0.Y, v1.Y, v2.Y, half) || Separated(v0.Z, v1.Z, v2.Z, half))
        {
            return false;
        }

        Vector3 e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;
        if (AcrossSeparates(e0, v0, v1, v2, half) || AcrossSeparates(e1, v0, v1, v2, half) || AcrossSeparates(e2, v0, v1, v2, half))
        {
            return false;
        }

        Vector3 normal = Vector3.Cross(e0, e1);
        float reach = half * (MathF.Abs(normal.X) + MathF.Abs(normal.Y) + MathF.Abs(normal.Z));
        return MathF.Abs(Vector3.Dot(normal, v0)) <= reach;
    }

    private static bool Separated(float p0, float p1, float p2, float reach) =>
        MathF.Min(p0, MathF.Min(p1, p2)) > reach || MathF.Max(p0, MathF.Max(p1, p2)) < -reach;

    /// <summary>Whether one of the three axes across an edge and the cube's own separates them.</summary>
    private static bool AcrossSeparates(Vector3 edge, Vector3 v0, Vector3 v1, Vector3 v2, float half)
    {
        Vector3 x = new(0f, edge.Z, -edge.Y);
        Vector3 y = new(-edge.Z, 0f, edge.X);
        Vector3 z = new(edge.Y, -edge.X, 0f);
        foreach (Vector3 axis in (ReadOnlySpan<Vector3>)[x, y, z])
        {
            float reach = half * (MathF.Abs(axis.X) + MathF.Abs(axis.Y) + MathF.Abs(axis.Z));
            if (Separated(Vector3.Dot(v0, axis), Vector3.Dot(v1, axis), Vector3.Dot(v2, axis), reach))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The point of a triangle nearest another, as weights for its corners, and how far it is, squared — Ericson's regions.</summary>
    public static (float A, float B, float C, float Distance) Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        static float Squared(Vector3 from, Vector3 to) => Vector3.DistanceSquared(from, to);
        static float Divide(float over, float under) => MathF.Abs(under) < 1e-12f ? 0f : over / under;

        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return (1f, 0f, 0f, Squared(p, a));
        }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return (0f, 1f, 0f, Squared(p, b));
        }

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = Divide(d1, d1 - d3);
            return (1f - v, v, 0f, Squared(p, a + (ab * v)));
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return (0f, 0f, 1f, Squared(p, c));
        }

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = Divide(d2, d2 - d6);
            return (1f - w, 0f, w, Squared(p, a + (ac * w)));
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = Divide(d4 - d3, (d4 - d3) + (d5 - d6));
            return (0f, 1f - w, w, Squared(p, b + ((c - b) * w)));
        }

        float denominator = va + vb + vc;
        float vv = Divide(vb, denominator), ww = Divide(vc, denominator);
        return (1f - vv - ww, vv, ww, Squared(p, a + (ab * vv) + (ac * ww)));
    }
}
