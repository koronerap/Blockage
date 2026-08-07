using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EditorApp.Core.Tests;

/// <summary>
/// A minimal PNG reader used only by the tests, so the writer is verified against something other
/// than itself. Handles exactly what <c>PngWriter</c> emits: 8-bit RGBA, no interlacing, filter 0.
/// </summary>
public static class TestPngReader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public sealed record Image(int Width, int Height, byte[] Rgba, IReadOnlyList<string> ChunkTypes)
    {
        public (byte R, byte G, byte B, byte A) Pixel(int x, int y)
        {
            int offset = (y * Width + x) * 4;
            return (Rgba[offset], Rgba[offset + 1], Rgba[offset + 2], Rgba[offset + 3]);
        }

        /// <param name="u">0..1 from the left.</param>
        /// <param name="v">0..1 from the top.</param>
        public (byte R, byte G, byte B, byte A) Sample(float u, float v) =>
            Pixel(
                Math.Clamp((int)(u * Width), 0, Width - 1),
                Math.Clamp((int)(v * Height), 0, Height - 1));
    }

    public static Image Decode(byte[] png)
    {
        Assert.True(png.Length > Signature.Length, "PNG is too short.");
        Assert.Equal(Signature, png[..Signature.Length]);

        int width = 0;
        int height = 0;
        var chunkTypes = new List<string>();
        using var idat = new MemoryStream();

        int offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);

            uint declaredCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4));
            Assert.Equal(Crc32(png.AsSpan(offset + 4, 4 + length)), declaredCrc);

            chunkTypes.Add(type);

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    Assert.Equal(8, data[8]);    // bit depth
                    Assert.Equal(6, data[9]);    // RGBA
                    Assert.Equal(0, data[10]);
                    Assert.Equal(0, data[11]);
                    Assert.Equal(0, data[12]);   // not interlaced
                    break;

                case "IDAT":
                    idat.Write(data);
                    break;
            }

            offset += 12 + length;
        }

        Assert.Contains("IHDR", chunkTypes);
        Assert.Contains("IEND", chunkTypes);

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);

        byte[] scanlines = raw.ToArray();
        int stride = width * 4;
        Assert.Equal(height * (stride + 1), scanlines.Length);

        var rgba = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            int source = row * (stride + 1);
            Assert.Equal(0, scanlines[source]);   // filter type "none"
            Array.Copy(scanlines, source + 1, rgba, row * stride, stride);
        }

        return new Image(width, height, rgba, chunkTypes);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
