using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;
using Silk.NET.Input;

namespace EditorApp.Tests;

/// <summary>What F3 finds for what is typed, and in what order.</summary>
public class CommandSearchFindTests
{
    private static readonly SearchCommand[] Commands =
    [
        new("edit.duplicate", "Duplicate", "Edit", () => { }) { Keywords = "copy clone" },
        new("edit.delete", "Delete", "Edit", () => { }) { Keywords = "remove erase" },
        new("edit.setparent", "Parent to...", "Edit", () => { }) { Keywords = "child link" },
        new("edit.clearparent", "Clear parent", "Edit", () => { }) { Keywords = "unparent free" },
        new("view.xray", "X-Ray", "View", () => { }) { Keywords = "xray see through" },
        new("view.framelevel", "Frame the level", "View", () => { }),
        new("view.grid", "Ground grid", "View", () => { }) { Keywords = "floor" },
        new("add.sun", "Sun", "Add", () => { }),
        new("add.spot", "Spot light", "Add", () => { }),
    ];

    private static string[] Ids(string query, params string[] recent) =>
        [.. CommandSearch.Find(Commands, query, recent).Select(c => c.Id)];

    [Fact]
    public void TypingTheStartOfANameFindsIt()
    {
        Assert.Equal("edit.duplicate", Ids("dup")[0]);
        Assert.Equal("view.framelevel", Ids("fra")[0]);
    }

    [Fact]
    public void EveryWordTypedHasToBeFound()
    {
        Assert.Equal(["edit.clearparent"], Ids("clear par"));
        Assert.Empty(Ids("clear grid"));
    }

    /// <summary>"par" is the start of Parent to... and inside Clear parent: the one it starts comes first.</summary>
    [Fact]
    public void ANameBegunWithWhatWasTypedComesFirst()
    {
        string[] found = Ids("par");

        Assert.Equal("edit.setparent", found[0]);
        Assert.Contains("edit.clearparent", found);
    }

    /// <summary>Inside a word of the name counts; inside one of the words it answers to does not — "par" is not X-Ray's "transparent".</summary>
    [Fact]
    public void OnlyTheNameIsSearchedInsideItsWords()
    {
        SearchCommand[] commands =
        [
            new("view.xray", "X-Ray", "View", () => { }) { Keywords = "see through transparent" },
            new("edit.clearparent", "Clear parent", "Edit", () => { }),
        ];

        Assert.Equal(["edit.clearparent"], CommandSearch.Find(commands, "arent", []).Select(c => c.Id));
        Assert.DoesNotContain(CommandSearch.Find(commands, "par", []), c => c.Id == "view.xray");
    }

    [Fact]
    public void TheWordsACommandAnswersToFindItToo()
    {
        Assert.Equal("edit.clearparent", Ids("unparent")[0]);
        Assert.Equal("view.xray", Ids("xray")[0]);
        Assert.Equal("view.grid", Ids("floor")[0]);
        Assert.Equal("edit.duplicate", Ids("clone")[0]);

        // Begun, too, and the menu's name the same way.
        Assert.Equal(["view.grid"], Ids("flo"));
        Assert.Equal(["add.spot", "add.sun"], Ids("add").Order());
    }

    /// <summary>Words typed in the order the name has them beat the same words the other way round.</summary>
    [Fact]
    public void WordsInTheNamesOrderComeFirst()
    {
        SearchCommand[] commands =
        [
            new("a", "Grid snap", "View", () => { }),
            new("b", "Snap grid", "View", () => { }),
        ];

        Assert.Equal("b", CommandSearch.Find(commands, "snap grid", []).First().Id);
        Assert.Equal("a", CommandSearch.Find(commands, "grid snap", []).First().Id);
    }

    [Fact]
    public void AWordTypedShortStillFindsItsCommand()
    {
        Assert.Equal("edit.setparent", Ids("prnt")[0]);
        Assert.Equal("edit.duplicate", Ids("dupicate")[0]);

        // A few letters that are in no word, in order or not, find nothing.
        Assert.Empty(Ids("qzx"));
        Assert.Empty(Ids("zzzz"));

        // Nor does a short word find every long one that happens to hold its letters: "dlte" is in
        // Duplicate too, with five letters to spare.
        Assert.Equal(["edit.delete"], Ids("dlte"));
    }

