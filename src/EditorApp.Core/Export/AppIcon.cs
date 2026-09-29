using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export;

/// <summary>
/// The application mark, drawn rather than shipped as an image.
///
/// It is a single voxel seen from the editor's own default angle, shaded with the editor's own
/// per-face constants — the top at full brightness, the two sides at 0.80 and 0.65 (see
/// <see cref="FaceInfo"/>). That is the whole idea: the icon is one of the things the tool makes,
/// lit the way the tool lights it, so it stays right if those constants ever change.
///
/// Drawing it also means every size is rendered at its own resolution instead of being scaled down
/// from one bitmap, which is what keeps the 16-pixel version from turning to mud — and it reuses the
/// PNG writer that already exists for the palette texture, so it costs no dependency.
/// </summary>
public static class AppIcon
{
    /// <summary>Sizes Windows actually asks for, smallest first.</summary>
    public static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>Samples per axis inside each pixel. The silhouette is all diagonals; it needs them.</summary>
    private const int Supersample = 4;

    private static readonly Color32 Body = new(0xED, 0x9E, 0x5C);

    /// <summary>Row-major RGBA, top row first — the layout both the PNG writer and GLFW expect.</summary>
    public static byte[] RenderRgba(int size)
    {
        var pixels = new byte[size * size * 4];

        // A hexagon of this radius leaves a little air on every side at every size.
        float radius = size * 0.46f;
        var centre = new Vector2(size * 0.5f, size * 0.5f);

        // The six corners of the silhouette, and the centre where the three faces meet.
        Vector2 top = centre + new Vector2(0f, -radius);
        Vector2 upperRight = centre + new Vector2(radius * 0.866f, -radius * 0.5f);
        Vector2 lowerRight = centre + new Vector2(radius * 0.866f, radius * 0.5f);
        Vector2 bottom = centre + new Vector2(0f, radius);
        Vector2 lowerLeft = centre + new Vector2(-radius * 0.866f, radius * 0.5f);
        Vector2 upperLeft = centre + new Vector2(-radius * 0.866f, -radius * 0.5f);

        Vector2[] topFace = [top, upperRight, centre, upperLeft];
        Vector2[] leftFace = [upperLeft, centre, bottom, lowerLeft];
        Vector2[] rightFace = [upperRight, lowerRight, bottom, centre];

        float topShade = FaceInfo.Shade(Face.PosY);
        float leftShade = FaceInfo.Shade(Face.NegX);
        float rightShade = FaceInfo.Shade(Face.NegZ);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Coverage and colour are accumulated together, so a pixel straddling the seam
                // between two faces blends them instead of picking one.
                float coverage = 0f;
                var sum = Vector3.Zero;

                for (int sy = 0; sy < Supersample; sy++)
                {
                    for (int sx = 0; sx < Supersample; sx++)
                    {
                        var point = new Vector2(
                            x + ((sx + 0.5f) / Supersample),
                            y + ((sy + 0.5f) / Supersample));

                        float shade =
                            Inside(topFace, point) ? topShade
                            : Inside(leftFace, point) ? leftShade
                            : Inside(rightFace, point) ? rightShade
                            : 0f;

                        if (shade <= 0f)
                        {
                            continue;
                        }

                        coverage += 1f;
                        sum += new Vector3(Body.R, Body.G, Body.B) * shade;
                    }
                }

                if (coverage <= 0f)
                {
                    continue;
                }

                // Divided by the covered samples, not by all of them: the colour is the average of
                // the body, and only the alpha records how much of the pixel the body reaches.
                Vector3 colour = sum / coverage;
                int offset = ((y * size) + x) * 4;

                pixels[offset] = (byte)MathF.Round(colour.X);
                pixels[offset + 1] = (byte)MathF.Round(colour.Y);
                pixels[offset + 2] = (byte)MathF.Round(colour.Z);
                pixels[offset + 3] = (byte)MathF.Round(255f * coverage / (Supersample * Supersample));
            }
        }

        return pixels;
    }

    public static byte[] EncodePng(int size) => PngWriter.EncodeRgba(RenderRgba(size), size, size);

    /// <summary>
    /// A Windows .ico holding every size, each as its own PNG. PNG-compressed entries have been
    /// understood since Vista, and they keep the 256-pixel version from costing 256 KB on its own.
    /// </summary>
    public static byte[] EncodeIco()
    {
        byte[][] images = [.. Sizes.Select(EncodePng)];

        const int headerSize = 6;
        const int entrySize = 16;
        int offset = headerSize + (entrySize * images.Length);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);                  // reserved
        writer.Write((ushort)1);                  // 1 = icon
        writer.Write((ushort)images.Length);

        for (int i = 0; i < images.Length; i++)
        {
            // 256 is written as 0: the field is one byte and the format says so.
            writer.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
            writer.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
            writer.Write((byte)0);                // palette entries, none
            writer.Write((byte)0);                // reserved
            writer.Write((ushort)1);              // colour planes
            writer.Write((ushort)32);             // bits per pixel
            writer.Write(images[i].Length);
            writer.Write(offset);

            offset += images[i].Length;
        }

        foreach (byte[] image in images)
        {
            writer.Write(image);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// What a Mac asks an .icns for: each size under its own type code, the Retina ones — a size drawn
    /// at twice its pixels — as PNGs too.
    /// </summary>
    public static readonly (string Type, int Pixels)[] IcnsEntries =
    [
        ("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
        ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512),
    ];

    /// <summary>
    /// A macOS .icns: a four-letter type and a big-endian length, then each image as a PNG under its
    /// own type and length — every length counting its own eight bytes. A size wanted twice is drawn
    /// once.
    /// </summary>
    public static byte[] EncodeIcns()
    {
        var drawn = new Dictionary<int, byte[]>();
        using var body = new MemoryStream();
        Span<byte> header = stackalloc byte[8];
        foreach ((string type, int pixels) in IcnsEntries)
        {
            if (!drawn.TryGetValue(pixels, out byte[]? png))
            {
                png = EncodePng(pixels);
                drawn[pixels] = png;
            }

            Encoding.ASCII.GetBytes(type, header);
            BinaryPrimitives.WriteInt32BigEndian(header[4..], png.Length + 8);
            body.Write(header);
            body.Write(png);
        }

        using var file = new MemoryStream();
        Encoding.ASCII.GetBytes("icns", header);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], (int)body.Length + 8);
        file.Write(header);
        body.WriteTo(file);
        return file.ToArray();
    }

    /// <summary>Winding-agnostic point-in-convex-polygon: every cross product on the same side.</summary>
    private static bool Inside(Vector2[] polygon, Vector2 point)
    {
        bool negative = false;
        bool positive = false;

        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
            float cross = (edge.X * (point.Y - polygon[i].Y)) - (edge.Y * (point.X - polygon[i].X));

            negative |= cross < 0f;
            positive |= cross > 0f;

            if (negative && positive)
            {
                return false;
            }
        }

        return true;
    }
}
