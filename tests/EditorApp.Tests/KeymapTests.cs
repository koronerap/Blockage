using EditorApp.Input;
using Silk.NET.Input;

namespace EditorApp.Tests;

/// <summary>The two presets, and what a keymap does when a key is moved, reset and saved.</summary>
public class KeymapTests
{
    private static KeyChord K(Key key) => new(key);

    [Theory]
    [InlineData(KeymapPreset.Default)]
    [InlineData(KeymapPreset.MimicBusters)]
    public void NoPresetPutsOneChordOnTwoActions(KeymapPreset preset) =>
        Assert.Empty(Keymap.For(preset).Duplicates());

    /// <summary>Mimic Busters is exactly the keys the editor had before there was a choice — the game's.</summary>
    [Fact]
    public void MimicBustersKeepsTheKeysTheEditorHad()
    {
        Keymap keymap = Keymap.For(KeymapPreset.MimicBusters);

        (KeyChord Chord, EditorAction Action)[] expected =
        [
            (K(Key.Q), EditorAction.ToolTransform),
            (K(Key.T), EditorAction.ToolSelect),
            (K(Key.W), EditorAction.ToolExtrude),
            (K(Key.E), EditorAction.ToolPaint),
            (K(Key.R), EditorAction.ToolLoopCut),
            (K(Key.V), EditorAction.ToolView),
            (K(Key.F), EditorAction.ToolOtherMode),
            (K(Key.X), EditorAction.ToolCycleMode),
            (K(Key.G), EditorAction.ToggleGrid),
            (K(Key.D), EditorAction.ToggleMeasurements),
            (K(Key.N), EditorAction.ToggleSidebar),
            (K(Key.H), EditorAction.Hide),
            (KeyChord.AltOf(Key.H), EditorAction.ShowAll),
            (KeyChord.ShiftOf(Key.D), EditorAction.Duplicate),
            (K(Key.Delete), EditorAction.Delete),
            (K(Key.F2), EditorAction.Rename),
            (K(Key.F1), EditorAction.ShortcutSheet),
            (K(Key.Home), EditorAction.FrameLevel),
            (K(Key.KeypadDecimal), EditorAction.FrameFocused),
            (K(Key.Keypad1), EditorAction.ViewFront),
            (KeyChord.Ctrl(Key.Keypad1), EditorAction.ViewBack),
            (K(Key.Keypad5), EditorAction.ToggleOrthographic),
            (K(Key.Keypad9), EditorAction.ViewTurnRound),
            (K(Key.Keypad4), EditorAction.OrbitLeft),
            (K(Key.Enter), EditorAction.KeepExtrude),
            (K(Key.KeypadEnter), EditorAction.KeepExtrude),
            (K(Key.Escape), EditorAction.Cancel),
            (KeyChord.Ctrl(Key.Z), EditorAction.Undo),
            (KeyChord.Ctrl(Key.Y), EditorAction.Redo),
            (KeyChord.CtrlShift(Key.Z), EditorAction.Redo),
            (KeyChord.Ctrl(Key.S), EditorAction.Save),
            (KeyChord.CtrlShift(Key.S), EditorAction.SaveAs),
            (KeyChord.Ctrl(Key.E), EditorAction.Export),
        ];

        foreach ((KeyChord chord, EditorAction action) in expected)
        {
            Assert.True(keymap.ActionFor(chord) == action, $"{chord} should be {action}, is {keymap.ActionFor(chord)}");
        }
    }

