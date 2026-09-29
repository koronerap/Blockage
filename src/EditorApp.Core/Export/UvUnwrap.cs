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

/// <summary>
/// Where every quad ended up, and how big the sheet is.
///
/// The sheet is not required to be square, and only its width is a power of two — the height is
/// rounded up to a whole block instead. Insisting on a square power of two meant a level that
/// overflowed a size by a little paid for the whole next one, which on a large level was three
/// quarters of the texture thrown away.
/// </summary>
public sealed record UvAtlas(
    int Width,
    int Height,
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

            return used / (double)((long)Width * Height);
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
    private readonly record struct PlaneKey(long Nx, long Ny, long Nz, long Ux, long Uy, long Uz, long Depth, long Size);

    /// <summary>
    /// A quad reduced to an integer rectangle in the coordinates of its own plane. The plane key
    /// carries the voxel size as well, so two objects of different sizes that happen to be coplanar
    /// never share a chart — their cells are not the same cells.
    /// </summary>
    private readonly record struct PlanarQuad(PlaneKey Plane, int X, int Y, int Width, int Height);

    /// <summary>
    /// Replaces the mesh's UVs with a packed layout and reports it.
    ///
    /// Charts are measured in voxels rather than in world units, each part at its own voxel size.
    /// Density is per voxel on purpose: changing an object's scale should not reshuffle its texture.
    /// </summary>
    public static UvAtlas Apply(
        ExportMesh mesh,
        int texelsPerVoxel = DefaultTexelsPerVoxel,
        int padding = DefaultPadding,
        int maxSize = DefaultMaxSize)
    {
        texelsPerVoxel = Math.Max(texelsPerVoxel, 1);
        padding = Math.Max(padding, 0);

        PlanarQuad[] quads = Describe(mesh);
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
    /// A second layout for lightmaps (Fullreleaseplan 8.8), into <see cref="ExportMesh.LightmapUvs"/>:
    /// each part unwrapped on its own, so its charts never overlap and fill its own square — which is
    /// how a lightmapper packs, a mesh at a time. The texture's UVs are left as they were.
    /// </summary>
    public static void AddLightmapUvs(ExportMesh mesh, int texelsPerVoxel = 2, int padding = DefaultPadding)
    {
        mesh.LightmapUvs.Clear();
        mesh.LightmapUvs.AddRange(new Vector2[mesh.VertexCount]);
        foreach (MeshPart part in mesh.PartsOrWhole)
        {
            var alone = new ExportMesh();
            for (int quad = part.FirstQuad; quad < part.FirstQuad + part.QuadCount; quad++)
            {
                int first = quad * 4;
                alone.AddQuad(
                    mesh.Positions[first], mesh.Positions[first + 1], mesh.Positions[first + 2], mesh.Positions[first + 3],
                    mesh.Normals[first], mesh.Uvs[first], mesh.QuadPaletteIndices[quad], mesh.QuadCells[quad]);
            }

            alone.BeginPart(part.Name, 0, part.VoxelSize);
            Apply(alone, texelsPerVoxel, padding);
            for (int i = 0; i < alone.VertexCount; i++)
            {
                mesh.LightmapUvs[(part.FirstQuad * 4) + i] = alone.Uvs[i];
            }
        }
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
    private static PlanarQuad[] Describe(ExportMesh mesh)
    {
        var quads = new PlanarQuad[mesh.QuadCount];
        float[] sizes = VoxelSizePerQuad(mesh);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            float scale = sizes[quad];
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
                Round(Vector3.Dot(origin, n)),
                Round(scale));

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

    /// <summary>Each quad's voxel size, read from the part it belongs to — one, outside any part.</summary>
    private static float[] VoxelSizePerQuad(ExportMesh mesh)
    {
        var sizes = new float[mesh.QuadCount];
        Array.Fill(sizes, 1f);

        foreach (MeshPart part in mesh.Parts)
        {
            float size = part.VoxelSize > 0f ? part.VoxelSize : 1f;
            int end = Math.Min(part.FirstQuad + part.QuadCount, sizes.Length);

            for (int quad = Math.Max(part.FirstQuad, 0); quad < end; quad++)
            {
                sizes[quad] = size;
            }
        }

        return sizes;
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
    /// Skyline packing, largest chart first: the sheet's filled profile is kept as a run of steps,
    /// and each chart goes wherever it can sit lowest.
    ///
    /// Rows were the obvious thing and left nearly half the sheet empty. A row is only as useful as
    /// its tallest member — everything shorter in it leaves a band of dead space above, and on a
    /// model whose charts are all different shapes that is most of them. A skyline has no rows, so a
    /// short chart tucks under a tall neighbour instead of reserving a strip of its own.
    ///
    /// Each chart is offered both ways round and takes whichever sits lower, which matters more here
    /// than it would elsewhere: charts come from surfaces, and surfaces are mostly long thin strips.
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

        // Biggest first. A skyline fills in around what is already placed, so the large awkward
        // pieces have to go down while there is still open sheet to choose from.
        int[] order = [.. Enumerable.Range(0, charts.Length)
            .OrderByDescending(i => (long)charts[i].Width * charts[i].Height)
            .ThenByDescending(i => Math.Max(charts[i].Width, charts[i].Height))];

        // Every width is tried and the smallest sheet wins. Packing to a fixed square and stopping
        // at the first fit meant a level that overflowed one power of two by a little paid for the
        // whole next one; letting the height settle wherever the rows end recovers it.
        //
        // Shape is kept as well as size. The narrowest candidate often has the smallest area — long
        // rows waste less on their ragged ends — but a sheet eight times as tall as it is wide is
        // awkward in a paint program and some tools refuse it outright. A reasonably square one wins
        // whenever there is one; the smallest of any shape is only the answer if there is not.
        UvAtlas? bestAtlas = null;
        long bestArea = long.MaxValue;
        UvAtlas? bestShaped = null;
        long bestShapedArea = long.MaxValue;

        for (int size = NextPowerOfTwo(longest); size <= maxSize; size *= 2)
        {
            var placed = new UvChart[charts.Length];
            var skyline = new List<Step> { new(0, 0, size) };
            int usedHeight = 0;
            bool fits = true;

            foreach (int chart in order)
            {
                int wide = charts[chart].Width * texelsPerVoxel;
                int tall = charts[chart].Height * texelsPerVoxel;

                // Offered both ways round; whichever sits lower wins. Charts are surfaces, and
                // surfaces are mostly long thin strips, so the choice is rarely a wash.
                (int x, int y, int step) = Lowest(skyline, size, wide + gutter, tall + gutter);
                (int rx, int ry, int rstep) = Lowest(skyline, size, tall + gutter, wide + gutter);

                bool rotated = rx >= 0 && (x < 0 || ry < y || (ry == y && rx < x));
                if (rotated)
                {
                    (x, y, step) = (rx, ry, rstep);
                }

                int boxWidth = (rotated ? tall : wide) + gutter;
                int boxHeight = (rotated ? wide : tall) + gutter;

                // Height is not capped while packing — only the width is a decision. How tall the
                // result turned out is measured afterwards.
                if (x < 0 || y + boxHeight > maxSize)
                {
                    fits = false;
                    break;
                }

                placed[chart] = new UvChart(
                    x + padding,
                    y + padding,
                    rotated ? tall : wide,
                    rotated ? wide : tall,
                    0,
                    rotated);

                Raise(skyline, step, x, y + boxHeight, boxWidth);
                usedHeight = Math.Max(usedHeight, y + boxHeight);
            }

            if (!fits)
            {
                continue;
            }

            int height = RoundHeight(usedHeight);
            if (height > maxSize)
            {
                continue;
            }

            long area = (long)size * height;
            bool shaped = Math.Max(size, height) <= Math.Min(size, height) * MaxAspect;

            if (area >= bestArea && (!shaped || area >= bestShapedArea))
            {
                continue;
            }

            UvAtlas candidate = Build(quads, chartOf, charts, placed, mesh, size, height, texelsPerVoxel, padding);

            if (area < bestArea)
            {
                bestArea = area;
                bestAtlas = candidate;
            }

            if (shaped && area < bestShapedArea)
            {
                bestShapedArea = area;
                bestShaped = candidate;
            }
        }

        atlas = bestShaped ?? bestAtlas;
        return atlas is not null;
    }

    /// <summary>How far from square a sheet may get before its shape counts against it.</summary>
    private const int MaxAspect = 4;

    /// <summary>Places every quad inside its chart, at its own offset within it.</summary>
    private static UvAtlas Build(
        PlanarQuad[] quads,
        int[] chartOf,
        (int X, int Y, int Width, int Height)[] charts,
        UvChart[] placed,
        ExportMesh mesh,
        int width,
        int height,
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

        return new UvAtlas(width, height, texelsPerVoxel, padding, islands, finished);
    }

    private static void WriteUvs(ExportMesh mesh, UvAtlas atlas)
    {
        float width = atlas.Width;
        float height = atlas.Height;

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
                    (island.X + (across * island.Width)) / width,
                    (island.Y + (down * island.Height)) / height);
            }
        }
    }

    /// <summary>
    /// Rounds the packed height up to a whole number of blocks rather than to the next power of two.
    ///
    /// This is where most of the sheet was going. The packer settles the rows at whatever height they
    /// need, and doubling that to the next power of two threw away everything between — on a large
    /// level, packing that finished at 2200 rows was being charged for 4096. Only the width is a
    /// power of two now; block-aligned heights keep the texture usable by every compressor and tool
    /// that cares, which is what the power of two was ever really for.
    /// </summary>
    private static int RoundHeight(int used)
    {
        const int Block = 64;
        return Math.Max(((used + Block - 1) / Block) * Block, Block);
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

    /// <summary>One flat run of the filled profile: where it starts, how high it is, how wide.</summary>
    private readonly record struct Step(int X, int Y, int Width);

    /// <summary>
    /// The lowest place a box of this size will sit, and which step it starts on. Returns a negative
    /// x when there is nowhere across this width that will take it.
    ///
    /// A box has to clear every step it spans, so its resting height is the highest of them — which
    /// is why the search walks forward from each candidate start rather than reading one step.
    /// </summary>
    private static (int X, int Y, int Step) Lowest(List<Step> skyline, int sheetWidth, int width, int height)
    {
        int bestX = -1;
        int bestY = int.MaxValue;
        int bestStep = -1;

        for (int i = 0; i < skyline.Count; i++)
        {
            int x = skyline[i].X;
            if (x + width > sheetWidth)
            {
                break;
            }

            int y = 0;
            int spanned = 0;
            int j = i;

            while (spanned < width && j < skyline.Count)
            {
                y = Math.Max(y, skyline[j].Y);
                spanned += skyline[j].Width;
                j++;
            }

            if (spanned < width)
            {
                break;
            }

            // Ties go to the leftmost, which keeps the profile growing evenly instead of towering
            // up on one side.
            if (y < bestY || (y == bestY && x < bestX))
            {
                bestX = x;
                bestY = y;
                bestStep = i;
            }
        }

        return (bestX, bestY == int.MaxValue ? 0 : bestY, bestStep);
    }

    /// <summary>Raises the profile where a box was just placed, and tidies up what it covered.</summary>
    private static void Raise(List<Step> skyline, int step, int x, int top, int width)
    {
        skyline.Insert(step, new Step(x, top, width));

        // Whatever the new step now sits over is either shortened or gone entirely.
        for (int i = step + 1; i < skyline.Count;)
        {
            int previousEnd = skyline[i - 1].X + skyline[i - 1].Width;
            if (skyline[i].X >= previousEnd)
            {
                break;
            }

            int overlap = previousEnd - skyline[i].X;
            if (skyline[i].Width <= overlap)
            {
                skyline.RemoveAt(i);
                continue;
            }

            skyline[i] = skyline[i] with { X = skyline[i].X + overlap, Width = skyline[i].Width - overlap };
            break;
        }

        // Runs at the same height are merged, or the profile turns into thousands of slivers and
        // every placement gets slower than the one before it.
        for (int i = 1; i < skyline.Count;)
        {
            if (skyline[i - 1].Y == skyline[i].Y)
            {
                skyline[i - 1] = skyline[i - 1] with { Width = skyline[i - 1].Width + skyline[i].Width };
                skyline.RemoveAt(i);
                continue;
            }

            i++;
        }
    }
}
