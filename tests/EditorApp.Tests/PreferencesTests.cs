using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Input;
using EditorApp.Rendering;
using EditorApp.Ui;
using Silk.NET.Input;

namespace EditorApp.Tests;

/// <summary>The preferences file: what goes in comes out, and what should not go in does not.</summary>
public sealed class PreferencesFileTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "editorapp-preferences-tests", Guid.NewGuid().ToString("N"), "preferences.json");

    public void Dispose()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WhatIsSavedIsWhatComesBack()
    {
        var saved = new Preferences
        {
            Theme = ThemeKind.Light,
            Accent = AccentKind.Rose,
            TextSize = TextSize.Large,
            MouseHints = false,
            FieldOfView = 75f,
            LineWidth = 1.5f,
            GizmoSize = 0.75f,
            VSync = false,
            ShowGrid = false,
            OrbitSpeed = 2f,
            FlySpeed = 0.5f,
            InvertZoom = true,
            RotationStep = 45f,
            UndoMemory = 32,
            AutosaveMinutes = 5,
            RecentFilesKept = 20,
        };

        Keymap keymap = Keymap.For(KeymapPreset.MimicBusters);
        keymap.Bind(EditorAction.ToolPaint, 0, new KeyChord(Key.B));
        saved.Remember(keymap);
        saved.Save(_path);

        Preferences loaded = Preferences.Load(_path);

        Assert.Equal(ThemeKind.Light, loaded.Theme);
        Assert.Equal(AccentKind.Rose, loaded.Accent);
        Assert.Equal(TextSize.Large, loaded.TextSize);
        Assert.False(loaded.MouseHints);
        Assert.Equal(75f, loaded.FieldOfView);
        Assert.Equal(1.5f, loaded.LineWidth);
        Assert.Equal(0.75f, loaded.GizmoSize);
        Assert.False(loaded.VSync);
        Assert.False(loaded.ShowGrid);
        Assert.Equal(2f, loaded.OrbitSpeed);
        Assert.Equal(0.5f, loaded.FlySpeed);
        Assert.True(loaded.InvertZoom);
        Assert.Equal(45f, loaded.RotationStep);
        Assert.Equal(32, loaded.UndoMemory);
        Assert.Equal(5, loaded.AutosaveMinutes);
        Assert.Equal(20, loaded.RecentFilesKept);

        Keymap rebuilt = loaded.BuildKeymap();
        Assert.Equal(KeymapPreset.MimicBusters, rebuilt.Preset);
        Assert.Equal(EditorAction.ToolPaint, rebuilt.ActionFor(new KeyChord(Key.B)));
        Assert.Null(rebuilt.ActionFor(new KeyChord(Key.E)));
    }

    [Fact]
    public void ValuesOutOfRangeAreKeptInIt()
    {
        var preferences = new Preferences
        {
            FieldOfView = 500f,
            LineWidth = float.NaN,
            GizmoSize = 0.01f,
            RotationStep = 7f,
            UndoMemory = 3,
            AutosaveMinutes = 3,
            RecentFilesKept = 99,
        };

        Assert.Equal(Preferences.MaxFieldOfView, preferences.FieldOfView);
        Assert.Equal(1f, preferences.LineWidth);
        Assert.Equal(0.5f, preferences.GizmoSize);
        Assert.Equal(15f, preferences.RotationStep);
        Assert.Equal(8, preferences.UndoMemory);
        Assert.Equal(2, preferences.AutosaveMinutes);
        Assert.Equal(RecentFiles.MaxCapacity, preferences.RecentFilesKept);
    }

    [Fact]
    public void AFileThatCannotBeReadGivesTheDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not json");

        Preferences loaded = Preferences.Load(_path);

        Assert.Equal(KeymapPreset.Default, loaded.KeymapPreset);
        Assert.Equal(ThemeKind.Dark, loaded.Theme);
    }

    /// <summary>A number that names no theme — a file edited by hand — is the default rather than a crash later.</summary>
    [Fact]
    public void AnUnknownThemeIsTheDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, """{ "Theme": 9, "Accent": 42, "FieldOfView": 70 }""");

        Preferences loaded = Preferences.Load(_path);

        Assert.Equal(ThemeKind.Dark, loaded.Theme);
        Assert.Equal(AccentKind.Blue, loaded.Accent);
        Assert.Equal(70f, loaded.FieldOfView);
    }

    [Fact]
    public void WithNoFileTheKeysAreTheDefaultPreset() =>
        Assert.Equal(KeymapPreset.Default, Preferences.Load(_path).BuildKeymap().Preset);
}

