using System.Numerics;

namespace EditorApp.Mobile.Rendering;

public enum TouchGestureKind
{
    /// <summary>Nothing to do: the first frame after the number of fingers changed.</summary>
    None,

    /// <summary>A finger has landed on an empty screen. Nothing has moved yet.</summary>
    Began,

    /// <summary>One finger dragging.</summary>
    Orbit,

    /// <summary>Two or more fingers: sliding together, and spreading or closing.</summary>
    PanAndZoom,

    /// <summary>The last finger has left.</summary>
    Ended,
}

/// <param name="Position">Where the fingers are now, or where the last one left. Screen pixels.</param>
/// <param name="Delta">Movement since the last frame, in pixels, screen axes.</param>
/// <param name="Scale">How much the fingers spread since the last frame. 1 is no change.</param>
/// <param name="WasTap">
/// On <see cref="TouchGestureKind.Ended"/>: whether the touch stayed put rather than travelling.
/// A tap is how a tool is used; travel is how the camera is moved.
/// </param>
public readonly record struct TouchGesture(
    TouchGestureKind Kind,
    Vector2 Position,
    Vector2 Delta,
    float Scale,
    bool WasTap)
{
    public static readonly TouchGesture None =
        new(TouchGestureKind.None, Vector2.Zero, Vector2.Zero, 1f, false);
}

/// <summary>
/// Turns a stream of finger positions into camera movement and tool gestures. Knows nothing about
/// Android — it is fed positions, which is what makes the awkward parts testable without a phone.
///
/// The first awkward part is that a gesture changes shape while it is happening. A second finger
/// landing moves the centroid a long way in one frame, and a finger lifting moves it back; taken as
/// movement, either one throws the camera across the scene. So whenever the number of fingers
/// changes the tracker re-baselines and reports nothing for that frame.
///
/// The second is telling a tap from a drag. Both start identically, and the difference only becomes
/// known when the finger leaves, so a touch is measured by how far it wandered in total rather than
/// by where it ended — a finger that goes out and comes back has not tapped.
/// </summary>
public sealed class TouchGestureTracker
{
    private Vector2 _centroid;
    private float _spread;
    private int _count;
    private float _travel;
    private bool _multiTouched;

    /// <summary>
    /// How far a touch may wander and still count as a tap. Android's own slop is 8dp; this is
    /// deliberately more, because a tap meant for a voxel is aimed with a fingertip that rolls.
    /// </summary>
    public float TapSlopPixels { get; set; } = 28f;

    /// <summary>Feeds the current positions of every finger. An empty span ends the gesture.</summary>
    public TouchGesture Update(ReadOnlySpan<Vector2> pointers)
    {
        if (pointers.Length == 0)
        {
            if (_count == 0)
            {
                return TouchGesture.None;
            }

            // A gesture that ever had two fingers on it was a camera move, whatever the centroid
            // did. Ending it as a tap would fire a tool at wherever the last finger happened to be.
            bool wasTap = !_multiTouched && _travel <= TapSlopPixels;

            _count = 0;
            return new TouchGesture(TouchGestureKind.Ended, _centroid, Vector2.Zero, 1f, wasTap);
        }

        Vector2 centroid = Centroid(pointers);
        float spread = Spread(pointers, centroid);

        if (pointers.Length != _count)
        {
            bool began = _count == 0;

            if (began)
            {
                _travel = 0f;
                _multiTouched = false;
            }

            _multiTouched |= pointers.Length > 1;
            _count = pointers.Length;
            _centroid = centroid;
            _spread = spread;

            return began
                ? new TouchGesture(TouchGestureKind.Began, centroid, Vector2.Zero, 1f, false)
                : TouchGesture.None;
        }

        Vector2 delta = centroid - _centroid;
        _centroid = centroid;
        _travel += delta.Length();

        if (pointers.Length == 1)
        {
            return new TouchGesture(TouchGestureKind.Orbit, centroid, delta, 1f, false);
        }

        // Below a pixel the ratio is noise, not a pinch, and dividing by it would amplify jitter
        // into a jump.
        float scale = _spread > 1f && spread > 1f ? spread / _spread : 1f;
        _spread = spread;

        return new TouchGesture(TouchGestureKind.PanAndZoom, centroid, delta, scale, false);
    }

    private static Vector2 Centroid(ReadOnlySpan<Vector2> pointers)
    {
        Vector2 total = Vector2.Zero;
        foreach (Vector2 pointer in pointers)
        {
            total += pointer;
        }

        return total / pointers.Length;
    }

    /// <summary>
    /// Mean distance from the centroid. Using every finger rather than the first two means a third
    /// finger joining a pinch does not change what the pinch means.
    /// </summary>
    private static float Spread(ReadOnlySpan<Vector2> pointers, Vector2 centroid)
    {
        if (pointers.Length < 2)
        {
            return 0f;
        }

        float total = 0f;
        foreach (Vector2 pointer in pointers)
        {
            total += (pointer - centroid).Length();
        }

        return total / pointers.Length;
    }
}
