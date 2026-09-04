using System.Numerics;
using EditorApp.Mobile.Rendering;

namespace EditorApp.Tests.Mobile;

/// <summary>
/// The camera a finger drives. Most of these are about direction, because direction is the part
/// that is easy to get backwards and impossible to argue about from the code alone — a camera that
/// turns the wrong way still looks like a working camera in a screenshot.
/// </summary>
public class OrbitCameraTests
{
    private static readonly Vector2 Viewport = new(1000f, 1000f);

    /// <summary>
    /// Where a fixed world point lands on screen. A gesture is right when the scene moves the way
    /// the finger did, and this is the only way to ask that question directly.
    /// </summary>
    private static Vector2 Project(OrbitCamera camera, Vector3 world)
    {
        Assert.True(camera.Camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    /// <summary>
    /// A point on the near side of the model, facing the camera. This is the probe the direction
    /// tests need: it starts under the finger at the centre of the screen, so wherever it goes next
    /// is unambiguously the direction the scene travelled.
    ///
    /// A point offset sideways from the target will not do. Rotating the camera either way shortens
    /// its lateral offset by the same cosine, so it drifts towards the centre whichever way the
    /// finger went and the test passes or fails for a reason that has nothing to do with direction.
    /// </summary>
    private static Vector3 NearSurfacePoint(OrbitCamera camera) =>
        camera.Target - (Vector3.Normalize(camera.Camera.Forward) * 4f);

    /// <summary>
    /// Dragging right must carry the scene right with the finger. Turning the camera the other way
    /// is just as plausible in code and completely wrong in the hand — it is what a mouse does when
    /// it turns your head, not what a hand does when it turns an object.
    /// </summary>
    [Fact]
    public void DraggingRightCarriesTheSceneRight()
    {
        var camera = new OrbitCamera();
        Vector3 marker = NearSurfacePoint(camera);

        float before = Project(camera, marker).X;
        camera.Orbit(new Vector2(80f, 0f));
        float after = Project(camera, marker).X;

        Assert.True(after > before, $"the marker moved from x={before:F1} to x={after:F1}");
    }

    [Fact]
    public void DraggingLeftCarriesTheSceneLeft()
    {
        var camera = new OrbitCamera();
        Vector3 marker = NearSurfacePoint(camera);

        float before = Project(camera, marker).X;
        camera.Orbit(new Vector2(-80f, 0f));

        Assert.True(Project(camera, marker).X < before);
    }

    /// <summary>And the same on the other axis: the scene follows the finger down the screen.</summary>
    [Fact]
    public void DraggingDownCarriesTheSceneDown()
    {
        var camera = new OrbitCamera();
        Vector3 marker = NearSurfacePoint(camera);

        float before = Project(camera, marker).Y;
        camera.Orbit(new Vector2(0f, 70f));

        Assert.True(Project(camera, marker).Y > before);
    }

    /// <summary>Dragging down tips the model towards you, which means the camera rises.</summary>
    [Fact]
    public void DraggingDownRaisesTheCamera()
    {
        var camera = new OrbitCamera();
        float before = camera.Camera.Position.Y;

        camera.Orbit(new Vector2(0f, 60f));

        Assert.True(camera.Camera.Position.Y > before);
    }

    /// <summary>
    /// Whatever a gesture does, the camera has to end up looking at the target from the distance it
    /// is meant to be at. Everything else in the editor — framing, picking, the grid — assumes it.
    /// </summary>
    [Theory]
    [InlineData(120f, 0f)]
    [InlineData(0f, 90f)]
    [InlineData(-300f, -220f)]
    public void OrbitingKeepsTheTargetInFrontAtTheSameDistance(float dx, float dy)
    {
        var camera = new OrbitCamera { Target = new Vector3(5f, 3f, -2f), Distance = 25f };

        camera.Orbit(new Vector2(dx, dy));

        Vector3 toTarget = camera.Target - camera.Camera.Position;
        Assert.Equal(25f, toTarget.Length(), 3);
        Assert.Equal(1f, Vector3.Dot(Vector3.Normalize(toTarget), camera.Camera.Forward), 4);
    }

    /// <summary>
    /// Straight up and straight down are where a yaw/pitch camera falls apart: the up vector becomes
    /// undefined and the view flips over. Pitch is clamped short of both.
    /// </summary>
    [Fact]
    public void PitchStopsShortOfStraightUpAndDown()
    {
        var camera = new OrbitCamera();

        camera.Orbit(new Vector2(0f, 100_000f));
        Assert.True(camera.Camera.Pitch < MathF.PI / 2f);

        camera.Orbit(new Vector2(0f, -200_000f));
        Assert.True(camera.Camera.Pitch > -MathF.PI / 2f);
    }

    [Fact]
    public void SpreadingFingersComesCloser()
    {
        var camera = new OrbitCamera { Distance = 40f };

        camera.Zoom(2f);

        Assert.Equal(20f, camera.Distance, 4);
    }

    [Fact]
    public void DistanceIsClampedAtBothEnds()
    {
        var camera = new OrbitCamera { Distance = 40f };

        camera.Zoom(1e9f);
        Assert.Equal(camera.MinDistance, camera.Distance, 4);

        camera.Zoom(1e-9f);
        Assert.Equal(camera.MaxDistance, camera.Distance, 4);
    }

    /// <summary>A pinch that reports a nonsense scale must leave the camera alone, not send it to infinity.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ANonsenseZoomIsIgnored(float scale)
    {
        var camera = new OrbitCamera { Distance = 40f };

        camera.Zoom(scale);

        Assert.Equal(40f, camera.Distance, 4);
    }

    /// <summary>Two fingers sliding right carry the scene right, the same as one finger turning it.</summary>
    [Fact]
    public void PanningRightCarriesTheSceneRight()
    {
        var camera = new OrbitCamera();
        Vector3 marker = camera.Target;

        float before = Project(camera, marker).X;
        camera.Pan(new Vector2(100f, 0f), Viewport);

        Assert.True(Project(camera, marker).X > before);
    }

    [Fact]
    public void PanningDownCarriesTheSceneDown()
    {
        var camera = new OrbitCamera();
        Vector3 marker = camera.Target;

        float before = Project(camera, marker).Y;
        camera.Pan(new Vector2(0f, 100f), Viewport);

        // Screen Y counts downward, so "further down the screen" is a larger number.
        Assert.True(Project(camera, marker).Y > before);
    }

    /// <summary>
    /// Panning moves the whole camera rig, so the model must not swing round as a side effect —
    /// the view direction is the one thing a two-finger slide is not allowed to change.
    /// </summary>
    [Fact]
    public void PanningDoesNotTurnTheCamera()
    {
        var camera = new OrbitCamera();
        Vector3 forward = camera.Camera.Forward;

        camera.Pan(new Vector2(120f, -75f), Viewport);

        Assert.Equal(1f, Vector3.Dot(forward, camera.Camera.Forward), 5);
    }

    [Fact]
    public void FramingCentresTheBoxAndBacksOffFarEnoughToSeeIt()
    {
        var camera = new OrbitCamera();

        camera.Frame(new Vector3(-8f, 0f, -8f), new Vector3(8f, 16f, 8f));

        Assert.Equal(new Vector3(0f, 8f, 0f), camera.Target);

        // Every corner has to project inside the viewport, or "framed" means nothing.
        foreach (Vector3 corner in Corners(new Vector3(-8f, 0f, -8f), new Vector3(8f, 16f, 8f)))
        {
            Vector2 screen = Project(camera, corner);
            Assert.InRange(screen.X, 0f, Viewport.X);
            Assert.InRange(screen.Y, 0f, Viewport.Y);
        }
    }

    private static IEnumerable<Vector3> Corners(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < 8; i++)
        {
            yield return new Vector3(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z);
        }
    }
}
