using System.Numerics;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// The navigation gizmo, clicked and dragged through the headless ImGui. What it looks like is a
/// screenshot's business; what a click on it does is only visible by trying it.
/// </summary>
[Collection(nameof(ImGuiCollection))]
public sealed class NavigationGizmoTests : IDisposable
{
    private static readonly ViewportRect Viewport = new(new Vector2(0f, 40f), new Vector2(1000f, 600f));

    private readonly ImGuiHarness _ui = new();
    private readonly NavigationGizmo _gizmo = new();
    private readonly FlyCamera _camera = new();
    private int _framed;

    public NavigationGizmoTests()
    {
        _camera.FrameBox(new Vector3(-4f, 0f, -4f), new Vector3(4f, 8f, 4f));

        // Windows that size themselves are hidden on the frame they first appear, and a window has to
        // have been on screen for a frame before it can be hovered. Two frames with the mouse away.
        _ui.Frame(Draw, new Vector2(-100f, -100f), inWindow: false);
        _ui.Frame(Draw, new Vector2(-100f, -100f), inWindow: false);
    }

    public void Dispose() => _ui.Dispose();

    private void Draw() => _gizmo.Draw(_camera, Viewport, () => _framed++);

    private static Vector2 Centre => NavigationGizmo.Centre(Viewport);

    private static Vector2 BallFor(FlyCamera camera, AlignedView view) =>
        NavigationGizmo.BallPosition(camera, -FlyCamera.LookDirection(view), Centre);

    /// <summary>
    /// The middle of the n-th button in the column under the disc. Measured on a frame that still
    /// draws the gizmo: ImGui does not hand the first item of a window a click on the frame that
    /// window reappears, and in the editor the gizmo never disappears.
    /// </summary>
    private Vector2 Button(int index)
    {
        float height = 0f;
        _ui.Frame(() => { Draw(); height = ImGui.GetFrameHeight(); }, inWindow: false);

        Vector2 top = NavigationGizmo.ButtonColumnTop(Viewport);
        return top + new Vector2(0f, (index * (height + 4f)) + (height * 0.5f));
    }

    public static TheoryData<AlignedView> Views() => new(Enum.GetValues<AlignedView>());

    [Theory]
    [MemberData(nameof(Views))]
    public void ClickingABallLooksFromThatSide(AlignedView view)
    {
        _ui.Click(BallFor(_camera, view), Draw, inWindow: false);

        Assert.Equal(view, _camera.CurrentAlignedView());
    }

    /// <summary>
    /// Looking straight along an axis puts both its ends in the middle of the gizmo. The one facing
    /// the viewer is on top, and clicking it is the way round to the other side.
    /// </summary>
    [Fact]
    public void ClickingTheBallFacingYouTurnsTheViewRound()
    {
        _camera.Align(AlignedView.Front);

        _ui.Click(Centre, Draw, inWindow: false);

        Assert.Equal(AlignedView.Back, _camera.CurrentAlignedView());
    }

    [Fact]
    public void DraggingTheDiscOrbits()
    {
        float yaw = _camera.Yaw;
        Vector3 pivot = _camera.Pivot;

        _ui.Drag(Centre, Centre + new Vector2(40f, 0f), Draw, inWindow: false);

        Assert.NotEqual(yaw, _camera.Yaw);
        Assert.True(Vector3.Distance(pivot, _camera.Pivot) < 1e-3f);
    }

    /// <summary>A press that turns into a drag is an orbit, even if it began on a ball.</summary>
    [Fact]
    public void ADragThatStartsOnABallDoesNotAlign()
    {
        Vector2 ball = BallFor(_camera, AlignedView.Right);

        _ui.Drag(ball, ball + new Vector2(0f, 40f), Draw, inWindow: false);

        Assert.Null(_camera.CurrentAlignedView());
        Assert.False(_camera.Orthographic);
    }

    [Fact]
    public void TheProjectionButtonSwitchesAndSwitchesBack()
    {
        Vector2 button = Button(0);

        _ui.Click(button, Draw, inWindow: false);
        Assert.True(_camera.Orthographic);

        _ui.Click(button, Draw, inWindow: false);
        Assert.False(_camera.Orthographic);
    }

    [Fact]
    public void DraggingTheZoomButtonUpClosesIn()
    {
        Vector2 button = Button(1);
        float distance = _camera.PivotDistance;
        Vector3 pivot = _camera.Pivot;

        _ui.Drag(button, button - new Vector2(0f, 40f), Draw, inWindow: false);

        Assert.True(_camera.PivotDistance < distance);
        Assert.True(Vector3.Distance(pivot, _camera.Pivot) < 1e-3f);
    }

    [Fact]
    public void DraggingThePanButtonSlidesTheViewWithoutTurningIt()
    {
        Vector2 button = Button(2);
        Vector3 pivot = _camera.Pivot;
        Vector3 forward = _camera.Forward;

        _ui.Drag(button, button + new Vector2(30f, 0f), Draw, inWindow: false);

        Assert.True(Vector3.Distance(pivot, _camera.Pivot) > 0.1f);
        Assert.True(Vector3.Distance(forward, _camera.Forward) < 1e-5f);
    }

    [Fact]
    public void TheFrameButtonFramesTheLevel()
    {
        _ui.Click(Button(3), Draw, inWindow: false);

        Assert.Equal(1, _framed);
    }

    /// <summary>The gizmo must keep the viewport's clicks off itself — and nothing else.</summary>
    [Fact]
    public void TheGizmoTakesTheMouseOnlyOverItself()
    {
        bool captured = false;

        _ui.Frame(Draw, Centre, inWindow: false);
        _ui.Frame(() => { Draw(); captured = ImGui.GetIO().WantCaptureMouse; }, Centre, inWindow: false);
        Assert.True(captured);

        Vector2 beside = Centre - new Vector2(NavigationGizmo.Radius + 30f, 0f);
        _ui.Frame(Draw, beside, inWindow: false);
        _ui.Frame(() => { Draw(); captured = ImGui.GetIO().WantCaptureMouse; }, beside, inWindow: false);
        Assert.False(captured);
    }
}
