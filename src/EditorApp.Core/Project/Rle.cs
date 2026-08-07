namespace EditorApp.Core.Project;

/// <summary>
/// Run-length coding for a chunk's palette-index array (EditorApp.md §7). Voxel chunks are mostly
/// long runs of one value — empty space, a solid interior — so an empty or uniform 32768-byte chunk
/// collapses to three bytes. The zip container's deflate then handles anything RLE does badly.
/// </summary>
public static class Rle
{
    // A run is [ushort count][byte value]. Counts run to 65535 so a uniform chunk is a single run;
    // a byte-sized count would need 129 runs for the same chunk.
    private const int RunHeaderSize = 3;

    public static byte[] Encode(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(Math.Min(data.Length, 1024));

        int index = 0;
        while (index < data.Length)
        {
            byte value = data[index];
            int run = 1;
            while (index + run < data.Length && data[index + run] == value && run < ushort.MaxValue)
            {
                run++;
            }

            output.Add((byte)(run & 0xFF));
            output.Add((byte)(run >> 8));
            output.Add(value);
            index += run;
        }

        return [.. output];
    }

    /// <summary>Decodes into a buffer of known size. Throws when the stream does not fill it exactly.</summary>
    public static void Decode(ReadOnlySpan<byte> encoded, Span<byte> destination)
    {
        int written = 0;

        for (int offset = 0; offset + RunHeaderSize <= encoded.Length; offset += RunHeaderSize)
        {
            int run = encoded[offset] | (encoded[offset + 1] << 8);
            byte value = encoded[offset + 2];

            if (run == 0 || written + run > destination.Length)
            {
                throw new VxLevelFormatException(
                    $"Corrupt run-length data: run of {run} at output offset {written} does not fit {destination.Length} bytes.");
            }

            destination.Slice(written, run).Fill(value);
            written += run;
        }

        if (written != destination.Length)
        {
            throw new VxLevelFormatException(
                $"Corrupt run-length data: decoded {written} bytes, expected {destination.Length}.");
        }
    }

    public static byte[] Decode(ReadOnlySpan<byte> encoded, int length)
    {
        var destination = new byte[length];
        Decode(encoded, destination);
        return destination;
    }
}
