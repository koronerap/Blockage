using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export;

/// <summary>
/// Turns the palette into the exported texture (EditorApp.md §6). The data model already stores
/// color as a palette index, and that palette <i>is</i> the texture: one flat block per entry, and
/// every quad samples the exact center of its block.
///
/// Blocks are 8x8 rather than a single texel because of edge bleed: at 1x1, bilinear filtering and
/// mipmapping pull in the neighbouring entry's color and the model's colors run together as it
/// recedes. With a centered UV inside an 8x8 block, no neighbouring texel comes close for three or
/// four mip levels.
/// </summary>
public static class PaletteTexture
{
    public const int BlockSize = 8;
    public const int Columns = 16;
    public const int Rows = 16;
    public const int Width = Columns * BlockSize;    // 128
    public const int Height = Rows * BlockSize;      // 128

    /// <summary>The file name written next to the exported mesh.</summary>
    public const string DefaultFileName = "palette.png";

    /// <summary>
    /// UV of an index's block center, with the origin at the <b>top-left</b> — the glTF convention.
    /// OBJ counts V from the bottom, so <see cref="ObjExporter"/> flips it.
    /// </summary>
    public static Vector2 TexelCenterUv(byte paletteIndex)
    {
        int column = paletteIndex % Columns;
        int row = paletteIndex / Columns;

        float u = (column * BlockSize + BlockSize * 0.5f) / Width;
        float v = (row * BlockSize + BlockSize * 0.5f) / Height;
        return new Vector2(u, v);
    }

    /// <summary>Row-major RGBA pixels, top row first.</summary>
    public static byte[] CreateRgba(Palette palette)
    {
        var pixels = new byte[Width * Height * 4];

        for (int index = 0; index < Palette.Size; index++)
        {
            Color32 color = palette[index];

            // Index 0 is the empty marker and never appears on a quad, but leaving it transparent
            // would show up as a hole if anyone ever sampled it. Paint it opaque magenta instead so
            // a UV bug is obvious rather than invisible.
            if (index == Palette.EmptyIndex)
            {
                color = new Color32(255, 0, 255);
            }

            int blockX = index % Columns * BlockSize;
            int blockY = index / Columns * BlockSize;

            for (int y = 0; y < BlockSize; y++)
            {
                int rowStart = ((blockY + y) * Width + blockX) * 4;
                for (int x = 0; x < BlockSize; x++)
                {
                    int offset = rowStart + x * 4;
                    pixels[offset] = color.R;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.B;
                    pixels[offset + 3] = color.A;
                }
            }
        }

        return pixels;
    }

    public static byte[] EncodePng(Palette palette) =>
        PngWriter.EncodeRgba(CreateRgba(palette), Width, Height);

    public static void WritePng(Palette palette, string path) =>
        File.WriteAllBytes(path, EncodePng(palette));

    /// <summary>Import settings the texture expects, shown in the export dialog and written beside the mesh.</summary>
    public static string ImportNotes =>
        $"""
        {DefaultFileName} — generated palette texture ({Width}x{Height})

          Filtering : Point / Nearest        (voxel art wants hard edges)
          Mipmaps   : fine to leave on       ({BlockSize}x{BlockSize} blocks survive 3-4 levels without bleeding)
          Color     : sRGB                   (this is color data, not linear)
          Wrapping  : Clamp

        Every face samples the exact center of its palette block, so quad size never affects the
        color. Keep the PNG next to the mesh file: the mesh references it by relative path.
        """;
}
