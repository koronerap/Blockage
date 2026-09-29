using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// The Properties tabs, driven through the headless ImGui: a drag on a field changes the thing it
/// shows and lands as one undo step, and a row of choice buttons chooses.
/// </summary>
[Collection(nameof(ImGuiCollection))]
public sealed class PropertiesTabTests : IDisposable
{
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(600f, 680f) };
    private readonly EditorSession _session = new();

    public PropertiesTabTests() => _session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);

    public void Dispose() => _ui.Dispose();

    private VoxelObject Cube => _session.Scene.Objects[0];

    private static Vector2 Centre(string id)
    {
        (Vector2 min, Vector2 max) = Props.RectOf(id) ?? throw new InvalidOperationException($"No field '{id}' was drawn.");
        return (min + max) * 0.5f;
    }

    private void Settle(Action draw)
    {
        _ui.Frame(draw);
        _ui.Frame(draw);
    }

    [Fact]
    public void DraggingLocationMovesTheObjectAsOneUndoStep()
    {
        void Draw() => ObjectPropertiesPanel.DrawContent(_session);
        Settle(Draw);
        Vector3 before = Cube.Transform.Position;
        int history = _session.History.UndoCount;

        Vector2 field = Centre("location-x");
        _ui.Drag(field, field + new Vector2(60f, 0f), Draw);
        _ui.Frame(Draw);

        Assert.True(Cube.Transform.Position.X > before.X);
        Assert.Equal(history + 1, _session.History.UndoCount);

        _session.Undo();
        Assert.Equal(before, Cube.Transform.Position);
    }

    /// <summary>The button under the counts cuts the voxels, keeps the object's size, and says how many there are now.</summary>
    [Fact]
    public void TheSubdivideButtonCutsTheVoxelsAndSaysSo()
    {
        _ui.WindowSize = new Vector2(600f, 1200f);
        void Draw() => ObjectPropertiesPanel.DrawContent(_session);
        Settle(Draw);

        _ui.Click(Centre("voxel-subdivide-0"), Draw);

        Assert.Equal(512 * 8, Cube.Grid.SolidCount);
        Assert.Equal(0.5f, Cube.VoxelSize);
        Assert.Equal("Subdivide", _session.History.NextUndoName);
        Assert.Contains(
            ReportLog.Shared.Recent,
            report => report.Text.Contains("512 voxels became 4,096", StringComparison.Ordinal));
    }

    [Fact]
    public void DraggingRotationTurnsTheObject()
    {
        void Draw() => ObjectPropertiesPanel.DrawContent(_session);
        Settle(Draw);

        Vector2 field = Centre("rotation-y");
        _ui.Drag(field, field + new Vector2(40f, 0f), Draw);
        _ui.Frame(Draw);

        Assert.NotEqual(Quaternion.Identity, Cube.Transform.Rotation);
        Assert.StartsWith("Rotate", _session.History.NextUndoName, StringComparison.Ordinal);
    }

    /// <summary>
    /// The angles shown are kept while they are being typed, but a turn made some other way — the
    /// gizmo, an undo — has to replace them. Otherwise the next angle typed would be written together
    /// with the old other two, and the turn made elsewhere would be lost.
    /// </summary>
    [Fact]
    public void ATurnMadeElsewhereIsWhatTheNextTypedAngleStartsFrom()
    {
        void Draw() => ObjectPropertiesPanel.DrawContent(_session);
        Settle(Draw);

        _session.ApplyTransform(Cube, Cube.Transform with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f) });
        Settle(Draw);

        Vector2 field = Centre("rotation-x");
        _ui.Drag(field, field + new Vector2(20f, 0f), Draw);
        _ui.Frame(Draw);

        Assert.Equal(90f, Rotations.ToEulerDegrees(Cube.Transform.Rotation).Y, 1);
    }

    [Fact]
    public void TheVoxelSizeIsItsOwnFieldAndOneUndoStep()
    {
        void Draw() => ObjectPropertiesPanel.DrawContent(_session);
        Settle(Draw);

        Vector2 field = Centre("voxel-size");
        _ui.Drag(field, field + new Vector2(50f, 0f), Draw);
        _ui.Frame(Draw);

        Assert.True(Cube.VoxelSize > 1f);
        Assert.StartsWith("Voxel size", _session.History.NextUndoName, StringComparison.Ordinal);

        _session.Undo();
        Assert.Equal(1f, Cube.VoxelSize);
    }

    [Fact]
    public void APickedLightShowsItsOwnSettingsAndItsTypeIsAButtonAway()
    {
        SceneLight sun = _session.Scene.Lights[0];
        _session.SelectLight(sun.Id);

        void Draw() => ObjectPropertiesPanel.DrawContent(_session);
        Settle(Draw);

        _ui.Click(Centre("light-kind-2"), Draw);
        _ui.Frame(Draw);

        Assert.Equal(LightKind.Spot, sun.Kind);
        Assert.Equal($"Edit {sun.Name}", _session.History.NextUndoName);

        _session.Undo();
        Assert.Equal(LightKind.Directional, sun.Kind);
    }

    [Fact]
    public void TheToolTabChoosesThePaintMode()
    {
        _session.ActiveTool = EditorTool.Paint;

        void Draw() => ToolPanel.DrawContent(_session);
        Settle(Draw);

        _ui.Click(Centre("paint-mode-1"), Draw);

        Assert.Equal(PaintMode.Bucket, _session.PaintMode);
    }

    [Fact]
    public void TheWorldTabSwitchesShading()
    {
        var viewport = new ViewportSettings();

        void Draw() => LightingPanel.DrawContent(_session, viewport, droppedLights: 0);
        Settle(Draw);

        _ui.Click(Centre("shading-1"), Draw);
        Assert.Equal(ShadingMode.Unlit, viewport.Shading);

        _ui.Click(Centre("shading-2"), Draw);
        Assert.Equal(ShadingMode.Wireframe, viewport.Shading);
    }

    /// <summary>Drawing a tab and leaving it alone must never change anything or leave history behind.</summary>
    [Theory]
    [InlineData(EditorTool.Transform)]
    [InlineData(EditorTool.Extrude)]
    [InlineData(EditorTool.Paint)]
    [InlineData(EditorTool.LoopCut)]
    public void LookingAtATabChangesNothing(EditorTool tool)
    {
        _session.ActiveTool = tool;
        ulong before = _session.Scene.ContentHash();

        for (int i = 0; i < 4; i++)
        {
            _ui.Frame(() =>
            {
                ToolPanel.DrawContent(_session);
                ObjectPropertiesPanel.DrawContent(_session);
                LevelPanel.DrawContent(_session);
            });
        }

        Assert.Equal(before, _session.Scene.ContentHash());
        Assert.False(_session.History.CanUndo);
        Assert.Equal(tool, _session.ActiveTool);
    }
}
