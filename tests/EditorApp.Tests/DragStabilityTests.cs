using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// A drag held still must stay still. Every gesture is re-run from the same mouse position for a
/// handful of frames, at every position along its axis: a preview that flickers between two values
/// with the mouse not moving is a gesture that feeds its own result back into its next reading.
/// </summary>
public class DragStabilityTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    private const int HeldFrames = 6;

    private static VoxelWorld Cube(int side)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        return grid;
    }

    private static (EditorSession Session, FlyCamera Camera) Scene(EditorTool tool)
    {
        var scene = new VoxelScene();
        scene.Add(Cube(4), ObjectTransform.Identity, "cube");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveTool = tool;

        // From above and to one side, so pulling along any axis changes the distance to the camera —
        // the case in which the perspective makes a pixel worth a different amount as things move.
        var camera = new FlyCamera { Position = new Vector3(6f, 16f, 9f) };
        camera.LookAt(new Vector3(2f, 2f, 2f));
        return (session, camera);
    }

    private static Vector2 ScreenOf(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    [Fact]
    public void AnExtrudePullHeldStillDoesNotFlicker()
    {
        (EditorSession session, FlyCamera camera) = Scene(EditorTool.Extrude);
        session.SelectPatch(new RaycastHit(new Int3(2, 3, 2), Face.PosY, 1f));
        var extrude = new ExtrudeInteraction(session);

        (Vector3 start, _) = extrude.Arrow()!.Value;
        Vector2 press = ScreenOf(camera, start);
        extrude.OnPress(null, press, Viewport, camera, shift: false, alt: false);
        Assert.True(extrude.IsDraggingArrow);

        Vector2 along = ScreenOf(camera, start + Vector3.UnitY) - press;
        var unstable = new List<string>();

        // Every pixel along ten units of the arrow, held for a few frames each.
        for (float t = -10f; t <= 10f; t += 0.05f)
        {
            Vector2 mouse = press + (along * t);
            var seen = new HashSet<int>();
            for (int frame = 0; frame < HeldFrames; frame++)
            {
                extrude.OnDrag(null, mouse, Viewport, camera);
                seen.Add(session.ExtrudeSteps);
            }

            if (seen.Count > 1)
            {
                unstable.Add($"{t:0.00} units: {string.Join('/', seen)}");
            }
        }

        Assert.True(unstable.Count == 0, "flickered at " + string.Join("; ", unstable.Take(5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AMoveHeldStillDoesNotFlicker(int axis)
    {
        (EditorSession session, FlyCamera camera) = Scene(EditorTool.Transform);
        session.TransformMode = TransformMode.Move;
        var transform = new TransformInteraction(session);

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == axis);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Vector2 press = ScreenOf(camera, (start + end) * 0.5f);
        Assert.True(transform.OnPress(press, Viewport, camera));

        Vector2 along = ScreenOf(camera, ((start + end) * 0.5f) + arrow.Direction) - press;
        VoxelObject cube = session.Scene.Objects[0];
        var unstable = new List<string>();

        for (float t = -10f; t <= 10f; t += 0.05f)
        {
            Vector2 mouse = press + (along * t);
            var seen = new HashSet<Vector3>();
            for (int frame = 0; frame < HeldFrames; frame++)
            {
                transform.OnDrag(mouse, Viewport, camera, snap: true);
                seen.Add(cube.Transform.Position);
            }

            if (seen.Count > 1)
            {
                unstable.Add($"{t:0.00} units");
            }
        }

        Assert.True(unstable.Count == 0, "flickered at " + string.Join("; ", unstable.Take(5)));
    }

    [Fact]
    public void ARotationHeldStillDoesNotFlicker()
    {
        (EditorSession session, FlyCamera camera) = Scene(EditorTool.Transform);
        session.TransformMode = TransformMode.Rotate;
        var transform = new TransformInteraction(session);

        GizmoHandle ring = transform.Handles(camera).First(h => h.Kind == GizmoKind.RotateRing && h.Axis == 1);
        Vector3 grip = transform.RingPoints(ring, camera).First();
        Vector2 press = ScreenOf(camera, grip);
        Assert.True(transform.OnPress(press, Viewport, camera));

        Vector2 centre = ScreenOf(camera, ring.Pivot);
        VoxelObject cube = session.Scene.Objects[0];
        var unstable = new List<string>();

        for (float degrees = 0f; degrees < 360f; degrees += 0.5f)
        {
            float angle = MathF.Atan2(press.Y - centre.Y, press.X - centre.X) + (degrees * MathF.PI / 180f);
            Vector2 mouse = centre + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Vector2.Distance(press, centre));
            var seen = new HashSet<Quaternion>();
            for (int frame = 0; frame < HeldFrames; frame++)
            {
                transform.OnDrag(mouse, Viewport, camera, snap: true);
                seen.Add(cube.Transform.Rotation);
            }

            if (seen.Count > 1)
            {
                unstable.Add($"{degrees:0.0} degrees");
            }
        }

        Assert.True(unstable.Count == 0, "flickered at " + string.Join("; ", unstable.Take(5)));
    }
}
