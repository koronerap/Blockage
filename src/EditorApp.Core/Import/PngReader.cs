using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Import;

/// <summary>A decoded image: row-major pixels, top row first.</summary>
public sealed class DecodedImage(int width, int height, Color32[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public Color32[] Pixels { get; } = pixels;

    public Color32 this[int x, int y] => Pixels[y * Width + x];

    /// <summary>Samples with wrapping, which is what a tiled pattern needs.</summary>
    public Color32 Tiled(int x, int y) => this[
        ((x % Width) + Width) % Width,
        ((y % Height) + Height) % Height];
}

/// <summary>Raised when an image cannot be decoded.</summary>
public sealed class ImageDecodeException(string message) : Exception(message);

/// <summary>
/// A PNG decoder for the formats a pattern is realistically saved in: 8 bits per channel,
/// greyscale, palette, RGB or RGBA, not interlaced.
///
/// Written rather than taken from a library for the same reason as the writer — the only images
/// this tool touches are small, and an imaging dependency brings a licence question with it.
/// </summary>
public static class PngReader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static DecodedImage Decode(string path)
    {
        try
        {
            return Decode(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ImageDecodeException($"Could not read {Path.GetFileName(path)}: {exception.Message}");
        }
    }

    public static DecodedImage Decode(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new ImageDecodeException("Not a PNG file.");
        }

        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        Color32[]? palette = null;
        byte[]? transparency = null;

        using var compressed = new MemoryStream();

        int offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (length < 0 || offset + 12 + length > png.Length)
            {
                throw new ImageDecodeException("Truncated PNG chunk.");
            }

            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    bitDepth = data[8];
                    colorType = data[9];

                    if (data[12] != 0)
                    {
                        throw new ImageDecodeException("Interlaced PNGs are not supported.");
                    }

                    if (bitDepth != 8)
                    {
                        throw new ImageDecodeException($"Only 8-bit PNGs are supported (this one is {bitDepth}-bit).");
                    }

                    break;

                case "PLTE":
                    palette = new Color32[data.Length / 3];
                    for (int i = 0; i < palette.Length; i++)
                    {
                        palette[i] = new Color32(data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
                    }

                    break;

                case "tRNS":
                    transparency = data.ToArray();
                    break;

                case "IDAT":
                    compressed.Write(data);
                    break;
            }

            offset += 12 + length;
        }

        if (width <= 0 || height <= 0)
        {
            throw new ImageDecodeException("PNG has no image header.");
        }

        int channels = ChannelCount(colorType);
        compressed.Position = 0;

        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);

        return BuildImage(raw.ToArray(), width, height, channels, colorType, palette, transparency);
    }

    private static int ChannelCount(int colorType) => colorType switch
    {
        0 => 1,   // greyscale
        2 => 3,   // truecolor
        3 => 1,   // palette index
        4 => 2,   // greyscale + alpha
        6 => 4,   // truecolor + alpha
        _ => throw new ImageDecodeException($"Unsupported PNG colour type {colorType}."),
    };

    private static DecodedImage BuildImage(
        byte[] scanlines,
        int width,
        int height,
        int channels,
        int colorType,
        Color32[]? palette,
        byte[]? transparency)
    {
        int stride = width * channels;
        if (scanlines.Length < height * (stride + 1))
        {
            throw new ImageDecodeException("PNG image data is shorter than its header claims.");
        }

        var previous = new byte[stride];
        var current = new byte[stride];
        var pixels = new Color32[width * height];

        for (int row = 0; row < height; row++)
        {
            int source = row * (stride + 1);
            byte filter = scanlines[source];
            Array.Copy(scanlines, source + 1, current, 0, stride);

            Unfilter(filter, current, previous, channels);

            for (int x = 0; x < width; x++)
            {
                pixels[row * width + x] = ToColor(current, x * channels, colorType, palette, transparency);
            }

            (previous, current) = (current, previous);
        }

        return new DecodedImage(width, height, pixels);
    }

    /// <summary>
    /// Reverses the per-row filter. Each byte is a delta from some neighbour, so a row can only be
    /// decoded after the one above it.
    /// </summary>
    private static void Unfilter(byte filter, byte[] current, byte[] previous, int bpp)
    {
        switch (filter)
        {
            case 0:
                break;

            case 1:
                for (int i = bpp; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + current[i - bpp]);
                }

                break;

            case 2:
                for (int i = 0; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + previous[i]);
                }

                break;

            case 3:
                for (int i = 0; i < current.Length; i++)
                {
                    int left = i >= bpp ? current[i - bpp] : 0;
                    current[i] = (byte)(current[i] + ((left + previous[i]) >> 1));
                }

                break;

            case 4:
                for (int i = 0; i < current.Length; i++)
                {
                    int left = i >= bpp ? current[i - bpp] : 0;
                    int above = previous[i];
                    int aboveLeft = i >= bpp ? previous[i - bpp] : 0;
                    current[i] = (byte)(current[i] + Paeth(left, above, aboveLeft));
                }

                break;

            default:
                throw new ImageDecodeException($"Unknown PNG row filter {filter}.");
        }
    }

    /// <summary>Picks whichever neighbour the gradient predicts best.</summary>
    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static Color32 ToColor(byte[] row, int offset, int colorType, Color32[]? palette, byte[]? transparency) =>
        colorType switch
        {
            0 => new Color32(row[offset], row[offset], row[offset]),
            2 => new Color32(row[offset], row[offset + 1], row[offset + 2]),
            3 => FromPalette(row[offset], palette, transparency),
            4 => new Color32(row[offset], row[offset], row[offset], row[offset + 1]),
            _ => new Color32(row[offset], row[offset + 1], row[offset + 2], row[offset + 3]),
        };

    private static Color32 FromPalette(byte index, Color32[]? palette, byte[]? transparency)
    {
        if (palette is null || index >= palette.Length)
        {
            throw new ImageDecodeException("PNG uses a palette index with no matching PLTE entry.");
        }

        Color32 color = palette[index];
        byte alpha = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
        return color with { A = alpha };
    }
}