    [Fact]
    public void CaseDoesNotMatter()
    {
        Assert.Equal(Ids("dup"), Ids("DUP"));
    }

    [Fact]
    public void NothingTypedListsTheRecentOnesFirstThenEverything()
    {
        string[] listed = Ids("   ", "add.spot", "edit.delete", "no.longer.there");

        Assert.Equal(Commands.Length, listed.Length);
        Assert.Equal(["add.spot", "edit.delete"], listed[..2]);

        // The rest by menu, then name.
        Assert.Equal("add.sun", listed[2]);
        Assert.Equal("edit.clearparent", listed[3]);
    }

    /// <summary>Between two that match alike, the one used more lately.</summary>
    [Fact]
    public void AmongEqualsTheOneUsedComesFirst()
    {
        Assert.Equal("add.sun", Ids("s")[0]);
        Assert.Equal("add.spot", Ids("s", "add.spot")[0]);
    }

    [Fact]
    public void TheRecentListKeepsTheNewestTen()
    {
        var recent = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            CommandSearch.Remember(recent, $"c{i}");
        }

        Assert.Equal(CommandSearch.RecentKept, recent.Count);
        Assert.Equal("c11", recent[0]);
        Assert.DoesNotContain("c1", recent);

        CommandSearch.Remember(recent, "c5");
        Assert.Equal("c5", recent[0]);
        Assert.Single(recent, id => id == "c5");
    }
}

