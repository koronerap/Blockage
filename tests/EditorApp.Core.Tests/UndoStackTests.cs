using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class UndoStackTests
{
    private static VoxelEditCommand Paint(VoxelWorld world, string name, byte index, params Int3[] cells)
    {
        var command = new VoxelEditCommand(name);
        foreach (Int3 cell in cells)
        {
            command.Apply(world, cell, index);
        }

        return command;
    }

    [Fact]
    public void UndoRestoresTheExactPreviousState()
    {
        var world = new VoxelWorld();
        Paint(world, "base", 5, new Int3(0, 0, 0), new Int3(1, 0, 0), new Int3(2, 0, 0));
        ulong before = world.ContentHash();

        var stack = new UndoStack();
        stack.Push(Paint(world, "edit", 9, new Int3(1, 0, 0), new Int3(5, 5, 5)));
        Assert.NotEqual(before, world.ContentHash());

        Assert.True(stack.Undo(world));
        Assert.Equal(before, world.ContentHash());

        Assert.True(stack.Redo(world));
        Assert.Equal(9, world.GetVoxel(1, 0, 0));
        Assert.Equal(9, world.GetVoxel(5, 5, 5));
    }

    [Fact]
    public void DeletionUndoesBackToTheOriginalColor()
    {
        var world = new VoxelWorld();
        Paint(world, "base", 7, new Int3(4, 4, 4));
        ulong before = world.ContentHash();

        var stack = new UndoStack();
        stack.Push(Paint(world, "delete", Palette.EmptyIndex, new Int3(4, 4, 4)));
        Assert.False(world.IsSolid(4, 4, 4));

        stack.Undo(world);
        Assert.Equal(7, world.GetVoxel(4, 4, 4));
        Assert.Equal(before, world.ContentHash());
    }

    [Fact]
    public void ACellTouchedTwiceInOneCommandKeepsItsOriginalBefore()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 3);

        var command = new VoxelEditCommand("drag");
        command.Apply(world, new Int3(0, 0, 0), 4);
        command.Apply(world, new Int3(0, 0, 0), 5);

        Assert.Equal(1, command.RetainedCells);

        command.Undo(world);
        Assert.Equal(3, world.GetVoxel(0, 0, 0));

        command.Redo(world);
        Assert.Equal(5, world.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void NewActionDiscardsTheRedoBranch()
    {
        var world = new VoxelWorld();
        var stack = new UndoStack();

        stack.Push(Paint(world, "a", 1, new Int3(0, 0, 0)));
        stack.Undo(world);
        Assert.True(stack.CanRedo);

        stack.Push(Paint(world, "b", 2, new Int3(1, 0, 0)));
        Assert.False(stack.CanRedo);
        Assert.Equal(1, stack.RetainedCells);
    }

    [Fact]
    public void StackTrimsByCellsNotByCommandCount()
    {
        var world = new VoxelWorld();
        var stack = new UndoStack { CellBudget = 10 };

        // Twenty one-cell commands: the budget keeps only the last ten.
        for (int i = 0; i < 20; i++)
        {
            stack.Push(Paint(world, $"dab {i}", 1, new Int3(i, 0, 0)));
        }

        Assert.Equal(10, stack.UndoCount);
        Assert.Equal(10, stack.RetainedCells);
    }

    [Fact]
    public void ASingleOversizedCommandStaysUndoable()
    {
        var world = new VoxelWorld();
        var stack = new UndoStack { CellBudget = 4 };

        var cells = new Int3[100];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = new Int3(i, 0, 0);
        }

        stack.Push(Paint(world, "big extrude", 1, cells));

        Assert.Equal(1, stack.UndoCount);
        Assert.True(stack.Undo(world));
        Assert.Equal(0, world.SolidCount);
    }

    [Fact]
    public void PaletteEditIsUndoable()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 20);

        Color32 before = world.Palette[20];
        var after = new Color32(1, 2, 3);

        var stack = new UndoStack();
        world.Palette[20] = after;
        stack.Push(new PaletteEditCommand(20, before, after));

        stack.Undo(world);
        Assert.Equal(before, world.Palette[20]);

        stack.Redo(world);
        Assert.Equal(after, world.Palette[20]);
    }

    [Fact]
    public void FloodFillRecolorsOnlyTheConnectedSameColorRegion()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 5; x++)
        {
            world.SetVoxel(x, 0, 0, 3);
        }

        world.SetVoxel(2, 0, 0, 4);          // splits the run in two
        world.SetVoxel(50, 0, 0, 3);         // same color, disconnected

        var command = new VoxelEditCommand("fill");
        int filled = FloodFill.Fill(world, new Int3(0, 0, 0), 8, command);

        Assert.Equal(2, filled);
        Assert.Equal(8, world.GetVoxel(0, 0, 0));
        Assert.Equal(8, world.GetVoxel(1, 0, 0));
        Assert.Equal(4, world.GetVoxel(2, 0, 0));
        Assert.Equal(3, world.GetVoxel(3, 0, 0));
        Assert.Equal(3, world.GetVoxel(50, 0, 0));

        command.Undo(world);
        Assert.Equal(3, world.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void FloodFillDoesNotSpreadThroughEmptySpace()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 3);
        world.SetVoxel(2, 0, 0, 3);

        var command = new VoxelEditCommand("fill");
        Assert.Equal(1, FloodFill.Fill(world, new Int3(0, 0, 0), 8, command));
        Assert.Equal(3, world.GetVoxel(2, 0, 0));
    }

    [Fact]
    public void UndoRedoAcrossAChunkBoundaryRestoresBothChunks()
    {
        var world = new VoxelWorld();
        var stack = new UndoStack();

        stack.Push(Paint(world, "seam", 6, new Int3(31, 0, 0), new Int3(32, 0, 0)));
        Assert.Equal(2, world.Chunks.Count);

        ulong afterEdit = world.ContentHash();
        stack.Undo(world);
        Assert.Equal(0, world.SolidCount);

        stack.Redo(world);
        Assert.Equal(afterEdit, world.ContentHash());
    }
}
