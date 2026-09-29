using System.IO.Compression;
using System.Text;

namespace EditorApp.Core.Export;

/// <summary>
/// One node of an FBX file: a name, its values in order, and the nodes under it — what the binary
/// format is built from, from the header's version to each mesh's vertex list.
/// </summary>
public sealed class FbxNode(string name, params object[] values)
{
    public string Name { get; } = name;

    public List<object> Values { get; } = [.. values];

    public List<FbxNode> Children { get; } = [];

    /// <summary>Adds a node under this one and returns it.</summary>
    public FbxNode Add(string name, params object[] values)
    {
        var node = new FbxNode(name, values);
        Children.Add(node);
        return node;
    }

    /// <summary>A property of a Properties70 list: its name, type, label and flags, then its values.</summary>
    public FbxNode Property(string name, string type, string label, string flags, params object[] values) =>
        Add("P", [name, type, label, flags, .. values]);

    /// <summary>Binary FBX names an object "name", a 0 and a 1, then its class — where the text form writes "Class::name".</summary>
    public static string ObjectName(string name, string kind) => $"{name}\u0000\u0001{kind}";
}

/// <summary>
/// Writes binary FBX 7.4 (Fullreleaseplan 8.5), laid out as Blender's own exporter lays it out, with
/// the fixed file id, creation time and footer it uses — which is what the programs that read FBX
/// accept without a timestamp of their own to check against.
/// </summary>
public static class FbxWriter
{
    public const int Version = 7400;

    private static readonly byte[] Head = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\u001a\0");

    private static readonly byte[] FootId = [0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e];

    private static readonly byte[] FootMagic = [0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b];

    /// <summary>The file id that goes with <see cref="CreationTime"/>.</summary>
    public static readonly byte[] FileId = [0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1];

    public const string CreationTime = "1970-01-01 10:00:00:000";

    /// <summary>A node with no values and nothing under it, or the end of a list of nodes: thirteen zeroes.</summary>
    private const int Sentinel = 13;

    public static void Write(Stream stream, IReadOnlyList<FbxNode> top)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Head);
        writer.Write(Version);

        WriteList(writer, top);

        writer.Write(FootId);
        writer.Write(new byte[4]);
        long at = stream.Position;
        long padding = ((at + 15) & ~15L) - at;
        writer.Write(new byte[padding == 0 ? 16 : padding]);
        writer.Write(Version);
        writer.Write(new byte[120]);
        writer.Write(FootMagic);
    }

    private static void WriteList(BinaryWriter writer, IReadOnlyList<FbxNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            WriteNode(writer, nodes[i], isLast: i == nodes.Count - 1);
        }

        writer.Write(new byte[Sentinel]);
    }

    private static void WriteNode(BinaryWriter writer, FbxNode node, bool isLast)
    {
        byte[] values = Values(node.Values);
        byte[] name = Encoding.UTF8.GetBytes(node.Name);

        long start = writer.BaseStream.Position;
        writer.Write(0u);
        writer.Write((uint)node.Values.Count);
        writer.Write((uint)values.Length);
        writer.Write((byte)name.Length);
        writer.Write(name);
        writer.Write(values);

        if (node.Children.Count > 0)
        {
            WriteList(writer, node.Children);
        }
        else if (node.Values.Count == 0 && !isLast)
        {
            writer.Write(new byte[Sentinel]);
        }

        long end = writer.BaseStream.Position;
        writer.BaseStream.Position = start;
        writer.Write((uint)end);
        writer.BaseStream.Position = end;
    }

    private static byte[] Values(List<object> values)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8);
        foreach (object value in values)
        {
            switch (value)
            {
                case bool flag:
                    writer.Write((byte)'C');
                    writer.Write((byte)(flag ? 1 : 0));
                    break;
                case short small:
                    writer.Write((byte)'Y');
                    writer.Write(small);
                    break;
                case int number:
                    writer.Write((byte)'I');
                    writer.Write(number);
                    break;
                case long big:
                    writer.Write((byte)'L');
                    writer.Write(big);
                    break;
                case float single:
                    writer.Write((byte)'F');
                    writer.Write(single);
                    break;
                case double real:
                    writer.Write((byte)'D');
                    writer.Write(real);
                    break;
                case string text:
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    writer.Write((byte)'S');
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    break;
                case byte[] raw:
                    writer.Write((byte)'R');
                    writer.Write(raw.Length);
                    writer.Write(raw);
                    break;
                case double[] reals:
                    WriteArray(writer, 'd', reals.Length, MemoryMarshalBytes(reals));
                    break;
                case int[] numbers:
                    WriteArray(writer, 'i', numbers.Length, MemoryMarshalBytes(numbers));
                    break;
                case long[] bigs:
                    WriteArray(writer, 'l', bigs.Length, MemoryMarshalBytes(bigs));
                    break;
                case float[] singles:
                    WriteArray(writer, 'f', singles.Length, MemoryMarshalBytes(singles));
                    break;
                default:
                    throw new ArgumentException($"FBX has no value of type {value.GetType().Name}.", nameof(values));
            }
        }

        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>An array, zlib-packed as FBX writers pack them once they are worth packing.</summary>
    private static void WriteArray(BinaryWriter writer, char code, int count, byte[] data)
    {
        writer.Write((byte)code);
        writer.Write(count);
        if (data.Length < 128)
        {
            writer.Write(0);
            writer.Write(data.Length);
            writer.Write(data);
            return;
        }

        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        writer.Write(1);
        writer.Write((int)packed.Length);
        writer.Write(packed.ToArray());
    }

    private static byte[] MemoryMarshalBytes<T>(T[] values) where T : unmanaged =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
}
