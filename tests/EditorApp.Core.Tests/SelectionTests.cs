using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Selecting objects and lights (Fullreleaseplan 0.2): what a click, Shift, Ctrl and a box do, and the
/// operations that act on everything selected at once — each one undo step — while picking things
/// is never an undo step itself.
/// </summary>
public class SelectionTests
{
    private static VoxelWorld Cube(int size)
    {
        var world = new VoxelWorld();
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    world.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        return world;
    }

    /// <summary>Three 2³ cubes in a row along X, eight units apart, and the sun; nothing selected.</summary>
    private static (EditorSession Session, VoxelObject A, VoxelObject B, VoxelObject C) Three()
    {
        var scene = new VoxelScene();
        VoxelObject a = scene.Add(Cube(2), ObjectTransform.Identity, "A");
        VoxelObject b = scene.Add(Cube(2), new ObjectTransform(new Vector3(8f, 0f, 0f), Quaternion.Identity), "B");
        VoxelObject c = scene.Add(Cube(2), new ObjectTransform(new Vector3(16f, 0f, 0f), Quaternion.Identity), "C");
        scene.AddDefaultSun();

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.DeselectAll();
        return (session, a, b, c);
    }

    // ---- Clicks -------------------------------------------------------------------------------

    [Fact]
    public void AClickSelectsOnlyThatAndMakesItActive()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();

        session.ClickSelect(a.Id);
        session.ClickSelect(b.Id);

