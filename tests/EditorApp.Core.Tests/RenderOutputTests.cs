using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Turntables, sprite sheets and the GIF they can be (Fullreleaseplan 5.5).</summary>
public class RenderOutputTests
{
    /// <summary>A GIF decoder just good enough to read back what <see cref="GifWriter"/> wrote: frames of palette indices.</summary>
    private static (int Width, int Height, byte[] Palette, List<byte[]> Frames, List<int> Delays) ReadGif(byte[] gif)
    {
        Assert.Equal("GIF89a"u8.ToArray(), gif[..6]);
        int width = gif[6] | (gif[7] << 8);
        int height = gif[8] | (gif[9] << 8);
        byte[] palette = gif[13..(13 + 768)];
        int at = 13 + 768;
        var frames = new List<byte[]>();
        var delays = new List<int>();

        while (gif[at] != 0x3B)
        {
            if (gif[at] == 0x21)
            {
                if (gif[at + 1] == 0xF9)
                {
                    delays.Add(gif[at + 4] | (gif[at + 5] << 8));
                }

                at += 2;
                while (gif[at] != 0)
                {
                    at += gif[at] + 1;
                }

                at++;
                continue;
            }

            Assert.Equal(0x2C, gif[at]);
            at += 10;
            int minimum = gif[at++];
            var data = new List<byte>();
            while (gif[at] != 0)
            {
                data.AddRange(gif[(at + 1)..(at + 1 + gif[at])]);
                at += gif[at] + 1;
            }

            at++;
            frames.Add(Decode([.. data], minimum, width * height));
        }

        return (width, height, palette, frames, delays);
    }

    private static byte[] Decode(byte[] data, int minimum, int count)
    {
        int clear = 1 << minimum;
        int end = clear + 1;
        int bits = minimum + 1;
        var table = new List<byte[]>();
        void Reset()
        {
            table.Clear();
            for (int i = 0; i < clear + 2; i++)
            {
                table.Add([(byte)i]);
            }

            bits = minimum + 1;
        }

        Reset();
        var output = new List<byte>(count);
        int position = 0;
        byte[]? previous = null;

        while (true)
        {
            int code = 0;
            for (int i = 0; i < bits; i++, position++)
            {
                code |= ((data[position >> 3] >> (position & 7)) & 1) << i;
            }

            if (code == clear)
            {
                Reset();
                previous = null;
                continue;
            }

            if (code == end)
            {
                break;
            }

            byte[] entry = code < table.Count ? table[code] : [.. previous!, previous![0]];
            output.AddRange(entry);
            if (previous is not null && table.Count < 4096)
            {
                table.Add([.. previous, entry[0]]);
            }

            previous = entry;
            if (table.Count == (1 << bits) && bits < 12)
            {
                bits++;
            }
        }

        return [.. output];
    }

    [Fact]
    public void AGifReadsBackAsTheFramesItWasMadeOf()
    {
        const int Size = 40;
        var frames = new List<byte[]>();
        for (int f = 0; f < 3; f++)
        {
            var frame = new byte[Size * Size * 4];
            for (int i = 0; i < Size * Size; i++)
            {
                // Stripes that move from frame to frame, and a see-through corner.
                bool clear = i % Size < 5 && i / Size < 5;
                byte shade = (byte)(((i % Size) + (f * 7)) % 4 * 80);
                frame[(i * 4) + 0] = shade;
                frame[(i * 4) + 1] = (byte)(255 - shade);
                frame[(i * 4) + 2] = 40;
                frame[(i * 4) + 3] = clear ? (byte)0 : (byte)255;
            }

            frames.Add(frame);
        }

        using var stream = new MemoryStream();
        GifWriter.Write(stream, frames, Size, Size, 4);
        (int width, int height, byte[] palette, List<byte[]> decoded, List<int> delays) = ReadGif(stream.ToArray());

        Assert.Equal(Size, width);
        Assert.Equal(Size, height);
        Assert.Equal(3, decoded.Count);
        Assert.All(delays, delay => Assert.Equal(4, delay));

        for (int f = 0; f < 3; f++)
        {
            Assert.Equal(Size * Size, decoded[f].Length);
            for (int i = 0; i < Size * Size; i++)
            {
                byte index = decoded[f][i];
                if (frames[f][(i * 4) + 3] == 0)
                {
                    Assert.Equal(255, index);
                    continue;
                }

                // Four shades are few enough to come out as they went in.
                Assert.InRange(Math.Abs(palette[index * 3] - frames[f][i * 4]), 0, 8);
                Assert.InRange(Math.Abs(palette[(index * 3) + 1] - frames[f][(i * 4) + 1]), 0, 8);
            }
        }
    }

    [Fact]
    public void LzwCodesGrowAndTheTableIsClearedWhenFull()
    {
        // Noise fills the table many times over, so every width and the clear are gone through.
        var random = new Random(3);
        var indices = new byte[200_000];
        random.NextBytes(indices);

        byte[] compressed = GifWriter.Lzw(indices, 8);

        Assert.Equal(indices, Decode(compressed, 8, indices.Length));
    }

    [Fact]
    public void ATurntableGoesOnceRoundTheMiddle()
    {
        var start = new RenderCamera(new Vector3(10f, 5f, 0f), -Vector3.UnitX, Vector3.UnitY, 40f, false, 0f);
        RenderCamera[] frames = RenderOutputs.Orbit(start, Vector3.Zero, 4);

        Assert.Equal(start, frames[0]);
        Assert.All(frames, frame => Assert.Equal(new Vector2(10f, 5f).Length(), frame.Position.Length(), 3));
        Assert.All(frames, frame => Assert.True(Vector3.Dot(Vector3.Normalize(-frame.Position with { Y = 0f }), frame.Forward) > 0.99f));

        // A quarter of the way round, and all the four sides seen.
        Assert.True(Vector3.Distance(new Vector3(0f, 5f, 10f), frames[1].Position) < 1e-3f || Vector3.Distance(new Vector3(0f, 5f, -10f), frames[1].Position) < 1e-3f);
        Assert.True(Vector3.Distance(new Vector3(-10f, 5f, 0f), frames[2].Position) < 1e-3f);
    }

    [Fact]
    public void TheMiddleOfTheLevelIsTheMiddleOfWhatIsShown()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 1);
        grid.SetVoxel(9, 3, 1, 1);
        scene.Add(grid, ObjectTransform.At(new Vector3(10f, 0f, 0f)), "Block");

        Assert.Equal(new Vector3(15f, 2f, 1f), RenderOutputs.LevelCentre(scene));
        Assert.Equal(Vector3.Zero, RenderOutputs.LevelCentre(new VoxelScene()));
    }

    [Fact]
    public void ASheetLaysSpritesLeftToRightThenDown()
    {
        byte[] Solid(byte value) => Enumerable.Repeat(value, 2 * 2 * 4).ToArray();
        (byte[] sheet, int width, int height) = RenderOutputs.Sheet([Solid(10), Solid(20), Solid(30)], 2, 2, 2);

        Assert.Equal(4, width);
        Assert.Equal(4, height);
        Assert.Equal(10, sheet[0]);
        Assert.Equal(20, sheet[2 * 4]);
        Assert.Equal(30, sheet[(2 * width) * 4]);

        // The cell after the last sprite is left see-through.
        Assert.Equal(0, sheet[((2 * width) + 2) * 4 + 3]);
    }
}
