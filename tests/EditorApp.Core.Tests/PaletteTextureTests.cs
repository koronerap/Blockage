using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class PngWriterTests
{
    [Fact]
    public void EncodedImageDecodesBackToTheOriginalPixels()
    {
        var random = new Random(99);
        var pixels = new byte[16 * 9 * 4];
        random.NextBytes(pixels);

        TestPngReader.Image image = TestPngReader.Decode(PngWriter.EncodeRgba(pixels, 16, 9));

        Assert.Equal(16, image.Width);
        Assert.Equal(9, image.Height);
        Assert.Equal(pixels, image.Rgba);
    }

    [Fact]
    public void ImageIsTaggedAsSrgb()
    {
        var pixels = new byte[4];
        TestPngReader.Image image = TestPngReader.Decode(PngWriter.EncodeRgba(pixels, 1, 1));
        Assert.Contains("sRGB", image.ChunkTypes);
    }

    [Fact]
    public void MismatchedBufferLengthIsRejected()
    {
        Assert.Throws<ArgumentException>(() => PngWriter.EncodeRgba(new byte[10], 4, 4));
    }
}

public class PaletteTextureTests
{
    private static Palette DistinctPalette()
    {
        var palette = new Palette();
        for (int i = 1; i < Palette.Size; i++)
        {
            // Every entry a different color, so a UV that lands one block over is caught.
            palette[i] = new Color32((byte)i, (byte)(255 - i), (byte)((i * 7) & 0xFF));
        }

        return palette;
    }

    [Fact]
    public void TextureIs128x128()
    {
        TestPngReader.Image image = TestPngReader.Decode(PaletteTexture.EncodePng(DistinctPalette()));
        Assert.Equal(128, image.Width);
        Assert.Equal(128, image.Height);
    }

    [Fact]
    public void EveryIndexSamplesItsOwnColorAtTheBlockCenter()
    {
        Palette palette = DistinctPalette();
        TestPngReader.Image image = TestPngReader.Decode(PaletteTexture.EncodePng(palette));

        for (int index = 1; index < Palette.Size; index++)
        {
            Vector2 uv = PaletteTexture.TexelCenterUv((byte)index);
            (byte r, byte g, byte b, byte a) = image.Sample(uv.X, uv.Y);

            Color32 expected = palette[index];
            Assert.Equal((expected.R, expected.G, expected.B, expected.A), (r, g, b, a));
        }
    }

    [Fact]
    public void NoNeighbouringColorComesNearTheSampledCenter()
    {
        // The reason blocks are 8x8 rather than 1x1: three texels in any direction from the center
        // must still be the same color, so bilinear filtering and several mip levels cannot bleed
        // a neighbouring palette entry into a face.
        Palette palette = DistinctPalette();
        TestPngReader.Image image = TestPngReader.Decode(PaletteTexture.EncodePng(palette));

        for (int index = 1; index < Palette.Size; index++)
        {
            Vector2 uv = PaletteTexture.TexelCenterUv((byte)index);
            int centerX = (int)(uv.X * image.Width);
            int centerY = (int)(uv.Y * image.Height);
            Color32 expected = palette[index];

            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    (byte r, byte g, byte b, byte a) = image.Pixel(centerX + dx, centerY + dy);
                    Assert.Equal((expected.R, expected.G, expected.B, expected.A), (r, g, b, a));
                }
            }
        }
    }

    [Fact]
    public void BlocksAreFilledEdgeToEdge()
    {
        Palette palette = DistinctPalette();
        TestPngReader.Image image = TestPngReader.Decode(PaletteTexture.EncodePng(palette));

        const int index = 200;
        Color32 expected = palette[index];
        int blockX = index % PaletteTexture.Columns * PaletteTexture.BlockSize;
        int blockY = index / PaletteTexture.Columns * PaletteTexture.BlockSize;

        for (int y = 0; y < PaletteTexture.BlockSize; y++)
        {
            for (int x = 0; x < PaletteTexture.BlockSize; x++)
            {
                (byte r, byte g, byte b, byte a) = image.Pixel(blockX + x, blockY + y);
                Assert.Equal((expected.R, expected.G, expected.B, expected.A), (r, g, b, a));
            }
        }
    }

    [Fact]
    public void EmptyIndexIsMagentaSoAUvBugIsVisible()
    {
        TestPngReader.Image image = TestPngReader.Decode(PaletteTexture.EncodePng(DistinctPalette()));
        Assert.Equal(((byte)255, (byte)0, (byte)255, (byte)255), image.Pixel(4, 4));
    }
}
