using System.Text;

namespace EditorApp.Core.Export.Mimicraft;

/// <summary>
/// The primitives Mimicraft's decoders expect (EditorCodec.md §1). Everything little-endian.
///
/// Small enough to inline anywhere, kept together because the two length encodings look identical
/// and are not interchangeable in the reader's mind: a varint counts a value, a string's prefix
/// counts UTF-8 bytes. Writing one where the other belongs produces a file that decodes for a while
/// and then fails somewhere unrelated.
/// </summary>
public static class MimicraftBinary
{
    /// <summary>The reader stops at five bytes; anything longer is rejected outright.</summary>
    public const int MaxVarintBytes = 5;

    /// <summary>LEB128, unsigned: seven bits of payload per byte, high bit means "more follows".</summary>
    public static void WriteVarint(List<byte> output, uint value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)(value | 0x80));
            value >>= 7;
        }

        output.Add((byte)value);
    }

    /// <summary>
    /// .NET <c>BinaryWriter.Write(string)</c>: a 7-bit encoded byte length, then UTF-8. The same
    /// layout as a varint, but counting bytes rather than meaning a number.
    /// </summary>
    public static void WriteString(List<byte> output, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteVarint(output, (uint)bytes.Length);
        output.AddRange(bytes);
    }

    public static void WriteAscii(List<byte> output, string value) =>
        output.AddRange(Encoding.ASCII.GetBytes(value));

    public static void WriteInt32(List<byte> output, int value) =>
        output.AddRange(BitConverter.GetBytes(value));

    public static void WriteInt16(List<byte> output, short value) =>
        output.AddRange(BitConverter.GetBytes(value));

    public static void WriteUInt16(List<byte> output, ushort value) =>
        output.AddRange(BitConverter.GetBytes(value));

    public static void WriteSingle(List<byte> output, float value) =>
        output.AddRange(BitConverter.GetBytes(value));
}
