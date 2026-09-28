using System.Numerics;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// The pivot, the orthographic projection and the six aligned views. Most of this is about
/// exactness, because an aligned view that is off by a fraction of a degree looks almost right in a
/// screenshot and is wrong in the one way that matters: in orthographic every side face shows up as
/// a sliver.
/// </summary>
public class FlyCameraViewTests
{
    private static readonly Vector2 Viewport = new(1200f, 800f);

    private static FlyCamera Framed()
    {
        var camera = new FlyCamera();
        camera.FrameBox(new Vector3(-4f, 0f, -4f), new Vector3(4f, 8f, 4f));
        return camera;
    }

    private static Vector2 Project(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    public static TheoryData<AlignedView> Views() => new(Enum.GetValues<AlignedView>());

    [Theory]
    [MemberData(nameof(Views))]
    public void AnAlignedViewLooksExactlyAlongItsAxis(AlignedView view)
    {
        FlyCamera camera = Framed();

        camera.Align(view);

        Vector3 expected = FlyCamera.LookDirection(view);
        Assert.True(
            Vector3.Distance(expected, camera.Forward) < 1e-5f,
            $"{view} looks along {camera.Forward}, not {expected}.");
        Assert.Equal(view, camera.CurrentAlignedView());
    }

    /// <summary>
    /// Straight down and straight up are where a yaw/pitch camera usually falls apart — world up and
    /// the view direction line up and the sideways axis vanishes. The top and bottom views sit right
    /// on that, so the camera's own axes have to stay whole there.
    /// </summary>
    [Theory]
    [InlineData(AlignedView.Top)]
    [InlineData(AlignedView.Bottom)]
    public void LookingStraightDownOrUpStillHasAWholeSetOfAxes(AlignedView view)
    {
        FlyCamera camera = Framed();
        camera.Align(view);

        Assert.Equal(1f, camera.Right.Length(), 5);
        Assert.Equal(1f, camera.Up.Length(), 5);
        Assert.Equal(0f, Vector3.Dot(camera.Right, camera.Forward), 5);
        Assert.Equal(0f, Vector3.Dot(camera.Up, camera.Forward), 5);

        Matrix4x4 view4 = camera.ViewMatrix;
        Assert.True(float.IsFinite(view4.M11) && float.IsFinite(view4.M22) && float.IsFinite(view4.M33));
    }

    /// <summary>Which way the world lies on screen in each view, the way Blender draws it.</summary>
    [Theory]
    [InlineData(AlignedView.Front, 1f, 0f, 0f, 0f, 1f, 0f)]    // +X right, +Y up
    [InlineData(AlignedView.Back, -1f, 0f, 0f, 0f, 1f, 0f)]    // -X right, +Y up
    [InlineData(AlignedView.Right, 0f, 0f, -1f, 0f, 1f, 0f)]   // -Z right, +Y up
    [InlineData(AlignedView.Left, 0f, 0f, 1f, 0f, 1f, 0f)]     // +Z right, +Y up
    [InlineData(AlignedView.Top, 1f, 0f, 0f, 0f, 0f, -1f)]     // +X right, -Z up
    public void TheWorldLiesTheRightWayRoundOnScreen(
        AlignedView view,
        float rx, float ry, float rz,
        float ux, float uy, float uz)
    {
        FlyCamera camera = Framed();
        camera.Align(view);

        Vector3 pivot = camera.Pivot;
        Vector2 centre = Project(camera, pivot);

        Vector2 towardsRight = Project(camera, pivot + new Vector3(rx, ry, rz)) - centre;
        Vector2 towardsUp = Project(camera, pivot + new Vector3(ux, uy, uz)) - centre;

        // Screen Y counts downward.
        Assert.True(towardsRight.X > 1f && MathF.Abs(towardsRight.Y) < 1e-2f, $"right came out {towardsRight}");
        Assert.True(towardsUp.Y < -1f && MathF.Abs(towardsUp.X) < 1e-2f, $"up came out {towardsUp}");
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void AligningKeepsThePivotWhereItWas(AlignedView view)
    {
        FlyCamera camera = Framed();
        Vector3 pivot = camera.Pivot;

        camera.Align(view);

        Assert.True(Vector3.Distance(pivot, camera.Pivot) < 1e-3f);
    }

    [Fact]
    public void OrbitingKeepsThePivotAndTheDistance()
    {
        FlyCamera camera = Framed();
        Vector3 pivot = camera.Pivot;
        float distance = camera.PivotDistance;

        camera.Orbit(new Vector2(140f, -60f));

        Assert.True(Vector3.Distance(pivot, camera.Pivot) < 1e-3f);
        Assert.Equal(distance, Vector3.Distance(camera.Position, pivot), 3);
    }

    /// <summary>
    /// The scene follows the mouse, as on the phone and in Blender: a point on the near side of the
    /// model, at the centre of the screen, moves the way the mouse did.
    /// </summary>
    [Fact]
    public void DraggingRightCarriesTheSceneRight()
    {
        FlyCamera camera = Framed();
        Vector3 nearSide = camera.Pivot - (camera.Forward * 4f);

        float before = Project(camera, nearSide).X;
        camera.Orbit(new Vector2(60f, 0f));

        Assert.True(Project(camera, nearSide).X > before);
    }

    [Fact]
    public void ZoomingClosesInOnThePivotWithoutMovingIt()
    {
        FlyCamera camera = Framed();
        Vector3 pivot = camera.Pivot;
        float distance = camera.PivotDistance;

        camera.Zoom(2f);

        Assert.True(Vector3.Distance(pivot, camera.Pivot) < 1e-3f);
        Assert.True(camera.PivotDistance < distance);

        camera.Zoom(-2f);
        Assert.Equal(distance, camera.PivotDistance, 3);
    }

    /// <summary>What makes it orthographic: sliding a point along the view does not move it on screen.</summary>
    [Fact]
    public void InOrthographicDepthDoesNotMoveAnything()
    {
        FlyCamera camera = Framed();
        camera.Orthographic = true;

        Vector3 point = camera.Pivot + camera.Right * 2f + camera.Up;
        Vector2 near = Project(camera, point - camera.Forward * 5f);
        Vector2 far = Project(camera, point + camera.Forward * 5f);

        Assert.True(Vector2.Distance(near, far) < 1e-2f, $"{near} and {far} should coincide.");
    }

    /// <summary>
    /// Switching projection must not make the thing being looked at jump in size: the plane through
    /// the pivot is framed identically either way.
    /// </summary>
    [Fact]
    public void SwitchingProjectionKeepsThePivotPlaneTheSameSizeOnScreen()
    {
        FlyCamera camera = Framed();
        Vector3 onPivotPlane = camera.Pivot + camera.Right * 3f + camera.Up * 2f;

        Vector2 perspective = Project(camera, onPivotPlane);
        camera.Orthographic = true;
        Vector2 orthographic = Project(camera, onPivotPlane);

        Assert.True(Vector2.Distance(perspective, orthographic) < 0.5f, $"{perspective} became {orthographic}.");
    }

    [Fact]
    public void InOrthographicEveryPickingRayIsParallel()
    {
        FlyCamera camera = Framed();
        camera.Align(AlignedView.Front);

        Vector3 corner = camera.ScreenPointToRay(new Vector2(10f, 10f), Viewport).Direction;
        Vector3 centre = camera.ScreenPointToRay(Viewport * 0.5f, Viewport).Direction;

        Assert.True(Vector3.Distance(corner, centre) < 1e-4f);
        Assert.True(Vector3.Distance(centre, -Vector3.UnitZ) < 1e-4f);
    }

    /// <summary>
    /// Blender's auto-perspective: an aligned view switches to orthographic by itself, and the first
    /// free turn switches back — because it was the view that asked for it, not the user.
    /// </summary>
    [Fact]
    public void AnAlignedViewGoesOrthographicAndATurnComesBack()
    {
        FlyCamera camera = Framed();

        camera.Align(AlignedView.Right);
        Assert.True(camera.Orthographic);

        camera.Orbit(new Vector2(30f, 0f));
        Assert.False(camera.Orthographic);
    }

    [Fact]
    public void OrthographicChosenOnPurposeSurvivesATurn()
    {
        FlyCamera camera = Framed();
        camera.Orthographic = true;

        camera.Align(AlignedView.Top);
        camera.Orbit(new Vector2(30f, 20f));

        Assert.True(camera.Orthographic);
    }

    /// <summary>Holding the look button without moving is not a turn, and keeps the aligned view.</summary>
    [Fact]
    public void AStillLookKeepsTheAlignedView()
    {
        FlyCamera camera = Framed();
        camera.Align(AlignedView.Front);

        camera.Look(Vector2.Zero);

        Assert.True(camera.Orthographic);
        Assert.Equal(AlignedView.Front, camera.CurrentAlignedView());
    }

    /// <summary>
    /// A top view can be spun about its own centre without being tipped off it first: orbiting is
    /// allowed all the way to straight down.
    /// </summary>
    [Fact]
    public void ATopViewSpinsWithoutTipping()
    {
        FlyCamera camera = Framed();
        camera.Align(AlignedView.Top);

        camera.Orbit(new Vector2(80f, 0f));

        Assert.Equal(AlignedView.Top, camera.CurrentAlignedView());
        Assert.NotEqual(MathF.PI, camera.Yaw);
    }

    /// <summary>
    /// Flying forward in orthographic would change nothing on screen, so it closes in on the pivot —
    /// which is what it would have looked like in perspective.
    /// </summary>
    [Fact]
    public void FlyingForwardInOrthographicZoomsIn()
    {
        FlyCamera camera = Framed();
        camera.Orthographic = true;
        float height = camera.OrthographicHeight;

        camera.Move(new Vector3(0f, 0f, 1f), 0.25f);

        Assert.True(camera.OrthographicHeight < height);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void OppositeViewsLookOppositeWays(AlignedView view)
    {
        Assert.Equal(-FlyCamera.LookDirection(view), FlyCamera.LookDirection(FlyCamera.Opposite(view)));
        Assert.Equal(view, FlyCamera.Opposite(FlyCamera.Opposite(view)));
    }

    [Fact]
    public void PanningInOrthographicKeepsThePointUnderTheCursor()
    {
        FlyCamera camera = Framed();
        camera.Align(AlignedView.Front);

        Vector3 point = camera.Pivot;
        Vector2 before = Project(camera, point);

        camera.Pan(new Vector2(37f, -21f), camera.PivotDistance, Viewport);

        Vector2 after = Project(camera, point);
        Assert.True(Vector2.Distance(after, before + new Vector2(37f, -21f)) < 0.05f, $"{before} went to {after}.");
    }
}