/// <summary>The Preferences window, driven through the headless ImGui.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class PreferencesWindowTests : IDisposable
{
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(1000f, 700f) };
    private readonly Preferences _preferences = new();
    private readonly Keymap _before = Keymap.Active;
    private int _applied;

    public PreferencesWindowTests()
    {
        Keymap.Active = _preferences.BuildKeymap();
        PreferencesWindow.Open();
    }

    public void Dispose()
    {
        PreferencesWindow.Close();
        PreferencesWindow.Current = PreferencesWindow.Page.Interface;
        Keymap.Active = _before;
        _ui.Dispose();
    }

    private void Draw() => PreferencesWindow.Draw(_preferences, () => _applied++, () => { });

    private static Vector2 Centre((Vector2 Min, Vector2 Max)? rect)
    {
        Assert.True(rect.HasValue, "not drawn");
        return (rect.Value.Min + rect.Value.Max) * 0.5f;
    }

    private void Settle()
    {
        _ui.Frame(Draw, inWindow: false);
        _ui.Frame(Draw, inWindow: false);
    }

    [Fact]
    public void ChoosingLightChangesTheThemeAndAppliesIt()
    {
        PreferencesWindow.Current = PreferencesWindow.Page.Interface;
        Settle();

        _ui.Click(Centre(Props.RectOf("pref-theme-2")), Draw, inWindow: false);

        Assert.Equal(ThemeKind.Light, _preferences.Theme);
        Assert.True(_applied > 0);
    }

    [Fact]
    public void AKeyIsReboundByClickingItAndPressingTheNewOne()
    {
        PreferencesWindow.Current = PreferencesWindow.Page.Keymap;
        Settle();

        _ui.Click(Centre(PreferencesWindow.KeyRect(EditorAction.ToolExtrude, 0)), Draw, inWindow: false);
        Assert.True(KeyCapture.IsWaiting);

        // The key arrives from the key handler, between frames.
        KeyCapture.Offer(new KeyChord(Key.K));
        _ui.Frame(Draw, inWindow: false);

        Assert.Equal(EditorAction.ToolExtrude, Keymap.Active.ActionFor(new KeyChord(Key.K)));
        Assert.Null(Keymap.Active.ActionFor(new KeyChord(Key.E)));
        Assert.True(_preferences.KeymapChanges.ContainsKey("tool.extrude"));
        Assert.True(PreferencesWindow.PendingApply);
        PreferencesWindow.PendingApply = false;
    }

    [Fact]
    public void RightClickClearsAKey()
    {
        PreferencesWindow.Current = PreferencesWindow.Page.Keymap;
        Settle();

        _ui.RightClick(Centre(PreferencesWindow.KeyRect(EditorAction.ToolPaint, 0)), Draw, inWindow: false);

        Assert.Empty(Keymap.Active.Bindings(EditorAction.ToolPaint));
        Assert.True(_preferences.KeymapChanges.ContainsKey("tool.paint"));
    }

    [Fact]
    public void RestoreDefaultsPutsAPageBack()
    {
        _preferences.FieldOfView = 90f;
        _preferences.LineWidth = 2f;
        PreferencesWindow.Current = PreferencesWindow.Page.Viewport;
        Settle();

        _ui.Click(Centre(Props.RectOf("pref-restore-Viewport-0")), Draw, inWindow: false);

        Assert.Equal(60f, _preferences.FieldOfView);
        Assert.Equal(1f, _preferences.LineWidth);
    }
}

/// <summary>What the preferences change in the parts they reach.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class PreferenceEffectTests : IDisposable
{
    private readonly ImGuiHarness _ui = new();

    public void Dispose()
    {
        Theme.Apply(ThemeKind.Dark, AccentKind.Blue);
        _ui.Dispose();
    }

    private static float Luminance(Vector4 colour) => (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z);

    [Fact]
    public void TheLightThemePutsDarkTextOnLightPanels()
    {
        Theme.Apply(ThemeKind.Light, AccentKind.Rose);

        Assert.True(Luminance(Theme.Text) < 0.2f);
        Assert.True(Luminance(Theme.Surface) > 0.6f);
        Assert.Equal(Theme.AccentOf(AccentKind.Rose), Theme.Accent);

        // And ImGui draws with it.
        Assert.Equal(Theme.Text, ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiNET.ImGuiCol.Text]);
    }

    [Fact]
    public void BackToDarkPutsItBack()
    {
        Theme.Apply(ThemeKind.Light, AccentKind.Green);
        Theme.Apply(ThemeKind.Dark, AccentKind.Blue);

        Assert.True(Luminance(Theme.Text) > 0.7f);
        Assert.True(Luminance(Theme.Surface) < 0.2f);
    }

    /// <summary>Text on the accent stays light in every theme: every accent is a mid tone.</summary>
    [Theory]
    [InlineData(ThemeKind.Dark)]
    [InlineData(ThemeKind.Darker)]
    [InlineData(ThemeKind.Light)]
    public void TextOnTheAccentIsLight(ThemeKind kind)
    {
        Theme.Apply(kind, AccentKind.Blue);

        Assert.True(Luminance(Theme.TextOnAccent) > 0.8f);
    }
}

public class GizmoSizeTests
{
    [Fact]
    public void TheGizmoGrowsWithItsSetting()
    {
        var session = new EditorSession();
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        var camera = new FlyCamera { Position = new Vector3(20f, 20f, 20f) };
        camera.LookAt(Vector3.Zero);
        var transform = new TransformInteraction(session);

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        float designed = Vector3.Distance(start, end);

        transform.SizeScale = 2f;
        (start, end) = transform.Segment(arrow, camera);

        Assert.Equal(designed * 2f, Vector3.Distance(start, end), 3);
    }
}
