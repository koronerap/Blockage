using Silk.NET.Input;

namespace EditorApp.Input;

/// <summary>
/// One key with the modifiers held for it: what a shortcut is. Matched exactly — Shift+G is not G —
/// so one key can carry different actions under different modifiers without an order of cases to
/// get wrong.
/// </summary>
public readonly record struct KeyChord(Key Key, bool Control = false, bool Shift = false, bool Alt = false)
{
    public static KeyChord Ctrl(Key key) => new(key, Control: true);

    public static KeyChord CtrlShift(Key key) => new(key, Control: true, Shift: true);

    public static KeyChord ShiftOf(Key key) => new(key, Shift: true);

    public static KeyChord AltOf(Key key) => new(key, Alt: true);

    public static KeyChord CtrlAlt(Key key) => new(key, Control: true, Alt: true);

    /// <summary>Keys that only ever modify another: a shortcut cannot be one of these alone.</summary>
    public static bool IsModifier(Key key) => key is Key.ShiftLeft or Key.ShiftRight or Key.ControlLeft
        or Key.ControlRight or Key.AltLeft or Key.AltRight or Key.SuperLeft or Key.SuperRight or Key.Menu
        or Key.CapsLock or Key.NumLock or Key.ScrollLock or Key.Unknown;

    /// <summary>As it is shown: "Ctrl+Shift+Z", "Numpad 1", "Delete".</summary>
    public override string ToString() => Modifiers() + NameOf(Key);

    /// <summary>
    /// As it is saved: modifiers, then the key's own name in the input library — stable where a
    /// display name might be reworded.
    /// </summary>
    public string Storage => Modifiers() + (Key == Key.Number0 ? "Number0" : Key.ToString());

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        bool control = false, shift = false, alt = false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": control = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default: return false;
            }
        }

        // A lone "+" key is not a thing any preset binds; the last part is always a name.
        if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key) || !Enum.IsDefined(key) || IsModifier(key))
        {
            return false;
        }

        chord = new KeyChord(key, control, shift, alt);
        return true;
    }

    private string Modifiers() =>
        (Control ? "Ctrl+" : string.Empty) + (Shift ? "Shift+" : string.Empty) + (Alt ? "Alt+" : string.Empty);

    /// <summary>What a key is called on screen, where the library's own name would not do.</summary>
    public static string NameOf(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.Number0 and <= Key.Number9 => ((int)(key - Key.Number0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= Key.Keypad0 and <= Key.Keypad9 => $"Numpad {(int)(key - Key.Keypad0)}",
        Key.KeypadDecimal => "Numpad .",
        Key.KeypadEnter => "Numpad Enter",
        Key.KeypadAdd => "Numpad +",
        Key.KeypadSubtract => "Numpad -",
        Key.KeypadMultiply => "Numpad *",
        Key.KeypadDivide => "Numpad /",
        Key.KeypadEqual => "Numpad =",
        Key.Escape => "Esc",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        Key.PrintScreen => "Print Screen",
        Key.Apostrophe => "'",
        Key.Comma => ",",
        Key.Minus => "-",
        Key.Period => ".",
        Key.Slash => "/",
        Key.Semicolon => ";",
        Key.Equal => "=",
        Key.LeftBracket => "[",
        Key.BackSlash => "\\",
        Key.RightBracket => "]",
        Key.GraveAccent => "`",
        _ => key.ToString(),
    };
}
