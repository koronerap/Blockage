using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class SessionFocusTests
{
    private static (EditorSession Session, VoxelObject A, VoxelObject B) TwoObjects()
    {
        var session = new EditorSession();
        var scene = new VoxelScene();

        var first = new VoxelWorld();
        first.SetVoxel(0, 0, 0, 5);
        VoxelObject a = scene.Add(first, ObjectTransform.Identity, "a");

        var second = new VoxelWorld();
        second.SetVoxel(0, 0, 0, 5);
        VoxelObject b = scene.Add(second, ObjectTransform.At(new Vector3(20f, 0f, 0f)), "b");

        session.ReplaceScene(scene, projectPath: null);
        return (session, a, b);
    }

    [Fact]
    public void ToolsEditWhicheverObjectHasFocus()
    {
        (EditorSession session, VoxelObject a, VoxelObject b) = TwoObjects();

        Assert.Same(a.Grid, session.World);

        Assert.True(session.TryFocus(b.Id));
        Assert.Same(b.Grid, session.World);
    }

    [Fact]
    public void FocusIsLockedWhileAStrokeIsRunning()
    {
        // Focus changing mid-drag would hand the rest of the gesture to a different object.
        (EditorSession session, VoxelObject a, VoxelObject b) = TwoObjects();

        session.BeginStroke();
        Assert.False(session.TryFocus(b.Id));
        Assert.Same(a.Grid, session.World);

        session.EndStroke();
        Assert.True(session.TryFocus(b.Id));
    }

    [Fact]
    public void FocusIsLockedWhileAnExtrudePreviewIsPending()
    {
        (EditorSession session, VoxelObject a, VoxelObject b) = TwoObjects();

        session.SetSelection(FaceSelection.ConnectedPatch(session.World, Int3.Zero, Face.PosY));
        session.PreviewExtrude(2);

        Assert.False(session.TryFocus(b.Id));

        session.CancelExtrude();
        Assert.True(session.TryFocus(b.Id));
        Assert.Same(b.Grid, session.World);
    }

    [Fact]
    public void ChangingFocusDropsTheSelectionItWasMadeOn()
    {
        (EditorSession session, _, VoxelObject b) = TwoObjects();

        session.SetSelection(FaceSelection.ConnectedPatch(session.World, Int3.Zero, Face.PosY));
        Assert.True(session.HasSelection);

        session.TryFocus(b.Id);
        Assert.False(session.HasSelection);
    }

    [Fact]
    public void DeletingAnObjectIsUndoable()
    {
        (EditorSession session, VoxelObject a, VoxelObject b) = TwoObjects();

        Assert.True(session.DeleteObject(a.Id));
        Assert.Single(session.Scene.Objects);
        Assert.Equal(b.Id, session.Scene.Objects[0].Id);

        session.Undo();

        Assert.Equal(2, session.Scene.Objects.Count);
        Assert.Contains(session.Scene.Objects, o => o.Id == a.Id);
    }

    [Fact]
    public void TheLastObjectCanBeDeletedAndComesBackOnUndo()
    {
        // Shift+A adds to an empty level, so an empty one is no dead end any more.
        (EditorSession session, VoxelObject a, VoxelObject b) = TwoObjects();
        session.DeleteObject(a.Id);

        Assert.True(session.DeleteObject(b.Id));
        Assert.Empty(session.Scene.Objects);
        Assert.Equal(0, session.Scene.FocusId);

        Assert.True(session.Undo());
        Assert.Same(b, Assert.Single(session.Scene.Objects));
        Assert.Equal(b.Id, session.Scene.FocusId);
    }

    [Fact]
    public void PaletteEditsReachEveryObject()
    {
        (EditorSession session, VoxelObject a, VoxelObject b) = TwoObjects();

        session.ApplyPaletteColor(5, new Color32(9, 9, 9));

        Assert.Equal(new Color32(9, 9, 9), a.Grid.Palette[5]);
        Assert.Equal(new Color32(9, 9, 9), b.Grid.Palette[5]);
    }

    [Fact]
    public void AnEmptyLevelHasNothingForTheToolsToWriteInto()
    {
        // No objects: World can still be read, but nothing is made to write into, and no tool writes.
        var session = new EditorSession { ActiveTool = EditorTool.Paint };

        Assert.Equal(0, session.World.SolidCount);
        session.BeginStroke();
        Assert.False(session.Paint(new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f)));
        session.EndStroke();

        Assert.Empty(session.Scene.Objects);
        Assert.False(session.History.CanUndo);
    }
}

public class ObjectTransformTests
{
    [Fact]
    public void PointsRoundTripThroughTheTransform()
    {
        var transform = new ObjectTransform(
            new Vector3(10f, -3f, 7f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 3f));

        var local = new Vector3(2f, 5f, -1f);
        Vector3 world = transform.TransformPoint(local);

        Assert.True(Vector3.Distance(local, transform.InverseTransformPoint(world)) < 1e-4f);
    }

