using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// The move arrows, driven the way the mouse drives them: grab an arrow, drag along it, let go.
/// Snapping lands on whole voxels of the object being moved, so it stays on a lattice of its own size.
/// </summary>
public class TransformSnapTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    private static (EditorSession Session, TransformInteraction Transform, FlyCamera Camera) Fixture(float voxelSize)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 4; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity with { VoxelSize = voxelSize }, "cube");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveTool = EditorTool.Transform;
        session.TransformMode = TransformMode.Move;

        var camera = new FlyCamera { Position = new Vector3(10f, 12f, 18f) };
        camera.LookAt(session.Scene.Objects[0].WorldCentre());

        return (session, new TransformInteraction(session), camera);
    }

    private static Vector2 ScreenOf(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    [Theory]
    [InlineData(1f, 1f)]      // 0.7 of a unit rounds to one whole voxel
    [InlineData(0.5f, 0.5f)]  // ...but to one half-unit voxel when that is the object's size
    [InlineData(0.25f, 0.75f)]
    public void AMoveSnapsToWholeVoxelsOfTheObjectsSize(float voxelSize, float expected)
    {
        (EditorSession session, TransformInteraction transform, FlyCamera camera) = Fixture(voxelSize);

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Vector3 grip = (start + end) * 0.5f;

        Assert.True(transform.OnPress(ScreenOf(camera, grip), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, grip + (arrow.Direction * 0.7f)), Viewport, camera, freeform: false);
        transform.OnRelease();

        Assert.Equal(expected, session.Scene.Objects[0].Transform.Position.X, 4);
    }
}
