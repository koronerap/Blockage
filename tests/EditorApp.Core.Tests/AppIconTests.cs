using System.Buffers.Binary;
using System.Text;
using EditorApp.Core.Export;

namespace EditorApp.Core.Tests;

public class AppIconTests
{
    /// <summary>
    /// The .icns reads back as a Mac reads it: the file's length in its header, then every size in
    /// turn under its type code, each a PNG of exactly the pixels that type stands for.
    /// </summary>
    [Fact]
    public void IcnsHoldsEverySizeAMacAsksFor()
    {
        byte[] icns = AppIcon.EncodeIcns();

        Assert.Equal("icns", Encoding.ASCII.GetString(icns, 0, 4));
        Assert.Equal(icns.Length, BinaryPrimitives.ReadInt32BigEndian(icns.AsSpan(4)));

        int at = 8;
        foreach ((string type, int pixels) in AppIcon.IcnsEntries)
        {
            Assert.Equal(type, Encoding.ASCII.GetString(icns, at, 4));
            int length = BinaryPrimitives.ReadInt32BigEndian(icns.AsSpan(at + 4));
            ReadOnlySpan<byte> png = icns.AsSpan(at + 8, length - 8);

            Assert.True(png[..8].SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }), type);
            Assert.Equal("IHDR", Encoding.ASCII.GetString(png.Slice(12, 4)));
            Assert.Equal(pixels, BinaryPrimitives.ReadInt32BigEndian(png[16..]));
            Assert.Equal(pixels, BinaryPrimitives.ReadInt32BigEndian(png[20..]));
            at += length;
        }

        Assert.Equal(icns.Length, at);
    }
}
