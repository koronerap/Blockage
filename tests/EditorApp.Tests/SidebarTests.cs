using System.Numerics;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// The sidebar's handles and tabs, driven through the headless ImGui: what a click on a tab does,
/// how far a drag moves the divider and the edge, and where each stops.
/// </summary>
[Collection(nameof(ImGuiCollection))]
public sealed class SidebarTests : IDisposable
{
    private const float Top = 60f;
    private const float Bottom = 700f;
    private const float Screen = 1280f;

    private readonly ImGuiHarness _ui = new();
    private readonly LayoutSettings _layout = new();
    private readonly Sidebar _sidebar;

    private Vector2 _outlinerSize;
    private PropertiesTab? _drawnTab;
    private float _left;

    public SidebarTests()
    {
        _sidebar = new Sidebar(_layout);
        _ui.Frame(Draw, new Vector2(-100f, -100f), inWindow: false);
        _ui.Frame(Draw, new Vector2(-100f, -100f), inWindow: false);
    }

    public void Dispose() => _ui.Dispose();

    private void Draw() => _left = _sidebar.Draw(Top, Bottom, Screen, new SidebarContent
    {
        Outliner = size => _outlinerSize = size,
        Properties = tab => _drawnTab = tab,
        Tab = tab => (Icons.ObjectTab, tab.ToString()),
    });

    private float DividerY => Top + _layout.OutlinerHeightWithin(Bottom - Top - Sidebar.DividerThickness) + (Sidebar.DividerThickness * 0.5f);

    /// <summary>The middle of the n-th tab button down the strip.</summary>
    private Vector2 TabButton(int index)
    {
        float stripTop = DividerY + (Sidebar.DividerThickness * 0.5f);
        return new Vector2(
            _left + (Sidebar.TabStripWidth * 0.5f),
            stripTop + 6f + (index * (Sidebar.TabButtonSize + 4f)) + (Sidebar.TabButtonSize * 0.5f));
    }

    [Fact]
    public void TheColumnSitsAgainstTheRightEdgeAtItsWidth() =>
        Assert.Equal(Screen - LayoutSettings.DefaultSidebarWidth, _left);

    [Fact]
    public void ClickingATabOpensIt()
    {
        _ui.Click(TabButton(2), Draw, inWindow: false);

        Assert.Equal(PropertiesTab.Palette, _layout.Tab);
        _ui.Frame(Draw, inWindow: false);
        Assert.Equal(PropertiesTab.Palette, _drawnTab);
    }

    /// <summary>
    /// The edge's grip is eight pixels, half over the column. ImGui widens a window to 32 pixels unless
    /// told otherwise, and a grip that wide would sit over the tab buttons and take their clicks.
    /// </summary>
    [Fact]
    public void TheEdgeGripLeavesTheTabButtonsClickable()
    {
        _layout.Tab = PropertiesTab.Reference;
        _ui.Frame(Draw, inWindow: false);

        _ui.Click(new Vector2(_left + Sidebar.EdgeGrip, TabButton(0).Y), Draw, inWindow: false);

        Assert.Equal(PropertiesTab.Object, _layout.Tab);
        Assert.Equal(LayoutSettings.DefaultSidebarWidth, _layout.SidebarWidth);
    }

    [Fact]
    public void DraggingTheDividerMovesItWithTheCursor()
    {
        float before = _layout.OutlinerHeight;
        var grab = new Vector2(_left + 150f, DividerY);

        _ui.Drag(grab, grab + new Vector2(0f, 60f), Draw, inWindow: false);

        Assert.Equal(before + 60f, _layout.OutlinerHeight, 1);
    }

    /// <summary>However far it is dragged, the Properties keep their minimum and the Outliner its own.</summary>
    [Fact]
    public void TheDividerStopsShortOfEitherEnd()
    {
        var grab = new Vector2(_left + 150f, DividerY);
        _ui.Drag(grab, grab + new Vector2(0f, 2000f), Draw, inWindow: false);
        _ui.Frame(Draw, inWindow: false);

        float column = Bottom - Top - Sidebar.DividerThickness;
        Assert.Equal(column - LayoutSettings.MinPropertiesHeight, _layout.OutlinerHeight, 1);

        grab = new Vector2(_left + 150f, DividerY);
        _ui.Drag(grab, grab - new Vector2(0f, 2000f), Draw, inWindow: false);

        Assert.Equal(LayoutSettings.MinOutlinerHeight, _layout.OutlinerHeight, 1);
    }

