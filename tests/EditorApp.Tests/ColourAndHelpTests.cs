using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>The colour under the tool column, the quick palette it opens, and the shortcut sheet.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class ColourAndHelpTests : IDisposable
{
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(600f, 680f) };
    private readonly EditorSession _session = new();
    private readonly PalettePanel _palette = new();

    public ColourAndHelpTests() => _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);

    public void Dispose()
    {
        ShortcutSheet.Close();
        _ui.Dispose();
    }

    private static Vector2 Centre((Vector2 Min, Vector2 Max) rect) => (rect.Min + rect.Max) * 0.5f;

    [Fact]
    public void PickingFromTheQuickPaletteChoosesTheColourAndSaysSo()
    {
        bool picked = false;
        void Draw() => picked |= _palette.DrawQuickPalette(_session);

        _ui.Frame(Draw);
        _ui.Frame(Draw);
        _ui.Click(Centre(PalettePanel.QuickSwatchRect(40)!.Value), Draw);

        Assert.Equal(40, _session.ActiveColorIndex);
        Assert.True(picked);
    }

    /// <summary>The wheel sets any colour at all, and the popover stays open while it is dragged about.</summary>
    [Fact]
    public void TheQuickWheelChoosesAnyColourWithoutClosing()
    {
        bool picked = false;
        void Draw() => picked |= _palette.DrawQuickPalette(_session);

        Core.Voxels.Color32 before = _session.Scene.Palette[_session.ActiveColorIndex];

        _ui.Frame(Draw);
        _ui.Frame(Draw);

        // The middle of the ring is the middle of the triangle inside it.
        (Vector2 min, Vector2 max) = PalettePanel.QuickPickerRect;
        float side = max.X - min.X;
        _ui.Click(min + new Vector2(side * 0.5f), Draw);

        Assert.NotEqual(before, _session.Scene.Palette[_session.ActiveColorIndex]);
        Assert.True(Core.Voxels.Palette.IsCustomIndex(_session.ActiveColorIndex));
        Assert.False(picked);
    }

    [Fact]
    public void TheColourUnderTheToolsOpensThePalette()
    {
        bool open = false;
        void Draw()
        {
            ToolColumn.Draw(_session, _palette);
            open = ImGui.IsPopupOpen("##quick-palette");
        }

        _ui.Frame(Draw);
        _ui.Frame(Draw);
        _ui.Click(Centre(ToolColumn.ColourButtonRect), Draw);

        Assert.True(open);
    }

    [Fact]
    public void TheShortcutSheetOpensAndClosesWithAClickAway()
    {
        void Draw() => ShortcutSheet.Draw(new Vector2(1280f, 720f));

        ShortcutSheet.Toggle();
        _ui.Frame(Draw, inWindow: false);
        _ui.Frame(Draw, inWindow: false);
        Assert.True(ShortcutSheet.IsOpen);

        _ui.Click(new Vector2(5f, 5f), Draw, inWindow: false);
        Assert.False(ShortcutSheet.IsOpen);
    }

    /// <summary>Every tool key and every camera move is written down somewhere on the sheet.</summary>
    [Theory]
    [InlineData("Q")]
    [InlineData("W")]
    [InlineData("E")]
    [InlineData("R")]
    [InlineData("Shift+D")]
    [InlineData("Middle drag")]
    [InlineData("Numpad 5")]
    [InlineData("N")]
    [InlineData("F1")]
    public void TheSheetListsTheKey(string keys) =>
        Assert.Contains(ShortcutSheet.Groups, group => group.Entries.Any(entry => entry.Keys == keys));
}

public sealed class SidebarVisibilityTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "editorapp-layout-tests", Guid.NewGuid().ToString("N"), "layout.json");

    public void Dispose()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AHiddenSidebarStaysHidden()
    {
        new LayoutSettings { SidebarVisible = false }.Save(_path);

        Assert.False(LayoutSettings.Load(_path).SidebarVisible);
    }

    /// <summary>A layout saved before the sidebar could be hidden shows it.</summary>
    [Fact]
    public void AnOlderLayoutShowsTheSidebar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, """{ "SidebarWidth": 400, "OutlinerHeight": 200, "Tab": "Object", "StatisticsVisible": false }""");

        Assert.True(LayoutSettings.Load(_path).SidebarVisible);
    }
}
