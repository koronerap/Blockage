using System.Numerics;
using EditorApp.Mobile.Rendering;

namespace EditorApp.Tests.Mobile;

/// <summary>
/// What a hand on a screen means. The tracker takes finger positions rather than Android events
/// precisely so this can be asked without a phone.
/// </summary>
public class TouchGestureTests
{
    private static Vector2[] One(float x, float y) => [new Vector2(x, y)];

    private static Vector2[] Two(float x1, float y1, float x2, float y2) =>
        [new Vector2(x1, y1), new Vector2(x2, y2)];

    /// <summary>
    /// The frame a finger lands on has nothing to compare against, so it must report no movement —
    /// otherwise every gesture would begin by throwing the camera at wherever the finger touched.
    /// </summary>
    [Fact]
    public void FirstFrameOfATouchReportsNothing()
    {
        var tracker = new TouchGestureTracker();

        Assert.Equal(TouchGestureKind.None, tracker.Update(One(100, 100)).Kind);
    }

    [Fact]
    public void OneFingerMovingOrbitsByItsTravel()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(100, 100));

        TouchGesture gesture = tracker.Update(One(140, 90));

        Assert.Equal(TouchGestureKind.Orbit, gesture.Kind);
        Assert.Equal(new Vector2(40, -10), gesture.Delta);
    }

    /// <summary>
    /// The one that matters. A second finger landing moves the centroid a long way in a single
    /// frame; read as movement it would fling the camera across the scene. The tracker has to
    /// re-baseline instead and report nothing for that frame.
    /// </summary>
    [Fact]
    public void SecondFingerLandingDoesNotCountAsMovement()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(100, 100));
        tracker.Update(One(110, 100));

        TouchGesture landing = tracker.Update(Two(110, 100, 900, 100));

        Assert.Equal(TouchGestureKind.None, landing.Kind);
        Assert.Equal(Vector2.Zero, landing.Delta);
    }

    /// <summary>And the same on the way out: lifting one of two fingers is not a pan.</summary>
    [Fact]
    public void LiftingOneOfTwoFingersDoesNotCountAsMovement()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(Two(100, 100, 900, 100));
        tracker.Update(Two(110, 100, 910, 100));

        TouchGesture lifted = tracker.Update(One(110, 100));

        Assert.Equal(TouchGestureKind.None, lifted.Kind);
        Assert.Equal(Vector2.Zero, lifted.Delta);
    }

    [Fact]
    public void TwoFingersMovingTogetherPanWithoutZooming()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(Two(100, 100, 300, 100));

        TouchGesture gesture = tracker.Update(Two(150, 120, 350, 120));

        Assert.Equal(TouchGestureKind.PanAndZoom, gesture.Kind);
        Assert.Equal(new Vector2(50, 20), gesture.Delta);
        Assert.Equal(1f, gesture.Scale, 4);
    }

    [Fact]
    public void FingersSpreadingZoomInWithoutPanning()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(Two(100, 100, 300, 100));

        // Same centre, twice the separation.
        TouchGesture gesture = tracker.Update(Two(0, 100, 400, 100));

        Assert.Equal(TouchGestureKind.PanAndZoom, gesture.Kind);
        Assert.Equal(Vector2.Zero, gesture.Delta);
        Assert.Equal(2f, gesture.Scale, 4);
    }

    [Fact]
    public void FingersClosingZoomOut()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(Two(0, 100, 400, 100));

        Assert.Equal(0.5f, tracker.Update(Two(100, 100, 300, 100)).Scale, 4);
    }

    /// <summary>
    /// A third finger joining should not change what a pinch means, so spread is measured from the
    /// centroid rather than between the first two fingers.
    /// </summary>
    [Fact]
    public void AThirdFingerOnTheCentreLeavesThePinchAlone()
    {
        var tracker = new TouchGestureTracker();
        Vector2[] before = [new(0, 100), new(400, 100), new(200, 100)];
        Vector2[] after = [new(-200, 100), new(600, 100), new(200, 100)];

        tracker.Update(before);

        Assert.Equal(2f, tracker.Update(after).Scale, 4);
    }

    [Fact]
    public void LiftingEveryFingerEndsTheGesture()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(100, 100));

        Assert.Equal(TouchGestureKind.None, tracker.Update([]).Kind);
    }

    /// <summary>
    /// Putting a finger back down after lifting starts a fresh gesture rather than continuing from
    /// where the last one stopped.
    /// </summary>
    [Fact]
    public void TouchingAgainAfterLiftingStartsOver()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(100, 100));
        tracker.Update([]);

        Assert.Equal(TouchGestureKind.None, tracker.Update(One(900, 900)).Kind);
    }
}
