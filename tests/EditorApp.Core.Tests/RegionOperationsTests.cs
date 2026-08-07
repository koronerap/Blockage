using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class VoxelBoxTests
{
    [Fact]
    public void CornersAreOrderIndependent()
    {
        var a = VoxelBox.FromCorners(new Int3(5, 1, 9), new Int3(-2, 4, 3));
        var b = VoxelBox.FromCorners(new Int3(-2, 4, 3), new Int3(5, 1, 9));

        Assert.Equal(a, b);
        Assert.Equal(new Int3(-2, 1, 3), a.Min);
        Assert.Equal(new Int3(5, 4, 9), a.Max);
        Assert.Equal(new Int3(8, 4, 7), a.Size);
        Assert.Equal(8L * 4 * 7, a.Volume);
    }

    [Fact]
    public void SingleCellBoxHasVolumeOne()
    {
        VoxelBox box = VoxelBox.Single(new Int3(3, 3, 3));
        Assert.Equal(1L, box.Volume);
        Assert.True(box.Contains(new Int3(3, 3, 3)));
        Assert.False(box.Contains(new Int3(4, 3, 3)));
    }

    [Theory]
    [InlineData((int)Face.PosY, 4, 4)]
    [InlineData((int)Face.NegY, 0, 0)]
    [InlineData((int)Face.PosX, 6, 6)]
    [InlineData((int)Face.NegX, 1, 1)]
    public void FaceSlabIsOneVoxelThickOnTheRightSide(int face, int expectedMin, int expectedMax)
    {
        var box = new VoxelBox(new Int3(1, 0, 2), new Int3(6, 4, 8));
        VoxelBox slab = box.FaceSlab((Face)face);

        int axis = FaceInfo.Axis((Face)face);
        Assert.Equal(expectedMin, VoxelBox.Component(slab.Min, axis));
        Assert.Equal(expectedMax, VoxelBox.Component(slab.Max, axis));

        // Thickness one along the face axis, full extent on the other two.
        Assert.Equal(1, VoxelBox.Component(slab.Size, axis));
        Assert.Equal(box.Volume / VoxelBox.Component(box.Size, axis), slab.Volume);
    }
}

