using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;
using Silk.NET.Input;

namespace EditorApp.Tests;

/// <summary>Shift+A: a submenu opened, a shape picked, and where it was picked for.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class AddMenuTests : IDisposable
{
    private readonly ImGuiHarness _ui = new();
    private static readonly Vector2 At = new(300f, 150f);

    public AddMenuTests() => AddMenu.TakePending();

    public void Dispose() => _ui.Dispose();

    private static void Draw() => AddMenu.DrawPopup();

    private static Vector2 Centre((Vector2 Min, Vector2 Max)? rect)
    {
        Assert.True(rect.HasValue, "not drawn");
        return (rect.Value.Min + rect.Value.Max) * 0.5f;
    }

    private void Open()
    {
        AddMenu.Open(At);
        _ui.Frame(Draw, At);
        _ui.Frame(Draw);
    }

    /// <summary>Hovering a submenu's entry opens it, as ImGui's menus do; a frame or two for it to appear.</summary>
    private void Hover(string entry)
    {
        Vector2 on = Centre(AddMenu.ItemRect(entry));
        _ui.Frame(Draw, on);
        _ui.Frame(Draw, on);
        _ui.Frame(Draw, on);
    }

    [Theory]
    [InlineData("Mesh", "Sphere")]
    [InlineData("Prop", "Barrel")]
    [InlineData("Light", "Spot light")]
    public void APickIsWaitingWithWhereTheMenuWasOpened(string list, string entry)
    {
        Open();
        Assert.True(AddMenu.IsOpen);

        Hover(list);
        _ui.Click(Centre(AddMenu.ItemRect(entry)), Draw);

        (AddChoice choice, Vector2? at) = AddMenu.TakePending()!.Value;
        Assert.Equal(At, at);
        Assert.Equal(list == "Mesh" ? ShapeKind.Sphere : null, choice.Shape);
        Assert.Equal(list == "Prop" ? PropKind.Barrel : null, choice.Prop);
        Assert.Equal(list == "Light" ? LightKind.Spot : null, choice.Light);

        _ui.Frame(Draw);
        Assert.False(AddMenu.IsOpen);
    }

    [Fact]
    public void ACameraIsOnTheMenuItself()
    {
        Open();
        _ui.Click(Centre(AddMenu.ItemRect("Camera")), Draw);

        Assert.True(AddMenu.TakePending()!.Value.Choice.Camera);
    }

    /// <summary>A note is for the people working on the level, not one of the game's markers: it has an entry of its own.</summary>
    [Fact]
    public void ANoteIsOnTheMenuItself()
    {
        Open();
        _ui.Click(Centre(AddMenu.ItemRect("Note")), Draw);

        Assert.Equal(MarkerKind.Note, AddMenu.TakePending()!.Value.Choice.Marker);
    }

    [Fact]
    public void APickIsTakenOnce()
    {
        AddMenu.Choose(new AddChoice(Shape: ShapeKind.Cube), null);

        Assert.Equal(ShapeKind.Cube, AddMenu.TakePending()?.Choice.Shape);
        Assert.Null(AddMenu.TakePending());
    }

    [Fact]
    public void EscClosesIt()
    {
        Open();

        _ui.Press(ImGuiKey.Escape, Draw);

        Assert.False(AddMenu.IsOpen);
        Assert.Null(AddMenu.TakePending());
    }

    [Theory]
    [InlineData(KeymapPreset.Default)]
    [InlineData(KeymapPreset.MimicBusters)]
    public void ShiftAIsTheAddMenu(KeymapPreset preset) =>
        Assert.Equal(EditorAction.AddMenu, Keymap.For(preset).ActionFor(KeyChord.ShiftOf(Key.A)));

    [Fact]
    public void TheSearchFindsWhatTheMenuHas()
    {
        var session = new EditorSession();
        session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);
        List<SearchCommand> commands = SearchCommandsTestSources.Build(session);

        commands.Single(c => c.Id == "add.shape.sphere").Run();
        Assert.Equal((new AddChoice(Shape: ShapeKind.Sphere), (Vector2?)null), AddMenu.TakePending());

        Assert.All(PropPresets.All, p => Assert.Contains(commands, c => c.Menu == "Add" && c.Name == PropPresets.NameOf(p)));
    }
}

