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

    private void Draw() => ObjectListPanel.Draw(_session, _camera);

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

    [Fact]
    public void TheEyeHidesAndShows()
    {
        VoxelObject wall = Named("Wall");
        Vector2 row = RowOf(wall);

        // The eye is the row's first thing, at its left edge.
        Vector2 eye = new(38f, row.Y);

        _ui.Click(eye, Draw);
        Assert.False(wall.Visible);

        _ui.Click(eye, Draw);
        Assert.True(wall.Visible);
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
}
