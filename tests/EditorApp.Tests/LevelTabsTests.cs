using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// Levels in tabs (Fullreleaseplan 7.8): what is opened goes in a tab of its own and leaves the level
/// in front as it was; a file open already is brought forward rather than opened twice; closing a
/// level asks about its unsaved work, as replacing it did; and the strip's tabs do what they say.
/// </summary>
[Collection(nameof(ImGuiCollection))]
public sealed class LevelTabsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tabs-{Guid.NewGuid():N}");

    public LevelTabsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        _ui.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static EditorSession Level(LevelTemplate template, string? path = null, bool unsaved = false)
    {
        var session = new EditorSession();
        session.ReplaceScene(LevelTemplates.Build(template), path);
        session.HasUnsavedChanges = unsaved;
        return session;
    }

    private string Saved(LevelTemplate template, string name)
    {
        string path = Path.Combine(_directory, name + VxLevelFile.Extension);
        VxLevelFile.Save(LevelTemplates.Build(template), path);
        return path;
    }

    // ---- Opening and closing ------------------------------------------------------------------------

    [Fact]
    public void OpeningALevelPutsItInATabOfItsOwnAndAsksNothing()
    {
        EditorSession front = Level(LevelTemplate.Cube, unsaved: true);
        var tab = new EditorSession();
        var project = new ProjectController(front, () => { }) { MakeRoom = () => tab, RemembersRecent = false };

        project.OpenRecent(Saved(LevelTemplate.Room, "castle"));

        Assert.False(project.IsAwaitingConfirmation);
        Assert.Equal(["Floor", "Walls"], tab.Scene.Objects.Select(o => o.Name));
        Assert.Equal(512, front.Scene.SolidCount);
        Assert.True(front.HasUnsavedChanges);
    }

    [Fact]
    public void ANewLevelGoesInATabOfItsOwn()
    {
        EditorSession front = Level(LevelTemplate.Cube, unsaved: true);
        var tab = new EditorSession();
        var project = new ProjectController(front, () => { }) { MakeRoom = () => tab, RemembersRecent = false };

        project.NewProject(LevelTemplate.Ground);

        Assert.False(project.IsAwaitingConfirmation);
        Assert.Equal(LevelTemplates.Build(LevelTemplate.Ground).ContentHash(), tab.Scene.ContentHash());
        Assert.Equal(512, front.Scene.SolidCount);
    }

    [Fact]
    public void AFileOpenAlreadyIsBroughtForwardNotOpenedAgain()
    {
        string path = Saved(LevelTemplate.Room, "castle");
        int tabsMade = 0;
        string? shown = null;
        var project = new ProjectController(Level(LevelTemplate.Cube), () => { })
        {
            MakeRoom = () =>
            {
                tabsMade++;
                return new EditorSession();
            },
            ShowOpen = open =>
            {
                shown = open;
                return true;
            },
            RemembersRecent = false,
        };

        project.OpenRecent(path);

        Assert.Equal(path, shown);
        Assert.Equal(0, tabsMade);
    }

    [Fact]
    public void AFileThatCannotBeReadMakesNoTab()
    {
        string path = Path.Combine(_directory, "broken" + VxLevelFile.Extension);
        File.WriteAllText(path, "not a level");
        int tabsMade = 0;
        var project = new ProjectController(Level(LevelTemplate.Cube), () => { })
        {
            MakeRoom = () =>
            {
                tabsMade++;
                return new EditorSession();
            },
            RemembersRecent = false,
        };

        project.OpenRecent(path);

        Assert.Equal(0, tabsMade);
    }

    [Fact]
    public void ClosingALevelWithUnsavedWorkAsksFirst()
    {
        var project = new ProjectController(Level(LevelTemplate.Cube, unsaved: true), () => { });
        bool closed = false;

        project.RequestClose(() => closed = true);
        Assert.True(project.IsAwaitingConfirmation);
        Assert.False(closed);

        project.ConfirmDiscard();
        Assert.True(closed);
    }

    [Fact]
    public void ALevelWithNothingUnsavedClosesAtOnce()
    {
        var project = new ProjectController(Level(LevelTemplate.Cube), () => { });
        bool closed = false;

        project.RequestClose(() => closed = true);

        Assert.True(closed);
        Assert.False(project.IsAwaitingConfirmation);
    }

    /// <summary>Save and the title follow the level in front as the host moves it.</summary>
    [Fact]
    public void SavingSavesTheLevelInFront()
    {
        string first = Path.Combine(_directory, "first" + VxLevelFile.Extension);
        string second = Path.Combine(_directory, "second" + VxLevelFile.Extension);
        var project = new ProjectController(Level(LevelTemplate.Cube, first, unsaved: true), () => { }) { RemembersRecent = false };
        EditorSession other = Level(LevelTemplate.Room, second, unsaved: true);

        project.Session = other;
        project.Save();

        Assert.True(File.Exists(second));
        Assert.False(File.Exists(first));
        Assert.False(other.HasUnsavedChanges);
        Assert.Equal("second - Blockage", project.WindowTitle);
    }

    [Fact]
    public void OnlyAnUntitledLevelNothingWasDoneToIsUntouched()
    {
        Assert.True(new LevelDocument(Level(LevelTemplate.Cube), slot: 0).IsUntouched);
        Assert.False(new LevelDocument(Level(LevelTemplate.Cube, unsaved: true), slot: 1).IsUntouched);
        Assert.False(new LevelDocument(Level(LevelTemplate.Cube, Path.Combine(_directory, "a.vxlevel")), slot: 2).IsUntouched);
    }

    [Fact]
    public void AViewComesBackAsItWasLeft()
    {
        var camera = new FlyCamera { Position = new Vector3(1f, 2f, 3f), Yaw = 0.5f, Pitch = -0.2f, PivotDistance = 7f, Orthographic = true };
        ViewPose pose = ViewPose.Of(camera);
        var other = new FlyCamera();

        pose.ApplyTo(other);

        Assert.Equal(pose, ViewPose.Of(other));
    }

    // ---- The strip ----------------------------------------------------------------------------------

    private readonly ImGuiHarness _ui = new();
    private readonly List<string> _done = [];

    private LevelTabsContext Strip(IReadOnlyList<LevelDocument> levels, LevelDocument active) => new()
    {
        Levels = levels,
        Active = active,
        Show = level => _done.Add($"show {level.Slot}"),
        Close = level => _done.Add($"close {level.Slot}"),
        New = () => _done.Add("new"),
    };

    private static Vector2 Middle((Vector2 Min, Vector2 Max)? rect) => (rect!.Value.Min + rect.Value.Max) * 0.5f;

    /// <summary>A few frames with the pointer away, for the strip to lay its tabs out and pick the one in front.</summary>
    private void Settle(Action draw)
    {
        for (int i = 0; i < 3; i++)
        {
            _ui.Frame(draw, mouse: new Vector2(-1f, -1f), inWindow: false);
        }
    }

    private (LevelDocument First, LevelDocument Second) TwoTabs() =>
        (new LevelDocument(Level(LevelTemplate.Cube, Path.Combine(_directory, "castle.vxlevel")), slot: 900),
         new LevelDocument(Level(LevelTemplate.Room, Path.Combine(_directory, "cave.vxlevel")), slot: 901));

    [Fact]
    public void ClickingATabBringsItsLevelForward()
    {
        (LevelDocument first, LevelDocument second) = TwoTabs();
        LevelDocument active = first;
        void Draw() => LevelTabs.Draw(Vector2.Zero, 1280f, new LevelTabsContext
        {
            Levels = [first, second],
            Active = active,
            Show = level =>
            {
                _done.Add($"show {level.Slot}");
                active = level;
            },
            Close = level => _done.Add($"close {level.Slot}"),
            New = () => _done.Add("new"),
        });
        Settle(Draw);

        _ui.Click(Middle(LevelTabs.TabRect(second)), Draw, inWindow: false);
        _ui.Frame(Draw, inWindow: false);

        Assert.Equal(["show 901"], _done);
    }

    /// <summary>A level brought forward by a key or by opening it takes its tab with it, and the old tab does not pull it back.</summary>
    [Fact]
    public void ALevelBroughtForwardOtherwiseTakesItsTab()
    {
        (LevelDocument first, LevelDocument second) = TwoTabs();
        LevelDocument active = first;
        void Draw() => LevelTabs.Draw(Vector2.Zero, 1280f, Strip([first, second], active));
        Settle(Draw);

        active = second;
        for (int i = 0; i < 4; i++)
        {
            _ui.Frame(Draw, inWindow: false);
        }

        Assert.Empty(_done);
    }

    [Fact]
    public void AMiddleClickClosesATab()
    {
        (LevelDocument first, LevelDocument second) = TwoTabs();
        void Draw() => LevelTabs.Draw(Vector2.Zero, 1280f, Strip([first, second], first));
        Settle(Draw);
        _ui.Frame(Draw, mouse: Middle(LevelTabs.TabRect(second)), inWindow: false);

        ImGui.GetIO().AddMouseButtonEvent(2, true);
        _ui.Frame(Draw, inWindow: false);
        ImGui.GetIO().AddMouseButtonEvent(2, false);
        _ui.Frame(Draw, inWindow: false);

        Assert.Equal(["close 901"], _done);
    }

    [Fact]
    public void ThePlusStartsANewLevel()
    {
        (LevelDocument first, LevelDocument second) = TwoTabs();
        void Draw() => LevelTabs.Draw(Vector2.Zero, 1280f, Strip([first, second], first));
        Settle(Draw);

        _ui.Click(Middle(LevelTabs.NewRect), Draw, inWindow: false);

        Assert.Equal(["new"], _done);
    }
}
