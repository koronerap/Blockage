using System.Numerics;
using System.Text.RegularExpressions;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>The welcome screen, clicked through: each entry does what it says, and then it goes.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class WelcomeScreenTests : IDisposable
{
    private readonly ImGuiHarness _ui = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"welcome-{Guid.NewGuid():N}");
    private readonly List<string> _done = [];
    private readonly List<string> _recent = [];
    private readonly Preferences _preferences = new();
    private LastSessionInfo? _last;
    private bool _autosaved;
    private readonly WelcomeActions _actions;

    public WelcomeScreenTests()
    {
        Directory.CreateDirectory(_directory);
        _actions = new WelcomeActions
        {
            New = template => _done.Add($"new {template}"),
            OpenSample = sample => _done.Add($"sample {sample}"),
            StartTour = () => _done.Add("tour"),
            Open = () => _done.Add("open"),
            Recent = () => _recent,
            OpenRecent = path => _done.Add($"recent {Path.GetFileName(path)}"),
            LastSession = () => _last,
            RecoverLastSession = () => _done.Add("recover last"),
            CanRecoverAutoSave = () => _autosaved,
            RecoverAutoSave = () => _done.Add("recover autosave"),
            OpenUrl = url => _done.Add($"url {url}"),
            ShowShortcuts = () => _done.Add("shortcuts"),
            Preferences = _preferences,
        };
    }

    public void Dispose()
    {
        _ui.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private void Draw() => WelcomeScreen.Draw(_actions);

    private void Open()
    {
        WelcomeScreen.Open();
        _ui.Frame(Draw);
        _ui.Frame(Draw);
    }

    private void Click(string id)
    {
        (Vector2 Min, Vector2 Max)? rect = WelcomeScreen.ItemRect(id);
        Assert.True(rect.HasValue, $"{id} is not on the welcome screen");
        _ui.Click((rect.Value.Min + rect.Value.Max) * 0.5f, Draw);
    }

    private string Level(string name)
    {
        string path = Path.Combine(_directory, name + VxLevelFile.Extension);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Theory]
    [InlineData(LevelTemplate.Cube)]
    [InlineData(LevelTemplate.Ground)]
    [InlineData(LevelTemplate.Room)]
    [InlineData(LevelTemplate.Voxel)]
    public void EachTemplateStartsANewLevelAndCloses(LevelTemplate template)
    {
        Open();

        Click($"new-{template}".ToLowerInvariant());
        _ui.Frame(Draw);

        Assert.Equal([$"new {template}"], _done);
        Assert.False(WelcomeScreen.IsOpen);
    }

    [Fact]
    public void OpenOpens()
    {
        Open();
        Click("open");

        Assert.Equal(["open"], _done);
    }

    [Fact]
    public void ARecentFileOpensItAndAMissingOneIsGreyed()
    {
        _recent.Add(Level("castle"));
        _recent.Add(Path.Combine(_directory, "gone" + VxLevelFile.Extension));
        Open();

        Click($"recent:{_recent[1]}");
        Assert.Empty(_done);
        Assert.True(WelcomeScreen.IsOpen);

        Click($"recent:{_recent[0]}");
        Assert.Equal(["recent castle.vxlevel"], _done);
    }

    [Fact]
    public void OnlyTheNewestRecentFilesAreListed()
    {
        for (int i = 0; i < WelcomeScreen.RecentShown + 3; i++)
        {
            _recent.Add(Level($"level{i}"));
        }

        Open();

        Assert.NotNull(WelcomeScreen.ItemRect($"recent:{_recent[WelcomeScreen.RecentShown - 1]}"));
        Assert.Null(WelcomeScreen.ItemRect($"recent:{_recent[WelcomeScreen.RecentShown]}"));
    }

    [Fact]
    public void TheLastSessionIsGreyedUntilOneIsKept()
    {
        Open();
        Click("recover-last");
        Assert.Empty(_done);

        _last = new LastSessionInfo("x.vxlevel", "castle", null, HadUnsavedChanges: true, DateTime.UtcNow);
        Click("recover-last");
        Assert.Equal(["recover last"], _done);
    }

    [Fact]
    public void AnAutosaveIsOfferedOnlyWhenACrashLeftOne()
    {
        Open();
        Assert.Null(WelcomeScreen.ItemRect("recover-autosave"));

        // One more line: the screen grows and centres itself again over a frame or two.
        _autosaved = true;
        _ui.Frame(Draw);
        _ui.Frame(Draw);
        _ui.Frame(Draw);
        Click("recover-autosave");

        Assert.Equal(["recover autosave"], _done);
    }

    [Fact]
    public void GitHubGoesToTheRepository()
    {
        Open();
        Click("github");

        Assert.Equal([$"url {Links.Repository}"], _done);
        Assert.StartsWith("https://github.com/", Links.Repository, StringComparison.Ordinal);
    }

    [Fact]
    public void TheManualIsALinkAway()
    {
        Open();
        Click("manual");

        Assert.Equal([$"url {Links.Manual}"], _done);
    }

    [Theory]
    [InlineData("sample-island", "sample Island")]
    [InlineData("sample-village", "sample Village")]
    [InlineData("sample-cave", "sample Cave")]
    public void EverySampleOpensFromIt(string id, string done)
    {
        Open();
        Click(id);

        Assert.Equal([done], _done);
    }

    [Fact]
    public void TheTourStartsFromIt()
    {
        Open();
        Click("tour");

        Assert.Equal(["tour"], _done);
    }

    [Fact]
    public void TheKeysAreOneClickAway()
    {
        Open();
        Click("shortcuts");

        Assert.Equal(["shortcuts"], _done);
    }

    /// <summary>The box is the preference itself, and leaves the screen up to be looked at.</summary>
    [Fact]
    public void ShowAtStartupIsThePreference()
    {
        Assert.True(_preferences.ShowWelcome);
        Open();

        Click("show-at-startup");

        Assert.False(_preferences.ShowWelcome);
        Assert.True(WelcomeScreen.IsOpen);
    }

    [Fact]
    public void EscPutsItAway()
    {
        Open();

        _ui.Press(ImGuiKey.Escape, Draw);

        Assert.False(WelcomeScreen.IsOpen);
        Assert.Empty(_done);
    }

    [Fact]
    public void AClickOutsidePutsItAway()
    {
        Open();

        _ui.Click(new Vector2(4f, 4f), Draw);

        Assert.False(WelcomeScreen.IsOpen);
        Assert.Empty(_done);
    }

    /// <summary>A key pressed for something else puts it away, as the editor does when one is.</summary>
    [Fact]
    public void DismissPutsItAway()
    {
        Open();

        WelcomeScreen.Dismiss();
        _ui.Frame(Draw);
        _ui.Frame(Draw);

        Assert.False(WelcomeScreen.IsOpen);
    }

    [Fact]
    public void TheVersionIsTheProjectsOwn()
    {
        // A milestone's number, "-dev" after it while the next is under way.
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(-dev)?$"), AppVersion.Number);
        Assert.Equal($"v{AppVersion.Number}", AppVersion.Label);
        Assert.NotEqual("0.0.0", AppVersion.Number);
    }
}