/// <summary>The search as it is used: F3, a few letters, the arrows, Enter or a click, Esc.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class CommandSearchTests : IDisposable
{
    private readonly ImGuiHarness _ui = new();
    private readonly List<string> _recent = [];
    private readonly List<string> _ran = [];
    private readonly SearchCommand[] _commands;
    private bool _blocked = true;

    public CommandSearchTests()
    {
        _commands =
        [
            Command("edit.duplicate", "Duplicate", "Edit"),
            Command("edit.delete", "Delete", "Edit"),
            Command("edit.setparent", "Parent to...", "Edit"),
            Command("view.grid", "Ground grid", "View"),
            Command("add.sun", "Sun", "Add") with { Problem = () => _blocked ? "Not now." : null },
        ];
    }

    public void Dispose() => _ui.Dispose();

    private SearchCommand Command(string id, string name, string menu) => new(id, name, menu, () => _ran.Add(id));

    private void Draw() => CommandSearch.Draw(() => _commands, _recent);

    /// <summary>
    /// Opens the search and gives it the frames it takes to take the keys. Drawn in the harness's
    /// window throughout: a popup is found by an id taken where it is drawn, and typing and pressing
    /// draw there.
    /// </summary>
    private void OpenAt(Vector2 mouse)
    {
        CommandSearch.Open();
        _ui.Frame(Draw, mouse);
        _ui.Frame(Draw);
        _ui.Frame(Draw);
    }

    private void Press(ImGuiKey key) => _ui.Press(key, Draw);

    [Fact]
    public void TypingThenEnterDoesTheCommandAndCloses()
    {
        OpenAt(new Vector2(300f, 200f));
        Assert.True(CommandSearch.IsOpen);

        _ui.Type("dup", Draw);
        Press(ImGuiKey.Enter);
        _ui.Frame(Draw);

        Assert.Equal(["edit.duplicate"], _ran);
        Assert.False(CommandSearch.IsOpen);
        Assert.Equal("edit.duplicate", _recent[0]);
    }

    [Fact]
    public void TheArrowsMoveDownTheListAndRoundAgain()
    {
        OpenAt(new Vector2(300f, 200f));
        _ui.Type("d", Draw);   // Duplicate, Delete, Ground grid, ...

        Assert.Equal(0, CommandSearch.Lit);
        Press(ImGuiKey.DownArrow);
        Assert.Equal(1, CommandSearch.Lit);
        Press(ImGuiKey.UpArrow);
        Press(ImGuiKey.UpArrow);
        Assert.NotEqual(0, CommandSearch.Lit);   // round to the bottom

        Press(ImGuiKey.DownArrow);
        Press(ImGuiKey.DownArrow);
        Press(ImGuiKey.Enter);

        Assert.Single(_ran);
        Assert.Equal(CommandSearch.Find(_commands, "d", []).ElementAt(1).Id, _ran[0]);
    }

    [Fact]
    public void EscGoesWithoutDoingAnything()
    {
        OpenAt(new Vector2(300f, 200f));
        _ui.Type("dup", Draw);

        Press(ImGuiKey.Escape);
        _ui.Frame(Draw);

        Assert.False(CommandSearch.IsOpen);
        Assert.Empty(_ran);
    }

    [Fact]
    public void ClickingARowDoesIt()
    {
        OpenAt(new Vector2(300f, 200f));
        _ui.Type("grid", Draw);

        (Vector2 Min, Vector2 Max) row = CommandSearch.RowRect("view.grid")!.Value;
        _ui.Click((row.Min + row.Max) * 0.5f, Draw);

        Assert.Equal(["view.grid"], _ran);
    }

    [Fact]
    public void AGreyedCommandIsNotDone()
    {
        OpenAt(new Vector2(300f, 200f));
        _ui.Type("sun", Draw);
        Press(ImGuiKey.Enter);

        Assert.Empty(_ran);
        Assert.True(CommandSearch.IsOpen);

        // The field has the keys back: what is typed next still goes into it.
        _ui.Frame(Draw);
        _ui.Type("x", Draw);
        Assert.Null(CommandSearch.RowRect("add.sun"));
        Press(ImGuiKey.Backspace);
        Assert.NotNull(CommandSearch.RowRect("add.sun"));

        _blocked = false;
        Press(ImGuiKey.Enter);
        Assert.Equal(["add.sun"], _ran);
    }

    [Fact]
    public void WhatWasDoneLastIsFirstNextTime()
    {
        OpenAt(new Vector2(300f, 200f));
        _ui.Type("grid", Draw);
        Press(ImGuiKey.Enter);

        // Open again, it starts empty — everything listed — with that one at the top.
        OpenAt(new Vector2(300f, 200f));
        Assert.NotNull(CommandSearch.RowRect("edit.duplicate"));
        Press(ImGuiKey.Enter);

        Assert.Equal(["view.grid", "view.grid"], _ran);
    }

    [Theory]
    [InlineData(KeymapPreset.Default)]
    [InlineData(KeymapPreset.MimicBusters)]
    public void F3IsTheSearch(KeymapPreset preset) =>
        Assert.Equal(EditorAction.Search, Keymap.For(preset).ActionFor(new KeyChord(Key.F3)));
}

/// <summary>The editor's own list: every action of the keymap, and what only menus offer.</summary>
/// <remarks>With the ImGui tests, which share the Add menu's waiting pick with it.</remarks>
[Collection(nameof(ImGuiCollection))]
public class SearchCommandsTests
{
    private readonly EditorSession _session = new();
    private readonly ViewportSettings _viewport = new();
    private readonly List<EditorAction> _run = [];

    public SearchCommandsTests()
    {
        var scene = new VoxelScene();
        foreach ((string name, float x) in new[] { ("Wall", 0f), ("Gate", 4f) })
        {
            var grid = new VoxelWorld();
            grid.SetVoxel(0, 0, 0, 10);
            grid.SetVoxel(1, 0, 0, 10);
            scene.Add(grid, ObjectTransform.At(new Vector3(x, 0f, 0f)), name);
        }

        _session.ReplaceScene(scene, projectPath: null);
    }

