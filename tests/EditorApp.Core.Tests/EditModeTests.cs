using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Edit Mode (Fullreleaseplan 0.3): inside the active object, choosing voxels by click, wand, colour
/// and all-none-invert-grow-shrink, and what can be done to them — delete, fill, separate, and the
/// gizmo's moves and quarter turns — each one undo step that brings the selection back with it.
/// </summary>
public class EditModeTests
{
    private const byte Red = 30;
    private const byte Blue = 60;

    /// <summary>A 4 × 1 × 1 bar along X, two red voxels then two blue, the one object of the level.</summary>
    private static (EditorSession Session, VoxelObject Bar) Bar()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Red);
        grid.SetVoxel(1, 0, 0, Red);
        grid.SetVoxel(2, 0, 0, Blue);
        grid.SetVoxel(3, 0, 0, Blue);

        var scene = new VoxelScene();
        VoxelObject bar = scene.Add(grid, ObjectTransform.Identity, "Bar");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        Assert.True(session.EnterEditMode());
        return (session, bar);
    }

    private static Int3 C(int x, int y = 0, int z = 0) => new(x, y, z);

    [Fact]
    public void EditModeGoesIntoTheActiveObjectAndOutAgain()
    {
        (EditorSession session, VoxelObject bar) = Bar();

        Assert.True(session.InEditMode);
        Assert.Same(bar, session.EditObject);

        session.ExitEditMode();
        Assert.False(session.InEditMode);
        Assert.Null(session.EditObject);
    }

    [Fact]
    public void ALockedOrMissingObjectCannotBeEdited()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.ExitEditMode();
        session.SetObjectLocked(bar.Id, true);

        Assert.False(session.EnterEditMode());

        var empty = new EditorSession();
        empty.ReplaceScene(new VoxelScene(), projectPath: null);
        Assert.False(empty.EnterEditMode());
    }

    [Fact]
    public void AClickTakesOneVoxelShiftTogglesAndCtrlTakesAway()
    {
        (EditorSession session, _) = Bar();

        session.ClickVoxel(C(0), shift: false, control: false);
        session.ClickVoxel(C(1), shift: false, control: false);
        Assert.Equal(new[] { C(1) }, session.VoxelSelection.Cells);

        session.ClickVoxel(C(2), shift: true, control: false);
        Assert.Equal(2, session.VoxelSelection.Count);

        session.ClickVoxel(C(2), shift: true, control: false);
        Assert.False(session.VoxelSelection.Contains(C(2)));

        session.ClickVoxel(C(1), shift: false, control: true);
        Assert.True(session.VoxelSelection.IsEmpty);
    }

    [Fact]
    public void TheWandTakesTheJoinedVoxelsOfOneColour()
    {
        (EditorSession session, VoxelObject bar) = Bar();

        // A red voxel apart from the others: the same colour, but not joined to them.
        bar.Grid.SetVoxel(6, 0, 0, Red);
        session.VoxelSelectMode = VoxelSelectMode.Wand;

        session.ClickVoxel(C(0), shift: false, control: false);

        Assert.Equal(new[] { C(0), C(1) }, session.VoxelSelection.Cells.OrderBy(c => c.X));
    }

    [Fact]
    public void SelectByColourTakesEveryVoxelOfIt()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        bar.Grid.SetVoxel(6, 0, 0, Red);
        session.VoxelSelectMode = VoxelSelectMode.Colour;

        session.ClickVoxel(C(1), shift: false, control: false);

        Assert.Equal(new[] { C(0), C(1), C(6) }, session.VoxelSelection.Cells.OrderBy(c => c.X));
    }

    [Fact]
    public void AllInvertGrowAndShrink()
    {
        (EditorSession session, _) = Bar();

        Assert.Equal(4, session.SelectAllVoxels());

        session.ClickVoxel(C(0), shift: false, control: false);
        session.InvertVoxelSelection();
        Assert.Equal(new[] { C(1), C(2), C(3) }, session.VoxelSelection.Cells.OrderBy(c => c.X));

        session.ClickVoxel(C(1), shift: false, control: false);
        Assert.Equal(2, session.GrowVoxelSelection());
        Assert.Equal(3, session.VoxelSelection.Count);

        // Along the bar only, the way it spreads: its two ends go, its middle stays.
        Assert.Equal(2, session.ShrinkVoxelSelection());
        Assert.Equal(new[] { C(1) }, session.VoxelSelection.Cells);
    }

    [Fact]
    public void ChoosingVoxelsIsNotAnUndoStep()
    {
        (EditorSession session, _) = Bar();

        session.SelectAllVoxels();
        session.InvertVoxelSelection();
        session.ClickVoxel(C(2), shift: true, control: false);

        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void DeletingTheChosenVoxelsIsOneStepAndUndoBringsThemBackChosen()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.SelectVoxels([C(0), C(1)], SelectionOperation.Replace);

        Assert.Equal(2, session.DeleteSelectedVoxels());
        Assert.Equal(2, bar.Grid.SolidCount);
        Assert.True(session.VoxelSelection.IsEmpty);

        Assert.True(session.Undo());
        Assert.Equal(4, bar.Grid.SolidCount);
        Assert.Equal(2, session.VoxelSelection.Count);
    }

    [Fact]
    public void FillColoursTheChosenVoxelsPaintedFacesAndAll()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        bar.Grid.SetFaceColor(C(2), Face.PosY, 90);
        session.ActiveColorIndex = 12;
        session.SelectVoxels([C(2), C(3)], SelectionOperation.Replace);

        Assert.Equal(2, session.FillSelectedVoxels());

        Assert.Equal(12, bar.Grid.GetVoxel(C(2)));
        Assert.Equal(12, bar.Grid.GetFaceColor(C(2), Face.PosY));
        Assert.Equal(Red, bar.Grid.GetVoxel(C(0)));

        session.Undo();
        Assert.Equal(Blue, bar.Grid.GetVoxel(C(2)));
        Assert.Equal(90, bar.Grid.GetFaceColor(C(2), Face.PosY));
    }

    [Fact]
    public void SeparateTakesTheChosenVoxelsIntoAnObjectOfTheirOwn()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.SelectVoxels([C(2), C(3)], SelectionOperation.Replace);

        VoxelObject? piece = session.SeparateSelectedVoxels();

        Assert.NotNull(piece);
        Assert.Equal(2, session.Scene.Objects.Count);
        Assert.Equal(2, bar.Grid.SolidCount);
        Assert.Equal(Blue, piece.Grid.GetVoxel(C(2)));
        Assert.Equal(bar.Transform, piece.Transform);

        // Still inside the object it came out of.
        Assert.Same(bar, session.EditObject);

        session.Undo();
        Assert.Single(session.Scene.Objects);
        Assert.Equal(4, bar.Grid.SolidCount);
    }

    [Fact]
    public void TheGizmoMovesTheChosenVoxelsByWholeVoxels()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.SelectVoxels([C(0), C(1)], SelectionOperation.Replace);

        IPlaceable handle = Assert.Single(session.TransformTargets);
        ObjectTransform start = handle.Transform;
        Assert.Equal(1f, start.Position.X, 3);

        // A little over five voxels along Y: five whole ones.
        session.ApplyTransform(handle, start.Translated(new Vector3(0f, 5.3f, 0f)));
        Assert.Equal(Red, bar.Grid.GetVoxel(C(0, 5)));
        Assert.False(bar.Grid.IsSolid(C(0)));

        Assert.True(session.PushTransformEdits([(handle, start)], "Move"));
        Assert.Equal(new[] { C(0, 5), C(1, 5) }, session.VoxelSelection.Cells.OrderBy(c => c.X));
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(Red, bar.Grid.GetVoxel(C(0)));
        Assert.False(bar.Grid.IsSolid(C(0, 5)));
        Assert.Equal(new[] { C(0), C(1) }, session.VoxelSelection.Cells.OrderBy(c => c.X));
    }

    [Fact]
    public void DraggingBackToTheStartMovesNothing()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.SelectVoxels([C(0), C(1)], SelectionOperation.Replace);
        ulong before = bar.Grid.ContentHash();

        IPlaceable handle = Assert.Single(session.TransformTargets);
        ObjectTransform start = handle.Transform;
        session.ApplyTransform(handle, start.Translated(new Vector3(3f, 0f, 0f)));
        session.ApplyTransform(handle, start);

        Assert.False(session.PushTransformEdits([(handle, start)], "Move"));
        Assert.Equal(before, bar.Grid.ContentHash());
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void AQuarterTurnOfTheGizmoTurnsTheChosenVoxels()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.SelectAllVoxels();

        IPlaceable handle = Assert.Single(session.TransformTargets);
        ObjectTransform start = handle.Transform;

        // About Y by a little under a quarter: a quarter. The bar lies along Z afterwards.
        session.ApplyTransform(handle, start.RotatedAbout(start.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 80f * MathF.PI / 180f)));
        session.PushTransformEdits([(handle, start)], "Rotate");

        Assert.True(bar.Grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(0, max.X - min.X);
        Assert.Equal(3, max.Z - min.Z);
        Assert.Equal(4, bar.Grid.SolidCount);
    }

    [Fact]
    public void MirroringSwapsTheChosenVoxelsEnds()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.SelectAllVoxels();

        Assert.True(session.MirrorSelectedVoxels(Axis.X));

        Assert.Equal(Blue, bar.Grid.GetVoxel(C(0)));
        Assert.Equal(Red, bar.Grid.GetVoxel(C(3)));
        Assert.Equal(4, bar.Grid.SolidCount);
    }

    [Fact]
    public void CopyInEditModeTakesOnlyTheChosenVoxels()
    {
        (EditorSession session, _) = Bar();
        session.SelectVoxels([C(3)], SelectionOperation.Replace);

        Assert.Equal(1, session.Copy());
        Assert.Equal(1, session.Clipboard!.Grid.SolidCount);
    }

    [Fact]
    public void TheToolsStayInsideTheObjectBeingEdited()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        VoxelObject other = session.Scene.Add(Cube(), new ObjectTransform(new Vector3(10f, 0f, 0f), Quaternion.Identity), "Other");
        session.Scene.Select(other.Id);

        Assert.False(session.TryFocus(other.Id));
        Assert.Same(bar, session.EditObject);

        // Choosing another object leaves Edit Mode.
        session.ClickSelect(other.Id);
        Assert.False(session.InEditMode);
    }

    [Fact]
    public void UndoingTheEditedObjectAwayLeavesEditMode()
    {
        var session = new EditorSession();
        session.ReplaceScene(new VoxelScene(), projectPath: null);
        session.AddObject(Cube(), "Cube", Vector3.Zero, Vector3.UnitY, 1f);
        Assert.True(session.EnterEditMode());

        session.Undo();

        Assert.False(session.InEditMode);
    }

    private static VoxelWorld Cube()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        return grid;
    }
}
