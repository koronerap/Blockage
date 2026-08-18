using System.Numerics;
using EditorApp.Core.Meshing;

namespace EditorApp.Core.Export;

/// <summary>One quad's rectangle in the atlas, in texels, with the origin at the top left.</summary>
/// <param name="PaletteIndex">The colour the quad was built in.</param>
/// <param name="Rotated">True when the chart it belongs to was turned a quarter turn to pack.</param>
public readonly record struct UvIsland(int X, int Y, int Width, int Height, byte PaletteIndex, bool Rotated);

/// <summary>
/// A packed chart: one connected run of coplanar faces, laid out as a piece. Its rectangle is the
/// chart's bounding box, which may contain texels no face uses where the surface has a hole.
/// </summary>
public readonly record struct UvChart(int X, int Y, int Width, int Height, int QuadCount, bool Rotated);

/// <summary>Where every quad ended up, and how big the sheet is.</summary>
public sealed record UvAtlas(
    int Size,
    int TexelsPerVoxel,
    int Padding,
    IReadOnlyList<UvIsland> Islands,
    IReadOnlyList<UvChart> Charts)
{
    /// <summary>
    /// How much of the sheet is actually surface, ignoring gutters and chart holes. Wasted space is
    /// resolution the model did not get, so this is worth being able to see.
    /// </summary>
    public double Coverage
    {
        get
        {
            long used = 0;
            foreach (UvIsland island in Islands)
            {
                used += (long)island.Width * island.Height;
            }

            return used / (double)((long)Size * Size);
        }
    }
}

/// <summary>
/// Gives the exported mesh a real UV layout: connected runs of coplanar faces are laid out as whole
/// pieces, sized to the surface they cover.
///
/// The palette layout (EditorApp.md §6) puts all four corners of a quad on a single texel. That is
/// ideal for what it was for — one tiny texture, one material, colour that cannot bleed — but it
/// leaves the model with no area in UV space at all, so it cannot be painted on in an external tool.
///
/// Charts are per connected surface rather than per quad, and that is the difference between a
/// texture you can work on and confetti. A flat wall arrives as one piece however many quads it was
/// merged from, so a brush stroke crosses it without meeting a seam every few texels. Grouping by
/// normal alone would be simpler still and is wrong: two walls facing the same way at different
/// depths project onto each other, and painting one would paint the other.
///
/// Voxel geometry makes the rest easy. Greedy quads are flat rectangles with whole-voxel sides, so
/// there is nothing to flatten and no distortion to minimise — the only real work is packing.
/// </summary>
public static class UvUnwrap
{
    public const int DefaultTexelsPerVoxel = 8;

    /// <summary>Texels kept clear around each chart, so filtering cannot reach a neighbouring one.</summary>
    public const int DefaultPadding = 2;

    public const int DefaultMaxSize = 8192;

    /// <summary>Rounding used to decide whether two faces are on the same plane, in voxels.</summary>
    private const float Quantum = 1f / 512f;

    /// <summary>Which plane a face lies in, and which way up it is within that plane.</summary>
    private readonly record struct PlaneKey(long Nx, long Ny, long Nz, long Ux, long Uy, long Uz, long Depth);

    /// <summary>A quad reduced to an integer rectangle in the coordinates of its own plane.</summary>
    private readonly record struct PlanarQuad(PlaneKey Plane, int X, int Y, int Width, int Height);

    /// <summary>Replaces the mesh's UVs with a packed layout and reports it.</summary>
    /// <param name="voxelSize">
    /// World size of a voxel, so charts can be measured in voxels rather than in world units.
    /// Density is per voxel on purpose: changing a level's scale should not reshuffle its texture.
    /// </param>
    public static UvAtlas Apply(
        ExportMesh mesh,
        float voxelSize = 1f,
        int texelsPerVoxel = DefaultTexelsPerVoxel,
        int padding = DefaultPadding,
        int maxSize = DefaultMaxSize)
    {
        texelsPerVoxel = Math.Max(texelsPerVoxel, 1);
        padding = Math.Max(padding, 0);

        PlanarQuad[] quads = Describe(mesh, voxelSize);
        int[] chartOf = BuildCharts(quads, out (int X, int Y, int Width, int Height)[] charts);

        UvAtlas? packed;
        while (!TryPack(quads, chartOf, charts, mesh, texelsPerVoxel, padding, maxSize, out packed))
        {
            if (texelsPerVoxel == 1)
            {
                // Nothing left to give: pack anyway and let the caller see an oversized sheet.
                TryPack(quads, chartOf, charts, mesh, 1, padding, int.MaxValue, out packed);
                break;
            }

            texelsPerVoxel /= 2;
        }

        UvAtlas atlas = packed!;
        WriteUvs(mesh, atlas);
        return atlas;
    }

