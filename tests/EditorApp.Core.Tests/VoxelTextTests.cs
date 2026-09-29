using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Voxel lettering (Fullreleaseplan 3.9): the built-in font, set as a shape the Add menu makes.</summary>
public class VoxelTextTests
{
    [Fact]
    public void EveryLetterIsFiveBySevenPixelsAndTheMarkedOnesGrowARow()
    {
        foreach (char letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,!?-+:;'\"/()&#=*_<>%$@")
        {
            (IReadOnlyList<string> rows, int above) = VoxelFont.Glyph(letter);
            Assert.Equal(VoxelFont.Height, rows.Count);
            Assert.Equal(0, above);
            Assert.All(rows, row => Assert.Equal(VoxelFont.Width, row.Length));
        }

        Assert.Equal((8, 1), (VoxelFont.Glyph('Ö').Rows.Count, VoxelFont.Glyph('Ö').Above));
        Assert.Equal((8, 0), (VoxelFont.Glyph('ş').Rows.Count, VoxelFont.Glyph('ş').Above));
    }

    [Fact]
    public void SmallLettersAreSetAsCapitalsTheInvariantWay()
    {
        Assert.Equal(VoxelFont.Glyph('A').Rows, VoxelFont.Glyph('a').Rows);

        // An i is an I, not the Turkish İ, and ı is an I too.
        Assert.Equal(VoxelFont.Glyph('I').Rows, VoxelFont.Glyph('i').Rows);
        Assert.Equal(VoxelFont.Glyph('I').Rows, VoxelFont.Glyph('ı').Rows);
        Assert.NotEqual(VoxelFont.Glyph('I').Rows.Count, VoxelFont.Glyph('İ').Rows.Count);
    }

    [Fact]
    public void ALetterStandsOnTheGroundCentredAcross()
    {
        HashSet<Int3> cells = VoxelFont.Cells("I", size: 1, depth: 1, spacing: 1);

        // A bar of three, a stem of five between, and a bar of three.
        Assert.Equal(11, cells.Count);
        Assert.Equal(0, cells.Min(c => c.Y));
        Assert.Equal(6, cells.Max(c => c.Y));
    }

    [Fact]
    public void SizeAndDepthMultiplyThePixels()
    {
        int one = VoxelFont.Cells("HI", 1, 1, 1).Count;

        Assert.Equal(one * 2 * 2 * 3, VoxelFont.Cells("HI", size: 2, depth: 3, spacing: 1).Count);
    }

    [Fact]
    public void ANewLineStacksTheNextLineUnderneath()
    {
        HashSet<Int3> two = VoxelFont.Cells("A\nB", 1, 1, 1);

        Assert.True(two.Max(c => c.Y) > VoxelFont.Height + 1);
        Assert.Equal(0, two.Min(c => c.Y));
    }

    [Fact]
    public void TheAddMenuMakesTextAndRemakesItWithOtherWords()
    {
        var session = new EditorSession();
        session.ReplaceScene(new Core.Scene.VoxelScene(), projectPath: null);

        var settings = Shapes.Defaults(ShapeKind.Text);
        Core.Scene.VoxelObject sign = session.AddShape(settings, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY, 1f);
        int before = sign.Grid.SolidCount;

        Assert.True(session.ReshapeLast(settings with { Text = "TEXT TEXT" }));

        Assert.True(sign.Grid.SolidCount > before);
        Assert.Equal(1, session.History.UndoCount);
    }
}
