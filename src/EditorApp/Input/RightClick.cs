using System.Numerics;

namespace EditorApp.Input;

/// <summary>
/// Tells a right click from a right drag. The right button looks round the scene while it is held;
/// let go again quickly, with the view barely turned and nothing flown, it was a click — which opens
/// the viewport's menu, as a right click does in Unity's scene view, where the button also looks.
/// </summary>
public sealed class RightClick
{
    /// <summary>How far the mouse may wander, in pixels, and still have clicked.</summary>
    public const float MaxTravel = 5f;

    /// <summary>How long a click may last. Longer, and it was a look that happened not to move.</summary>
    public const double MaxSeconds = 0.4;

    private Vector2 _at;
    private double _pressedAt;
    private float _travel;
    private bool _flew;
    private bool _down;
    private bool _counts;

    public bool IsDown => _down;

    /// <param name="counts">Whether a click here would mean anything — pressed over the viewport, and not in the middle of another drag.</param>
    public void Press(Vector2 at, double now, bool counts)
    {
        _at = at;
        _pressedAt = now;
        _travel = 0f;
        _flew = false;
        _down = true;
        _counts = counts;
    }

    public void Moved(Vector2 delta)
    {
        if (_down)
        {
            _travel += delta.Length();
        }
    }

    /// <summary>The camera flew while the button was down: a look, whatever the mouse did.</summary>
    public void Flew()
    {
        if (_down)
        {
            _flew = true;
        }
    }

    /// <summary>The button is let go: where the click was, when it was one.</summary>
    public Vector2? Release(double now)
    {
        if (!_down)
        {
            return null;
        }

        _down = false;
        return _counts && _travel <= MaxTravel && !_flew && now - _pressedAt <= MaxSeconds ? _at : null;
    }
}