/// <summary>The right-click menu, for an object, a light, and nothing at all.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class ViewportMenuTests : IDisposable
{
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(600f, 500f) };
    private readonly EditorSession _session = new();
    private readonly List<EditorAction> _run = [];
    private readonly ViewportMenuActions _actions;
    private static readonly Vector2 At = new(260f, 60f);

    public ViewportMenuTests()
    {
        _session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);
        _actions = new ViewportMenuActions { Session = _session, Run = _run.Add, Viewport = new ViewportSettings() };
        AddMenu.TakePending();
    }

    public void Dispose() => _ui.Dispose();

    private void Draw() => ViewportMenu.Draw(_actions);

    private static Vector2 Centre((Vector2 Min, Vector2 Max)? rect)
    {
        Assert.True(rect.HasValue, "not drawn");
        return (rect.Value.Min + rect.Value.Max) * 0.5f;
    }

    private void Open(int objectId, int lightId)
    {
        ViewportMenu.Open(At, objectId, lightId);
        _ui.Frame(Draw, At);
        _ui.Frame(Draw);
    }

    [Fact]
    public void OnAnObjectItsEntriesDoWhatTheirKeysDo()
    {
        Open(_session.Scene.Objects[0].Id, 0);

        _ui.Click(Centre(ViewportMenu.ItemRect("Duplicate")), Draw);
        _ui.Frame(Draw);

        Assert.Equal([EditorAction.Duplicate], _run);
        Assert.False(ViewportMenu.IsOpen);
    }

    [Fact]
    public void WhatCannotBeDoneIsGreyed()
    {
        // Nothing copied yet, and nothing selected to delete.
        _session.DeselectAll();
        Open(_session.Scene.Objects[0].Id, 0);

        _ui.Click(Centre(ViewportMenu.ItemRect("Paste")), Draw);
        _ui.Click(Centre(ViewportMenu.ItemRect("Delete")), Draw);

        Assert.Empty(_run);
    }

    [Fact]
    public void TheLastObjectCanBeDeleted()
    {
        _session.ClickSelect(_session.Scene.Objects[0].Id);
        Open(_session.Scene.Objects[0].Id, 0);

        _ui.Click(Centre(ViewportMenu.ItemRect("Delete")), Draw);

        Assert.Equal([EditorAction.Delete], _run);
    }

    [Fact]
    public void OnALightItIsAboutTheLight()
    {
        SceneLight sun = _session.Scene.Lights[0];
        Open(0, sun.Id);

        Assert.Null(ViewportMenu.ItemRect("Subdivide"));
        _ui.Click(Centre(ViewportMenu.ItemRect("Switch Off")), Draw);

        Assert.Equal([EditorAction.Hide], _run);
    }

    [Fact]
    public void OnNothingItIsAboutTheView()
    {
        Open(0, 0);

        _ui.Click(Centre(ViewportMenu.ItemRect("Frame All")), Draw);

        Assert.Equal([EditorAction.FrameLevel], _run);
    }

    /// <summary>What is added from it goes where the menu was opened.</summary>
    [Fact]
    public void OnNothingItAddsWhereItWasOpened()
    {
        Open(0, 0);

        Vector2 mesh = Centre(AddMenu.ItemRect("Mesh"));
        _ui.Frame(Draw, mesh);
        _ui.Frame(Draw, mesh);
        _ui.Frame(Draw, mesh);
        _ui.Click(Centre(AddMenu.ItemRect("Cube")), Draw);

        Assert.Equal((new AddChoice(Shape: ShapeKind.Cube), (Vector2?)At), AddMenu.TakePending());
    }

    [Fact]
    public void AnObjectGoneSinceIsNothing()
    {
        Open(9999, 0);

        Assert.NotNull(ViewportMenu.ItemRect("Frame All"));
        Assert.Null(ViewportMenu.ItemRect("Duplicate"));
    }
}

