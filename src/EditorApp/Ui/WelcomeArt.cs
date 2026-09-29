using System.Numerics;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The welcome screen's picture, until there is real art: a small voxel island at dusk, drawn the way
/// the editor draws a block — the top in full light, the two sides at the shades the editor gives
/// them — with the application's own orange block standing on it. Drawn rather than loaded, like the
/// application icon, so there is no image to ship or to go missing.
/// </summary>
public static class WelcomeArt
{
    private readonly record struct Block(int X, int Y, int Z, Vector3 Colour);

    private static readonly Vector3 Grass = Rgb(0x7DB04C);
    private static readonly Vector3 Soil = Rgb(0x8A5A3B);
    private static readonly Vector3 Water = Rgb(0x4F93C9);
    private static readonly Vector3 Trunk = Rgb(0x6B4A30);
    private static readonly Vector3 Leaves = Rgb(0x4C8A3C);
    private static readonly Vector3 Stone = Rgb(0xD9D9D9);

    /// <summary>The application icon's colour.</summary>
    private static readonly Vector3 Brand = Rgb(0xED9E5C);

    private static readonly Vector4 SkyTop = new Vector4(Rgb(0x1C2740), 1f);
    private static readonly Vector4 SkyBottom = new Vector4(Rgb(0x4A6A86), 1f);

    /// <summary>Back to front, so each block drawn covers what is behind it.</summary>
    private static readonly Block[] Island = BuildIsland();

    private static readonly HashSet<(int, int, int)> Filled = [.. Island.Select(b => (b.X, b.Y, b.Z))];

    /// <summary>
    /// The picture, filling <paramref name="min"/> to <paramref name="max"/>. Rounded at the top by
    /// <paramref name="rounding"/>, where it meets the corners of the window it heads.
    /// </summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding)
    {
        uint top = ImGui.ColorConvertFloat4ToU32(SkyTop);
        uint bottom = ImGui.ColorConvertFloat4ToU32(SkyBottom);

        // A gradient cannot be rounded, so the rounded corners are a band in the top colour above it.
        float band = MathF.Max(rounding, 0f);
        drawList.AddRectFilled(min, new Vector2(max.X, min.Y + band + 1f), top, rounding, ImDrawFlags.RoundCornersTop);
        drawList.AddRectFilledMultiColor(new Vector2(min.X, min.Y + band), max, top, top, bottom, bottom);

        Vector2 size = max - min;
        if (size.X <= 1f || size.Y <= 1f)
        {
            return;
        }

        DrawFloaters(drawList, min, size);

        // The island takes the right of the picture, leaving the left for the name over it.
        Vector2 areaMin = min + new Vector2(size.X * 0.42f, size.Y * 0.1f);
        Vector2 areaMax = max - new Vector2(size.X * 0.04f, size.Y * 0.08f);
        (Vector2 origin, float scale) = Fit(areaMin, areaMax);

        foreach (Block block in Island)
        {
            DrawBlock(drawList, block, origin, scale, 1f);
        }
    }

    /// <summary>A few blocks adrift in the sky, faint, for depth.</summary>
    private static void DrawFloaters(ImDrawListPtr drawList, Vector2 min, Vector2 size)
    {
        (float X, float Y, float Scale, float Alpha)[] floaters =
        [
            (0.30f, 0.20f, 0.030f, 0.30f),
            (0.36f, 0.34f, 0.018f, 0.22f),
            (0.90f, 0.16f, 0.024f, 0.26f),
        ];

        foreach ((float x, float y, float scale, float alpha) in floaters)
        {
            Vector2 at = min + new Vector2(size.X * x, size.Y * y);
            DrawBlock(drawList, new Block(0, 0, 0, Stone), at, size.X * scale, alpha);
        }
    }

    private static void DrawBlock(ImDrawListPtr drawList, Block block, Vector2 origin, float scale, float alpha)
    {
        int x = block.X, y = block.Y, z = block.Z;
        bool single = alpha < 1f;

        // Only the faces nothing stands against: the top, and the two sides that face the viewer.
        if (single || !Filled.Contains((x, y + 1, z)))
        {
            Quad(drawList, origin, scale, block.Colour * FaceInfo.Shade(Face.PosY), alpha,
                new Vector3(x, y + 1, z), new Vector3(x + 1, y + 1, z), new Vector3(x + 1, y + 1, z + 1), new Vector3(x, y + 1, z + 1));
        }

        if (single || !Filled.Contains((x, y, z + 1)))
        {
            Quad(drawList, origin, scale, block.Colour * FaceInfo.Shade(Face.NegX), alpha,
                new Vector3(x, y, z + 1), new Vector3(x + 1, y, z + 1), new Vector3(x + 1, y + 1, z + 1), new Vector3(x, y + 1, z + 1));
        }

        if (single || !Filled.Contains((x + 1, y, z)))
        {
            Quad(drawList, origin, scale, block.Colour * FaceInfo.Shade(Face.NegZ), alpha,
                new Vector3(x + 1, y, z), new Vector3(x + 1, y + 1, z), new Vector3(x + 1, y + 1, z + 1), new Vector3(x + 1, y, z + 1));
        }
    }