/// <summary>Blender's quit.blend: what the editor closed on, kept and had back.</summary>
public sealed class LastSessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"last-session-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static EditorSession Session(LevelTemplate template, string? path, bool unsaved)
    {
        var session = new EditorSession();
        session.ReplaceScene(LevelTemplates.Build(template), path);
        session.HasUnsavedChanges = unsaved;
        return session;
    }

    [Fact]
    public void NothingKeptIsNothingToRead() => Assert.Null(new LastSession(_directory).Read());

    [Fact]
    public void WhatIsKeptIsReadBack()
    {
        var last = new LastSession(_directory);
        last.Write(Session(LevelTemplate.Room, @"C:\levels\castle.vxlevel", unsaved: true));

        LastSessionInfo info = last.Read()!;

        Assert.Equal("castle", info.ProjectName);
        Assert.Equal(@"C:\levels\castle.vxlevel", info.ProjectPath);
        Assert.True(info.HadUnsavedChanges);
        Assert.Equal(2, VxLevelFile.LoadScene(info.LevelPath).Objects.Count);
    }

    [Fact]
    public void KeepingAgainReplacesIt()
    {
        var last = new LastSession(_directory);
        last.Write(Session(LevelTemplate.Room, null, unsaved: true));
        last.Write(Session(LevelTemplate.Voxel, null, unsaved: false));

        LastSessionInfo info = last.Read()!;

        Assert.False(info.HadUnsavedChanges);
        Assert.Equal(1, VxLevelFile.LoadScene(info.LevelPath).SolidCount);
        Assert.False(File.Exists(info.LevelPath + ".tmp"));
    }

    /// <summary>The level is what matters: without its note it comes back as untitled, unsaved work.</summary>
    [Fact]
    public void WithoutItsNoteTheLevelIsStillThere()
    {
        var last = new LastSession(_directory);
        last.Write(Session(LevelTemplate.Ground, @"C:\levels\castle.vxlevel", unsaved: false));
        File.Delete(Path.Combine(_directory, "last-session.json"));

        LastSessionInfo info = last.Read()!;

        Assert.Equal("Untitled", info.ProjectName);
        Assert.Null(info.ProjectPath);
        Assert.True(info.HadUnsavedChanges);
    }

    [Fact]
    public void RecoveringOpensItWhereItWasAndAsItWas()
    {
        var last = new LastSession(_directory);
        last.Write(Session(LevelTemplate.Room, @"C:\levels\castle.vxlevel", unsaved: true));

        EditorSession session = Session(LevelTemplate.Cube, null, unsaved: false);
        int replaced = 0;
        var project = new ProjectController(session, () => replaced++) { LastSession = last, RemembersRecent = false };
        Assert.NotNull(project.LastSessionFound);

        project.RecoverLastSession();

        Assert.Equal(["Floor", "Walls"], session.Scene.Objects.Select(o => o.Name));
        Assert.Equal(@"C:\levels\castle.vxlevel", session.ProjectPath);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(1, replaced);
    }

    /// <summary>Recovering over unsaved work asks first, as anything replacing the level does.</summary>
    [Fact]
    public void RecoveringOverUnsavedWorkAsksFirst()
    {
        var last = new LastSession(_directory);
        last.Write(Session(LevelTemplate.Voxel, null, unsaved: false));

        EditorSession session = Session(LevelTemplate.Cube, null, unsaved: true);
        var project = new ProjectController(session, () => { }) { LastSession = last, RemembersRecent = false };

        project.RecoverLastSession();
        Assert.True(project.IsAwaitingConfirmation);
        Assert.Equal(512, session.Scene.SolidCount);

        project.ConfirmDiscard();
        Assert.Equal(1, session.Scene.SolidCount);
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public void WithNothingKeptThereIsNothingToRecover()
    {
        EditorSession session = Session(LevelTemplate.Cube, null, unsaved: false);
        var project = new ProjectController(session, () => { }) { LastSession = new LastSession(_directory) };

        Assert.Null(project.LastSessionFound);
        project.RecoverLastSession();

        Assert.Equal(512, session.Scene.SolidCount);
    }

    [Theory]
    [InlineData(LevelTemplate.Ground)]
    [InlineData(LevelTemplate.Room)]
    public void NewFromATemplateIsThatTemplateUntitled(LevelTemplate template)
    {
        EditorSession session = Session(LevelTemplate.Cube, @"C:\levels\castle.vxlevel", unsaved: false);
        int replaced = 0;
        var project = new ProjectController(session, () => replaced++) { RemembersRecent = false };

        project.NewProject(template);

        Assert.Equal(LevelTemplates.Build(template).ContentHash(), session.Scene.ContentHash());
        Assert.Null(session.ProjectPath);
        Assert.Equal(1, replaced);
    }
}