    [Fact]
    public void DraggingTheEdgeLeftWidensTheColumn()
    {
        var grab = new Vector2(_left, 300f);

        _ui.Drag(grab, grab - new Vector2(80f, 0f), Draw, inWindow: false);
        _ui.Frame(Draw, inWindow: false);

        Assert.Equal(LayoutSettings.DefaultSidebarWidth + 80f, _layout.SidebarWidth, 1);
        Assert.Equal(Screen - _layout.SidebarWidth, _left, 1);
    }

    [Fact]
    public void TheEdgeStopsAtTheWidestAndNarrowestAllowed()
    {
        var grab = new Vector2(_left, 300f);
        _ui.Drag(grab, grab - new Vector2(900f, 0f), Draw, inWindow: false);
        Assert.Equal(LayoutSettings.MaxSidebarWidth, _layout.SidebarWidth);

        _ui.Frame(Draw, inWindow: false);
        grab = new Vector2(_left, 300f);
        _ui.Drag(grab, grab + new Vector2(900f, 0f), Draw, inWindow: false);
        Assert.Equal(LayoutSettings.MinSidebarWidth, _layout.SidebarWidth);
    }

    /// <summary>The Outliner is handed the space above the divider, less its padding.</summary>
    [Fact]
    public void TheOutlinerIsGivenItsArea()
    {
        _ui.Frame(Draw, inWindow: false);

        Assert.Equal(LayoutSettings.DefaultOutlinerHeight - 12f, _outlinerSize.Y, 1);
        Assert.Equal(LayoutSettings.DefaultSidebarWidth - 16f, _outlinerSize.X, 1);
    }

    /// <summary>A narrow window keeps room for the model, whatever width was saved.</summary>
    [Fact]
    public void ANarrowWindowKeepsTheViewport()
    {
        _layout.SidebarWidth = LayoutSettings.MaxSidebarWidth;

        _left = _sidebar.Draw(Top, Bottom, 800f, new SidebarContent
        {
            Outliner = _ => { },
            Properties = _ => { },
            Tab = tab => (Icons.ObjectTab, tab.ToString()),
        });

        Assert.True(_left >= 320f);
    }
}

public sealed class LayoutSettingsTests : IDisposable
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
    public void TheLayoutComesBackAsItWasLeft()
    {
        var layout = new LayoutSettings
        {
            SidebarWidth = 420f,
            OutlinerHeight = 260f,
            Tab = PropertiesTab.Reference,
            StatisticsVisible = true,
        };

        layout.Save(_path);
        LayoutSettings loaded = LayoutSettings.Load(_path);

        Assert.Equal(420f, loaded.SidebarWidth);
        Assert.Equal(260f, loaded.OutlinerHeight);
        Assert.Equal(PropertiesTab.Reference, loaded.Tab);
        Assert.True(loaded.StatisticsVisible);
    }

    [Fact]
    public void NoFileIsTheDefaultLayout()
    {
        LayoutSettings loaded = LayoutSettings.Load(_path);

        Assert.Equal(LayoutSettings.DefaultSidebarWidth, loaded.SidebarWidth);
        Assert.Equal(PropertiesTab.Object, loaded.Tab);
    }

    /// <summary>A file edited by hand, or broken, must not leave the editor with no room for the model.</summary>
    [Fact]
    public void AnUnreadableOrOutlandishFileIsTamed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        File.WriteAllText(_path, "{ not json");
        Assert.Equal(LayoutSettings.DefaultSidebarWidth, LayoutSettings.Load(_path).SidebarWidth);

        File.WriteAllText(_path, """{ "SidebarWidth": 99999, "OutlinerHeight": -5, "Tab": "Object", "StatisticsVisible": false }""");
        LayoutSettings loaded = LayoutSettings.Load(_path);
        Assert.Equal(LayoutSettings.MaxSidebarWidth, loaded.SidebarWidth);
        Assert.Equal(LayoutSettings.MinOutlinerHeight, loaded.OutlinerHeight);
    }

    [Fact]
    public void TheOutlinerNeverTakesThePropertiesMinimum()
    {
        var layout = new LayoutSettings { OutlinerHeight = 5000f };

        Assert.Equal(600f - LayoutSettings.MinPropertiesHeight, layout.OutlinerHeightWithin(600f));
        Assert.Equal(LayoutSettings.MinOutlinerHeight, layout.OutlinerHeightWithin(100f));
    }
}
