using EditorApp.Core.Import;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// A tiled image used by Paint's Pattern sub-mode (EditorApp.md, "Paint"). The flood fill is the
/// same as Bucket's; only the colour each voxel receives differs.
///
/// The image is projected flat onto the plane of the clicked face, with the clicked voxel landing
/// on the pattern's top-left pixel — a voxel surface has no UVs of its own, so a planar projection
/// along the face normal is the only mapping that stays predictable as the fill spreads.
///
/// Sampled colours are matched to the nearest palette entry rather than added to the palette:
/// voxels store an index, and a pattern must not silently consume the 256 slots a level has.
/// </summary>
public sealed class PatternSource
{
    private readonly DecodedImage _image;
    private readonly Dictionary<int, byte> _matchCache = new();

    private PatternSource(string name, DecodedImage image)
    {
        Name = name;
        _image = image;
    }

    public string Name { get; }

    public int Width => _image.Width;

    public int Height => _image.Height;

    public static PatternSource Load(string path) =>
        new(Path.GetFileName(path), PngReader.Decode(path));

    public static PatternSource FromImage(string name, DecodedImage image) => new(name, image);

    /// <summary>
    /// The palette index for a voxel, given where the fill started and which way the surface faces.
    /// </summary>
    public byte Sample(Palette palette, Int3 voxel, Int3 seed, Face face)
    {
        (int u, int v) = Project(voxel - seed, face);
        Color32 colour = _image.Tiled(u, v);
        return NearestIndex(palette, colour);
    }

    /// <summary>
    /// The palette entry for a point of the image, <paramref name="u"/> across from the left and
    /// <paramref name="v"/> down from the top, both 0 to 1 — or null where the image is see-through,
    /// so a stencil of a sign paints the sign and not the space round it.
    /// </summary>
    public byte? SampleAt(Palette palette, float u, float v)
    {
        int x = Math.Clamp((int)MathF.Floor(u * Width), 0, Width - 1);
        int y = Math.Clamp((int)MathF.Floor(v * Height), 0, Height - 1);
        Color32 colour = _image[x, y];
        return colour.A < 128 ? null : NearestIndex(palette, colour);
    }

    /// <summary>
    /// Drops the axis the surface faces along and keeps the other two, so the image lies flat on
    /// the surface being painted.
    /// </summary>
    private static (int U, int V) Project(Int3 offset, Face face) => FaceInfo.Axis(face) switch
    {
        0 => (offset.Z, -offset.Y),
        1 => (offset.X, offset.Z),
        _ => (offset.X, -offset.Y),
    };

    /// <summary>
    /// Nearest palette entry by squared RGB distance. Cached per colour, because a fill asks the
    /// same handful of questions thousands of times.
    /// </summary>
    private byte NearestIndex(Palette palette, Color32 colour)
    {
        int key = (int)(colour.Rgba & 0x00FFFFFF);
        if (_matchCache.TryGetValue(key, out byte cached))
        {
            return cached;
        }

        byte best = 1;
        int bestDistance = int.MaxValue;

        for (int index = 1; index < Palette.Size; index++)
        {
            Color32 candidate = palette[index];

            int dr = candidate.R - colour.R;
            int dg = candidate.G - colour.G;
            int db = candidate.B - colour.B;
            int distance = dr * dr + dg * dg + db * db;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (byte)index;

                if (distance == 0)
                {
                    break;
                }
            }
        }

        _matchCache[key] = best;
        return best;
    }

    /// <summary>Forgets matched colours, so a palette edit is picked up by the next fill.</summary>
    public void InvalidateMatches() => _matchCache.Clear();
}
