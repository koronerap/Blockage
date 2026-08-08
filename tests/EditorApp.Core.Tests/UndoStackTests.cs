using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class UndoStackTests
{
    private static VoxelEditCommand Paint(VoxelWorld world, string name, byte index, params Int3[] cells)
    {
        var command = new VoxelEditCommand(name, world);
        foreach (Int3 cell in cells)
        {
            command.Apply(cell, index);
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

        Assert.True(stack.Undo());
        Assert.Equal(before, world.ContentHash());

        Assert.True(stack.Redo());
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

        stack.Undo();
        Assert.Equal(7, world.GetVoxel(4, 4, 4));
        Assert.Equal(before, world.ContentHash());
    }

    [Fact]
    public void ACellTouchedTwiceInOneCommandKeepsItsOriginalBefore()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 3);

        var command = new VoxelEditCommand("drag", world);
        command.Apply(new Int3(0, 0, 0), 4);
        command.Apply(new Int3(0, 0, 0), 5);

        Assert.Equal(1, command.RetainedCells);

        command.Undo();
        Assert.Equal(3, world.GetVoxel(0, 0, 0));

        command.Redo();
        Assert.Equal(5, world.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void NewActionDiscardsTheRedoBranch()
    {
        var world = new VoxelWorld();
        var stack = new UndoStack();

        stack.Push(Paint(world, "a", 1, new Int3(0, 0, 0)));
        stack.Undo();
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
        Assert.True(stack.Undo());
        Assert.Equal(0, world.SolidCount);
    }

    [Fact]
    public void PaletteEditIsUndoableAcrossTheWholeScene()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 20);
        scene.Add(grid, ObjectTransform.Identity);

        Color32 before = scene.Palette[20];
        var after = new Color32(1, 2, 3);

        var stack = new UndoStack();
        scene.Palette[20] = after;
        stack.Push(new PaletteEditCommand(scene, 20, before, after));

        stack.Undo();
        Assert.Equal(before, grid.Palette[20]);

        stack.Redo();
        Assert.Equal(after, grid.Palette[20]);
    }

    [Fact]
    public void UndoReachesTheObjectTheEditWasMadeOn()
    {
        // Focus moving after an edit must not send its undo to a different object.
        var first = new VoxelWorld();
        var second = new VoxelWorld();
        second.SetVoxel(0, 0, 0, 4);

        var stack = new UndoStack();
        stack.Push(Paint(first, "on the first object", 7, new Int3(0, 0, 0)));

        stack.Undo();

        Assert.Equal(0, first.SolidCount);
        Assert.Equal(4, second.GetVoxel(0, 0, 0));
    }

    // Flood fill now belongs to Paint's Bucket sub-mode and is tested in PaintOperationsTests.

    [Fact]
    public void UndoRedoAcrossAChunkBoundaryRestoresBothChunks()
    {
        var world = new VoxelWorld();
        var stack = new UndoStack();

        stack.Push(Paint(world, "seam", 6, new Int3(31, 0, 0), new Int3(32, 0, 0)));
        Assert.Equal(2, world.Chunks.Count);

        ulong afterEdit = world.ContentHash();
        stack.Undo();
        Assert.Equal(0, world.SolidCount);

        stack.Redo();
        Assert.Equal(afterEdit, world.ContentHash());
    }
}