    [Fact]
    public void TheDefaultAnswersToAnEditingProgramsKeys()
    {
        Keymap keymap = Keymap.For(KeymapPreset.Default);

        Assert.Equal(EditorAction.ToolMove, keymap.ActionFor(K(Key.G)));
        Assert.Equal(EditorAction.ToolRotate, keymap.ActionFor(K(Key.R)));
        Assert.Equal(EditorAction.ToolExtrude, keymap.ActionFor(K(Key.E)));
        Assert.Equal(EditorAction.ToolPaint, keymap.ActionFor(K(Key.B)));
        Assert.Equal(EditorAction.ToolLoopCut, keymap.ActionFor(KeyChord.Ctrl(Key.R)));
        Assert.Equal(EditorAction.FrameFocused, keymap.ActionFor(K(Key.F)));
        Assert.Equal(EditorAction.Duplicate, keymap.ActionFor(KeyChord.Ctrl(Key.D)));
        Assert.Equal(EditorAction.ToggleGrid, keymap.ActionFor(KeyChord.ShiftOf(Key.G)));

        // The number row stands in for a numpad a laptop does not have.
        Assert.Equal(EditorAction.ViewFront, keymap.ActionFor(K(Key.Number1)));
        Assert.Equal(EditorAction.ViewBack, keymap.ActionFor(KeyChord.Ctrl(Key.Number1)));
        Assert.Equal(EditorAction.ViewFront, keymap.ActionFor(K(Key.Keypad1)));

        // W is Blender's key for the selecting tools; Q, the game's Transform, is left free.
        Assert.Equal(EditorAction.ToolSelect, keymap.ActionFor(K(Key.W)));
        Assert.Equal(EditorAction.SelectAll, keymap.ActionFor(K(Key.A)));
        Assert.Equal(EditorAction.DeselectAll, keymap.ActionFor(KeyChord.AltOf(Key.A)));
        Assert.Equal(EditorAction.InvertSelection, keymap.ActionFor(KeyChord.Ctrl(Key.I)));
        Assert.Equal(EditorAction.Join, keymap.ActionFor(KeyChord.Ctrl(Key.J)));
        Assert.Null(keymap.ActionFor(K(Key.Q)));
    }

    /// <summary>Every action the menus offer can be reached from the keyboard in each preset, or has a menu of its own.</summary>
    [Theory]
    [InlineData(KeymapPreset.Default)]
    [InlineData(KeymapPreset.MimicBusters)]
    public void EveryToolHasAKey(KeymapPreset preset)
    {
        Keymap keymap = Keymap.For(preset);

        Assert.NotEmpty(keymap.Bindings(EditorAction.ToolExtrude));
        Assert.NotEmpty(keymap.Bindings(EditorAction.ToolPaint));
        Assert.NotEmpty(keymap.Bindings(EditorAction.ToolLoopCut));
        Assert.True(keymap.Bindings(EditorAction.ToolTransform).Count > 0 || keymap.Bindings(EditorAction.ToolMove).Count > 0);
    }

    [Fact]
    public void BindingAKeyTakesItFromTheActionThatHadIt()
    {
        Keymap keymap = Keymap.For(KeymapPreset.Default);

        EditorAction? taken = keymap.Bind(EditorAction.ToggleGrid, 0, K(Key.G));

        Assert.Equal(EditorAction.ToolMove, taken);
        Assert.Equal(EditorAction.ToggleGrid, keymap.ActionFor(K(Key.G)));
        Assert.Empty(keymap.Bindings(EditorAction.ToolMove));
        Assert.Empty(keymap.Duplicates());
    }

    /// <summary>Moving a key to the other slot of the same action is not taking it from anyone.</summary>
    [Fact]
    public void MovingAKeyWithinAnActionTakesItFromNobody()
    {
        Keymap keymap = Keymap.For(KeymapPreset.Default);

        Assert.Null(keymap.Bind(EditorAction.Duplicate, 1, KeyChord.Ctrl(Key.D)));
        Assert.Equal([KeyChord.Ctrl(Key.D)], keymap.Bindings(EditorAction.Duplicate));
    }

    [Fact]
    public void ResetPutsAnActionBackAndTakesItsKeysBack()
    {
        Keymap keymap = Keymap.For(KeymapPreset.Default);
        keymap.Bind(EditorAction.ToggleGrid, 0, K(Key.G));

        keymap.Reset(EditorAction.ToolMove);

        Assert.True(keymap.IsDefault(EditorAction.ToolMove));
        Assert.Equal(EditorAction.ToolMove, keymap.ActionFor(K(Key.G)));
        Assert.Empty(keymap.Duplicates());
    }

    /// <summary>Both of an action's keys come back — the second as much as the first.</summary>
    [Fact]
    public void ResetBringsBackTheSecondKeyToo()
    {
        Keymap keymap = Keymap.For(KeymapPreset.Default);
        keymap.Bind(EditorAction.Duplicate, 1, K(Key.K));

        keymap.Reset(EditorAction.Duplicate);

        Assert.Equal([KeyChord.Ctrl(Key.D), KeyChord.ShiftOf(Key.D)], keymap.Bindings(EditorAction.Duplicate));
        Assert.Null(keymap.ActionFor(K(Key.K)));
    }

    [Fact]
    public void AnUntouchedPresetHasNoChanges() =>
        Assert.Empty(Keymap.For(KeymapPreset.MimicBusters).Changes());

