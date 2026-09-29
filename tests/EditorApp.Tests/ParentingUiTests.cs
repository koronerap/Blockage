using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using EditorApp.Rendering;
using EditorApp.Ui;
using Silk.NET.Input;

namespace EditorApp.Tests;

/// <summary>
/// Parenting as it is done by hand: the Outliner's tree and its drag and drop, the Ctrl+P list, the
/// Parent field, and the gizmo carrying children along.
/// </summary>
[Collection(nameof(ImGuiCollection))]
public sealed class ParentingUiTests : IDisposable
{
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(600f, 400f) };
    private readonly EditorSession _session = new();
    private readonly FlyCamera _camera = new();

    public ParentingUiTests()
    {
        var scene = new VoxelScene();
        float x = 0f;
        foreach (string name in new[] { "Wall", "Tower", "Gate" })
        {
            var grid = new VoxelWorld();
            grid.SetVoxel(0, 0, 0, 10);
            scene.Add(grid, ObjectTransform.At(new Vector3(x, 0f, 0f)), name);
            x += 4f;
        }

        _session.ReplaceScene(scene, projectPath: null);
        _ui.Frame(DrawOutliner);
        _ui.Frame(DrawOutliner);
    }

    public void Dispose() => _ui.Dispose();

    private void DrawOutliner() => ObjectListPanel.Draw(_session, _camera, new Vector2(560f, 300f));

    private VoxelObject Named(string name) => _session.Scene.Objects.First(o => o.Name == name);

    private static Vector2 Centre((Vector2 Min, Vector2 Max)? rect)
    {
        Assert.True(rect.HasValue, "not drawn");
        return (rect.Value.Min + rect.Value.Max) * 0.5f;
    }

    /// <summary>
    /// Press on one row, hold while moving onto another — two frames there, so a row above the one
    /// dragged has seen the drag before it is let go — and let go.
    /// </summary>
    private void DragRow(Vector2 from, Vector2 to)
    {
        _ui.Frame(DrawOutliner, from, pressed: false);
        _ui.Frame(DrawOutliner, from, pressed: true);
        _ui.Frame(DrawOutliner, (from + to) * 0.5f, pressed: true);
        _ui.Frame(DrawOutliner, to, pressed: true);
        _ui.Frame(DrawOutliner, to, pressed: true);
        _ui.Frame(DrawOutliner, to, pressed: false);
        _ui.Frame(DrawOutliner, to, pressed: false);
    }

    [Fact]
    public void AChildIsListedUnderItsParentAStepIn()
    {
        _session.SetParent(Named("Gate").Id, Named("Wall").Id);
        _ui.Frame(DrawOutliner);

        (Vector2 Min, Vector2 Max) wall = ObjectListPanel.RowRect(Named("Wall").Id)!.Value;
        (Vector2 Min, Vector2 Max) gate = ObjectListPanel.RowRect(Named("Gate").Id)!.Value;
        (Vector2 Min, Vector2 Max) tower = ObjectListPanel.RowRect(Named("Tower").Id)!.Value;

        Assert.True(gate.Min.X > wall.Min.X, "the child is not indented");
        Assert.True(gate.Min.Y > wall.Min.Y && gate.Min.Y < tower.Min.Y, "the child is not straight under its parent");
        Assert.Equal(wall.Min.X, tower.Min.X, 1);
    }

    /// <summary>With no parents in the level the list is the flat one it always was: no arrows, no indent.</summary>
    [Fact]
    public void AFlatLevelHasNoArrows()
    {
        Assert.Null(ObjectListPanel.ToggleRect(Named("Wall").Id, "fold"));
        Assert.Equal(ObjectListPanel.RowRect(Named("Wall").Id)!.Value.Min.X, ObjectListPanel.RowRect(Named("Gate").Id)!.Value.Min.X, 1);
    }

    [Fact]
    public void TheArrowFoldsTheChildrenAwayAndBack()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");
        _session.SetParent(gate.Id, wall.Id);
        _ui.Frame(DrawOutliner);

        _ui.Click(Centre(ObjectListPanel.ToggleRect(wall.Id, "fold")), DrawOutliner);
        Assert.True(ObjectListPanel.IsFolded(wall.Id));
        Assert.Null(ObjectListPanel.RowRect(gate.Id));

        _ui.Click(Centre(ObjectListPanel.ToggleRect(wall.Id, "fold")), DrawOutliner);
        Assert.NotNull(ObjectListPanel.RowRect(gate.Id));
    }

    /// <summary>Picking a child elsewhere — in the viewport — unfolds its parent so its row can be seen.</summary>
    [Fact]
    public void AFoldedChildIsShownWhenItIsPicked()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");
        _session.SetParent(gate.Id, wall.Id);
        _ui.Frame(DrawOutliner);
        _ui.Click(Centre(ObjectListPanel.ToggleRect(wall.Id, "fold")), DrawOutliner);

        _session.ChooseObject(gate.Id);
        _ui.Frame(DrawOutliner);

        Assert.False(ObjectListPanel.IsFolded(wall.Id));
    }

    [Fact]
    public void DraggingARowOntoAnotherMakesItTheParent()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");

        DragRow(Centre(ObjectListPanel.RowRect(gate.Id)), Centre(ObjectListPanel.RowRect(wall.Id)));

        Assert.Same(wall, _session.Scene.ParentOf(gate));
        Assert.Equal(new Vector3(8f, 0f, 0f), gate.Transform.Position);
    }

    [Fact]
    public void DroppingARowBelowTheListFreesIt()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");
        _session.SetParent(gate.Id, wall.Id);
        _ui.Frame(DrawOutliner);

        (Vector2 Min, Vector2 Max) space = ObjectListPanel.DropSpaceRect!.Value;
        DragRow(Centre(ObjectListPanel.RowRect(gate.Id)), new Vector2(space.Min.X + 40f, space.Min.Y + 20f));

        Assert.Null(_session.Scene.ParentOf(gate));
    }

    [Fact]
    public void AParentDroppedOnItsOwnChildStaysAsItWas()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");
        _session.SetParent(gate.Id, wall.Id);
        _ui.Frame(DrawOutliner);

        DragRow(Centre(ObjectListPanel.RowRect(wall.Id)), Centre(ObjectListPanel.RowRect(gate.Id)));

        Assert.Null(_session.Scene.ParentOf(wall));
        Assert.Same(wall, _session.Scene.ParentOf(gate));
    }

    [Fact]
    public void ALightIsListedUnderItsParentAndCanBeDraggedThere()
    {
        VoxelObject tower = Named("Tower");
        SceneLight lamp = _session.AddLight(LightKind.Point, new Vector3(4f, 3f, 0f), -Vector3.UnitY);
        _ui.Frame(DrawOutliner);

        DragRow(Centre(ObjectListPanel.RowRect(lamp.Id)), Centre(ObjectListPanel.RowRect(tower.Id)));

        Assert.Same(tower, _session.Scene.ParentOf(lamp));
        (Vector2 Min, Vector2 Max) light = ObjectListPanel.RowRect(lamp.Id)!.Value;
        (Vector2 Min, Vector2 Max) gate = ObjectListPanel.RowRect(Named("Gate").Id)!.Value;
        Assert.True(light.Min.Y < gate.Min.Y, "the light is not listed under its parent");
    }

    // ---- Ctrl+P and the Parent field ---------------------------------------------------------------

    private void DrawPopup() => ParentMenu.DrawPopup(_session);

    [Fact]
    public void CtrlPListsTheObjectsAndPicksTheParent()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");

        ParentMenu.Open(gate.Id);
        _ui.Frame(DrawPopup, new Vector2(300f, 200f), inWindow: false);
        _ui.Frame(DrawPopup, inWindow: false);

        Assert.Null(ParentMenu.ItemRect(gate.Id));   // not its own parent
        _ui.Click(Centre(ParentMenu.ItemRect(wall.Id)), DrawPopup, inWindow: false);

        Assert.Same(wall, _session.Scene.ParentOf(gate));
    }

    [Fact]
    public void ALoopIsGreyedInTheList()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");
        _session.SetParent(gate.Id, wall.Id);

        ParentMenu.Open(wall.Id);
        _ui.Frame(DrawPopup, new Vector2(300f, 200f), inWindow: false);
        _ui.Frame(DrawPopup, inWindow: false);
        _ui.Click(Centre(ParentMenu.ItemRect(gate.Id)), DrawPopup, inWindow: false);

        Assert.Null(_session.Scene.ParentOf(wall));
    }

    [Fact]
    public void ClearParentInTheListFreesIt()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");
        _session.SetParent(gate.Id, wall.Id);

        ParentMenu.Open(gate.Id);
        _ui.Frame(DrawPopup, new Vector2(300f, 200f), inWindow: false);
        _ui.Frame(DrawPopup, inWindow: false);
        _ui.Click(Centre(ParentMenu.ItemRect(0)), DrawPopup, inWindow: false);

        Assert.Null(_session.Scene.ParentOf(gate));
        Assert.Equal(new Vector3(8f, 0f, 0f), gate.Transform.Position);
    }

    [Fact]
    public void TheParentFieldPicksAParent()
    {
        VoxelObject tower = Named("Tower");
        VoxelObject gate = Named("Gate");
        void Draw() => ObjectPropertiesPanel.DrawRelations(_session, gate);

        _ui.Frame(Draw);
        _ui.Click(Centre(Props.RectOf("parent")), Draw);
        _ui.Frame(Draw);
        _ui.Click(Centre(ParentMenu.ItemRect(tower.Id)), Draw);

        Assert.Same(tower, _session.Scene.ParentOf(gate));
    }

    [Theory]
    [InlineData(KeymapPreset.Default)]
    [InlineData(KeymapPreset.MimicBusters)]
    public void CtrlPAndAltPAreBlendersKeys(KeymapPreset preset)
    {
        Keymap keymap = Keymap.For(preset);

        Assert.Equal(EditorAction.SetParent, keymap.ActionFor(KeyChord.Ctrl(Key.P)));
        Assert.Equal(EditorAction.ClearParent, keymap.ActionFor(KeyChord.AltOf(Key.P)));
    }

    // ---- In the viewport ---------------------------------------------------------------------------

    [Fact]
    public void RelationshipLinesRunFromChildToParent()
    {
        VoxelObject wall = Named("Wall");
        VoxelObject gate = Named("Gate");
        SceneLight lamp = _session.AddLight(LightKind.Point, new Vector3(4f, 3f, 0f), -Vector3.UnitY);
        var camera = new FlyCamera { Position = new Vector3(4f, 10f, 20f) };

        var none = new RecordingLines();
        EditorOverlays.AddRelationshipLines(none, _session.Scene, camera, lights: true);
        Assert.Equal(0, none.Hairlines);

        _session.SetParent(gate.Id, wall.Id);
        _session.SetParent(lamp.Id, wall.Id);

        var objects = new RecordingLines();
        EditorOverlays.AddRelationshipLines(objects, _session.Scene, camera, lights: false);
        Assert.True(objects.Hairlines > 2, "a dashed line is several dashes");

        // Every dash lies on the line between the two centres.
        Vector3 from = gate.WorldCentre();
        Vector3 to = wall.WorldCentre();
        foreach (Vector3 point in objects.HairlinePoints)
        {
            float along = Vector3.Dot(point - from, Vector3.Normalize(to - from));
            Assert.True(Vector3.Distance(from + (Vector3.Normalize(to - from) * along), point) < 1e-3f);
        }

        var withLights = new RecordingLines();
        EditorOverlays.AddRelationshipLines(withLights, _session.Scene, camera, lights: true);
        Assert.True(withLights.Hairlines > objects.Hairlines, "the light's line is missing");

        _session.SetObjectVisible(wall.Id, false);
        var hidden = new RecordingLines();
        EditorOverlays.AddRelationshipLines(hidden, _session.Scene, camera, lights: true);
        Assert.Equal(0, hidden.Hairlines);
    }

    /// <summary>
    /// Dragging a parent carries its children, and a snap sees through them as it does through the
    /// dragged object itself: a child's own corner cannot be the place its parent snaps to.
    /// </summary>
    [Fact]
    public void AGizmoDragCarriesTheChildrenAndSnapsPastThem()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 4; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        var scene = new VoxelScene();
        VoxelObject cube = scene.Add(grid, ObjectTransform.Identity, "cube");
        var block = new VoxelWorld();
        block.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        VoxelObject child = scene.Add(block, ObjectTransform.At(new Vector3(10f, 3f, 5f)), "child");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveTool = EditorTool.Transform;
        session.TransformMode = TransformMode.Move;
        session.ChooseObject(cube.Id);
        session.SetParent(child.Id, cube.Id);
        session.Snap.Targets = SnapTarget.Corner;

        var camera = new FlyCamera { Position = new Vector3(10f, 12f, 18f) };
        camera.LookAt(cube.WorldCentre());
        var transform = new TransformInteraction(session);
        var viewport = new Vector2(1280f, 720f);

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Assert.True(camera.TryProjectToScreen((start + end) * 0.5f, viewport, out Vector2 press));
        Assert.True(camera.TryProjectToScreen(new Vector3(10f, 3f, 5f), viewport, out Vector2 onChild));

        Assert.True(transform.OnPress(press, viewport, camera));
        transform.OnDrag(onChild, viewport, camera, snap: true);

        Assert.Null(transform.SnapPoint);
        Assert.NotEqual(0f, cube.Transform.Position.X);
        Assert.Equal(new Vector3(10f + cube.Transform.Position.X, 3f, 5f), child.Transform.Position);

        transform.OnRelease();
        session.Undo();
        Assert.Equal(new Vector3(10f, 3f, 5f), child.Transform.Position);
    }
}