public class RegionOperationsTests
{
    private static VoxelWorld Slab(int width, int height, int depth, byte index = 5)
    {
        var world = new VoxelWorld();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    world.SetVoxel(x, y, z, index);
                }
            }
        }

        return world;
    }

    [Fact]
    public void FillWritesEveryCellIncludingEmptyOnes()
    {
        var world = new VoxelWorld();
        var command = new VoxelEditCommand("fill");

        int changed = RegionOperations.Fill(world, new VoxelBox(Int3.Zero, new Int3(2, 2, 2)), 7, command);

        Assert.Equal(27, changed);
        Assert.Equal(27, world.SolidCount);

        command.Undo(world);
        Assert.Equal(0, world.SolidCount);
    }

    [Fact]
    public void PaintLeavesEmptyCellsAlone()
    {
        VoxelWorld world = Slab(3, 1, 3);
        world.SetVoxel(1, 0, 1, Palette.EmptyIndex);   // a hole in the middle

        var command = new VoxelEditCommand("paint");
        int changed = RegionOperations.Paint(world, new VoxelBox(Int3.Zero, new Int3(2, 0, 2)), 9, command);

        Assert.Equal(8, changed);
        Assert.False(world.IsSolid(1, 0, 1));
        Assert.Equal(9, world.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void DeleteClearsTheRegionAndUndoesCleanly()
    {
        VoxelWorld world = Slab(4, 4, 4);
        ulong before = world.ContentHash();

        var command = new VoxelEditCommand("delete");
        RegionOperations.Delete(world, new VoxelBox(new Int3(1, 1, 1), new Int3(2, 2, 2)), command);

        Assert.Equal(64 - 8, world.SolidCount);

        command.Undo(world);
        Assert.Equal(before, world.ContentHash());
    }

    [Fact]
    public void MirrorFlipsTheContentsInPlace()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);
        world.SetVoxel(1, 0, 0, 2);
        world.SetVoxel(2, 0, 0, 3);

        var command = new VoxelEditCommand("mirror");
        RegionOperations.Mirror(world, new VoxelBox(Int3.Zero, new Int3(2, 0, 0)), Axis.X, command);

        Assert.Equal(3, world.GetVoxel(0, 0, 0));
        Assert.Equal(2, world.GetVoxel(1, 0, 0));
        Assert.Equal(1, world.GetVoxel(2, 0, 0));

        command.Undo(world);
        Assert.Equal(1, world.GetVoxel(0, 0, 0));
        Assert.Equal(3, world.GetVoxel(2, 0, 0));
    }

    [Fact]
    public void MirrorTwiceIsTheIdentity()
    {
        VoxelWorld world = Slab(5, 3, 4);
        world.SetVoxel(0, 2, 0, 200);
        ulong before = world.ContentHash();

        var box = new VoxelBox(Int3.Zero, new Int3(4, 2, 3));
        RegionOperations.Mirror(world, box, Axis.Z, new VoxelEditCommand("a"));
        RegionOperations.Mirror(world, box, Axis.Z, new VoxelEditCommand("b"));

        Assert.Equal(before, world.ContentHash());
    }

    [Fact]
    public void MoveLeavesNothingBehindEvenWhenRegionsOverlap()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            world.SetVoxel(x, 0, 0, (byte)(x + 1));
        }

        var command = new VoxelEditCommand("move");
        RegionOperations.Move(world, new VoxelBox(Int3.Zero, new Int3(3, 0, 0)), new Int3(2, 0, 0), command);

        // Shifted two along X: the source cells that are not overlapped must be empty.
        Assert.False(world.IsSolid(0, 0, 0));
        Assert.False(world.IsSolid(1, 0, 0));
        Assert.Equal(1, world.GetVoxel(2, 0, 0));
        Assert.Equal(4, world.GetVoxel(5, 0, 0));
        Assert.Equal(4, world.SolidCount);
    }

    [Fact]
    public void ExtrudePullsTheFaceSlabOutward()
    {
        VoxelWorld world = Slab(3, 1, 3, index: 6);
        var box = new VoxelBox(Int3.Zero, new Int3(2, 0, 2));

        var command = new VoxelEditCommand("extrude");
        int changed = RegionOperations.Extrude(world, box, Face.PosY, 2, command);

        Assert.Equal(18, changed);                 // 9 cells x 2 layers
        Assert.Equal(27, world.SolidCount);
        Assert.Equal(6, world.GetVoxel(1, 2, 1));

        command.Undo(world);
        Assert.Equal(9, world.SolidCount);
    }

    [Fact]
    public void NegativeExtrudeIntrudes()
    {
        VoxelWorld world = Slab(3, 4, 3);
        var box = new VoxelBox(Int3.Zero, new Int3(2, 3, 2));

        var command = new VoxelEditCommand("intrude");
        RegionOperations.Extrude(world, box, Face.PosY, -2, command);

        Assert.Equal(3 * 2 * 3, world.SolidCount);   // two top layers gone
        Assert.False(world.IsSolid(1, 3, 1));
        Assert.True(world.IsSolid(1, 1, 1));
    }

    [Fact]
    public void ExtrudeCopiesColorsPerCellNotOneFlatColor()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 11);
        world.SetVoxel(1, 0, 0, 22);

        var command = new VoxelEditCommand("extrude");
        RegionOperations.Extrude(world, new VoxelBox(Int3.Zero, new Int3(1, 0, 0)), Face.PosY, 1, command);

        Assert.Equal(11, world.GetVoxel(0, 1, 0));
        Assert.Equal(22, world.GetVoxel(1, 1, 0));
    }

    [Fact]
    public void SurfaceExtrudeFollowsTheConnectedSameColorPlane()
    {
        // A flat floor with one differently colored tile: the extrude must skip that tile.
        VoxelWorld world = Slab(5, 1, 5, index: 4);
        world.SetVoxel(2, 0, 2, 8);

        var command = new VoxelEditCommand("surface extrude");
        int changed = RegionOperations.ExtrudeSurface(world, new Int3(0, 0, 0), Face.PosY, 1, command);

        Assert.Equal(24, changed);
        Assert.True(world.IsSolid(0, 1, 0));
        Assert.False(world.IsSolid(2, 1, 2));
    }

    [Fact]
    public void SurfaceExtrudeStopsAtCoveredCells()
    {
        // A tile with something already sitting on it is not part of the exposed surface.
        VoxelWorld world = Slab(4, 1, 1, index: 3);
        world.SetVoxel(2, 1, 0, 3);

        var command = new VoxelEditCommand("surface extrude");
        RegionOperations.ExtrudeSurface(world, new Int3(0, 0, 0), Face.PosY, 1, command);

        Assert.True(world.IsSolid(0, 1, 0));
        Assert.True(world.IsSolid(1, 1, 0));
        // (2,0,0) was covered, so the flood fill stopped there and (3,0,0) was never reached.
        Assert.False(world.IsSolid(3, 1, 0));
    }

    [Fact]
    public void SurfaceExtrudeOnEmptySpaceDoesNothing()
    {
        var world = new VoxelWorld();
        var command = new VoxelEditCommand("surface extrude");

        Assert.Equal(0, RegionOperations.ExtrudeSurface(world, Int3.Zero, Face.PosY, 1, command));
        Assert.True(command.IsEmpty);
    }

    [Fact]
    public void RegionEditOfAnySizeIsASingleUndoStep()
    {
        var world = new VoxelWorld();
        var stack = new UndoStack();

        var command = new VoxelEditCommand("big fill");
        RegionOperations.Fill(world, new VoxelBox(Int3.Zero, new Int3(19, 19, 19)), 3, command);
        stack.Push(command);

        Assert.Equal(1, stack.UndoCount);
        Assert.Equal(8000, stack.RetainedCells);

        stack.Undo(world);
        Assert.Equal(0, world.SolidCount);
    }
}

