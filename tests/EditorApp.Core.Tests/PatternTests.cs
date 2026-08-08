using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Import;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class PngReaderTests
{
    private static byte[] Encode(Color32[,] pixels)
    {
        int width = pixels.GetLength(0);
        int height = pixels.GetLength(1);

        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                Color32 c = pixels[x, y];
                rgba[offset] = c.R;
                rgba[offset + 1] = c.G;
                rgba[offset + 2] = c.B;
                rgba[offset + 3] = c.A;
            }
        }

        return PngWriter.EncodeRgba(rgba, width, height);
    }

    [Fact]
    public void OurOwnWriterRoundTripsThroughTheReader()
    {
        var random = new Random(7);
        var pixels = new Color32[9, 5];
        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                pixels[x, y] = new Color32(
                    (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
        }

        DecodedImage image = PngReader.Decode(Encode(pixels));

        Assert.Equal(9, image.Width);
        Assert.Equal(5, image.Height);

        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                Assert.Equal(pixels[x, y], image[x, y]);
            }
        }
    }

    [Fact]
    public void ThePaletteTextureItselfDecodes()
    {
        // The one image this tool actually produces, read back by the one decoder it has.
        Palette palette = Palette.CreateDefault();
        DecodedImage image = PngReader.Decode(PaletteTexture.EncodePng(palette));

        Assert.Equal(PaletteTexture.Width, image.Width);
        Assert.Equal(PaletteTexture.Height, image.Height);

        // Centre of the block for index 20 must be that entry's colour.
        System.Numerics.Vector2 uv = PaletteTexture.TexelCenterUv(20);
        Color32 sampled = image[(int)(uv.X * image.Width), (int)(uv.Y * image.Height)];

        Assert.Equal(palette[20] with { A = 255 }, sampled);
    }

    [Fact]
    public void TilingWrapsInBothDirections()
    {
        var pixels = new Color32[2, 2];
        pixels[0, 0] = new Color32(10, 0, 0);
        pixels[1, 0] = new Color32(20, 0, 0);
        pixels[0, 1] = new Color32(30, 0, 0);
        pixels[1, 1] = new Color32(40, 0, 0);

        DecodedImage image = PngReader.Decode(Encode(pixels));

        Assert.Equal(image[0, 0], image.Tiled(2, 2));
        Assert.Equal(image[1, 1], image.Tiled(-1, -1));
        Assert.Equal(image[1, 0], image.Tiled(-1, 4));
    }

    [Fact]
    public void ANonPngIsRejected()
    {
        Assert.Throws<ImageDecodeException>(() => PngReader.Decode("not a png"u8.ToArray()));
    }

    [Fact]
    public void ATruncatedFileIsRejected()
    {
        byte[] png = Encode(new Color32[2, 2]);
        Assert.Throws<ImageDecodeException>(() => PngReader.Decode(png[..(png.Length / 2)]));
    }
}

public class PatternPaintTests
{
    /// <summary>
    /// A blank palette, so a test's own entries are the only candidates. The default palette
    /// already contains pure red at index 20, and nearest-match quite correctly stops at the first
    /// exact hit — which says nothing about whether the projection is right.
    /// </summary>
    private static VoxelWorld Plate(int width, int depth, byte index = 5)
    {
        var grid = new VoxelWorld();
        grid.ReplacePalette(new Palette());

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                grid.SetVoxel(x, 0, z, index);
            }
        }

        return grid;
    }

    /// <summary>A 2x2 chequer of two palette colours, so where each pixel lands is checkable.</summary>
    private static PatternSource Chequer(Palette palette)
    {
        palette[100] = new Color32(255, 0, 0);
        palette[101] = new Color32(0, 0, 255);

        var pixels = new Color32[4];
        pixels[0] = new Color32(255, 0, 0);     // (0,0)
        pixels[1] = new Color32(0, 0, 255);     // (1,0)
        pixels[2] = new Color32(0, 0, 255);     // (0,1)
        pixels[3] = new Color32(255, 0, 0);     // (1,1)

        return PatternSource.FromImage("chequer", new DecodedImage(2, 2, pixels));
    }

    [Fact]
    public void TheClickedVoxelTakesTheTopLeftPixel()
    {
        VoxelWorld grid = Plate(4, 4);
        PatternSource pattern = Chequer(grid.Palette);

        var command = new VoxelEditCommand("pattern", grid);
        PaintOperations.Pattern(new Int3(1, 0, 1), Face.PosY, pattern, 0, command);

        // Seed maps to pixel (0,0), which is the red entry.
        Assert.Equal(100, grid.GetVoxel(1, 0, 1));
    }

    [Fact]
    public void ThePatternTilesAcrossTheSurface()
    {
        VoxelWorld grid = Plate(4, 4);
        PatternSource pattern = Chequer(grid.Palette);

        var command = new VoxelEditCommand("pattern", grid);
        PaintOperations.Pattern(Int3.Zero, Face.PosY, pattern, 0, command);

        // Chequer on the Y-facing plane: X and Z both step through the image.
        Assert.Equal(100, grid.GetVoxel(0, 0, 0));
        Assert.Equal(101, grid.GetVoxel(1, 0, 0));
        Assert.Equal(100, grid.GetVoxel(2, 0, 0));
        Assert.Equal(101, grid.GetVoxel(0, 0, 1));
        Assert.Equal(100, grid.GetVoxel(1, 0, 1));
    }

    [Fact]
    public void ColoursAreMatchedToTheNearestPaletteEntryNotAddedToIt()
    {
        // A pattern must not silently consume the 256 slots a level has.
        var grid = new VoxelWorld();
        grid.ReplacePalette(new Palette());
        grid.SetVoxel(0, 0, 0, 5);

        var pixels = new Color32[1];
        pixels[0] = new Color32(254, 1, 2);       // very close to a pure red entry, not equal to it
        grid.Palette[77] = new Color32(255, 0, 0);

        PatternSource pattern = PatternSource.FromImage("near-red", new DecodedImage(1, 1, pixels));

        var command = new VoxelEditCommand("pattern", grid);
        PaintOperations.Pattern(Int3.Zero, Face.PosY, pattern, 0, command);

        Assert.Equal(77, grid.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void PatternPaintsOnlyVisibleVoxelsAndIsUndoable()
    {
        VoxelWorld grid = Plate(3, 3);
        PatternSource pattern = Chequer(grid.Palette);
        ulong before = grid.ContentHash();

        var command = new VoxelEditCommand("pattern", grid);
        int changed = PaintOperations.Pattern(Int3.Zero, Face.PosY, pattern, 0, command);

        Assert.Equal(9, changed);
        Assert.Equal(9, grid.SolidCount);   // never creates or removes

        command.Undo();
        Assert.Equal(before, grid.ContentHash());
    }

    [Fact]
    public void PatternModeFallsBackToAPlainFillWhenNothingIsLoaded()
    {
        var session = new EditorSession { ActiveTool = EditorTool.Paint, PaintMode = PaintMode.Pattern };
        var scene = new Core.Scene.VoxelScene();
        scene.Add(Plate(3, 3), Core.Scene.ObjectTransform.Identity);
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveColorIndex = 33;

        Assert.Null(session.Pattern);
        Assert.True(session.Paint(new Core.Raycast.RaycastHit(Int3.Zero, Face.PosY, 1f)));
        session.EndStroke();

        Assert.Equal(33, session.World.GetVoxel(0, 0, 0));
    }
}