    private static void Quad(ImDrawListPtr drawList, Vector2 origin, float scale, Vector3 colour, float alpha, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        uint fill = ImGui.ColorConvertFloat4ToU32(new Vector4(Vector3.Clamp(colour, Vector3.Zero, Vector3.One), alpha));
        drawList.AddQuadFilled(Project(a, origin, scale), Project(b, origin, scale), Project(c, origin, scale), Project(d, origin, scale), fill);
    }

    /// <summary>
    /// The editor's own angle, as the icon has it: +X down to the right, +Z down to the left, up up.
    /// The top is a diamond twice as wide as it is tall.
    /// </summary>
    private static Vector2 Project(Vector3 p, Vector2 origin, float scale) =>
        origin + (new Vector2((p.X - p.Z) * 0.866f, ((p.X + p.Z) * 0.5f) - p.Y) * scale);

    /// <summary>Where the island's origin goes and how big a block is, for it to fill the area without leaving it.</summary>
    private static (Vector2 Origin, float Scale) Fit(Vector2 areaMin, Vector2 areaMax)
    {
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);

        foreach (Block block in Island)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                var p = new Vector3(block.X + (corner & 1), block.Y + ((corner >> 1) & 1), block.Z + ((corner >> 2) & 1));
                Vector2 projected = Project(p, Vector2.Zero, 1f);
                low = Vector2.Min(low, projected);
                high = Vector2.Max(high, projected);
            }
        }

        Vector2 extent = high - low;
        Vector2 area = areaMax - areaMin;
        float scale = MathF.Min(area.X / extent.X, area.Y / extent.Y);

        // Centred in the area.
        Vector2 used = extent * scale;
        Vector2 corner00 = areaMin + ((area - used) * 0.5f);
        return (corner00 - (low * scale), scale);
    }

    /// <summary>A round island of grass on soil, with a pond, a tree, a white slab and the orange block.</summary>
    private static Block[] BuildIsland()
    {
        const int Size = 11;
        var blocks = new Dictionary<(int, int, int), Vector3>();

        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                float dx = x - 5f;
                float dz = z - 5f;
                float distance = MathF.Sqrt((dx * dx) + (dz * dz));
                if (distance > 5.3f)
                {
                    continue;
                }

                // Deeper at the middle, as an island hangs.
                int depth = distance < 2.5f ? 3 : distance < 4.2f ? 2 : 1;
                bool pond = ((x - 7) * (x - 7)) + ((z - 3) * (z - 3)) <= 2;

                for (int y = -depth; y < 0; y++)
                {
                    blocks[(x, y, z)] = Soil;
                }

                blocks[(x, 0, z)] = pond ? Water : Grass;
            }
        }

        // A tree: a trunk and a crown of leaves.
        for (int y = 1; y <= 3; y++)
        {
            blocks[(3, y, 6)] = Trunk;
        }

        for (int x = 2; x <= 4; x++)
        {
            for (int z = 5; z <= 7; z++)
            {
                for (int y = 4; y <= 5; y++)
                {
                    blocks[(x, y, z)] = Leaves;
                }
            }
        }

        blocks[(3, 6, 6)] = Leaves;

        // A white slab at the front, the white a new level starts in.
        for (int x = 2; x <= 3; x++)
        {
            for (int z = 8; z <= 9; z++)
            {
                blocks[(x, 1, z)] = Stone;
            }
        }

        // The application's own block: its icon, two voxels to a side.
        for (int x = 6; x <= 7; x++)
        {
            for (int y = 1; y <= 2; y++)
            {
                for (int z = 6; z <= 7; z++)
                {
                    blocks[(x, y, z)] = Brand;
                }
            }
        }

        return [.. blocks
            .Select(pair => new Block(pair.Key.Item1, pair.Key.Item2, pair.Key.Item3, pair.Value))
            .OrderBy(b => b.X + b.Y + b.Z)
            .ThenBy(b => b.Y)];
    }

    private static Vector3 Rgb(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
}
