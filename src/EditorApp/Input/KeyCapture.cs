using Silk.NET.Input;

namespace EditorApp.Input;

/// <summary>
/// A shortcut waiting to be rebound: the next chord pressed goes to it rather than to the keymap.
///
/// Modifiers on their own are held back — Ctrl is on its way to being Ctrl+Something — and Esc
/// alone gives up, as it does everywhere else. The press is taken from the key handler, not from
/// ImGui, because ImGui turns Ctrl+letter chords into its own shortcuts before a widget sees them.
/// </summary>
public static class KeyCapture
{
    private static Action<KeyChord>? _deliver;

    public static bool IsWaiting => _deliver is not null;

    /// <summary>What is being rebound, for the button that is waiting to show it.</summary>
    public static object? Owner { get; private set; }

    public static void Begin(object owner, Action<KeyChord> deliver)
    {
        Owner = owner;
        _deliver = deliver;
    }

    public static void Cancel()
    {
        Owner = null;
        _deliver = null;
    }

    /// <summary>Hands a pressed chord to whatever is waiting. True when it was taken, and so is not a shortcut.</summary>
    public static bool Offer(KeyChord chord)
    {
        if (_deliver is not { } deliver)
        {
            return false;
        }

        if (KeyChord.IsModifier(chord.Key))
        {
            return true;
        }

        Cancel();

        if (chord == new KeyChord(Key.Escape))
        {
            return true;
        }

        deliver(chord);
        return true;
    }
}
