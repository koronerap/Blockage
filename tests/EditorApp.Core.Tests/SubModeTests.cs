using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class BrushShapeTests
{
    private static VoxelWorld Plate(int width, int depth, byte index = 5)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                grid.SetVoxel(x, 0, z, index);
            }
        }

        return grid;
    }

    [Fact]
    public void WalkVisitsEveryCellAlongAnAxis()
    {
        Int3[] cells = [.. PaintOperations.Walk(new Int3(0, 0, 0), new Int3(4, 0, 0))];

        Assert.Equal(5, cells.Length);
        Assert.Equal(new Int3(0, 0, 0), cells[0]);
        Assert.Equal(new Int3(4, 0, 0), cells[^1]);
    }

    [Fact]
    public void WalkIsContinuousWithNoGaps()
    {
        // Every consecutive pair has to be adjacent, or the painted line would have holes in it.
        Int3[] cells = [.. PaintOperations.Walk(new Int3(-3, 2, 1), new Int3(9, -4, 7))];

        for (int i = 1; i < cells.Length; i++)
        {
            Int3 step = cells[i] - cells[i - 1];
            Assert.True(
                Math.Abs(step.X) <= 1 && Math.Abs(step.Y) <= 1 && Math.Abs(step.Z) <= 1,
                $"Jumped from {cells[i - 1]} to {cells[i]}.");
            Assert.NotEqual(Int3.Zero, step);
        }

        Assert.Equal(new Int3(9, -4, 7), cells[^1]);
    }

    [Fact]
    public void WalkOfASingleCellIsThatCell()
    {
        Assert.Single(PaintOperations.Walk(new Int3(2, 2, 2), new Int3(2, 2, 2)));
    }

    [Fact]
    public void LinePaintsOnlyTheCellsItPassesThrough()
    {
        VoxelWorld grid = Plate(6, 3);
        var command = new VoxelEditCommand("line", grid);

        PaintOperations.Line(new Int3(0, 0, 0), new Int3(5, 0, 0), Face.PosY, 0f, 9, command);

        for (int x = 0; x <= 5; x++)
        {
            Assert.Equal(9, grid.GetFaceColor(new Int3(x, 0, 0), Face.PosY));
        }

        Assert.Equal(5, grid.GetFaceColor(new Int3(0, 0, 1), Face.PosY));   // neighbouring row untouched
    }

    [Fact]
    public void BoxFramePaintsTheEdgesAndLeavesTheFacesAlone()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 5; x++)
        {
            for (int z = 0; z < 5; z++)
            {
                grid.SetVoxel(x, 0, z, 5);
            }
        }

        var command = new VoxelEditCommand("box", grid);
        PaintOperations.BoxFrame(new Int3(0, 0, 0), new Int3(4, 0, 4), Face.PosY, 0f, 9, command);

        Assert.Equal(9, grid.GetFaceColor(new Int3(0, 0, 0), Face.PosY));   // corner
        Assert.Equal(9, grid.GetFaceColor(new Int3(2, 0, 0), Face.PosY));   // edge
        Assert.Equal(9, grid.GetFaceColor(new Int3(0, 0, 2), Face.PosY));   // edge
        Assert.Equal(5, grid.GetFaceColor(new Int3(2, 0, 2), Face.PosY));   // middle, not an edge
    }

    [Fact]
    public void AShapeIsOneUndoStep()
    {
        var session = new EditorSession { ActiveTool = EditorTool.Paint, ActiveColorIndex = 12 };
        var scene = new VoxelScene();
        scene.Add(Plate(8, 1), ObjectTransform.Identity);
        session.ReplaceScene(scene, projectPath: null);

        Assert.True(session.PaintShape(new Int3(0, 0, 0), new Int3(7, 0, 0), Face.PosY, asBox: false));
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(5, session.World.GetFaceColor(new Int3(3, 0, 0), Face.PosY));
    }
}

public class ExtrudeCreateTests
{
    private static (EditorSession Session, VoxelObject Source) SessionWithPlate()
    {
        var session = new EditorSession
        {
            ActiveTool = EditorTool.Extrude,
            ExtrudeCreatesObject = true,
        };

        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 3; z++)
            {
                grid.SetVoxel(x, 0, z, 7);
            }
        }

        VoxelObject source = scene.Add(grid, ObjectTransform.At(new Vector3(5f, 0f, 0f)), "plate");
        session.ReplaceScene(scene, projectPath: null);
        return (session, source);
    }

    private static void SelectTop(EditorSession session) =>
        session.SetSelection(FaceSelection.Box(session.World, Face.PosY, 0, Int3.Zero, new Int3(2, 0, 2)));

    [Fact]
    public void PulledVoxelsBecomeTheirOwnObject()
    {
        (EditorSession session, VoxelObject source) = SessionWithPlate();
        SelectTop(session);

        session.PreviewExtrude(2);
        Assert.True(session.ConfirmExtrude());

        Assert.Equal(2, session.Scene.Objects.Count);
        Assert.Equal(9, source.Grid.SolidCount);            // the original is untouched
        Assert.NotEqual(source.Id, session.Scene.FocusId);  // focus moves to the new piece

        VoxelObject created = session.Scene.Objects[^1];
        Assert.Equal(18, created.Grid.SolidCount);          // 9 cells x 2 layers
        Assert.Equal(source.Transform, created.Transform);  // appears exactly where it was drawn
    }

    [Fact]
    public void TheNewObjectKeepsTheSourceColours()
    {
        (EditorSession session, _) = SessionWithPlate();
        SelectTop(session);

        session.PreviewExtrude(1);
        session.ConfirmExtrude();

        VoxelObject created = session.Scene.Objects[^1];
        Assert.Equal(7, created.Grid.GetVoxel(1, 1, 1));
    }

    [Fact]
    public void CreateIsOneUndoStepThatRemovesTheObjectAgain()
    {
        (EditorSession session, VoxelObject source) = SessionWithPlate();
        ulong before = session.Scene.ContentHash();
        SelectTop(session);

        session.PreviewExtrude(2);
        session.ConfirmExtrude();
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();

        Assert.Single(session.Scene.Objects);
        Assert.Equal(source.Id, session.Scene.Objects[0].Id);
        Assert.Equal(before, session.Scene.ContentHash());

        session.Redo();
        Assert.Equal(2, session.Scene.Objects.Count);
    }

    [Fact]
    public void PushingInIgnoresCreateAndJustDeletes()
    {
        // There is nothing to hand to a new object when pushing in, so it behaves as a plain extrude.
        (EditorSession session, VoxelObject source) = SessionWithPlate();
        SelectTop(session);

        session.PreviewExtrude(-1);
        Assert.True(session.ConfirmExtrude());

        Assert.Single(session.Scene.Objects);
        Assert.Equal(0, source.Grid.SolidCount);
    }

    [Fact]
    public void WithCreateOffTheVoxelsJoinTheSameObject()
    {
        (EditorSession session, VoxelObject source) = SessionWithPlate();
        session.ExtrudeCreatesObject = false;
        SelectTop(session);

        session.PreviewExtrude(2);
        session.ConfirmExtrude();

        Assert.Single(session.Scene.Objects);
        Assert.Equal(27, source.Grid.SolidCount);
    }
}
