using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// The outliner, driven through the headless ImGui: which row a click lands on, what the eye does,
/// and how a rename ends.
/// </summary>
[Collection(nameof(ImGuiCollection))]
public sealed class OutlinerTests : IDisposable
{
    private readonly ImGuiHarness _ui = new();
    private readonly EditorSession _session = new();
    private readonly FlyCamera _camera = new();

    public OutlinerTests()
    {
        var scene = new VoxelScene();
        foreach (string name in new[] { "Wall", "Tower", "Gate" })
        {
            var grid = new VoxelWorld();
            grid.SetVoxel(0, 0, 0, 10);
            scene.Add(grid, ObjectTransform.Identity, name);
        }

        _session.ReplaceScene(scene, projectPath: null);

        _ui.Frame(Draw);
        _ui.Frame(Draw);
    }

    public void Dispose() => _ui.Dispose();

    private void Draw() => ObjectListPanel.Draw(_session, _camera, new Vector2(560f, 180f));

    private VoxelObject Named(string name) => _session.Scene.Objects.First(o => o.Name == name);

    /// <summary>
    /// Finds a row by moving the mouse down the list until the panel reports that object under it —
    /// the panel's own answer, rather than a layout guessed at from outside.
    /// </summary>
    private Vector2 RowOf(VoxelObject target, float x = 300f)
    {
        for (float y = 20f; y < 220f; y += 2f)
        {
            _ui.Frame(Draw, new Vector2(x, y));
            _ui.Frame(Draw, new Vector2(x, y));

            if (ObjectListPanel.HoveredId == target.Id)
            {
                return new Vector2(x, y + 4f);
            }
        }

        throw new InvalidOperationException($"No row found for {target.Name}.");
    }

    [Fact]
    public void ClickingARowFocusesItsObject()
    {
        VoxelObject gate = Named("Gate");

        _ui.Click(RowOf(gate), Draw);

        Assert.Equal(gate.Id, _session.Scene.FocusId);
    }

    /// <summary>A click with a modifier key held down, as the keyboard would send it.</summary>
    private void ClickWith(ImGuiKey modifier, Vector2 at)
    {
        ImGui.GetIO().AddKeyEvent(modifier, true);
        _ui.Click(at, Draw);
        ImGui.GetIO().AddKeyEvent(modifier, false);
        _ui.Frame(Draw);
    }

    [Fact]
    public void ARowClickSelectsOnlyThatRow()
    {
        Vector2 wall = RowOf(Named("Wall"));
        Vector2 gate = RowOf(Named("Gate"));

        _ui.Click(wall, Draw);
        _ui.Click(gate, Draw);

        Assert.Equal(new[] { Named("Gate").Id }, _session.Scene.SelectedIds);
    }

    [Fact]
    public void CtrlClickAddsARowAndShiftClickTakesTheRange()
    {
        Vector2 wall = RowOf(Named("Wall"));
        Vector2 gate = RowOf(Named("Gate"));

        _ui.Click(wall, Draw);
        ClickWith(ImGuiKey.ModCtrl, gate);
        Assert.True(_session.IsSelected(Named("Wall").Id) && _session.IsSelected(Named("Gate").Id));
        Assert.False(_session.IsSelected(Named("Tower").Id));

        // From the active row, Gate now, back up to Wall: all three.
        _ui.Click(gate, Draw);
        ClickWith(ImGuiKey.ModShift, wall);
        Assert.Equal(3, _session.SelectedCount);
        Assert.Equal(Named("Gate").Id, _session.ActiveId);
    }

    /// <summary>A row being pointed at is reported, so the viewport can outline that object.</summary>
    [Fact]
    public void PointingAtARowReportsItAndLeavingIt()
    {
        VoxelObject tower = Named("Tower");
        Vector2 row = RowOf(tower);

        _ui.Frame(Draw, row);
        Assert.Equal(tower.Id, ObjectListPanel.HoveredId);

        _ui.Frame(Draw, new Vector2(900f, 600f));
        _ui.Frame(Draw, new Vector2(900f, 600f));
        Assert.Equal(0, ObjectListPanel.HoveredId);
    }

    private static Vector2 Centre((Vector2 Min, Vector2 Max)? rect)
    {
        Assert.True(rect.HasValue, "the switch was not drawn");
        return (rect.Value.Min + rect.Value.Max) * 0.5f;
    }

    [Fact]
    public void TheEyeHidesAndShows()
    {
        VoxelObject wall = Named("Wall");
        Vector2 eye = Centre(ObjectListPanel.ToggleRect(wall.Id, "eye"));

        _ui.Click(eye, Draw);
        Assert.False(wall.Visible);

        _ui.Click(eye, Draw);
        Assert.True(wall.Visible);
    }