public class VoxelClipTests
{
    [Fact]
    public void CopyAndPasteReproducesTheRegion()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                world.SetVoxel(x, y, 0, (byte)(1 + x + y * 3));
            }
        }

        VoxelClip clip = VoxelClip.Copy(world, new VoxelBox(Int3.Zero, new Int3(2, 1, 0)));
        Assert.Equal(new Int3(3, 2, 1), clip.Size);
        Assert.Equal(6, clip.SolidCount);

        var command = new VoxelEditCommand("paste");
        clip.Paste(world, new Int3(10, 0, 0), command);

        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                Assert.Equal(world.GetVoxel(x, y, 0), world.GetVoxel(10 + x, y, 0));
            }
        }
    }

    [Fact]
    public void PasteSkipsEmptyCellsByDefault()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);
        // (1,0,0) stays empty inside the copied box.

        VoxelClip clip = VoxelClip.Copy(world, new VoxelBox(Int3.Zero, new Int3(1, 0, 0)));

        world.SetVoxel(11, 0, 0, 99);   // must survive a paste that lands on it
        var command = new VoxelEditCommand("paste");
        clip.Paste(world, new Int3(10, 0, 0), command, skipEmpty: true);

        Assert.Equal(1, world.GetVoxel(10, 0, 0));
        Assert.Equal(99, world.GetVoxel(11, 0, 0));
    }

    [Fact]
    public void PasteCanOverwriteWithEmptyWhenAsked()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);
        VoxelClip clip = VoxelClip.Copy(world, new VoxelBox(Int3.Zero, new Int3(1, 0, 0)));

        world.SetVoxel(11, 0, 0, 99);
        var command = new VoxelEditCommand("paste");
        clip.Paste(world, new Int3(10, 0, 0), command, skipEmpty: false);

        Assert.False(world.IsSolid(11, 0, 0));
    }

    [Fact]
    public void MirroredClipFlipsAlongTheChosenAxisOnly()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);
        world.SetVoxel(1, 0, 0, 2);
        world.SetVoxel(0, 1, 0, 3);

        VoxelClip clip = VoxelClip.Copy(world, new VoxelBox(Int3.Zero, new Int3(1, 1, 0)));
        VoxelClip mirrored = clip.Mirrored(Axis.X);

        Assert.Equal(2, mirrored[0, 0, 0]);
        Assert.Equal(1, mirrored[1, 0, 0]);
        Assert.Equal(0, mirrored[0, 1, 0]);
        Assert.Equal(3, mirrored[1, 1, 0]);
    }

    [Fact]
    public void CopyingAcrossChunkBoundariesKeepsEverything()
    {
        var world = new VoxelWorld();
        for (int x = 28; x < 40; x++)
        {
            world.SetVoxel(x, 0, 0, (byte)(x - 27));
        }

        VoxelClip clip = VoxelClip.Copy(world, new VoxelBox(new Int3(28, 0, 0), new Int3(39, 0, 0)));

        Assert.Equal(12, clip.SolidCount);
        Assert.Equal(1, clip[0, 0, 0]);
        Assert.Equal(12, clip[11, 0, 0]);
    }

    [Fact]
    public void BoxAtDescribesWhereAPasteWouldLand()
    {
        var clip = new VoxelClip(new Int3(3, 2, 4));
        VoxelBox box = clip.BoxAt(new Int3(5, 5, 5));

        Assert.Equal(new Int3(5, 5, 5), box.Min);
        Assert.Equal(new Int3(7, 6, 8), box.Max);
    }
}
