using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export;

/// <summary>
/// The texture that goes with an unwrapped mesh: every quad's island filled with the colour it was
/// built in.
///
/// This is a starting point rather than the finished article, and that is the whole reason for it.
/// The palette texture was the model's colour and could never be anything else — 128x128 of flat
/// swatches, with nowhere to paint. Here each island is a real patch of surface at a real size, so
/// the file opens in a texture painter as the model's own layout with the block colours already
/// laid in underneath.
///
/// Islands are flooded out into their padding as well. A greedy quad is one colour by construction,
/// so the gutter costs nothing to fill and no amount of filtering or mip reduction can pull a
/// neighbouring island's colour across the seam.
/// </summary>
public static class AtlasTexture
{
    public const string DefaultFileName = "basecolor.png";

    /// <summary>Row-major RGBA pixels, top row first.</summary>
    public static byte[] CreateRgba(UvAtlas atlas, Palette palette)
    {
        int width = atlas.Width;
        int height = atlas.Height;
        var pixels = new byte[width * height * 4];
        var painted = new bool[width * height];

        // Anything no face covers. Magenta rather than black or transparent: unmapped texture is a
        // mistake somewhere, and it should look like one instead of like shadow.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 0;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        foreach (UvIsland island in atlas.Islands)
        {
            Color32 color = palette[island.PaletteIndex];

            for (int y = island.Y; y < island.Y + island.Height; y++)
            {
                int row = y * width;
                for (int x = island.X; x < island.X + island.Width; x++)
                {
                    int offset = (row + x) * 4;
                    pixels[offset] = color.R;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.B;
                    pixels[offset + 3] = 255;
                    painted[row + x] = true;
                }
            }
        }

        Dilate(pixels, painted, width, height, atlas.Padding);
        return pixels;
    }

    /// <summary>
    /// Grows the painted texels outwards a ring at a time.
    ///
    /// This is what fills the gutters, and it has to be a spread rather than a border drawn around
    /// each face: faces inside one chart are touching, so painting a margin around every one of them
    /// would paint over its neighbours. Spreading afterwards only ever reaches texels nothing owns —
    /// the gaps between charts, and the holes inside a chart where the surface is not solid — so
    /// filtering and mip levels find a face's own colour next to it instead of magenta.
    /// </summary>
    private static void Dilate(byte[] pixels, bool[] painted, int width, int height, int rings)
    {
        for (int ring = 0; ring < rings; ring++)
        {
            bool[] before = (bool[])painted.Clone();
            bool spread = false;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int cell = (y * width) + x;
                    if (before[cell])
                    {
                        continue;
                    }

                    int source = Neighbour(before, width, height, x, y);
                    if (source < 0)
                    {
                        continue;
                    }

                    Array.Copy(pixels, source * 4, pixels, cell * 4, 4);
                    painted[cell] = true;
                    spread = true;
                }
            }

            if (!spread)
            {
                return;
            }
        }
    }

    private static int Neighbour(bool[] painted, int width, int height, int x, int y)
    {
        if (x > 0 && painted[(y * width) + x - 1])
        {
            return (y * width) + x - 1;
        }

        if (x + 1 < width && painted[(y * width) + x + 1])
        {
            return (y * width) + x + 1;
        }

        if (y > 0 && painted[((y - 1) * width) + x])
        {
            return ((y - 1) * width) + x;
        }

        if (y + 1 < height && painted[((y + 1) * width) + x])
        {
            return ((y + 1) * width) + x;
        }

        return -1;
    }

    public static byte[] EncodePng(UvAtlas atlas, Palette palette) =>
        PngWriter.EncodeRgba(CreateRgba(atlas, palette), atlas.Width, atlas.Height);

    /// <summary>Import settings, written beside the mesh and shown in the export dialog.</summary>
    public static string ImportNotes(UvAtlas atlas) =>
        $"""
        {DefaultFileName} — generated base colour ({atlas.Width}x{atlas.Height})

          Filtering : Point / Nearest        (until you paint something that wants smoothing)
          Mipmaps   : fine to leave on       ({atlas.Padding}-texel gutters carry each island's own colour)
          Color     : sRGB                   (this is color data, not linear)
          Wrapping  : Clamp

        The mesh is unwrapped at {atlas.TexelsPerVoxel} texels to a voxel, in {atlas.Charts.Count}
        pieces: each one a connected run of faces lying in the same plane, so a wall arrives whole
        and a brush stroke crosses it without meeting a seam. The flat colours are only what the
        level was built in — paint over them.

        Colour is spread {atlas.Padding} texels past the edge of every piece, so filtering and mip
        levels cannot pull one piece's colour into another. Keep that margin if you repaint by hand.
        Magenta marks texture nothing samples.

        Keep the PNG next to the mesh file: the mesh references it by relative path.
        """;
}