    /// <summary>
    /// Blender's arrangement: the switches stacked at the right end of the row, the eye last, the
    /// lock before it — and the name, with its kind before it, taking the rest.
    /// </summary>
    [Fact]
    public void TheSwitchesSitAtTheRightEndWithTheEyeLast()
    {
        VoxelObject wall = Named("Wall");
        (Vector2 Min, Vector2 Max) eye = ObjectListPanel.ToggleRect(wall.Id, "eye")!.Value;
        (Vector2 Min, Vector2 Max) padlock = ObjectListPanel.ToggleRect(wall.Id, "lock")!.Value;

        Assert.True(padlock.Max.X <= eye.Min.X, "the lock comes before the eye");
        Assert.True(eye.Max.X > 500f, "the eye is at the far end of a 560 wide list");
        Assert.Equal(padlock.Min.Y, eye.Min.Y);
    }

    [Fact]
    public void ThePadlockLocksAndALockedRowIsNotChosen()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject tower = Named("Tower");
        Assert.True(_session.ChooseObject(tower.Id));

        _ui.Click(Centre(ObjectListPanel.ToggleRect(wall.Id, "lock")), Draw);
        Assert.True(wall.Locked);

        // A click on the locked row's name changes nothing: focus stays where it was.
        _ui.Click(RowOf(wall), Draw);
        Assert.Equal(tower.Id, _session.Scene.FocusId);

        _ui.Click(Centre(ObjectListPanel.ToggleRect(wall.Id, "lock")), Draw);
        Assert.False(wall.Locked);

        _ui.Click(RowOf(wall), Draw);
        Assert.Equal(wall.Id, _session.Scene.FocusId);
    }

    [Fact]
    public void ARenameEndsWithEnterAndKeepsTheNewName()
    {
        VoxelObject tower = Named("Tower");

        ObjectListPanel.StartRename(tower);
        _ui.Frame(Draw);
        _ui.Frame(Draw);
        _ui.Type("Keep", Draw);
        _ui.Press(ImGuiKey.Enter, Draw);

        Assert.Equal("Keep", tower.Name);
        Assert.True(_session.HasUnsavedChanges);
    }

    [Fact]
    public void EscapeKeepsTheOldName()
    {
        VoxelObject tower = Named("Tower");

        ObjectListPanel.StartRename(tower);
        _ui.Frame(Draw);
        _ui.Frame(Draw);
        _ui.Type("Nope", Draw);
        _ui.Press(ImGuiKey.Escape, Draw);

        Assert.Equal("Tower", tower.Name);
        Assert.False(_session.HasUnsavedChanges);

        // And the rename is over: the row answers a click again instead of holding a text field.
        _ui.Click(RowOf(Named("Gate")), Draw);
        _ui.Click(RowOf(tower), Draw);
        Assert.Equal(tower.Id, _session.Scene.FocusId);
    }

    /// <summary>The field is gone once the rename is over, and the row is a row again.</summary>
    [Fact]
    public void AfterARenameTheRowCanBeClickedAgain()
    {
        VoxelObject tower = Named("Tower");

        ObjectListPanel.StartRename(tower);
        _ui.Frame(Draw);
        _ui.Frame(Draw);
        _ui.Press(ImGuiKey.Enter, Draw);

        VoxelObject gate = Named("Gate");
        _ui.Click(RowOf(gate), Draw);
        _ui.Click(RowOf(tower), Draw);

        Assert.Equal(tower.Id, _session.Scene.FocusId);
    }

    [Fact]
    public void ARowDraggedOntoACollectionGoesIntoIt()
    {
        SceneCollection walls = _session.NewCollection(moving: [Named("Wall").Id]);
        _ui.Frame(Draw);
        _ui.Frame(Draw);

        Vector2 gate = Centre(ObjectListPanel.RowRect(Named("Gate").Id));
        Vector2 collection = Centre(ObjectListPanel.RowRect(walls.Id));
        // A drag and drop wants the button held over several frames, moving, before it is let go.
        _ui.Frame(Draw, gate, pressed: false);
        _ui.Frame(Draw, gate, pressed: true);
        _ui.Frame(Draw, (gate + collection) * 0.5f, pressed: true);
        _ui.Frame(Draw, collection, pressed: true);
        _ui.Frame(Draw, collection, pressed: true);
        _ui.Frame(Draw, collection, pressed: false);

        Assert.Equal(walls.Id, Named("Gate").CollectionId);
    }

    [Fact]
    public void ACollectionsEyeHidesWhatIsInIt()
    {
        SceneCollection walls = _session.NewCollection(moving: [Named("Wall").Id]);
        _ui.Frame(Draw);
        _ui.Frame(Draw);

        _ui.Click(Centre(ObjectListPanel.ToggleRect(walls.Id, "eye")), Draw);

        Assert.False(walls.Visible);
        Assert.False(Named("Wall").Visible);
        Assert.True(Named("Wall").OwnVisible);
        Assert.True(Named("Gate").Visible);
    }
}
