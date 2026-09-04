using System.Numerics;
using Android.Views;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// Reads finger positions out of a <see cref="MotionEvent"/> and hands them to
/// <see cref="TouchGestureTracker"/>. Nothing but the reading lives here — that keeps the part with
/// the arithmetic in it testable on a machine with no touchscreen.
/// </summary>
public sealed class TouchGestures
{
    /// <summary>
    /// More fingers than this and the extras are ignored. Nothing in the editor means anything
    /// different with five fingers than with four, and the cap keeps the positions on the stack.
    /// </summary>
    private const int MaxPointers = 4;

    private readonly TouchGestureTracker _tracker = new();

    public TouchGesture Consume(MotionEvent e)
    {
        MotionEventActions action = e.ActionMasked;

        if (action is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            return _tracker.Update([]);
        }

        // On a pointer-up the finger that is leaving is still in the list, and counting it would
        // baseline the next gesture against a hand that has already changed.
        int leaving = action == MotionEventActions.PointerUp ? e.ActionIndex : -1;

        Span<Vector2> pointers = stackalloc Vector2[MaxPointers];
        int count = 0;

        for (int i = 0; i < e.PointerCount && count < MaxPointers; i++)
        {
            if (i == leaving)
            {
                continue;
            }

            pointers[count++] = new Vector2(e.GetX(i), e.GetY(i));
        }

        return _tracker.Update(pointers[..count]);
    }
}
