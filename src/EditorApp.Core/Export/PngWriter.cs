using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EditorApp.Core.Export;

/// <summary>
/// Writes 8-bit RGBA PNGs. The only image this tool ever produces is a 128x128 palette texture, so
/// a hundred lines over the in-box <see cref="ZLibStream"/> beats taking on an imaging library and
/// its licence.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <param name="pixels">Row-major RGBA, 4 bytes per pixel, top row first.</param>
    public static byte[] EncodeRgba(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        }

        int expected = width * height * 4;
        if (pixels.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} bytes of RGBA, got {pixels.Length}.", nameof(pixels));
        }

        using var output = new MemoryStream(expected / 4);
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8;    // bit depth
        header[9] = 6;    // color type: truecolor with alpha
        header[10] = 0;   // deflate
        header[11] = 0;   // adaptive filtering
        header[12] = 0;   // no interlace
        WriteChunk(output, "IHDR", header);

        // Tag the data as sRGB: the palette holds color, not linear values (EditorApp.md §6).
        WriteChunk(output, "sRGB", [0]);

        WriteChunk(output, "IDAT", CompressScanlines(pixels, width, height));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    public static void WriteRgba(string path, ReadOnlySpan<byte> pixels, int width, int height) =>
        File.WriteAllBytes(path, EncodeRgba(pixels, width, height));

    private static byte[] CompressScanlines(ReadOnlySpan<byte> pixels, int width, int height)
    {
        int stride = width * 4;

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int row = 0; row < height; row++)
            {
                // Filter type 0 (none). Flat color blocks compress to almost nothing anyway, and an
                // unfiltered stream keeps the writer trivial to verify by hand.
                deflate.WriteByte(0);
                deflate.Write(pixels.Slice(row * stride, stride));
            }
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Accumulate(crc, type);
        crc = Accumulate(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Accumulate(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
