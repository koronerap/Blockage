using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// The two fills a bucket can do, and the box a drag leaves. Filling the whole object is the one
/// with a trap in it: setting a voxel's colour forgets what was painted on its faces, so undo has to
/// put back something the write itself destroyed.
/// </summary>
public class PaintFillTests
{
    private static VoxelWorld Box(int side, byte color = 40)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, color);
                }
            }
        }

        return grid;
    }

    /// <summary>One voxel thick, so every top face is exposed.</summary>
    private static VoxelWorld Slab(int side, byte color = 40)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int z = 0; z < side; z++)
            {
                grid.SetVoxel(x, 0, z, color);
            }
        }

        return grid;
    }

    [Fact]
    public void FillingTheObjectRecoloursEveryVoxel()
    {
        VoxelWorld grid = Box(4);
        var command = new VoxelEditCommand("fill", grid);

        Assert.Equal(64, PaintOperations.FillObject(90, command));

        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    Assert.Equal(90, grid.GetVoxel(x, y, z));
                }
            }
        }
    }

    [Fact]
    public void FillingReachesFacesABucketNeverCould()
    {
        // The complaint this exists for: a bucket walks one surface, so it can only ever paint the
        // faces pointing the way the one under the cursor does.
        VoxelWorld grid = Box(3);
        var bucket = new VoxelEditCommand("bucket", grid);
        PaintOperations.Bucket(new Int3(1, 2, 1), Face.PosY, 90, 0, bucket);

        Assert.Equal(90, grid.GetFaceColor(new Int3(1, 2, 1), Face.PosY));
        Assert.Equal(40, grid.GetFaceColor(new Int3(1, 1, 0), Face.NegZ));

        PaintOperations.FillObject(91, new VoxelEditCommand("fill", grid));

        Assert.Equal(91, grid.GetFaceColor(new Int3(1, 2, 1), Face.PosY));
        Assert.Equal(91, grid.GetFaceColor(new Int3(1, 1, 0), Face.NegZ));
    }

    [Fact]
    public void UndoingAFillPutsBackThePaintItWipedOff()
    {
        // Writing a voxel's colour clears the faces painted on it. Undo restores the voxel first and
        // the faces after, and the order is the whole of why this works: the other way round, the
        // voxel's own write would wipe the faces that had just been restored.
        VoxelWorld grid = Box(3);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosY, 70);
        grid.SetFaceColor(new Int3(2, 2, 2), Face.NegX, 71);

        var command = new VoxelEditCommand("fill", grid);
        PaintOperations.FillObject(90, command);

        Assert.Equal(90, grid.GetFaceColor(new Int3(0, 0, 0), Face.PosY));

        command.Undo();

        Assert.Equal(40, grid.GetVoxel(0, 0, 0));
        Assert.Equal(70, grid.GetFaceColor(new Int3(0, 0, 0), Face.PosY));
        Assert.Equal(71, grid.GetFaceColor(new Int3(2, 2, 2), Face.NegX));
    }

    [Fact]
    public void RedoingAFillWipesThemAgain()
    {
        VoxelWorld grid = Box(2);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosY, 70);

        var command = new VoxelEditCommand("fill", grid);
        PaintOperations.FillObject(90, command);
        command.Undo();
        command.Redo();

        Assert.Equal(90, grid.GetVoxel(0, 0, 0));
        Assert.Equal(90, grid.GetFaceColor(new Int3(0, 0, 0), Face.PosY));
    }

    [Fact]
    public void AFilledBoxPaintsItsInsideAndAFrameDoesNot()
    {
        // On a slab, so every top face is exposed and the difference between the two is the only
        // thing being measured.
        VoxelWorld filled = Slab(5);
        VoxelWorld framed = Slab(5);

        PaintOperations.BoxFilled(new Int3(1, 0, 1), new Int3(3, 0, 3), Face.PosY, 90, new VoxelEditCommand("f", filled));
        PaintOperations.BoxFrame(new Int3(1, 0, 1), new Int3(3, 0, 3), Face.PosY, 0f, 90, new VoxelEditCommand("f", framed));

        // The middle: inside the fill, inside the hollow of the frame.
        Assert.Equal(90, filled.GetFaceColor(new Int3(2, 0, 2), Face.PosY));
        Assert.Equal(40, framed.GetFaceColor(new Int3(2, 0, 2), Face.PosY));

        // A corner belongs to both.
        Assert.Equal(90, filled.GetFaceColor(new Int3(1, 0, 1), Face.PosY));
        Assert.Equal(90, framed.GetFaceColor(new Int3(1, 0, 1), Face.PosY));
    }

    [Fact]
    public void AFilledBoxSkipsFacesNothingCanSee()
    {
        // The same rule the brush follows. A face buried inside the model is work nobody looks at,
        // and every one of them is an entry in the sparse override table.
        VoxelWorld grid = Box(5);
        var command = new VoxelEditCommand("f", grid);

        PaintOperations.BoxFilled(new Int3(1, 1, 1), new Int3(3, 3, 3), Face.PosY, 90, command);

        Assert.Equal(40, grid.GetFaceColor(new Int3(2, 2, 2), Face.PosY));
        Assert.True(command.IsEmpty);
    }

    [Fact]
    public void AFilledBoxCoversEverySeenCellItSpans()
    {
        VoxelWorld grid = Slab(6);
        var command = new VoxelEditCommand("f", grid);

        Assert.Equal(3 * 4, PaintOperations.BoxFilled(new Int3(1, 0, 2), new Int3(3, 0, 5), Face.PosY, 90, command));
    }

    [Theory]
    [InlineData(PaintMode.Brush, false)]
    [InlineData(PaintMode.Bucket, true)]
    [InlineData(PaintMode.Pattern, true)]
    public void WhetherAShapeIsFilledFollowsTheMode(PaintMode mode, bool expected)
    {
        var session = new EditorSession { PaintMode = mode };

        Assert.Equal(expected, session.FillsSolid);
    }
}