    private List<SearchCommand> Build() => SearchCommands.Build(new SearchSources
    {
        Session = _session,
        Project = new ProjectController(_session, () => { }) { RemembersRecent = false },
        Mimicraft = new MimicraftController(_session),
        View = new ViewActions
        {
            FrameLevel = () => { },
            FrameFocused = () => { },
            LookAtCenter = () => { },
            ResetCamera = () => { },
            Camera = new FlyCamera(),
            GridVisible = () => _viewport.Grid,
            ToggleGrid = () => { },
            MeasurementsVisible = () => false,
            ToggleMeasurements = () => { },
            SidebarVisible = () => true,
            ToggleSidebar = () => { },
            StatisticsVisible = () => false,
            ToggleStatistics = () => { },
            LightIconsVisible = () => true,
            ToggleLightIcons = () => { },
            MirrorPlanesVisible = () => true,
            ToggleMirrorPlanes = () => { },
            Lighting = new SceneLighting(),
            Viewport = _viewport,
        },
        Preferences = new Preferences(),
        Run = _run.Add,
        ApplyPreferences = () => { },
        Exit = () => { },
    });

    [Fact]
    public void EveryActionCanBeFoundButTheSearchItself()
    {
        List<SearchCommand> commands = Build();

        foreach (ActionInfo info in EditorActions.All)
        {
            bool listed = commands.Any(c => c.Action == info.Action);
            bool expected = info.Action is not (EditorAction.Search or EditorAction.Cancel or EditorAction.KeepExtrude);
            Assert.True(listed == expected, $"{info.Action} {(expected ? "is missing" : "should not be listed")}");
        }
    }

    [Fact]
    public void IdsAreUnique()
    {
        List<SearchCommand> commands = Build();

        Assert.Equal(commands.Count, commands.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void AnActionFromTheListDoesWhatItsKeyDoes()
    {
        Build().Single(c => c.Action == EditorAction.Duplicate).Run();

        Assert.Equal([EditorAction.Duplicate], _run);
    }

    [Fact]
    public void WhatOnlyAMenuOffersIsThereToo()
    {
        List<SearchCommand> commands = Build();

        commands.Single(c => c.Name == "A colour per object").Run();
        Assert.Equal(ColourMode.Random, _viewport.Colour);

        // Handed to the application as the Add menu hands it, to be set down where the view looks.
        AddMenu.TakePending();
        commands.Single(c => c.Name == "Point light").Run();
        Assert.Equal(LightKind.Point, AddMenu.TakePending()?.Choice.Light);

        commands.Single(c => c.Name == "Relationship lines").Run();
        Assert.False(_viewport.RelationshipLines);

        Assert.Contains(commands, c => c.Name == "Turn left" && c.Menu == "Object");
        Assert.Contains(commands, c => c.Name == "Keymap preferences");

        // The welcome screen's too: every template, the screen itself, and the last session.
        Assert.All(LevelTemplates.All, t => Assert.Contains(commands, c => c.Menu == "New" && c.Name == LevelTemplates.NameOf(t)));
        Assert.Contains(commands, c => c.Id == "help.welcome");
        Assert.NotNull(commands.Single(c => c.Id == "file.recoverlast").Problem?.Invoke());
    }

    [Fact]
    public void JoinIntoNamesEveryOtherObject()
    {
        _session.ChooseObject(_session.Scene.Objects[0].Id);

        SearchCommand join = Assert.Single(Build(), c => c.Name.StartsWith("Join into", StringComparison.Ordinal));
        Assert.Equal("Join into Gate", join.Name);
    }

    [Fact]
    public void WhatCannotBeDoneNowSaysWhy()
    {
        List<SearchCommand> commands = Build();

        Assert.NotNull(commands.Single(c => c.Action == EditorAction.Undo).Problem?.Invoke());
        Assert.NotNull(commands.Single(c => c.Action == EditorAction.Paste).Problem?.Invoke());
        Assert.NotNull(commands.Single(c => c.Action == EditorAction.ClearParent).Problem?.Invoke());

        _session.SetParent(_session.Scene.Objects[1].Id, _session.Scene.Objects[0].Id);
        _session.ChooseObject(_session.Scene.Objects[1].Id);

        Assert.Null(commands.Single(c => c.Action == EditorAction.Undo).Problem?.Invoke());
        Assert.Null(commands.Single(c => c.Action == EditorAction.ClearParent).Problem?.Invoke());
    }
}
