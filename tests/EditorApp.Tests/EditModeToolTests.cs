using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// The Select and Transform tools inside an object: a click and a box choose voxels — the seen ones,
/// or all of them with X-Ray — and the gizmo moves what was chosen by whole voxels.
/// </summary>
public class EditModeToolTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    /// <summary>A 3³ cube in Edit Mode, looked at straight on from the front, nothing chosen.</summary>
    private static (EditorSession Session, VoxelObject Cube, FlyCamera Camera) Cube()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 3; y++)
        for (int z = 0; z < 3; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        var scene = new VoxelScene();
        VoxelObject cube = scene.Add(grid, ObjectTransform.Identity, "Cube");
        var session = new EditorSession { ActiveTool = EditorTool.Select };
        session.ReplaceScene(scene, projectPath: null);
        Assert.True(session.EnterEditMode());

        var camera = new FlyCamera { Position = new Vector3(1.5f, 1.5f, 20f) };
        camera.LookAt(new Vector3(1.5f, 1.5f, 1.5f));
        return (session, cube, camera);
    }

    private static Vector2 ScreenOf(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    [Theory]
    [InlineData(false, 9)]   // the front face's voxels are all that can be seen
    [InlineData(true, 27)]   // with X-Ray, every voxel the box covers, the middle one too
    public void ABoxChoosesTheSeenVoxelsOrWithXRayAllOfThem(bool xRay, int expected)
    {
        (EditorSession session, VoxelObject cube, FlyCamera camera) = Cube();
        var select = new SelectInteraction(session);

        Vector2 min = ScreenOf(camera, new Vector3(-0.5f, 3.5f, 3f));
        Vector2 max = ScreenOf(camera, new Vector3(3.5f, -0.5f, 3f));
        select.OnPress(min, shift: false, control: false);
        select.OnDrag(max);
        select.OnReleaseVoxels(_ => null, (a, b) => SelectInteraction.VoxelsInBox(cube, camera, Viewport, a, b, xRay));

        Assert.Equal(expected, session.VoxelSelection.Count);
        if (!xRay)
        {
            Assert.All(session.VoxelSelection.Cells, cell => Assert.Equal(2, cell.Z));
        }
    }

    [Fact]
    public void AClickChoosesTheVoxelUnderItAndAClickOnNothingLetsGo()
    {
        (EditorSession session, _, _) = Cube();
        var select = new SelectInteraction(session);

        select.OnPress(new Vector2(10f, 10f), shift: false, control: false);
        select.OnReleaseVoxels(_ => new Int3(1, 1, 2), (_, _) => []);
        Assert.Equal(new[] { new Int3(1, 1, 2) }, session.VoxelSelection.Cells);

        select.OnPress(new Vector2(10f, 10f), shift: false, control: false);
        select.OnReleaseVoxels(_ => null, (_, _) => []);
        Assert.True(session.VoxelSelection.IsEmpty);
    }

    [Fact]
    public void TheGizmoMovesTheChosenVoxelsByWholeVoxels()
    {
        (EditorSession session, VoxelObject cube, FlyCamera camera) = Cube();
        session.ActiveTool = EditorTool.Transform;
        session.SelectVoxels([new Int3(1, 2, 1)], SelectionOperation.Replace);
        var transform = new TransformInteraction(session);

        GizmoHandle up = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 1);
        Assert.Equal(2.5f, up.Origin.Y, 3);

        (Vector3 start, Vector3 end) = transform.Segment(up, camera);
        Vector3 grip = (start + end) * 0.5f;
        Assert.True(transform.OnPress(ScreenOf(camera, grip), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, grip + (Vector3.UnitY * 2f)), Viewport, camera, snap: false);
        transform.OnRelease();

        // The top-middle voxel lifted two clear of the cube, the rest where it was.
        Assert.True(cube.Grid.IsSolid(new Int3(1, 4, 1)));
        Assert.False(cube.Grid.IsSolid(new Int3(1, 2, 1)));
        Assert.Equal(27, cube.Grid.SolidCount);
        Assert.Equal(new[] { new Int3(1, 4, 1) }, session.VoxelSelection.Cells);
        Assert.Equal(1, session.History.UndoCount);
    }
}
