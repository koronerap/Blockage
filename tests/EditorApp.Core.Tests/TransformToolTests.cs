using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class TransformToolTests
{
    private static (EditorSession Session, VoxelObject Target) SessionWithCube()
    {
        var session = new EditorSession { ActiveTool = EditorTool.Transform };
        var scene = new VoxelScene();

        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    grid.SetVoxel(x, y, z, 5);
                }
            }
        }

        VoxelObject target = scene.Add(grid, ObjectTransform.Identity, "cube");
        session.ReplaceScene(scene, projectPath: null);
        return (session, target);
    }

    [Fact]
    public void ADragBecomesOneUndoStep()
    {
        // The drag itself writes the transform every frame; only the finished gesture is recorded.
        (EditorSession session, VoxelObject target) = SessionWithCube();
        ObjectTransform before = target.Transform;

        session.ApplyTransform(target, before.Translated(new Vector3(1f, 0f, 0f)));
        session.ApplyTransform(target, before.Translated(new Vector3(4f, 0f, 0f)));
        Assert.False(session.History.CanUndo);

        Assert.True(session.PushTransformEdit(target, before, "Move object"));
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(before, target.Transform);

        session.Redo();
        Assert.Equal(new Vector3(4f, 0f, 0f), target.Transform.Position);
    }

    [Fact]
    public void ADragThatEndedWhereItStartedRecordsNothing()
    {
        (EditorSession session, VoxelObject target) = SessionWithCube();
        ObjectTransform before = target.Transform;

        session.ApplyTransform(target, before.Translated(new Vector3(3f, 0f, 0f)));
        session.ApplyTransform(target, before);

        Assert.False(session.PushTransformEdit(target, before, "Move object"));
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void MovingAnObjectDoesNotTouchItsVoxels()
    {
        // The grid stays axis aligned in its own space; only the transform knows about placement.
        (EditorSession session, VoxelObject target) = SessionWithCube();
        ulong gridBefore = target.Grid.ContentHash();

        session.ApplyTransform(target, target.Transform.Translated(new Vector3(17f, -4f, 9f)));

        Assert.Equal(gridBefore, target.Grid.ContentHash());
        Assert.Equal(64, target.Grid.SolidCount);
    }

    [Fact]
    public void RotatingAboutTheObjectCentreKeepsItInPlace()
    {
        (EditorSession session, VoxelObject target) = SessionWithCube();
        Vector3 centreBefore = target.WorldCentre();

        Quaternion quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        session.ApplyTransform(target, target.Transform.RotatedAbout(centreBefore, quarter));

        Assert.True(Vector3.Distance(centreBefore, target.WorldCentre()) < 1e-4f);
    }

    [Fact]
    public void HingingAboutACornerSwingsTheObjectAroundIt()
    {
        (EditorSession session, VoxelObject target) = SessionWithCube();
        var corner = new Vector3(0f, 0f, 0f);

        Quaternion quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        session.ApplyTransform(target, target.Transform.RotatedAbout(corner, quarter));

        // The corner is the pivot, so it must not have moved; the centre must have.
        Assert.True(target.TryGetWorldBounds(out Vector3 min, out Vector3 max));
        Assert.True(MathF.Abs(min.X) < 1e-4f || MathF.Abs(max.X) < 1e-4f);
        Assert.True(Vector3.Distance(target.WorldCentre(), new Vector3(2f, 2f, 2f)) > 1e-3f);
    }

    [Fact]
    public void TransformEditsBarelyCostTheUndoBudget()
    {
        // A placement is not voxels, so it must not evict brush strokes from the history.
        (EditorSession session, VoxelObject target) = SessionWithCube();

        session.ApplyTransform(target, target.Transform.Translated(Vector3.UnitX));
        session.PushTransformEdit(target, ObjectTransform.Identity, "Move object");

        Assert.Equal(1, session.History.RetainedCells);
    }

    [Fact]
    public void SnapKeepsMovementOnWholeVoxels()
    {
        ObjectTransform drifted = ObjectTransform.At(new Vector3(3.4f, -0.6f, 12.5f));
        Vector3 snapped = ObjectTransform.SnapPosition(drifted.Position);

        Assert.Equal(3f, snapped.X);
        Assert.Equal(-1f, snapped.Y);
        Assert.Equal(13f, snapped.Z);
    }

    [Fact]
    public void ATransformAppliesToItsTargetNotToWhateverHasFocus()
    {
        // A gizmo drag that passed over another object used to start moving that one instead, so
        // the target is explicit and focus has no say in it.
        (EditorSession session, VoxelObject target) = SessionWithCube();
        VoxelObject other = session.Scene.Add(new VoxelWorld(), ObjectTransform.Identity, "other");
        session.TryFocus(other.Id);

        session.ApplyTransform(target, ObjectTransform.At(new Vector3(6f, 0f, 0f)));

        Assert.Equal(new Vector3(6f, 0f, 0f), target.Transform.Position);
        Assert.Equal(Vector3.Zero, other.Transform.Position);
    }

    [Fact]
    public void UndoingATransformDoesNotDisturbOtherObjects()
    {
        (EditorSession session, VoxelObject target) = SessionWithCube();
        VoxelObject other = session.Scene.Add(new VoxelWorld(), ObjectTransform.At(new Vector3(50f, 0f, 0f)), "other");

        ObjectTransform before = target.Transform;
        session.ApplyTransform(target, before.Translated(new Vector3(5f, 0f, 0f)));
        session.PushTransformEdit(target, before, "Move object");

        session.Undo();

        Assert.Equal(before, target.Transform);
        Assert.Equal(new Vector3(50f, 0f, 0f), other.Transform.Position);
    }
}