    /// <summary>
    /// Reads each quad back as a rectangle in its own plane.
    ///
    /// A quad is emitted as p0, p0+e1, p0+e1+e2, p0+e2, so its two edges are the first and last steps
    /// from the first corner. Taking everything from the geometry rather than carrying it along from
    /// the mesher means this keeps working after the object transforms have been baked in — and the
    /// plane key includes the in-plane axis, so two objects that happen to be coplanar but are turned
    /// differently never land in the same chart.
    /// </summary>
    private static PlanarQuad[] Describe(ExportMesh mesh, float voxelSize)
    {
        float scale = voxelSize > 0f ? voxelSize : 1f;
        var quads = new PlanarQuad[mesh.QuadCount];

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            int first = quad * 4;
            Vector3 origin = mesh.Positions[first] / scale;
            Vector3 edgeU = (mesh.Positions[first + 1] / scale) - origin;
            Vector3 edgeV = (mesh.Positions[first + 3] / scale) - origin;

            Vector3 u = Vector3.Normalize(edgeU);
            Vector3 v = Vector3.Normalize(edgeV);
            Vector3 n = Vector3.Normalize(Vector3.Cross(u, v));

            var plane = new PlaneKey(
                Round(n.X), Round(n.Y), Round(n.Z),
                Round(u.X), Round(u.Y), Round(u.Z),
                Round(Vector3.Dot(origin, n)));

            quads[quad] = new PlanarQuad(
                plane,
                (int)MathF.Round(Vector3.Dot(origin, u)),
                (int)MathF.Round(Vector3.Dot(origin, v)),
                Math.Max((int)MathF.Round(edgeU.Length()), 1),
                Math.Max((int)MathF.Round(edgeV.Length()), 1));
        }

        return quads;

