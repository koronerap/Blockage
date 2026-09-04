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
    /// The frame a finger lands on has nothing to compare against, so it announces the touch and
    /// reports no movement — otherwise every gesture would begin by throwing the camera at wherever
    /// the finger happened to land.
    /// </summary>
    [Fact]
    public void FirstFrameOfATouchBeginsWithoutMoving()
    {
        var tracker = new TouchGestureTracker();

        TouchGesture began = tracker.Update(One(100, 100));

        Assert.Equal(TouchGestureKind.Began, began.Kind);
        Assert.Equal(Vector2.Zero, began.Delta);
        Assert.Equal(new Vector2(100, 100), began.Position);
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

        Assert.Equal(TouchGestureKind.Ended, tracker.Update([]).Kind);
    }

    /// <summary>A touch that goes nowhere is a tap, and a tap is how a tool gets used.</summary>
    [Fact]
    public void ATouchThatDoesNotTravelIsATap()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(100, 100));
        tracker.Update(One(104, 97));

        TouchGesture ended = tracker.Update([]);

        Assert.True(ended.WasTap);
        Assert.Equal(new Vector2(104, 97), ended.Position);
    }

    [Fact]
    public void ATouchThatTravelsIsNotATap()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(100, 100));
        tracker.Update(One(400, 100));

        Assert.False(tracker.Update([]).WasTap);
    }

    /// <summary>
    /// Distance travelled, not distance moved. A finger that wanders out and comes back has ended up
    /// where it started, and calling that a tap would fire a tool at the end of a camera drag.
    /// </summary>
    [Fact]
    public void AFingerThatWandersAndReturnsIsNotATap()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(100, 100));
        tracker.Update(One(400, 100));
        tracker.Update(One(100, 100));

        Assert.False(tracker.Update([]).WasTap);
    }

    /// <summary>
    /// A pinch usually ends with the fingers close to where they started, so measuring travel alone
    /// would call it a tap and fire a tool the moment the user let go of a zoom.
    /// </summary>
    [Fact]
    public void APinchIsNeverATapHoweverLittleItMoved()
    {
        var tracker = new TouchGestureTracker();
        tracker.Update(One(200, 200));
        tracker.Update(Two(200, 200, 260, 200));
        tracker.Update(Two(199, 201, 261, 199));
        tracker.Update(One(200, 200));

        Assert.False(tracker.Update([]).WasTap);
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
        tracker.Update(One(600, 600));
        tracker.Update([]);

        TouchGesture began = tracker.Update(One(900, 900));

        Assert.Equal(TouchGestureKind.Began, began.Kind);
        Assert.Equal(Vector2.Zero, began.Delta);

        // The travel from the drag before must not follow the new touch into its tap test.
        Assert.True(tracker.Update([]).WasTap);
    }

    /// <summary>Lifting when nothing was down is not the end of anything.</summary>
    [Fact]
    public void LiftingWithNoFingersDownReportsNothing()
    {
        Assert.Equal(TouchGestureKind.None, new TouchGestureTracker().Update([]).Kind);
    }
}
