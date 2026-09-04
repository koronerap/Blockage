using System.Numerics;

namespace EditorApp.Mobile.Rendering;

public enum TouchGestureKind
{
    /// <summary>Nothing to do: no fingers, or the first frame after their number changed.</summary>
    None,

    /// <summary>One finger dragging.</summary>
    Orbit,

    /// <summary>Two or more fingers: sliding together, and spreading or closing.</summary>
    PanAndZoom,
}

/// <param name="Delta">Movement since the last frame, in pixels, screen axes.</param>
/// <param name="Scale">How much the fingers spread since the last frame. 1 is no change.</param>
public readonly record struct TouchGesture(TouchGestureKind Kind, Vector2 Delta, float Scale)
{
    public static readonly TouchGesture None = new(TouchGestureKind.None, Vector2.Zero, 1f);
}

/// <summary>
/// Turns a stream of finger positions into camera movement. Knows nothing about Android — it is fed
/// positions, which is what makes the awkward part testable without a phone.
///
/// The awkward part is that a gesture changes shape while it is happening. A second finger landing
/// moves the centroid a long way in one frame, and a finger lifting moves it back; taken as
/// movement, either one throws the camera across the scene. So whenever the number of fingers
/// changes the tracker re-baselines and reports nothing for that frame, and motion resumes from
/// where the hand actually is.
/// </summary>
public sealed class TouchGestureTracker
{
    private Vector2 _centroid;
    private float _spread;
    private int _count;

    /// <summary>
    /// Feeds the current positions of every finger on the screen. An empty span ends the gesture.
    /// </summary>
    public TouchGesture Update(ReadOnlySpan<Vector2> pointers)
    {
        if (pointers.Length == 0)
        {
            _count = 0;
            return TouchGesture.None;
        }

        Vector2 centroid = Centroid(pointers);
        float spread = Spread(pointers, centroid);

        if (pointers.Length != _count)
        {
            _count = pointers.Length;
            _centroid = centroid;
            _spread = spread;
            return TouchGesture.None;
        }

        Vector2 delta = centroid - _centroid;
        _centroid = centroid;

        if (pointers.Length == 1)
        {
            return new TouchGesture(TouchGestureKind.Orbit, delta, 1f);
        }

        // Below a pixel the ratio is noise, not a pinch, and dividing by it would amplify a jitter
        // into a jump.
        float scale = _spread > 1f && spread > 1f ? spread / _spread : 1f;
        _spread = spread;

        return new TouchGesture(TouchGestureKind.PanAndZoom, delta, scale);
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
