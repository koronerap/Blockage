using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class SelectionSessionTests
{
    private static EditorSession SessionWithSlab(int width = 4, int height = 2, int depth = 3, byte index = 5)
    {
        var session = new EditorSession();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    session.World.SetVoxel(x, y, z, index);
                }
            }
        }

        session.HasUnsavedChanges = false;
        return session;
    }

    [Fact]
    public void SelectAllCoversTheWholeLevel()
    {
        EditorSession session = SessionWithSlab();

        Assert.True(session.SelectAll());
        Assert.Equal(new Int3(0, 0, 0), session.Selection!.Value.Min);
        Assert.Equal(new Int3(3, 1, 2), session.Selection.Value.Max);
    }

    [Fact]
    public void SelectAllOnAnEmptyLevelSelectsNothing()
    {
        var session = new EditorSession();
        Assert.False(session.SelectAll());
        Assert.False(session.HasSelection);
    }

    [Fact]
    public void RegionOperationsAreOneUndoStepEach()
    {
        EditorSession session = SessionWithSlab();
        session.SelectAll();
        session.ActiveColorIndex = 12;

        Assert.True(session.PaintSelection());
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(12, session.World.GetVoxel(0, 0, 0));

        session.Undo();
        Assert.Equal(5, session.World.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void OperationsWithNoSelectionDoNothing()
    {
        EditorSession session = SessionWithSlab();

        Assert.False(session.FillSelection());
        Assert.False(session.DeleteSelection());
        Assert.False(session.MirrorSelection(Axis.X));
        Assert.False(session.CopySelection());
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void AnOperationThatChangesNothingCreatesNoHistory()
    {
        EditorSession session = SessionWithSlab(index: 5);
        session.SelectAll();
        session.ActiveColorIndex = 5;   // already that color

        Assert.False(session.PaintSelection());
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void MoveCarriesTheSelectionWithIt()
    {
        EditorSession session = SessionWithSlab();
        session.SelectAll();
        VoxelBox before = session.Selection!.Value;

        Assert.True(session.MoveSelection(new Int3(0, 5, 0)));

        Assert.Equal(before.Translate(new Int3(0, 5, 0)), session.Selection);
        Assert.False(session.World.IsSolid(0, 0, 0));
        Assert.True(session.World.IsSolid(0, 5, 0));

        session.Undo();
        Assert.True(session.World.IsSolid(0, 0, 0));
    }

    [Fact]
    public void ExtrudingASelectionGrowsIt()
    {
        EditorSession session = SessionWithSlab(height: 1);
        session.SelectAll();

        Assert.True(session.ExtrudeSelection(Face.PosY, 3));

        Assert.Equal(3, session.Selection!.Value.Max.Y);
        Assert.True(session.World.IsSolid(0, 3, 0));
        Assert.False(session.World.IsSolid(0, 4, 0));
    }

    [Fact]
    public void ExtrudingANegativeFaceGrowsTheMinimumSide()
    {
        EditorSession session = SessionWithSlab(height: 1);
        session.SelectAll();

        Assert.True(session.ExtrudeSelection(Face.NegY, 2));

        Assert.Equal(-2, session.Selection!.Value.Min.Y);
        Assert.True(session.World.IsSolid(0, -2, 0));
    }

    [Fact]
    public void IntrudingShrinksTheSelectionAndClearsItWhenNothingIsLeft()
    {
        EditorSession session = SessionWithSlab(height: 3);
        session.SelectAll();

        Assert.True(session.ExtrudeSelection(Face.PosY, -1));
        Assert.Equal(1, session.Selection!.Value.Max.Y);

        Assert.True(session.ExtrudeSelection(Face.PosY, -2));
        Assert.False(session.HasSelection);
        Assert.Equal(0, session.World.SolidCount);
    }

    [Fact]
    public void CopyThenPasteLandsWhereItIsPutAndSelectsTheResult()
    {
        EditorSession session = SessionWithSlab(2, 1, 1, index: 9);
        session.Selection = new VoxelBox(Int3.Zero, new Int3(1, 0, 0));

        Assert.True(session.CopySelection());
        Assert.True(session.PasteAt(new Int3(10, 4, 6)));

        Assert.Equal(9, session.World.GetVoxel(10, 4, 6));
        Assert.Equal(9, session.World.GetVoxel(11, 4, 6));
        Assert.Equal(new VoxelBox(new Int3(10, 4, 6), new Int3(11, 4, 6)), session.Selection);

        session.Undo();
        Assert.False(session.World.IsSolid(10, 4, 6));
    }

    [Fact]
    public void CutRemovesTheSourceButKeepsTheClipboard()
    {
        EditorSession session = SessionWithSlab(2, 1, 1);
        session.SelectAll();

        Assert.True(session.CutSelection());
        Assert.Equal(0, session.World.SolidCount);
        Assert.True(session.HasClipboard);

        Assert.True(session.PasteAt(new Int3(20, 0, 0)));
        Assert.Equal(2, session.World.SolidCount);
    }

    [Fact]
    public void ClipboardSurvivesUndoOfTheCut()
    {
        EditorSession session = SessionWithSlab(2, 1, 1);
        session.SelectAll();
        session.CutSelection();

        session.Undo();
        Assert.Equal(2, session.World.SolidCount);
        Assert.True(session.HasClipboard);
    }

    [Fact]
    public void ExtrudeToolPullsTheSurfaceUnderTheCursor()
    {
        EditorSession session = SessionWithSlab(4, 1, 4, index: 6);
        session.ActiveTool = EditorTool.Extrude;

        Assert.True(session.ApplyTool(new RaycastHit(new Int3(1, 0, 1), Face.PosY, 1f)));

        Assert.Equal(32, session.World.SolidCount);   // 16 original + 16 new
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(16, session.World.SolidCount);
    }

    [Fact]
    public void BoxSelectToolDoesNotEditOnASingleClick()
    {
        EditorSession session = SessionWithSlab();
        session.ActiveTool = EditorTool.BoxSelect;

        Assert.False(session.ApplyTool(new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f)));
        Assert.Equal(24, session.World.SolidCount);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void ReplacingTheWorldDropsTheSelection()
    {
        EditorSession session = SessionWithSlab();
        session.SelectAll();

        session.ReplaceWorld(new VoxelWorld(), projectPath: null);

        Assert.False(session.HasSelection);
    }

    [Fact]
    public void MirroringTheSelectionIsUndoable()
    {
        var session = new EditorSession();
        session.World.SetVoxel(0, 0, 0, 1);
        session.World.SetVoxel(2, 0, 0, 2);
        session.Selection = new VoxelBox(Int3.Zero, new Int3(2, 0, 0));
        ulong before = session.World.ContentHash();

        Assert.True(session.MirrorSelection(Axis.X));
        Assert.Equal(2, session.World.GetVoxel(0, 0, 0));
        Assert.Equal(1, session.World.GetVoxel(2, 0, 0));

        session.Undo();
        Assert.Equal(before, session.World.ContentHash());
    }
}
