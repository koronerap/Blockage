using Silk.NET.Input;

namespace EditorApp.Input;

/// <summary>A starting set of shortcuts to change from.</summary>
public enum KeymapPreset
{
    /// <summary>What an editing program is expected to answer to: G moves, R rotates, E extrudes, B paints.</summary>
    Default,

    /// <summary>
    /// The keys Mimic Busters itself uses — Q W E R for the tools — so moving between the game and
    /// its level editor does not mean moving between two sets of habits.
    /// </summary>
    MimicBusters,
}

/// <summary>
/// Which keys do what: every action and the chords bound to it, begun from a preset and changed from
/// there. Changes are kept as the difference from the preset, so a preset that gains a shortcut in a
/// later version hands it on to everyone who had not bound that action themselves.
///
/// A chord does one thing. Binding it to an action takes it from whatever had it — a key that did
/// two things would do whichever came first, which is no answer at all.
/// </summary>
public sealed class Keymap
{
    /// <summary>More than two is a list nobody reads, and nothing needs it.</summary>
    public const int SlotsPerAction = 2;

    private readonly Dictionary<EditorAction, KeyChord?[]> _bindings = [];

    private Keymap(KeymapPreset preset)
    {
        Preset = preset;
        foreach (ActionInfo info in EditorActions.All)
        {
            _bindings[info.Action] = new KeyChord?[SlotsPerAction];
        }

        foreach ((EditorAction action, KeyChord[] chords) in Defaults(preset))
        {
            for (int i = 0; i < chords.Length && i < SlotsPerAction; i++)
            {
                _bindings[action][i] = chords[i];
            }
        }
    }

    /// <summary>The keymap every key press is looked up in, and every label shown from. Set from the preferences.</summary>
    public static Keymap Active { get; set; } = For(KeymapPreset.Default);

    public KeymapPreset Preset { get; }

    public static Keymap For(KeymapPreset preset) => new(preset);

    public static string NameOf(KeymapPreset preset) => preset switch
    {
        KeymapPreset.MimicBusters => "Mimic Busters",
        _ => "Default",
    };

    /// <summary>The action a chord runs, if any.</summary>
    public EditorAction? ActionFor(KeyChord chord)
    {
        foreach ((EditorAction action, KeyChord?[] slots) in _bindings)
        {
            if (Array.IndexOf(slots, chord) >= 0)
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>An action's chords, in slot order, empty slots left out.</summary>
    public IReadOnlyList<KeyChord> Bindings(EditorAction action) =>
        [.. _bindings[action].Where(c => c is not null).Select(c => c!.Value)];

    public KeyChord? Slot(EditorAction action, int slot) => _bindings[action][slot];

    /// <summary>The first chord, as shown beside a menu item; empty when there is none.</summary>
    public string Label(EditorAction action) => Bindings(action) is [var first, ..] ? first.ToString() : string.Empty;

    /// <summary>
    /// Puts a chord in an action's slot, or clears the slot with null. Returns the action the chord was
    /// taken from, if another had it.
    /// </summary>
    public EditorAction? Bind(EditorAction action, int slot, KeyChord? chord)
    {
        EditorAction? takenFrom = null;

        if (chord is { } bound)
        {
            foreach ((EditorAction other, KeyChord?[] slots) in _bindings)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == bound && (other != action || i != slot))
                    {
                        slots[i] = null;
                        takenFrom = other == action ? null : other;
                    }
                }
            }
        }