    [Fact]
    public void QuarterTurnAboutYMapsXOntoMinusZ()
    {
        var transform = new ObjectTransform(
            Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));

        Vector3 rotated = transform.TransformDirection(Vector3.UnitX);

        Assert.True(Vector3.Distance(rotated, -Vector3.UnitZ) < 1e-4f);
    }

    [Fact]
    public void RotatingAboutAPivotKeepsThatPivotStill()
    {
        // The rotate rings and the edge hinge are the same operation with different pivots.
        var transform = new ObjectTransform(new Vector3(4f, 0f, 0f), Quaternion.Identity);
        var pivot = new Vector3(4f, 0f, 0f);

        ObjectTransform rotated = transform.RotatedAbout(
            pivot, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));

        Assert.True(Vector3.Distance(rotated.Position, pivot) < 1e-4f);
    }

    [Fact]
    public void RotatingAboutADistantPivotSwingsTheObjectAround()
    {
        var transform = new ObjectTransform(new Vector3(2f, 0f, 0f), Quaternion.Identity);

        ObjectTransform rotated = transform.RotatedAbout(
            Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));

        Assert.True(Vector3.Distance(rotated.Position, new Vector3(-2f, 0f, 0f)) < 1e-4f);
    }

    [Fact]
    public void SnapLandsOnWholeUnitsAndFixedAngles()
    {
        Assert.Equal(new Vector3(3f, -1f, 8f), ObjectTransform.SnapPosition(new Vector3(2.6f, -1.2f, 7.51f)));
        Assert.Equal(15f, ObjectTransform.SnapAngleDegrees(11f));
        Assert.Equal(0f, ObjectTransform.SnapAngleDegrees(4f));
        Assert.Equal(90f, ObjectTransform.SnapAngleDegrees(87f));
    }

    [Fact]
    public void RayMovesIntoObjectSpaceForPicking()
    {
        var transform = new ObjectTransform(
            new Vector3(5f, 0f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));

        var worldRay = new Ray(new Vector3(5f, 0.5f, 10f), -Vector3.UnitZ);
        Ray local = transform.InverseTransformRay(worldRay);

        Assert.True(MathF.Abs(local.Direction.Length() - 1f) < 1e-4f);
        Assert.True(Vector3.Distance(transform.TransformPoint(local.Origin), worldRay.Origin) < 1e-4f);
    }
}

public class VoxelSceneTests
{
    private static VoxelWorld Cube(int side, byte index = 5)
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

