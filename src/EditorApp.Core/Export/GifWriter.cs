namespace EditorApp.Core.Export;

/// <summary>
/// An animated GIF from RGBA frames (Fullreleaseplan 5.5, the turntable): one palette for every
/// frame, so the colours hold still while the level turns, cut from all of them by median cut; a
/// light ordered dither, fixed to the pixel grid so it does not crawl from frame to frame, against
/// the bands a smooth sky would otherwise fall into; a see-through background kept as GIF's one
/// transparent colour; looping forever.
/// </summary>
public static class GifWriter
{
    /// <summary>Bits a channel keeps in the histogram the palette is cut from.</summary>
    private const int Bits = 6;

    private const int Levels = 1 << Bits;

    /// <summary>How far, in 8-bit steps, the dither nudges a colour either way.</summary>
    private const float DitherSpread = 5f;

    private static readonly int[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>Palette entries left for colours; the last one is kept for see-through pixels.</summary>
    private const int Colours = 255;

    private const int TransparentIndex = 255;

    /// <summary>
    /// Writes <paramref name="frames"/>, each <paramref name="width"/> by <paramref name="height"/>
    /// RGBA, rows from the top, shown <paramref name="delayCentiseconds"/> hundredths of a second each.
    /// </summary>
    public static void Write(Stream stream, IReadOnlyList<byte[]> frames, int width, int height, int delayCentiseconds)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("A GIF needs at least one frame.", nameof(frames));
        }

