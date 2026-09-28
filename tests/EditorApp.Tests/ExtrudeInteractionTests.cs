using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// The extrude gesture end to end, driven through the same entry points the mouse goes through.
/// The camera is plain arithmetic, so the arrow can be aimed at exactly where it is drawn.
/// </summary>
public class ExtrudeInteractionTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    private sealed record Fixture(EditorSession Session, ExtrudeInteraction Extrude, FlyCamera Camera);

    private static VoxelWorld Cube(int side, byte index = Palette.WhiteIndex)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, index);
                }
            }
        }

        return grid;
    }

    /// <summary>A 4³ cube with its top face selected, seen from a corner so the arrow is not edge on.</summary>
    private static Fixture WithTopFaceSelected(float voxelSize = 1f)
    {
        var session = new EditorSession();
        var scene = new VoxelScene();
        scene.Add(Cube(4), ObjectTransform.Identity with { VoxelSize = voxelSize }, "cube");
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveTool = EditorTool.Extrude;

        session.SelectPatch(new RaycastHit(new Int3(2, 3, 2), Face.PosY, 1f));
        Assert.True(session.HasSelection);

        var camera = new FlyCamera { Position = new Vector3(14f, 14f, 14f) };
        camera.LookAt(new Vector3(2f, 2f, 2f));

        return new Fixture(session, new ExtrudeInteraction(session), camera);
    }

    private static Vector2 ScreenOf(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    [Fact]
    public void LettingGoOfTheArrowCommitsTheExtrude()
    {
        // The whole point: no keystroke needed to make a drag stick.
        (EditorSession session, ExtrudeInteraction extrude, FlyCamera camera) = WithTopFaceSelected();

        int before = session.World.SolidCount;
        (Vector3 start, _) = extrude.Arrow()!.Value;

        extrude.OnPress(null, ScreenOf(camera, start), Viewport, camera, shift: false, alt: false);
        Assert.True(extrude.IsDraggingArrow);

        extrude.OnDrag(null, ScreenOf(camera, start + (Vector3.UnitY * 3f)), Viewport, camera);
        Assert.Equal(3, session.ExtrudeSteps);

        extrude.OnRelease();

        Assert.False(extrude.IsDraggingArrow);
        Assert.False(session.IsExtruding);
        Assert.Equal(0, session.ExtrudeSteps);

        // Committed, in history, and the voxels are actually there.
        Assert.True(session.History.CanUndo);
        Assert.Equal(before + (4 * 4 * 3), session.World.SolidCount);
        Assert.True(session.HasUnsavedChanges);
    }

    /// <summary>
    /// One step is one of the object's own voxels. At half a unit per voxel, a drag of one and a half
    /// units along the arrow is three steps, not one and a half.
    /// </summary>
    [Fact]
    public void AStepIsOneOfTheObjectsOwnVoxels()
    {
        (EditorSession session, ExtrudeInteraction extrude, FlyCamera camera) = WithTopFaceSelected(voxelSize: 0.5f);
        (Vector3 start, _) = extrude.Arrow()!.Value;

        extrude.OnPress(null, ScreenOf(camera, start), Viewport, camera, shift: false, alt: false);
        extrude.OnDrag(null, ScreenOf(camera, start + (Vector3.UnitY * 1.5f)), Viewport, camera);

        Assert.Equal(3, session.ExtrudeSteps);
    }

    [Fact]
    public void UndoStillTakesItBack()
    {
        // Committing on release is only reasonable because there is a way out that is not Esc.
        (EditorSession session, ExtrudeInteraction extrude, FlyCamera camera) = WithTopFaceSelected();

        int before = session.World.SolidCount;
        (Vector3 start, _) = extrude.Arrow()!.Value;

        extrude.OnPress(null, ScreenOf(camera, start), Viewport, camera, shift: false, alt: false);
        extrude.OnDrag(null, ScreenOf(camera, start + (Vector3.UnitY * 2f)), Viewport, camera);
        extrude.OnRelease();

        // Undoing a live preview would put the voxels back too, so the count alone proves nothing.
        // What has to be true is that the step reached history in the first place.
        Assert.True(session.History.CanUndo);

        session.Undo();

        Assert.Equal(before, session.World.SolidCount);
        Assert.False(session.History.CanUndo);

        // The selection had advanced onto the new faces, and they are gone: no arrow left in the air.
        Assert.False(session.HasSelection);
        Assert.Null(extrude.Arrow());
    }

    /// <summary>A push in leaves the selection buried; undone, it is back on the surface it came from.</summary>
    [Fact]
    public void UndoingAPushInLeavesNoBuriedSelection()
    {
        (EditorSession session, ExtrudeInteraction extrude, FlyCamera camera) = WithTopFaceSelected();
        (Vector3 start, _) = extrude.Arrow()!.Value;

        extrude.OnPress(null, ScreenOf(camera, start), Viewport, camera, shift: false, alt: false);
        extrude.OnDrag(null, ScreenOf(camera, start - (Vector3.UnitY * 2f)), Viewport, camera);
        extrude.OnRelease();
        Assert.Equal(1, session.Selection!.Plane);

        session.Undo();

        Assert.False(session.HasSelection);
    }

    /// <summary>Only what an edit takes away leaves the selection; the rest of it stays to be pulled again.</summary>
    [Fact]
    public void OnlyTheFacesAnEditRemovesLeaveTheSelection()
    {
        (EditorSession session, _, _) = WithTopFaceSelected();
        Assert.Equal(16, session.Selection!.Count);

        // One corner voxel of the top goes, as its own undoable step.
        var edit = new VoxelEditCommand("Remove corner", session.World);
        edit.Apply(new Int3(0, 3, 0), Palette.EmptyIndex);
        session.History.Push(edit);

        session.Undo();
        Assert.Equal(16, session.Selection!.Count);

        session.Redo();
        Assert.Equal(15, session.Selection!.Count);
        Assert.False(session.Selection.Contains(new Int3(0, 3, 0)));
    }

    [Fact]
    public void GrabbingTheArrowAndLettingGoWithoutMovingChangesNothing()
    {
        (EditorSession session, ExtrudeInteraction extrude, FlyCamera camera) = WithTopFaceSelected();

        int before = session.World.SolidCount;
        (Vector3 start, _) = extrude.Arrow()!.Value;

        extrude.OnPress(null, ScreenOf(camera, start), Viewport, camera, shift: false, alt: false);
        extrude.OnRelease();

        Assert.Equal(before, session.World.SolidCount);
        Assert.False(session.History.CanUndo);
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public void TheSelectionAdvancesSoTheNextDragCarriesOn()
    {
        // Releasing commits but does not end the job — the surface that was just pulled out is the
        // one still selected, so the arrow is right there to drag again.
        (EditorSession session, ExtrudeInteraction extrude, FlyCamera camera) = WithTopFaceSelected();

        (Vector3 start, _) = extrude.Arrow()!.Value;
        extrude.OnPress(null, ScreenOf(camera, start), Viewport, camera, shift: false, alt: false);
        extrude.OnDrag(null, ScreenOf(camera, start + (Vector3.UnitY * 2f)), Viewport, camera);
        extrude.OnRelease();

        Assert.True(session.HasSelection);

        // The arrow sits two voxels higher either way — an uncommitted preview moves it just as far.
        // It is only the selection that has advanced if the steps have gone back to zero with it.
        Assert.Equal(0, session.ExtrudeSteps);
        Assert.False(session.IsExtruding);

        (Vector3 movedStart, _) = extrude.Arrow()!.Value;
        Assert.Equal(start.Y + 2f, movedStart.Y, 3);
    }
}
