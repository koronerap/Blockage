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
        transform.OnDrag(ScreenOf(camera, grip + (arrow.Direction * 0.7f)), Viewport, camera, snap: true);
        transform.OnRelease();

        Assert.Equal(expected, session.Scene.Objects[0].Transform.Position.X, 4);
    }
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

    /// <summary>Off by default: a drag goes where the mouse goes, to the hundredth.</summary>
    [Fact]
    public void WithoutSnappingAMoveGoesWhereTheMouseGoes()
    {
        (EditorSession session, TransformInteraction transform, FlyCamera camera) = Fixture(1f);
        Assert.False(session.Snap.Enabled);

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Vector3 grip = (start + end) * 0.5f;

        Assert.True(transform.OnPress(ScreenOf(camera, grip), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, grip + (arrow.Direction * 0.7f)), Viewport, camera, snap: false);

        // Where the mouse went — near 0.7, perspective allowing — and not on a whole voxel.
        Assert.InRange(session.Scene.Objects[0].Transform.Position.X, 0.6f, 0.8f);
        Assert.Null(transform.SnapPoint);
    }

    /// <summary>Not absolute: whole steps from where the drag began, however far off the lattice that was.</summary>
    [Theory]
    [InlineData(true, 1f)]
    [InlineData(false, 1.3f)]
    public void AnIncrementIsOnTheLatticeOrCountedFromTheStart(bool absolute, float expected)
    {
        (EditorSession session, TransformInteraction transform, FlyCamera camera) = Fixture(1f);
        VoxelObject cube = session.Scene.Objects[0];
        cube.Transform = cube.Transform with { Position = new Vector3(0.3f, 0f, 0f) };
        session.Snap.AbsoluteGrid = absolute;

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Vector3 grip = (start + end) * 0.5f;

        Assert.True(transform.OnPress(ScreenOf(camera, grip), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, grip + (arrow.Direction * 0.8f)), Viewport, camera, snap: true);

        Assert.Equal(expected, cube.Transform.Position.X, 3);
    }

    /// <summary>On an arrow, a corner is met as nearly as the arrow allows: the corners line up along it.</summary>
    [Fact]
    public void ACornerSnapLinesTheCornersUpAlongTheArrow()
    {
        (EditorSession session, TransformInteraction transform, FlyCamera camera) = Fixture(1f);
        // Off the arrow's line: up and to one side, so meeting it exactly would leave the arrow.
        session.Scene.Add(Block(2), ObjectTransform.At(new Vector3(10f, 3f, 5f)), "other");
        session.Snap.Targets = SnapTarget.Corner;
        VoxelObject cube = session.Scene.Objects[0];

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);

        Assert.True(transform.OnPress(ScreenOf(camera, (start + end) * 0.5f), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, new Vector3(10f, 3f, 5f)), Viewport, camera, snap: true);

        // The 4-cube's corner nearest the target (x = 4) comes level with it along X, and the cube
        // stays on its arrow.
        Assert.Equal(6f, cube.Transform.Position.X, 3);
        Assert.Equal(0f, cube.Transform.Position.Y, 3);
        Assert.Equal(0f, cube.Transform.Position.Z, 3);
        Assert.Equal(new Vector3(10f, 3f, 5f), transform.SnapPoint);
    }

    /// <summary>The free ring follows the cursor onto a surface and sets the object down on it.</summary>
    [Fact]
    public void TheFreeRingSetsAnObjectDownOnTheSurfaceUnderTheCursor()
    {
        var scene = new VoxelScene();
        VoxelObject moved = scene.Add(Block(2), ObjectTransform.At(new Vector3(0f, 10f, 0f)), "moved");
        var floor = new VoxelWorld();
        for (int x = 0; x < 12; x++)
        for (int z = 0; z < 12; z++)
        {
            floor.SetVoxel(x, 0, z, Palette.WhiteIndex);
        }

        scene.Add(floor, ObjectTransform.At(new Vector3(-6f, 0f, -6f)), "floor");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveTool = EditorTool.Transform;
        session.ChooseObject(moved.Id);
        session.Snap.Targets = SnapTarget.Surface;

        var camera = new FlyCamera { Position = new Vector3(8f, 16f, 14f) };
        camera.LookAt(new Vector3(0f, 4f, 0f));
        var transform = new TransformInteraction(session);

        GizmoHandle ring = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveFree);
        Assert.True(transform.OnPress(ScreenOf(camera, ring.Origin), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, new Vector3(3.5f, 1f, 2.5f)), Viewport, camera, snap: true);

        // Standing on the floor's top at y = 1, its bottom's middle on the point under the cursor.
        Assert.Equal(1f, moved.Transform.Position.Y, 2);
        Assert.Equal(2.5f, moved.Transform.Position.X, 1);
        Assert.Equal(1.5f, moved.Transform.Position.Z, 1);

        // With whole voxels too, it lines up with the lattice along the floor as well.
        session.Snap.Set(SnapTarget.Increment, true);
        transform.OnDrag(ScreenOf(camera, new Vector3(3.3f, 1f, 2.4f)), Viewport, camera, snap: true);
        Assert.Equal(1f, moved.Transform.Position.Y, 3);
        Assert.Equal(MathF.Round(moved.Transform.Position.X), moved.Transform.Position.X, 3);
        Assert.Equal(MathF.Round(moved.Transform.Position.Z), moved.Transform.Position.Z, 3);
    }

    [Fact]
    public void AMoveNotAffectedBySnappingIsFree()
    {
        (EditorSession session, TransformInteraction transform, FlyCamera camera) = Fixture(1f);
        session.Snap.AffectMove = false;

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Vector3 grip = (start + end) * 0.5f;

        Assert.True(transform.OnPress(ScreenOf(camera, grip), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, grip + (arrow.Direction * 0.7f)), Viewport, camera, snap: true);

        Assert.InRange(session.Scene.Objects[0].Transform.Position.X, 0.6f, 0.8f);
    }

    [Theory]
    [InlineData(50f, 45f, 45f)]
    [InlineData(20f, 45f, 0f)]
    [InlineData(20f, 10f, 20f)]
    public void ARotationLandsOnTheIncrement(float dragged, float increment, float expected)
    {
        (EditorSession session, TransformInteraction transform, FlyCamera camera) = Fixture(1f);
        session.TransformMode = TransformMode.Rotate;
        session.Snap.RotationIncrement = increment;

        GizmoHandle ring = transform.Handles(camera).First(h => h.Kind == GizmoKind.RotateRing && h.Axis == 1);
        Vector2 centre = ScreenOf(camera, ring.Pivot);
        Vector2 press = ScreenOf(camera, transform.RingPoints(ring, camera).First());
        Assert.True(transform.OnPress(press, Viewport, camera));

        float angle = MathF.Atan2(press.Y - centre.Y, press.X - centre.X) + (dragged * MathF.PI / 180f);
        Vector2 mouse = centre + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Vector2.Distance(press, centre));
        transform.OnDrag(mouse, Viewport, camera, snap: true);

        Quaternion rotation = session.Scene.Objects[0].Transform.Rotation;
        float turned = 2f * MathF.Acos(Math.Clamp(MathF.Abs(rotation.W), 0f, 1f)) * (180f / MathF.PI);
        Assert.Equal(expected, turned, 0);
    }

    /// <summary>The middle of the gizmo is the free ring, not whichever arrow starts nearest.</summary>
    [Fact]
    public void APressOnTheMiddleTakesTheFreeRing()
    {
        (EditorSession session, TransformInteraction transform, FlyCamera camera) = Fixture(1f);
        Vector3 centre = session.Scene.Objects[0].WorldCentre();

        transform.UpdateHover(ScreenOf(camera, centre), Viewport, camera);

        Assert.Equal(GizmoKind.MoveFree, transform.Hovered?.Kind);
    }
}