    [Fact]
    public void ChangesCarryOverToAKeymapBuiltFromThem()
    {
        Keymap keymap = Keymap.For(KeymapPreset.MimicBusters);
        keymap.Bind(EditorAction.ToolPaint, 0, K(Key.B));
        keymap.Bind(EditorAction.Subdivide, 1, KeyChord.CtrlAlt(Key.D));
        keymap.Bind(EditorAction.Hide, 0, null);

        Keymap rebuilt = Keymap.With(KeymapPreset.MimicBusters, keymap.Changes());

        foreach (ActionInfo info in EditorActions.All)
        {
            Assert.Equal(keymap.Bindings(info.Action), rebuilt.Bindings(info.Action));
        }

        Assert.Null(rebuilt.ActionFor(K(Key.H)));
        Assert.Null(rebuilt.ActionFor(K(Key.E)));
    }

    /// <summary>A file edited by hand, or from a later version: what cannot be read is skipped, the rest kept.</summary>
    [Fact]
    public void UnreadableSavedKeysAreSkipped()
    {
        var changes = new Dictionary<string, string[]>
        {
            ["no.such.action"] = ["K"],
            ["tool.paint"] = ["Ctrl+NotAKey", "B"],
        };

        Keymap keymap = Keymap.With(KeymapPreset.MimicBusters, changes);

        Assert.Equal([K(Key.B)], keymap.Bindings(EditorAction.ToolPaint));
    }

    [Fact]
    public void EveryActionHasADistinctSavedName()
    {
        Assert.Equal(EditorActions.All.Length, EditorActions.All.Select(a => a.Id).Distinct().Count());
        Assert.Equal(Enum.GetValues<EditorAction>().Length, EditorActions.All.Length);
    }
}

public class KeyChordTests
{
    [Theory]
    [InlineData(Key.Keypad1, false, false, false, "Numpad 1")]
    [InlineData(Key.KeypadDecimal, false, false, false, "Numpad .")]
    [InlineData(Key.Escape, false, false, false, "Esc")]
    [InlineData(Key.Z, true, true, false, "Ctrl+Shift+Z")]
    [InlineData(Key.S, true, false, true, "Ctrl+Alt+S")]
    [InlineData(Key.Number1, true, false, false, "Ctrl+1")]
    [InlineData(Key.PageDown, false, false, false, "Page Down")]
    public void AChordReadsTheWayItIsPressed(Key key, bool control, bool shift, bool alt, string shown) =>
        Assert.Equal(shown, new KeyChord(key, control, shift, alt).ToString());

    /// <summary>Every key there is, with every modifier, comes back from its saved form as it went in.</summary>
    [Fact]
    public void EveryChordSurvivesBeingSaved()
    {
        foreach (Key key in Enum.GetValues<Key>().Distinct())
        {
            if (KeyChord.IsModifier(key))
            {
                continue;
            }

            for (int mask = 0; mask < 8; mask++)
            {
                var chord = new KeyChord(key, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
                Assert.True(KeyChord.TryParse(chord.Storage, out KeyChord back), chord.Storage);
                Assert.Equal(chord, back);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+ControlLeft")]
    [InlineData("Hyper+K")]
    [InlineData("Banana")]
    public void NonsenseIsNotAChord(string text) => Assert.False(KeyChord.TryParse(text, out _));
}

/// <summary>Waiting for the key a shortcut is to be rebound to.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class KeyCaptureTests : IDisposable
{
    public void Dispose() => KeyCapture.Cancel();

    [Fact]
    public void NothingWaitingTakesNothing() => Assert.False(KeyCapture.Offer(new KeyChord(Key.K)));

    [Fact]
    public void AModifierAloneIsHeldBackAndTheChordDelivered()
    {
        KeyChord? got = null;
        KeyCapture.Begin("test", chord => got = chord);

        Assert.True(KeyCapture.Offer(new KeyChord(Key.ControlLeft, Control: true)));
        Assert.True(KeyCapture.IsWaiting);

        Assert.True(KeyCapture.Offer(KeyChord.Ctrl(Key.K)));
        Assert.Equal(KeyChord.Ctrl(Key.K), got);
        Assert.False(KeyCapture.IsWaiting);
    }

    [Fact]
    public void EscGivesUp()
    {
        bool delivered = false;
        KeyCapture.Begin("test", _ => delivered = true);

        Assert.True(KeyCapture.Offer(new KeyChord(Key.Escape)));

        Assert.False(delivered);
        Assert.False(KeyCapture.IsWaiting);
    }
}