        static long Round(float value) => (long)MathF.Round(value / Quantum);
    }

    /// <summary>
    /// Joins quads that share an edge within the same plane, and reports which chart each ended up
    /// in along with every chart's bounding box.
    ///
    /// Done by marking the voxel cells each quad covers and joining neighbours, rather than by
    /// comparing every rectangle with every other: a level has tens of thousands of quads and the
    /// pairwise version of this is the kind of thing that is fine until the first real model.
    /// </summary>
    private static int[] BuildCharts(PlanarQuad[] quads, out (int X, int Y, int Width, int Height)[] bounds)
    {
        var parent = new int[quads.Length];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA != rootB)
            {
                parent[rootB] = rootA;
            }
        }

        // One cell map per plane, holding the quad that covers each voxel of it.
        var planes = new Dictionary<PlaneKey, Dictionary<(int X, int Y), int>>();
        for (int quad = 0; quad < quads.Length; quad++)
        {
            PlanarQuad q = quads[quad];
            if (!planes.TryGetValue(q.Plane, out Dictionary<(int, int), int>? cells))
            {
                cells = [];
                planes.Add(q.Plane, cells);
            }

            for (int y = 0; y < q.Height; y++)
            {
                for (int x = 0; x < q.Width; x++)
                {
                    cells[(q.X + x, q.Y + y)] = quad;
                }
            }
        }

        foreach (Dictionary<(int X, int Y), int> cells in planes.Values)
        {
            foreach (((int x, int y), int quad) in cells)
            {
                if (cells.TryGetValue((x + 1, y), out int right))
                {
                    Union(quad, right);
                }

                if (cells.TryGetValue((x, y + 1), out int below))
                {
                    Union(quad, below);
                }
            }
        }

        // Renumber the roots so charts are 0..n-1, then measure each one.
        var index = new Dictionary<int, int>();
        var chartOf = new int[quads.Length];
        var boxes = new List<(int MinX, int MinY, int MaxX, int MaxY)>();

        for (int quad = 0; quad < quads.Length; quad++)
        {
            int root = Find(quad);
            if (!index.TryGetValue(root, out int chart))
            {
                chart = boxes.Count;
                index.Add(root, chart);
                boxes.Add((int.MaxValue, int.MaxValue, int.MinValue, int.MinValue));
            }

            chartOf[quad] = chart;

            PlanarQuad q = quads[quad];
            (int minX, int minY, int maxX, int maxY) = boxes[chart];
            boxes[chart] = (
                Math.Min(minX, q.X),
                Math.Min(minY, q.Y),
                Math.Max(maxX, q.X + q.Width),
                Math.Max(maxY, q.Y + q.Height));
        }

        bounds = new (int, int, int, int)[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            (int minX, int minY, int maxX, int maxY) = boxes[i];
            bounds[i] = (minX, minY, maxX - minX, maxY - minY);
        }

        return chartOf;
    }

    /// <summary>
    /// Shelf packing, tallest chart first, each one going on the open shelf it wastes least height
    /// on rather than only on the newest.
    ///
    /// Keeping earlier shelves open is what makes this worth using. With a single open shelf every
    /// row ends in a ragged tail that nothing shorter is ever allowed back into, and the leftovers
    /// pile up as one large unusable wedge; letting a short chart drop into an older row costs one
    /// scan of a short list. Not the tightest algorithm in existence — a skyline packer would do
    /// better again — but it is the most that is worth doing for rectangles this uniform.
    /// </summary>
    private static bool TryPack(
        PlanarQuad[] quads,
        int[] chartOf,
        (int X, int Y, int Width, int Height)[] charts,
        ExportMesh mesh,
        int texelsPerVoxel,
        int padding,
        int maxSize,
        out UvAtlas? atlas)
    {
        atlas = null;

        int gutter = padding * 2;
        int longest = 1;
        foreach ((int _, int _, int width, int height) in charts)
        {
            // The long side goes across, so it is the long side that has to fit the width.
            longest = Math.Max(longest, (Math.Max(width, height) * texelsPerVoxel) + gutter);
        }

        // Sorted by the side that will become the shelf's height once the chart is laid on its long
        // edge, which is the number that actually decides how the rows come out.
        int[] order = [.. Enumerable.Range(0, charts.Length)
            .OrderByDescending(i => Math.Min(charts[i].Width, charts[i].Height))
            .ThenByDescending(i => Math.Max(charts[i].Width, charts[i].Height))];

        for (int size = NextPowerOfTwo(longest); size <= maxSize; size *= 2)
        {
            var placed = new UvChart[charts.Length];
            var shelves = new List<Shelf>();
            int nextY = 0;
            bool fits = true;

            foreach (int chart in order)
            {
                // Turned so the long side lies along the row. A texture has no up, and one tall
                // chart left standing sets a shelf height the whole rest of the row has to pay for —
                // which on a real model was the difference between filling an eighth of the sheet
                // and most of it.
                bool rotated = charts[chart].Height > charts[chart].Width;

                int chartWidth = (rotated ? charts[chart].Height : charts[chart].Width) * texelsPerVoxel;
                int chartHeight = (rotated ? charts[chart].Width : charts[chart].Height) * texelsPerVoxel;
                int boxWidth = chartWidth + gutter;
                int boxHeight = chartHeight + gutter;

                // Least leftover height among the shelves that can take it, so short charts settle
                // into the gaps rather than opening a new row.
                int best = -1;
                for (int i = 0; i < shelves.Count; i++)
                {
                    Shelf shelf = shelves[i];
                    if (shelf.Height < boxHeight || shelf.X + boxWidth > size)
                    {
                        continue;
                    }

                    if (best < 0 || shelf.Height < shelves[best].Height)
                    {
                        best = i;
                    }
                }

                if (best < 0)
                {
                    if (nextY + boxHeight > size)
                    {
                        fits = false;
                        break;
                    }

                    shelves.Add(new Shelf(nextY, boxHeight, 0));
                    nextY += boxHeight;
                    best = shelves.Count - 1;
                }

                Shelf target = shelves[best];
                placed[chart] = new UvChart(
                    target.X + padding,
                    target.Y + padding,
                    chartWidth,
                    chartHeight,
                    0,
                    rotated);

                shelves[best] = target with { X = target.X + boxWidth };
            }

            if (!fits)
            {
                continue;
            }

            atlas = Build(quads, chartOf, charts, placed, mesh, size, texelsPerVoxel, padding);
            return true;
        }

        return false;
    }

    /// <summary>Places every quad inside its chart, at its own offset within it.</summary>
    private static UvAtlas Build(
        PlanarQuad[] quads,
        int[] chartOf,
        (int X, int Y, int Width, int Height)[] charts,
        UvChart[] placed,
        ExportMesh mesh,
        int size,
        int texelsPerVoxel,
        int padding)
    {
        var islands = new UvIsland[quads.Length];
        var counts = new int[charts.Length];

        for (int quad = 0; quad < quads.Length; quad++)
        {
            int chart = chartOf[quad];
            PlanarQuad q = quads[quad];
            UvChart target = placed[chart];

            // Offsets within the chart, in texels, before any turn is applied.
            int alongU = (q.X - charts[chart].X) * texelsPerVoxel;
            int alongV = (q.Y - charts[chart].Y) * texelsPerVoxel;

            // A turned chart is turned a quarter of the way round, not transposed. Swapping the two
            // axes would be a reflection, and the difference only shows once something with a
            // direction is painted on it — at which point those pieces come out mirrored.
            int chartHeight = charts[chart].Height * texelsPerVoxel;

            islands[quad] = target.Rotated
                ? new UvIsland(
                    target.X + (chartHeight - alongV - (q.Height * texelsPerVoxel)),
                    target.Y + alongU,
                    q.Height * texelsPerVoxel,
                    q.Width * texelsPerVoxel,
                    mesh.QuadPaletteIndices[quad],
                    Rotated: true)
                : new UvIsland(
                    target.X + alongU,
                    target.Y + alongV,
                    q.Width * texelsPerVoxel,
                    q.Height * texelsPerVoxel,
                    mesh.QuadPaletteIndices[quad],
                    Rotated: false);

            counts[chart]++;
        }

        var finished = new UvChart[charts.Length];
        for (int i = 0; i < charts.Length; i++)
        {
            finished[i] = placed[i] with { QuadCount = counts[i] };
        }

        return new UvAtlas(size, texelsPerVoxel, padding, islands, finished);
    }

    private static void WriteUvs(ExportMesh mesh, UvAtlas atlas)
    {
        float size = atlas.Size;

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            UvIsland island = atlas.Islands[quad];
            int first = quad * 4;

            // The corner order is p0, p0+e1, p0+e1+e2, p0+e2, so the parametric corners are
            // (0,0), (1,0), (1,1), (0,1) — u along the first edge, v along the second.
            Set(first, 0f, 0f);
            Set(first + 1, 1f, 0f);
            Set(first + 2, 1f, 1f);
            Set(first + 3, 0f, 1f);

            void Set(int vertex, float u, float v)
            {
                // A quarter turn, so the surface's v runs backwards along the sheet's x and its u
                // runs down the sheet's y. Swapping them instead would be a reflection, and the
                // piece would arrive mirrored.
                (float across, float down) = island.Rotated ? (1f - v, u) : (u, v);

                mesh.Uvs[vertex] = new Vector2(
                    (island.X + (across * island.Width)) / size,
                    (island.Y + (down * island.Height)) / size);
            }
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 16;
        while (result < value)
        {
            result *= 2;
        }

        return result;
    }

    /// <summary>An open row: where it starts, how tall it is, and how far along it is filled.</summary>
    private readonly record struct Shelf(int Y, int Height, int X);
}