/// <summary>The Adjust panel: a shape's numbers, changed after it is added.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class AdjustPanelTests : IDisposable
{
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(600f, 500f) };
    private readonly EditorSession _session = new();

    public AdjustPanelTests() => _session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);

    public void Dispose() => _ui.Dispose();

    private void Draw() => AdjustPanel.Draw(_session, new Vector2(40f, 0f), 460f);

    private static Vector2 Centre((Vector2 Min, Vector2 Max)? rect)
    {
        Assert.True(rect.HasValue, "not drawn");
        return (rect.Value.Min + rect.Value.Max) * 0.5f;
    }

    [Fact]
    public void WithNothingJustAddedThereIsNoPanel()
    {
        _ui.Frame(Draw, inWindow: false);

        Assert.Equal(0f, AdjustPanel.Height);
    }

    [Fact]
    public void HollowRemakesTheShapeAsAShell()
    {
        VoxelObject cube = _session.AddShape(Shapes.Defaults(ShapeKind.Cube), new Vector3(0f, 8f, 0f), Vector3.UnitY, 1f);
        _ui.Frame(Draw, inWindow: false);
        _ui.Frame(Draw, inWindow: false);
        Assert.True(AdjustPanel.Height > 0f);

        _ui.Click(Centre(Props.RectOf("adjust-hollow")), Draw, inWindow: false);

        Assert.True(_session.LastShape!.Settings.Hollow);
        Assert.Equal(64 - 8, cube.Grid.SolidCount);
    }

    [Fact]
    public void AVoxelHasNothingToAdjust()
    {
        _session.AddShape(Shapes.Defaults(ShapeKind.Voxel), Vector3.Zero, Vector3.UnitY, 1f);

        _ui.Frame(Draw, inWindow: false);

        Assert.Equal(0f, AdjustPanel.Height);
    }
}

/// <summary>A right click against a right drag, which looks round.</summary>
public class RightClickTests
{
    private static readonly Vector2 At = new(100f, 80f);

    [Fact]
    public void AQuickStillPressIsAClickWhereItWasPressed()
    {
        var click = new RightClick();
        click.Press(At, 1.0, counts: true);
        click.Moved(new Vector2(2f, 1f));

        Assert.Equal(At, click.Release(1.2));
    }

    [Fact]
    public void LookingRoundIsNotAClick()
    {
        var moved = new RightClick();
        moved.Press(At, 1.0, counts: true);
        moved.Moved(new Vector2(4f, 0f));
        moved.Moved(new Vector2(0f, 4f));
        Assert.Null(moved.Release(1.1));

        var flew = new RightClick();
        flew.Press(At, 1.0, counts: true);
        flew.Flew();
        Assert.Null(flew.Release(1.1));

        var held = new RightClick();
        held.Press(At, 1.0, counts: true);
        Assert.Null(held.Release(1.0 + RightClick.MaxSeconds + 0.05));
    }

    [Fact]
    public void APressThatDoesNotCountIsNoClick()
    {
        var click = new RightClick();
        click.Press(At, 1.0, counts: false);

        Assert.Null(click.Release(1.1));
    }

    [Fact]
    public void ALetGoWithoutAPressIsNothing()
    {
        var click = new RightClick();

        Assert.Null(click.Release(1.0));
        click.Press(At, 1.0, counts: true);
        Assert.NotNull(click.Release(1.1));
        Assert.Null(click.Release(1.2));
    }
}

/// <summary>A light set down at a point on a surface, from the Add menu.</summary>
public class LightAtTests
{
    [Fact]
    public void ALightGoesOffTheSurfaceShiningAtIt()
    {
        var session = new EditorSession();
        session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);
        var point = new Vector3(2f, 8f, 0f);

        SceneLight lamp = LightMenu.AddAt(session, LightKind.Point, point, Vector3.UnitY);
        SceneLight spot = LightMenu.AddAt(session, LightKind.Spot, point, Vector3.UnitX);

        Assert.Equal(point + new Vector3(0f, 3f, 0f), lamp.Position);
        Assert.Equal(point + new Vector3(6f, 0f, 0f), spot.Position);
        Assert.True(Vector3.Distance(-Vector3.UnitX, spot.Direction) < 1e-4f);
        Assert.Equal(spot.Id, session.SelectedLightId);
        Assert.Equal(EditorTool.Transform, session.ActiveTool);
    }
}

/// <summary>The search's list, built for tests from a session and the least else it needs.</summary>
internal static class SearchCommandsTestSources
{
    public static List<SearchCommand> Build(EditorSession session)
    {
        var viewport = new ViewportSettings();
        return SearchCommands.Build(new SearchSources
        {
            Session = session,
            Project = new ProjectController(session, () => { }) { RemembersRecent = false },
            Mimicraft = new MimicraftController(session),
            View = new ViewActions
            {
                FrameLevel = () => { },
                FrameFocused = () => { },
                LookAtCenter = () => { },
                ResetCamera = () => { },
                Camera = new FlyCamera(),
                GridVisible = () => true,
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
                Viewport = viewport,
            },
            Preferences = new Preferences(),
            Run = _ => { },
            ApplyPreferences = () => { },
            Exit = () => { },
        });
    }
}
