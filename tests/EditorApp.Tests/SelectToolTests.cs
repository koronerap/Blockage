using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// The Select tool and the Transform tool with several things selected: a click picks, a box picks
/// what is in it, and the gizmo moves everything selected together as one undo step.
/// </summary>
public class SelectToolTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    private static VoxelWorld Block(int side)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        for (int y = 0; y < side; y++)
        for (int z = 0; z < side; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        return grid;
    }

    /// <summary>Two 2³ blocks side by side along X, seen from the front, nothing selected.</summary>
    private static (EditorSession Session, VoxelObject Left, VoxelObject Right, FlyCamera Camera) Two()
    {
        var scene = new VoxelScene();
        VoxelObject left = scene.Add(Block(2), ObjectTransform.Identity, "Left");
        VoxelObject right = scene.Add(Block(2), new ObjectTransform(new Vector3(6f, 0f, 0f), Quaternion.Identity), "Right");

        var session = new EditorSession { ActiveTool = EditorTool.Select };
        session.ReplaceScene(scene, projectPath: null);
        session.DeselectAll();

        var camera = new FlyCamera { Position = new Vector3(4f, 1f, 20f) };
        camera.LookAt(new Vector3(4f, 1f, 1f));
        return (session, left, right, camera);
    }

    private static Vector2 ScreenOf(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    private static void Click(SelectInteraction select, int under, bool shift = false, bool control = false)
    {
        select.OnPress(new Vector2(100f, 100f), shift, control);
        select.OnRelease(_ => under, (_, _) => []);
    }

    [Fact]
    public void AClickSelectsWhatIsUnderIt()
    {
        (EditorSession session, VoxelObject left, VoxelObject right, _) = Two();
        var select = new SelectInteraction(session);

        Click(select, left.Id);
        Click(select, right.Id);

        Assert.Equal(new[] { right.Id }, session.Scene.SelectedIds);
    }

    [Fact]
    public void ShiftAddsAndCtrlTakesAway()
    {
        (EditorSession session, VoxelObject left, VoxelObject right, _) = Two();
        var select = new SelectInteraction(session);

        Click(select, left.Id);
        Click(select, right.Id, shift: true);
        Assert.Equal(2, session.SelectedCount);

        Click(select, left.Id, control: true);
        Assert.Equal(new[] { right.Id }, session.Scene.SelectedIds);
    }

    [Fact]
    public void AClickOnNothingLetsGoUnlessShiftIsHeld()
    {
        (EditorSession session, VoxelObject left, _, _) = Two();
        var select = new SelectInteraction(session);
        Click(select, left.Id);

        Click(select, 0, shift: true);
        Assert.Equal(1, session.SelectedCount);

        Click(select, 0);
        Assert.Equal(0, session.SelectedCount);
    }

    [Fact]
    public void AShortWobbleIsStillAClick()
    {
        (EditorSession session, VoxelObject left, _, _) = Two();
        var select = new SelectInteraction(session);

        select.OnPress(new Vector2(100f, 100f), shift: false, control: false);
        select.OnDrag(new Vector2(102f, 101f));
        Assert.Null(select.Box);

        bool boxed = false;
        select.OnRelease(_ => left.Id, (_, _) => { boxed = true; return []; });

        Assert.False(boxed);
        Assert.True(session.IsSelected(left.Id));
    }

    [Fact]
    public void ABoxSelectsWhatItCovers()
    {
        (EditorSession session, VoxelObject left, VoxelObject right, FlyCamera camera) = Two();
        var select = new SelectInteraction(session);

        // Round the left block only.
        Vector2 min = ScreenOf(camera, new Vector3(-0.5f, 2.5f, 1f));
        Vector2 max = ScreenOf(camera, new Vector3(2.5f, -0.5f, 1f));

        select.OnPress(min, shift: false, control: false);
        select.OnDrag(max);
        Assert.NotNull(select.Box);
        select.OnRelease(_ => 0, (a, b) => SelectInteraction.InBox(session.Scene, camera, Viewport, a, b, _ => true));

        Assert.True(session.IsSelected(left.Id));
        Assert.False(session.IsSelected(right.Id));
    }

    [Fact]
    public void ABoxWithCtrlTakesAway()
    {
        (EditorSession session, VoxelObject left, VoxelObject right, FlyCamera camera) = Two();
        session.SelectAll();
        var select = new SelectInteraction(session);

        Vector2 min = ScreenOf(camera, new Vector3(5.5f, 2.5f, 1f));
        Vector2 max = ScreenOf(camera, new Vector3(8.5f, -0.5f, 1f));

        select.OnPress(min, shift: false, control: true);
        select.OnDrag(max);
        Assert.Equal(SelectionOperation.Subtract, select.Operation);
        select.OnRelease(_ => 0, (a, b) => SelectInteraction.InBox(session.Scene, camera, Viewport, a, b, _ => false));

        Assert.True(session.IsSelected(left.Id));
        Assert.False(session.IsSelected(right.Id));
    }

    [Fact]
    public void WithNothingSelectedThereIsNoGizmo()
    {
        (EditorSession session, _, _, FlyCamera camera) = Two();
        var transform = new TransformInteraction(session);

        Assert.Empty(transform.Handles(camera));
    }

    [Fact]
    public void TheGizmoMovesEverythingSelectedAsOneStep()
    {
        (EditorSession session, VoxelObject left, VoxelObject right, FlyCamera camera) = Two();
        session.ActiveTool = EditorTool.Transform;
        session.SelectAll();
        var transform = new TransformInteraction(session);

        // Several: at their middle, and without the one object's box edges.
        GizmoHandle[] handles = [.. transform.Handles(camera)];
        Assert.DoesNotContain(handles, h => h.Kind == GizmoKind.EdgeHinge);
        GizmoHandle up = handles.First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 1);
        Assert.Equal(4f, up.Origin.X, 3);

        (Vector3 start, Vector3 end) = transform.Segment(up, camera);
        Vector3 grip = (start + end) * 0.5f;
        Assert.True(transform.OnPress(ScreenOf(camera, grip), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, grip + (Vector3.UnitY * 3f)), Viewport, camera, snap: true);
        transform.OnRelease();

        Assert.Equal(3f, left.Transform.Position.Y, 3);
        Assert.Equal(3f, right.Transform.Position.Y, 3);
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(0f, left.Transform.Position.Y, 3);
        Assert.Equal(0f, right.Transform.Position.Y, 3);
    }

    /// <summary>Several moved together snap by the box round all of them, not by the active one's own.</summary>
    [Fact]
    public void SeveralSnapByTheBoxRoundThemAll()
    {
        var scene = new VoxelScene();
        VoxelObject left = scene.Add(Block(2), ObjectTransform.Identity, "Left");
        VoxelObject right = scene.Add(Block(2), new ObjectTransform(new Vector3(3f, 0f, 0f), Quaternion.Identity), "Right");
        scene.Add(Block(2), new ObjectTransform(new Vector3(10f, 0f, 0f), Quaternion.Identity), "Target");

        var session = new EditorSession { ActiveTool = EditorTool.Transform };
        session.ReplaceScene(scene, projectPath: null);
        session.SelectMany([left.Id, right.Id], SelectionOperation.Replace);
        session.Snap.Targets = SnapTarget.Corner;

        var camera = new FlyCamera { Position = new Vector3(6f, 1f, 25f) };
        camera.LookAt(new Vector3(6f, 1f, 1f));
        var transform = new TransformInteraction(session);

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Assert.True(transform.OnPress(ScreenOf(camera, (start + end) * 0.5f), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, new Vector3(10f, 0f, 2f)), Viewport, camera, snap: true);
        transform.OnRelease();

        // The right-hand block's far side lands on the target's near corner; the pair moved as one.
        Assert.Equal(5f, left.Transform.Position.X, 3);
        Assert.Equal(8f, right.Transform.Position.X, 3);
    }

    [Fact]
    public void CancellingADragOfSeveralPutsThemAllBack()
    {
        (EditorSession session, VoxelObject left, VoxelObject right, FlyCamera camera) = Two();
        session.SelectAll();
        var transform = new TransformInteraction(session);

        GizmoHandle up = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 1);
        (Vector3 start, Vector3 end) = transform.Segment(up, camera);
        Vector3 grip = (start + end) * 0.5f;
        transform.OnPress(ScreenOf(camera, grip), Viewport, camera);
        transform.OnDrag(ScreenOf(camera, grip + (Vector3.UnitY * 2f)), Viewport, camera, snap: true);
        transform.Cancel();

        Assert.Equal(0f, left.Transform.Position.Y, 3);
        Assert.Equal(0f, right.Transform.Position.Y, 3);
        Assert.False(session.History.CanUndo);
    }
}