        bool anyClear = frames.Any(frame => HasClear(frame));
        (byte[] palette, int count) = Quantize(frames);
        var nearest = new short[Levels * Levels * Levels];
        Array.Fill(nearest, (short)-1);

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("GIF89a"u8);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0xF7);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(palette);

        // Loop forever.
        writer.Write([0x21, 0xFF, 0x0B]);
        writer.Write("NETSCAPE2.0"u8);
        writer.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        var indices = new byte[width * height];
        foreach (byte[] frame in frames)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                int p = i * 4;
                if (frame[p + 3] < 128)
                {
                    indices[i] = TransparentIndex;
                    continue;
                }

                float nudge = ((Bayer[((i / width) % 4 * 4) + (i % width % 4)] + 0.5f) / 16f - 0.5f) * 2f * DitherSpread;
                int bin = Bin(Nudged(frame[p], nudge), Nudged(frame[p + 1], nudge), Nudged(frame[p + 2], nudge));
                if (nearest[bin] < 0)
                {
                    nearest[bin] = Nearest(palette, count, bin);
                }

                indices[i] = (byte)nearest[bin];
            }

            // Each frame put down on a cleared canvas when there is anything see-through, so the
            // last frame does not show through the holes in the next.
            byte packed = anyClear ? (byte)((2 << 2) | 1) : (byte)(1 << 2);
            writer.Write([0x21, 0xF9, 0x04, packed]);
            writer.Write((ushort)delayCentiseconds);
            writer.Write((byte)TransparentIndex);
            writer.Write((byte)0);

            writer.Write((byte)0x2C);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)0);

            writer.Write((byte)8);
            byte[] compressed = Lzw(indices, 8);
            for (int offset = 0; offset < compressed.Length; offset += 255)
            {
                int length = Math.Min(255, compressed.Length - offset);
                writer.Write((byte)length);
                writer.Write(compressed, offset, length);
            }

            writer.Write((byte)0);
        }

        writer.Write((byte)0x3B);
    }

    private static bool HasClear(byte[] frame)
    {
        for (int i = 3; i < frame.Length; i += 4)
        {
            if (frame[i] < 128)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A colour's cell in the histogram: <see cref="Bits"/> bits a channel.</summary>
    private static int Bin(byte r, byte g, byte b) =>
        ((r >> (8 - Bits)) << (2 * Bits)) | ((g >> (8 - Bits)) << Bits) | (b >> (8 - Bits));

    private static byte Nudged(byte value, float nudge) => (byte)Math.Clamp(value + nudge, 0f, 255f);

    /// <summary>The palette entry nearest a histogram cell's middle.</summary>
    private static short Nearest(byte[] palette, int count, int bin)
    {
        int half = 1 << (7 - Bits);
        int r = (Channel(bin, 0) << (8 - Bits)) + half;
        int g = (Channel(bin, 1) << (8 - Bits)) + half;
        int b = (Channel(bin, 2) << (8 - Bits)) + half;
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int dr = palette[i * 3] - r, dg = palette[(i * 3) + 1] - g, db = palette[(i * 3) + 2] - b;
            int distance = (dr * dr * 3) + (dg * dg * 4) + (db * db * 2);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return (short)best;
    }

    /// <summary>
    /// Median cut over every opaque pixel of every frame: the box of colours with the most pixels
    /// and room to split is cut across its longest side at the median, until there are enough boxes.
    /// Each box's colour is its pixels' mean.
    /// </summary>
    public static (byte[] Palette, int Count) Quantize(IReadOnlyList<byte[]> frames)
    {
        var counts = new long[Levels * Levels * Levels];
        var sums = new long[counts.Length * 3];
        foreach (byte[] frame in frames)
        {
            for (int p = 0; p < frame.Length; p += 4)
            {
                if (frame[p + 3] >= 128)
                {
                    int bin = Bin(frame[p], frame[p + 1], frame[p + 2]);
                    counts[bin]++;
                    sums[bin * 3] += frame[p];
                    sums[(bin * 3) + 1] += frame[p + 1];
                    sums[(bin * 3) + 2] += frame[p + 2];
                }
            }
        }

        var boxes = new List<List<int>> { Enumerable.Range(0, counts.Length).Where(bin => counts[bin] > 0).ToList() };
        if (boxes[0].Count == 0)
        {
            boxes[0].Add(0);
        }

        while (boxes.Count < Colours)
        {
            int widest = -1;
            long best = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2)
                {
                    continue;
                }

                long pixels = boxes[i].Sum(bin => counts[bin]);
                long score = pixels * Span(boxes[i]).Range;
                if (score > best)
                {
                    best = score;
                    widest = i;
                }
            }

            if (widest < 0)
            {
                break;
            }

            List<int> box = boxes[widest];
            int axis = Span(box).Axis;
            box.Sort((a, b) => Channel(a, axis).CompareTo(Channel(b, axis)));

            long half = box.Sum(bin => counts[bin]) / 2;
            long running = 0;
            int cut = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                running += counts[box[i]];
                cut = i + 1;
                if (running >= half)
                {
                    break;
                }
            }

            boxes[widest] = box.GetRange(0, cut);
            boxes.Add(box.GetRange(cut, box.Count - cut));
        }

        // Each box's colour: the mean of the very pixels in it, not of the cells' middles.
        var palette = new byte[256 * 3];
        for (int index = 0; index < boxes.Count; index++)
        {
            long total = 0, r = 0, g = 0, b = 0;
            foreach (int bin in boxes[index])
            {
                total += counts[bin];
                r += sums[bin * 3];
                g += sums[(bin * 3) + 1];
                b += sums[(bin * 3) + 2];
            }

            total = Math.Max(total, 1);
            palette[(index * 3) + 0] = (byte)((r + (total / 2)) / total);
            palette[(index * 3) + 1] = (byte)((g + (total / 2)) / total);
            palette[(index * 3) + 2] = (byte)((b + (total / 2)) / total);
        }

        return (palette, boxes.Count);
    }

    private static int Channel(int bin, int axis) => axis switch
    {
        0 => (bin >> (2 * Bits)) & (Levels - 1),
        1 => (bin >> Bits) & (Levels - 1),
        _ => bin & (Levels - 1),
    };

    private static (int Axis, int Range) Span(List<int> box)
    {
        int bestAxis = 0;
        int bestRange = -1;
        for (int axis = 0; axis < 3; axis++)
        {
            int min = Levels - 1, max = 0;
            foreach (int bin in box)
            {
                int value = Channel(bin, axis);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            if (max - min > bestRange)
            {
                bestRange = max - min;
                bestAxis = axis;
            }
        }

        return (bestAxis, bestRange);
    }

    /// <summary>
    /// GIF's variable-width LZW: codes start a bit wider than the pixels, grow as the table fills,
    /// and the table is cleared and begun again when it reaches 4096 entries — step for step as a
    /// decoder follows it.
    /// </summary>
    public static byte[] Lzw(byte[] indices, int minimumCodeSize)
    {
        var output = new List<byte>(indices.Length / 2);
        int clear = 1 << minimumCodeSize;
        int end = clear + 1;
        int bits = minimumCodeSize + 1;
        int maxCode = (1 << bits) - 1;
        int next = clear + 2;
        bool clearing = false;
        int buffer = 0;
        int buffered = 0;
        var table = new Dictionary<int, int>();

        void Output(int code)
        {
            buffer |= code << buffered;
            buffered += bits;
            while (buffered >= 8)
            {
                output.Add((byte)(buffer & 0xFF));
                buffer >>= 8;
                buffered -= 8;
            }

            if (next > maxCode || clearing)
            {
                if (clearing)
                {
                    bits = minimumCodeSize + 1;
                    maxCode = (1 << bits) - 1;
                    clearing = false;
                }
                else
                {
                    bits++;
                    maxCode = bits == 12 ? 4096 : (1 << bits) - 1;
                }
            }

            if (code == end && buffered > 0)
            {
                output.Add((byte)(buffer & 0xFF));
                buffer = 0;
                buffered = 0;
            }
        }

        Output(clear);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int pixel = indices[i];
            int key = (prefix << 8) | pixel;
            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            Output(prefix);
            prefix = pixel;
            if (next < 4096)
            {
                table[key] = next++;
            }
            else
            {
                table.Clear();
                next = clear + 2;
                clearing = true;
                Output(clear);
            }
        }

        Output(prefix);
        Output(end);
        return [.. output];
    }
}