        _bindings[action][slot] = chord;
        return takenFrom;
    }

    /// <summary>Puts one action back the way its preset has it, taking its chords back from wherever they went.</summary>
    public void Reset(EditorAction action)
    {
        KeyChord[] chords = Defaults(Preset).TryGetValue(action, out KeyChord[]? found) ? found : [];

        for (int slot = 0; slot < SlotsPerAction; slot++)
        {
            Bind(action, slot, slot < chords.Length ? chords[slot] : null);
        }
    }

    /// <summary>Whether an action is bound as its preset binds it.</summary>
    public bool IsDefault(EditorAction action)
    {
        KeyChord[] chords = Defaults(Preset).TryGetValue(action, out KeyChord[]? found) ? found : [];
        KeyChord?[] slots = _bindings[action];

        for (int slot = 0; slot < SlotsPerAction; slot++)
        {
            KeyChord? expected = slot < chords.Length ? chords[slot] : null;
            if (slots[slot] != expected)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What differs from the preset, by action id, as saved: each slot's chord or an empty string.</summary>
    public Dictionary<string, string[]> Changes()
    {
        var changes = new Dictionary<string, string[]>();
        foreach (ActionInfo info in EditorActions.All)
        {
            if (!IsDefault(info.Action))
            {
                changes[info.Id] = [.. _bindings[info.Action].Select(c => c?.Storage ?? string.Empty)];
            }
        }

        return changes;
    }

    /// <summary>A preset with saved changes laid over it. Anything unreadable is skipped rather than refused.</summary>
    public static Keymap With(KeymapPreset preset, IReadOnlyDictionary<string, string[]>? changes)
    {
        var keymap = For(preset);
        if (changes is null)
        {
            return keymap;
        }

        foreach ((string id, string[] slots) in changes)
        {
            if (EditorActions.ById(id) is not { } info)
            {
                continue;
            }

            for (int slot = 0; slot < SlotsPerAction; slot++)
            {
                string text = slot < slots.Length ? slots[slot] : string.Empty;
                keymap.Bind(info.Action, slot, KeyChord.TryParse(text, out KeyChord chord) ? chord : null);
            }
        }

        return keymap;
    }

    /// <summary>Every chord bound to more than one action — never, once <see cref="Bind"/> has had its say; kept for the tests.</summary>
    public IEnumerable<KeyChord> Duplicates() =>
        _bindings.Values.SelectMany(s => s).Where(c => c is not null).Select(c => c!.Value)
            .GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key);

    private static Dictionary<EditorAction, KeyChord[]> Defaults(KeymapPreset preset)
    {
        Dictionary<EditorAction, KeyChord[]> shared = new()
        {
            [EditorAction.Undo] = [KeyChord.Ctrl(Key.Z)],
            [EditorAction.Copy] = [KeyChord.Ctrl(Key.C)],
            [EditorAction.Cut] = [KeyChord.Ctrl(Key.X)],
            [EditorAction.Paste] = [KeyChord.Ctrl(Key.V)],
            [EditorAction.Hide] = [new(Key.H)],
            [EditorAction.ShowAll] = [KeyChord.AltOf(Key.H)],
            [EditorAction.Lock] = [new(Key.L)],
            [EditorAction.UnlockAll] = [KeyChord.AltOf(Key.L)],
            [EditorAction.Rename] = [new(Key.F2)],
            [EditorAction.KeepExtrude] = [new(Key.Enter), new(Key.KeypadEnter)],
            [EditorAction.Cancel] = [new(Key.Escape)],

            [EditorAction.NewLevel] = [KeyChord.Ctrl(Key.N)],
            [EditorAction.Open] = [KeyChord.Ctrl(Key.O)],
            [EditorAction.Save] = [KeyChord.Ctrl(Key.S)],
            [EditorAction.SaveAs] = [KeyChord.CtrlShift(Key.S)],
            [EditorAction.Export] = [KeyChord.Ctrl(Key.E)],

            [EditorAction.FrameLevel] = [new(Key.Home)],
            [EditorAction.ToggleMeasurements] = [new(Key.D)],
            [EditorAction.ToggleSidebar] = [new(Key.N)],
            [EditorAction.ShortcutSheet] = [new(Key.F1)],
            [EditorAction.Preferences] = [KeyChord.CtrlAlt(Key.S)],

            // Blender's: Shift+Tab. Free in the old keys too, so both have it.
            [EditorAction.ToggleSnap] = [KeyChord.ShiftOf(Key.Tab)],

            // Blender's viewport keys: Alt+Z X-Ray, Shift+Alt+Z overlays, Shift+Z wireframe.
            [EditorAction.ToggleXRay] = [KeyChord.AltOf(Key.Z)],
            [EditorAction.ToggleOverlays] = [new(Key.Z, Shift: true, Alt: true)],
            [EditorAction.ToggleWireframe] = [KeyChord.ShiftOf(Key.Z)],
        };

        if (preset == KeymapPreset.MimicBusters)
        {
            // Exactly the keys the editor had before there was a choice, which are the game's.
            shared[EditorAction.ToolTransform] = [new(Key.Q)];
            shared[EditorAction.ToolExtrude] = [new(Key.W)];
            shared[EditorAction.ToolPaint] = [new(Key.E)];
            shared[EditorAction.ToolLoopCut] = [new(Key.R)];
            shared[EditorAction.ToolView] = [new(Key.V)];
            shared[EditorAction.ToolOtherMode] = [new(Key.F)];
            shared[EditorAction.ToolCycleMode] = [new(Key.X)];
            shared[EditorAction.Redo] = [KeyChord.Ctrl(Key.Y), KeyChord.CtrlShift(Key.Z)];
            shared[EditorAction.Duplicate] = [KeyChord.ShiftOf(Key.D)];
            shared[EditorAction.Delete] = [new(Key.Delete)];
            shared[EditorAction.FrameFocused] = [new(Key.KeypadDecimal)];
            shared[EditorAction.ToggleGrid] = [new(Key.G)];
            AddNumpadViews(shared, digitsToo: false);
            return shared;
        }

        // Default: the keys an editing program is expected to answer to. G grabs and R rotates, as in
        // Blender; E extrudes and B is the brush; Ctrl+R is Blender's own loop cut. F frames what is
        // focused, as it does in every engine editor, and Ctrl+D duplicates, as in most of them.
        shared[EditorAction.ToolMove] = [new(Key.G)];
        shared[EditorAction.ToolRotate] = [new(Key.R)];
        shared[EditorAction.ToolExtrude] = [new(Key.E)];
        shared[EditorAction.ToolPaint] = [new(Key.B)];
        shared[EditorAction.ToolLoopCut] = [KeyChord.Ctrl(Key.R)];
        shared[EditorAction.ToolView] = [new(Key.V)];
        shared[EditorAction.ToolOtherMode] = [new(Key.Tab)];
        shared[EditorAction.ToolCycleMode] = [new(Key.X)];
        shared[EditorAction.Redo] = [KeyChord.CtrlShift(Key.Z), KeyChord.Ctrl(Key.Y)];
        shared[EditorAction.Duplicate] = [KeyChord.Ctrl(Key.D), KeyChord.ShiftOf(Key.D)];
        shared[EditorAction.Delete] = [new(Key.Delete), new(Key.Backspace)];
        shared[EditorAction.FrameFocused] = [new(Key.F), new(Key.KeypadDecimal)];
        shared[EditorAction.ToggleGrid] = [KeyChord.ShiftOf(Key.G)];

        // The number row as well as the numpad, which a laptop does not have — Blender's "emulate
        // numpad", on from the start.
        AddNumpadViews(shared, digitsToo: true);
        return shared;
    }

    /// <summary>Blender's numpad: 1 front, 3 right, 7 top, Ctrl for the opposite side; 9 turns round, 5 swaps projection, 2 4 6 8 step.</summary>
    private static void AddNumpadViews(Dictionary<EditorAction, KeyChord[]> map, bool digitsToo)
    {
        void Add(EditorAction action, Key numpad, Key digit, bool control = false) =>
            map[action] = digitsToo
                ? [new(numpad, control), new(digit, control)]
                : [new(numpad, control)];

        Add(EditorAction.ViewFront, Key.Keypad1, Key.Number1);
        Add(EditorAction.ViewBack, Key.Keypad1, Key.Number1, control: true);
        Add(EditorAction.ViewRight, Key.Keypad3, Key.Number3);
        Add(EditorAction.ViewLeft, Key.Keypad3, Key.Number3, control: true);
        Add(EditorAction.ViewTop, Key.Keypad7, Key.Number7);
        Add(EditorAction.ViewBottom, Key.Keypad7, Key.Number7, control: true);
        Add(EditorAction.ViewTurnRound, Key.Keypad9, Key.Number9);
        Add(EditorAction.ToggleOrthographic, Key.Keypad5, Key.Number5);
        Add(EditorAction.OrbitLeft, Key.Keypad4, Key.Number4);
        Add(EditorAction.OrbitRight, Key.Keypad6, Key.Number6);
        Add(EditorAction.OrbitUp, Key.Keypad8, Key.Number8);
        Add(EditorAction.OrbitDown, Key.Keypad2, Key.Number2);
    }
}