    [Fact]
    public void TheFirstObjectAddedTakesFocus()
    {
        var scene = new VoxelScene();
        VoxelObject first = scene.Add(Cube(2), ObjectTransform.Identity);
        VoxelObject second = scene.Add(Cube(2), ObjectTransform.At(new Vector3(20f, 0f, 0f)));

        Assert.Equal(first.Id, scene.FocusId);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, scene.Objects.Count);
    }

    [Fact]
    public void EveryObjectSharesTheScenePalette()
    {
        // Colour belongs to the level, not to a piece of it: one palette becomes one export texture.
        var scene = new VoxelScene();
        VoxelObject a = scene.Add(Cube(1), ObjectTransform.Identity);
        VoxelObject b = scene.Add(Cube(1), ObjectTransform.At(Vector3.UnitX * 10f));

        scene.Palette[7] = new Color32(1, 2, 3);

        Assert.Same(scene.Palette, a.Grid.Palette);
        Assert.Same(scene.Palette, b.Grid.Palette);
        Assert.Equal(new Color32(1, 2, 3), b.Grid.Palette[7]);
    }

    [Fact]
    public void RemovingTheFocusedObjectMovesFocusElsewhere()
    {
        var scene = new VoxelScene();
        VoxelObject first = scene.Add(Cube(1), ObjectTransform.Identity);
        VoxelObject second = scene.Add(Cube(1), ObjectTransform.At(Vector3.UnitX * 5f));

        Assert.True(scene.Remove(first.Id));
        Assert.Equal(second.Id, scene.FocusId);

        Assert.True(scene.Remove(second.Id));
        Assert.Equal(0, scene.FocusId);
        Assert.Null(scene.Focus);
    }

    [Fact]
    public void PickingChoosesTheNearestObjectAlongTheRay()
    {
        var scene = new VoxelScene();
        scene.Add(Cube(2), ObjectTransform.At(new Vector3(20f, 0f, 0f)), "far");
        VoxelObject near = scene.Add(Cube(2), ObjectTransform.At(new Vector3(5f, 0f, 0f)), "near");

        var ray = new Ray(new Vector3(-10f, 0.5f, 0.5f), Vector3.UnitX);

        Assert.True(scene.TryPick(ray, out ScenePick pick));
        Assert.Equal(near.Id, pick.Object.Id);
    }

    [Fact]
    public void PickingWorksThroughAnObjectsRotation()
    {
        // A rotated object still picks exactly, because the ray is moved into its own space and the
        // grid walk stays axis aligned.
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 3);
        grid.SetVoxel(1, 0, 0, 3);
        grid.SetVoxel(2, 0, 0, 3);

        var transform = new ObjectTransform(
            Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));

        scene.Add(grid, transform, "turned");

        // The local +X bar now runs along world -Z, so aim down it.
        var ray = new Ray(new Vector3(0.5f, 0.5f, -10f), Vector3.UnitZ);

        Assert.True(scene.TryPick(ray, out ScenePick pick));
        Assert.Equal(new Int3(2, 0, 0), pick.Hit.Voxel);
    }

    [Fact]
    public void HiddenAndEmptyObjectsAreNotPicked()
    {
        var scene = new VoxelScene();
        VoxelObject hidden = scene.Add(Cube(2), ObjectTransform.At(new Vector3(2f, 0f, 0f)));
        hidden.Visible = false;

        scene.Add(new VoxelWorld(), ObjectTransform.At(new Vector3(3f, 0f, 0f)), "empty");

        var ray = new Ray(new Vector3(-10f, 0.5f, 0.5f), Vector3.UnitX);
        Assert.False(scene.TryPick(ray, out _));
    }

    [Fact]
    public void WorldBoundsCoverEveryObjectWhereverItSits()
    {
        var scene = new VoxelScene();
        scene.Add(Cube(2), ObjectTransform.Identity);
        scene.Add(Cube(2), ObjectTransform.At(new Vector3(10f, 4f, 0f)));

        Assert.True(scene.TryGetWorldBounds(out Vector3 min, out Vector3 max));
        Assert.Equal(new Vector3(0f, 0f, 0f), min);
        Assert.Equal(new Vector3(12f, 6f, 2f), max);
    }

    [Fact]
    public void RotatedObjectBoundsUseAllEightCorners()
    {
        var scene = new VoxelScene();
        scene.Add(
            Cube(4),
            new ObjectTransform(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f)));

        Assert.True(scene.TryGetWorldBounds(out Vector3 min, out Vector3 max));

        // A 4x4 square turned 45 degrees spans 4 * sqrt(2) across, not 4.
        float span = max.X - min.X;
        Assert.True(span > 5.6f && span < 5.7f, $"Span was {span}.");
    }

    [Fact]
    public void EvenTheLastEmptyObjectIsPruned()
    {
        // An empty level is a level like any other now.
        var scene = new VoxelScene();
        scene.Add(new VoxelWorld(), ObjectTransform.Identity);

        Assert.Equal(1, scene.RemoveEmptyObjects());
        Assert.Empty(scene.Objects);
        Assert.Equal(0, scene.FocusId);
    }

    [Fact]
    public void EmptyObjectsAreDroppedWhenOthersRemain()
    {
        var scene = new VoxelScene();
        scene.Add(Cube(1), ObjectTransform.Identity, "keep");
        scene.Add(new VoxelWorld(), ObjectTransform.Identity, "gone");

        Assert.Equal(1, scene.RemoveEmptyObjects());
        Assert.Single(scene.Objects);
        Assert.Equal("keep", scene.Objects[0].Name);
    }

    [Fact]
    public void ObjectCentreFollowsItsTransform()
    {
        var scene = new VoxelScene();
        VoxelObject o = scene.Add(Cube(4), ObjectTransform.At(new Vector3(10f, 0f, 0f)));

        Assert.Equal(new Vector3(12f, 2f, 2f), o.WorldCentre());
    }

    [Fact]
    public void StarterSceneIsOneWhiteCube()
    {
        VoxelScene scene = VoxelScene.CreateStarter();

        Assert.Single(scene.Objects);
        Assert.Equal(8 * 8 * 8, scene.SolidCount);
        Assert.NotNull(scene.Focus);

        // Centred over the origin, standing on the grid.
        Assert.True(scene.TryGetWorldBounds(out Vector3 min, out Vector3 max));
        Assert.Equal(new Vector3(-4f, 0f, -4f), min);
        Assert.Equal(new Vector3(4f, 8f, 4f), max);
    }

    [Fact]
    public void ContentHashSeesTransformChanges()
    {
        var scene = new VoxelScene();
        VoxelObject o = scene.Add(Cube(2), ObjectTransform.Identity);
        ulong before = scene.ContentHash();

        o.Transform = o.Transform.Translated(new Vector3(1f, 0f, 0f));

        Assert.NotEqual(before, scene.ContentHash());
    }
}