        Assert.Equal(new[] { b.Id }, session.Scene.SelectedIds);
        Assert.Equal(b.Id, session.ActiveId);
        Assert.Equal(b.Id, session.Scene.FocusId);
    }

    [Fact]
    public void ShiftAddsThenMakesActiveThenLetsGo()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.ClickSelect(a.Id);

        // Not selected: added, and active.
        session.ClickSelect(b.Id, extend: true);
        Assert.True(session.IsSelected(a.Id) && session.IsSelected(b.Id));
        Assert.Equal(b.Id, session.ActiveId);

        // Selected but not active: made active, still selected.
        session.ClickSelect(a.Id, extend: true);
        Assert.True(session.IsSelected(a.Id));
        Assert.Equal(a.Id, session.ActiveId);

        // The active one: let go.
        session.ClickSelect(a.Id, extend: true);
        Assert.False(session.IsSelected(a.Id));
        Assert.True(session.IsSelected(b.Id));
    }

    [Fact]
    public void LockedAndHiddenThingsCannotBeSelected()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.SetObjectLocked(a.Id, true);
        session.SetObjectVisible(b.Id, false);

        Assert.False(session.ClickSelect(a.Id));
        Assert.False(session.ClickSelect(b.Id));
        Assert.Equal(0, session.SelectedCount);
    }

    [Fact]
    public void HidingOrLockingLetsGoOfIt()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.SelectAll();

        session.SetObjectVisible(a.Id, false);
        session.SetObjectLocked(b.Id, true);

        Assert.False(session.IsSelected(a.Id));
        Assert.False(session.IsSelected(b.Id));
    }

    [Fact]
    public void SelectingIsNotAnUndoStep()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();

        session.ClickSelect(a.Id);
        session.ClickSelect(b.Id, extend: true);
        session.SelectAll();
        session.InvertSelection();

        Assert.False(session.History.CanUndo);
    }

    // ---- Everything, nothing, the rest ----------------------------------------------------------

    [Fact]
    public void SelectAllTakesEveryObjectAndLightThatCanBe()
    {
        (EditorSession session, VoxelObject a, _, _) = Three();
        session.SetObjectLocked(a.Id, true);

        session.SelectAll();

        Assert.Equal(2 + session.Scene.Lights.Count, session.SelectedCount);
        Assert.False(session.IsSelected(a.Id));
    }

    [Fact]
    public void InvertSwapsWhatIsSelected()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();
        session.ClickSelect(a.Id);

        session.InvertSelection();

        Assert.False(session.IsSelected(a.Id));
        Assert.True(session.IsSelected(b.Id) && session.IsSelected(c.Id));
        Assert.True(session.IsSelected(session.ActiveId));
    }

    [Fact]
    public void ABoxReplacesAddsOrTakesAway()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();

        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);
        Assert.Equal(2, session.SelectedCount);

        session.SelectMany([c.Id], SelectionOperation.Add);
        Assert.Equal(3, session.SelectedCount);

        session.SelectMany([a.Id], SelectionOperation.Subtract);
        Assert.False(session.IsSelected(a.Id));

        // What is active stays among what is selected.
        Assert.True(session.IsSelected(session.ActiveId));
    }

    // ---- The phone, and opening a level --------------------------------------------------------

    [Fact]
    public void FocusingSomethingOutsideTheSelectionMakesItTheSelection()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.ClickSelect(a.Id);

        Assert.True(session.TryFocus(b.Id));

        Assert.Equal(new[] { b.Id }, session.Scene.SelectedIds);
    }

    [Fact]
    public void FocusingWithinTheSelectionKeepsIt()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);

        Assert.True(session.TryFocus(b.Id));

        Assert.Equal(2, session.SelectedCount);
        Assert.Equal(b.Id, session.Scene.FocusId);
    }

    [Fact]
    public void ALevelWithNothingSelectedOpensWithItsActiveObjectSelected()
    {
        var session = new EditorSession();
        session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);

        Assert.Equal(new[] { session.Scene.FocusId }, session.Scene.SelectedIds);
    }

    // ---- On everything selected -----------------------------------------------------------------

    [Fact]
    public void DeletingTheSelectionIsOneStepAndUndoBringsItBackSelected()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();
        session.SelectMany([a.Id, c.Id], SelectionOperation.Replace);

        Assert.Equal(2, session.DeleteSelected());
        Assert.Equal(new[] { b }, session.Scene.Objects);
        Assert.Equal(0, session.SelectedCount);

        Assert.True(session.Undo());
        Assert.Equal(new[] { a, b, c }, session.Scene.Objects);
        Assert.True(session.IsSelected(a.Id) && session.IsSelected(c.Id));
        Assert.False(session.IsSelected(b.Id));
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void EverythingCanBeDeletedAndTheEmptyLevelStillWorks()
    {
        (EditorSession session, _, _, _) = Three();
        session.SelectAll();

        session.DeleteSelected();

        Assert.Empty(session.Scene.Objects);
        Assert.Empty(session.Scene.Lights);
        Assert.Equal(0, session.ActiveId);
        Assert.Null(session.TransformTarget);
        Assert.Equal(0, session.World.SolidCount);
    }

    [Fact]
    public void DuplicatingTheSelectionKeepsItsArrangementAndSelectsTheCopies()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);

        IReadOnlyList<IPlaceable> copies = session.DuplicateSelected(Vector3.UnitZ);

        Assert.Equal(2, copies.Count);
        Assert.Equal(5, session.Scene.Objects.Count);
        Assert.Equal(copies.Select(c => c.Id).Order(), session.Scene.SelectedIds.Order());

        // Both moved by the same step, so they stand apart as the originals do.
        Vector3 offsetA = copies[0].Transform.Position - a.Transform.Position;
        Vector3 offsetB = copies[1].Transform.Position - b.Transform.Position;
        Assert.Equal(offsetA, offsetB);
        Assert.True(offsetA.Z > 0f);

        Assert.True(session.Undo());
        Assert.Equal(3, session.Scene.Objects.Count);
    }

    [Fact]
    public void ACopiedChildGoesUnderItsParentsCopy()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.SetParent(b.Id, a.Id);
        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);

        IReadOnlyList<IPlaceable> copies = session.DuplicateSelected(Vector3.UnitX);

        VoxelObject parentCopy = (VoxelObject)copies.Single(c => c.Name.StartsWith('A'));
        VoxelObject childCopy = (VoxelObject)copies.Single(c => c.Name.StartsWith('B'));
        Assert.Equal(parentCopy.Id, session.Scene.ParentOf(childCopy)?.Id);
    }

    [Fact]
    public void AChildIsNotMovedTwiceWhenItsParentIsSelectedToo()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();
        session.SetParent(b.Id, a.Id);
        session.SelectMany([a.Id, b.Id, c.Id], SelectionOperation.Replace);
        session.ClickSelect(c.Id, extend: true);

        IReadOnlyList<IPlaceable> targets = session.TransformTargets;

        Assert.Equal(new IPlaceable[] { c, a }, targets);
    }

    [Fact]
    public void MovingSeveralIsOneUndoStep()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        ObjectTransform beforeA = a.Transform;
        ObjectTransform beforeB = b.Transform;

        session.ApplyTransform(a, beforeA.Translated(Vector3.UnitY));
        session.ApplyTransform(b, beforeB.Translated(Vector3.UnitY));
        Assert.True(session.PushTransformEdits([(a, beforeA), (b, beforeB)], "Move 2 objects"));

        Assert.Equal(1, session.History.UndoCount);
        session.Undo();
        Assert.Equal(beforeA, a.Transform);
        Assert.Equal(beforeB, b.Transform);
    }

    [Theory]
    [InlineData(TransformPivot.MedianPoint, 9f)]
    [InlineData(TransformPivot.ActiveElement, 17f)]
    public void TheGizmoStandsAtThePivot(TransformPivot pivot, float x)
    {
        (EditorSession session, VoxelObject a, _, VoxelObject c) = Three();
        session.ClickSelect(a.Id);
        session.ClickSelect(c.Id, extend: true);
        session.Pivot = pivot;

        Vector3 point = session.PivotPoint(session.TransformTargets);

        // A's centre is at 1, C's at 17 — each a 2³ cube from its corner.
        Assert.Equal(x, point.X, 3);
    }

    [Fact]
    public void CtrlPParentsTheRestToTheActive()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();
        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);
        session.ClickSelect(c.Id, extend: true);

        Assert.Equal(2, session.ParentSelectedToActive());

        Assert.Equal(c.Id, session.Scene.ParentOf(a)?.Id);
        Assert.Equal(c.Id, session.Scene.ParentOf(b)?.Id);
        Assert.Equal(1, session.History.UndoCount);

        Assert.Equal(2, session.ClearParentOfSelected());
        Assert.Null(session.Scene.ParentOf(a));
    }

    [Fact]
    public void CtrlJJoinsTheRestIntoTheActive()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();
        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);
        session.ClickSelect(c.Id, extend: true);

        Assert.Equal(2, session.JoinSelectedIntoActive(out int refused));

        Assert.Equal(0, refused);
        Assert.Equal(new[] { c }, session.Scene.Objects);
        Assert.Equal(3 * 8, c.Grid.SolidCount);

        Assert.True(session.Undo());
        Assert.Equal(3, session.Scene.Objects.Count);
    }

    [Fact]
    public void CopyAndPasteTakeEverySelectedObject()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);

        Assert.Equal(16, session.Copy());
        VoxelObject? first = session.Paste(Vector3.UnitZ);

        Assert.NotNull(first);
        Assert.Equal(5, session.Scene.Objects.Count);
        Assert.Equal(2, session.SelectedCount);
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void NothingSelectedCopiesNothing()
    {
        (EditorSession session, _, _, _) = Three();

        Assert.Equal(0, session.Copy());
        Assert.Equal(0, session.Cut());
        Assert.Equal(3, session.Scene.Objects.Count);
    }

    [Fact]
    public void TurnsAndMirrorsReachEverySelectedObjectAsOneStep()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, _) = Three();
        a.Grid.SetVoxel(5, 0, 0, Palette.WhiteIndex);
        b.Grid.SetVoxel(5, 0, 0, Palette.WhiteIndex);
        ulong beforeA = a.Grid.ContentHash();
        ulong beforeB = b.Grid.ContentHash();
        session.SelectMany([a.Id, b.Id], SelectionOperation.Replace);

        Assert.Equal(2, session.FlipSelected(Axis.X));

        Assert.NotEqual(beforeA, a.Grid.ContentHash());
        Assert.NotEqual(beforeB, b.Grid.ContentHash());
        Assert.Equal(1, session.History.UndoCount);
    }

    // ---- In the file ----------------------------------------------------------------------------

    [Fact]
    public void TheSelectionAndTheActiveObjectAreSavedWithTheLevel()
    {
        (EditorSession session, VoxelObject a, _, VoxelObject c) = Three();
        session.ClickSelect(c.Id);
        session.ClickSelect(a.Id, extend: true);

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "selected");
        stream.Position = 0;
        VoxelScene loaded = VxLevelFile.LoadScene(stream);

        Assert.Equal(new[] { "A", "C" }, loaded.SelectedObjects.Select(o => o.Name));
        Assert.Equal("A", loaded.Focus?.Name);
    }

    [Fact]
    public void AnEmptyLevelComesBackEmpty()
    {
        (EditorSession session, _, _, _) = Three();
        session.SelectAll();
        session.DeleteSelected();

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "empty");
        stream.Position = 0;
        VoxelScene loaded = VxLevelFile.LoadScene(stream);

        // An empty object list is a level with nothing in it, not a version 1 file.
        Assert.Empty(loaded.Objects);
    }
}
