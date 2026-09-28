using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>Overlay geometry kept in memory, so what would be drawn can be counted.</summary>
internal sealed class RecordingLines : LineGeometry
{
    /// <summary>Tinted faces: each is two triangles.</summary>
    public int Fills => FillVertices.Length / 6;

    /// <summary>Thick strokes: each is two triangles. Nothing counted here draws a cone.</summary>
    public int Strokes => QuadVertices.Length / 6;

    public IEnumerable<Vector3> FillPoints => FillVertices.ToArray().Select(v => v.Position);

    public IEnumerable<uint> StrokeColours => QuadVertices.ToArray().Select(v => v.Rgba).Distinct();
}

/// <summary>Faces drawn as one surface: a tint over all of them and a line round the outside only.</summary>
public class FacePatchTests
{
    private static int StrokesFor(IReadOnlyCollection<Int3> cells, Face face)
    {
        var lines = new RecordingLines();
        var set = new HashSet<Int3>(cells);
        EditorOverlays.AddFacePatch(lines, set, set.Contains, face, EditorOverlays.Selection, EditorOverlays.SelectionWidth);
        return lines.Strokes;
    }

    [Fact]
    public void ASquareIsOutlinedRoundItsEdgeAndNotBetweenItsFaces()
    {
        var cells = new List<Int3>();
        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 3; z++)
            {
                cells.Add(new Int3(x, 0, z));
            }
        }

        var lines = new RecordingLines();
        var set = new HashSet<Int3>(cells);
        EditorOverlays.AddFacePatch(lines, set, set.Contains, Face.PosY, EditorOverlays.Selection, 1f);

        Assert.Equal(9, lines.Fills);
        Assert.Equal(12, lines.Strokes);
    }

    [Fact]
    public void AnLIsOutlinedRoundItsEightEdges()
    {
        Assert.Equal(8, StrokesFor([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], Face.PosY));
    }

    /// <summary>Every direction knows which of its edges face which neighbour.</summary>
    [Theory]
    [InlineData(Face.PosX)]
    [InlineData(Face.NegX)]
    [InlineData(Face.PosY)]
    [InlineData(Face.NegY)]
    [InlineData(Face.PosZ)]
    [InlineData(Face.NegZ)]
    public void TwoByTwoInAnyDirectionIsARingOfEight(Face face)
    {
        int axis = FaceInfo.Axis(face);
        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;

        var cells = new List<Int3>();
        for (int a = 0; a < 2; a++)
        {
            for (int b = 0; b < 2; b++)
            {
                Int3 cell = VoxelBox.WithComponent(new Int3(5, 5, 5), u, 5 + a);
                cells.Add(VoxelBox.WithComponent(cell, v, 5 + b));
            }
        }

        Assert.Equal(8, StrokesFor(cells, face));
    }

    [Fact]
    public void PastTheCapAPatchIsItsBounds()
    {
        var cells = new HashSet<Int3>();
        for (int x = 0; cells.Count <= EditorOverlays.MaxOutlinedFaces; x++)
        {
            for (int z = 0; z < 200; z++)
            {
                cells.Add(new Int3(x, 0, z));
            }
        }

        var lines = new RecordingLines();
        EditorOverlays.AddFacePatch(lines, cells, cells.Contains, Face.PosY, EditorOverlays.Selection, 1f);

        Assert.Equal(0, lines.Fills);
        Assert.Equal(12, lines.Strokes);
    }
}

/// <summary>How Extrude's surfaces, its hover and its arrow are drawn, and pressing on the selection.</summary>
public class ExtrudeDisplayTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    private readonly EditorSession _session = new();
    private readonly ExtrudeInteraction _extrude;
    private readonly FlyCamera _camera = new() { Position = new Vector3(14f, 14f, 14f) };
    private readonly VoxelObject _cube;

    /// <summary>A 4³ cube with its top face selected, seen from above a corner.</summary>
    public ExtrudeDisplayTests()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    grid.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        var scene = new VoxelScene();
        _cube = scene.Add(grid, ObjectTransform.Identity, "cube");
        _session.ReplaceScene(scene, projectPath: null);
        _session.ActiveTool = EditorTool.Extrude;
        _session.ExtrudeSelectionMode = ExtrudeSelectionMode.Box;
        _session.SelectPatch(new RaycastHit(new Int3(2, 3, 2), Face.PosY, 1f));

        _camera.LookAt(new Vector3(2f, 2f, 2f));
        _extrude = new ExtrudeInteraction(_session);
    }

    private ScenePick Pick(Int3 voxel, Face face) => new(_cube, new RaycastHit(voxel, face, 5f), 5f);

    private Vector2 ScreenOf(Vector3 world)
    {
        Assert.True(_camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    /// <summary>A corner of the top off the camera's diagonal, so well away from the arrow on screen.</summary>
    private static readonly Vector3 TopCorner = new(3.5f, 4f, 0.5f);

    private static readonly Int3 TopCornerCell = new(3, 3, 0);

    [Fact]
    public void PressingOnTheSelectionPullsIt()
    {
        int before = _session.World.SolidCount;
        Vector2 press = ScreenOf(TopCorner);

        (Vector3 start, Vector3 end) = _extrude.Arrow()!.Value;
        Vector2 a = ScreenOf(start);
        Vector2 b = ScreenOf(end);
        float along = Math.Clamp(Vector2.Dot(press - a, b - a) / (b - a).LengthSquared(), 0f, 1f);
        Assert.True(Vector2.Distance(press, a + ((b - a) * along)) > 30f, "the press must not be on the arrow");

        _extrude.OnPress(Pick(TopCornerCell, Face.PosY), press, Viewport, _camera, shift: false, alt: false);
        Assert.True(_extrude.IsDraggingArrow);
        Assert.False(_extrude.IsSelecting);

        _extrude.OnDrag(null, ScreenOf(TopCorner + (Vector3.UnitY * 2f)), Viewport, _camera);
        Assert.Equal(2, _session.ExtrudeSteps);

        _extrude.OnRelease();
        Assert.Equal(before + 32, _session.World.SolidCount);
    }

    [Fact]
    public void WithShiftAPressOnTheSelectionAddsToItInstead()
    {
        _extrude.OnPress(Pick(TopCornerCell, Face.PosY), ScreenOf(TopCorner), Viewport, _camera, shift: true, alt: false);

        Assert.True(_extrude.IsSelecting);
        Assert.False(_extrude.IsDraggingArrow);
    }

    [Fact]
    public void APressOffTheSelectionStartsANewOne()
    {
        _extrude.OnPress(Pick(new Int3(3, 1, 1), Face.PosX), ScreenOf(new Vector3(4f, 1.5f, 1.5f)), Viewport, _camera, shift: false, alt: false);

        Assert.True(_extrude.IsSelecting);
        Assert.False(_extrude.IsDraggingArrow);
    }

    /// <summary>A plain drag replaces the selection when it lands, so the old one and its arrow go at once.</summary>
    [Fact]
    public void DraggingOutAReplacementHidesTheOldSelectionAndItsArrow()
    {
        _extrude.OnPress(Pick(new Int3(3, 1, 1), Face.PosX), ScreenOf(new Vector3(4f, 1.5f, 1.5f)), Viewport, _camera, shift: false, alt: false);

        var lines = new RecordingLines();
        EditorOverlays.AddExtrudeSelection(lines, _session, _extrude);
        EditorOverlays.AddExtrudeArrow(lines, _session, _extrude);

        Assert.Equal(1, lines.Fills);
        Assert.Equal(4, lines.Strokes);
    }

    [Fact]
    public void AddingKeepsTheOldSelectionInSightAndDrawsTheNewPartBlue()
    {
        _extrude.OnPress(Pick(new Int3(3, 1, 1), Face.PosX), ScreenOf(new Vector3(4f, 1.5f, 1.5f)), Viewport, _camera, shift: true, alt: false);

        var lines = new RecordingLines();
        EditorOverlays.AddExtrudeSelection(lines, _session, _extrude);

        Assert.Equal(16 + 1, lines.Fills);
        Assert.Contains(EditorOverlays.SelectionAdd.Rgba, lines.StrokeColours);
        Assert.Contains(EditorOverlays.Selection.Rgba, lines.StrokeColours);
    }

    /// <summary>While the arrow is pulled, the selection is drawn on the faces being made, not left behind inside them.</summary>
    [Fact]
    public void TheSelectionRidesOutWithThePull()
    {
        _session.PreviewExtrude(2);

        var lines = new RecordingLines();
        EditorOverlays.AddExtrudeSelection(lines, _session, _extrude);

        Assert.Equal(16, lines.Fills);
        Assert.All(lines.FillPoints, point => Assert.Equal(6f + EditorOverlays.PatchOffset, point.Y, 3));
        Assert.True(_extrude.IsOnSelection(new RaycastHit(new Int3(1, 5, 1), Face.PosY, 1f)));
        Assert.False(_extrude.IsOnSelection(new RaycastHit(new Int3(1, 3, 1), Face.PosY, 1f)));
    }

    [Theory]
    [InlineData(SelectionOperation.Add)]
    [InlineData(SelectionOperation.Subtract)]
    [InlineData(SelectionOperation.Replace)]
    public void TheHoveredFaceIsInTheColourOfWhatAClickWouldDo(SelectionOperation operation)
    {
        var lines = new RecordingLines();
        EditorOverlays.AddExtrudeHover(lines, _session, _extrude, new RaycastHit(new Int3(3, 1, 1), Face.PosX, 1f), operation);

        Assert.Equal([EditorOverlays.SelectionColour(operation).Rgba], lines.StrokeColours);
    }

    [Fact]
    public void AddAndSubtractAreNotTheColourOfAPlainHover()
    {
        Assert.NotEqual(EditorOverlays.Highlight, EditorOverlays.SelectionAdd);
        Assert.NotEqual(EditorOverlays.Highlight, EditorOverlays.SelectionSubtract);
        Assert.NotEqual(EditorOverlays.SelectionAdd, EditorOverlays.SelectionSubtract);
    }

    /// <summary>Over the selection a press pulls rather than chooses, so no face is marked as if to be chosen.</summary>
    [Fact]
    public void OverTheSelectionTheHoverStepsAsideForThePull()
    {
        var hit = new RaycastHit(new Int3(1, 3, 1), Face.PosY, 1f);

        var plain = new RecordingLines();
        EditorOverlays.AddExtrudeHover(plain, _session, _extrude, hit, SelectionOperation.Replace);
        Assert.Equal(0, plain.Strokes);
        Assert.True(_extrude.WouldPull(Pick(hit.Voxel, hit.Face), new Vector2(-500f), Viewport, _camera, shift: false, alt: false));

        var adding = new RecordingLines();
        EditorOverlays.AddExtrudeHover(adding, _session, _extrude, hit, SelectionOperation.Add);
        Assert.Equal(4, adding.Strokes);
        Assert.False(_extrude.WouldPull(Pick(hit.Voxel, hit.Face), new Vector2(-500f), Viewport, _camera, shift: true, alt: false));
    }
}

/// <summary>The brush's footprint and the loop cut's ring.</summary>
public class PaintAndCutPreviewTests
{
    private static EditorSession SessionWith(VoxelWorld grid, EditorTool tool)
    {
        var session = new EditorSession();
        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "model");
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveTool = tool;
        return session;
    }

    [Fact]
    public void TheBrushPreviewTintsTheFacesItWouldPaint()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 9; x++)
        {
            for (int z = 0; z < 9; z++)
            {
                grid.SetVoxel(x, 0, z, Palette.WhiteIndex);
            }
        }

        EditorSession session = SessionWith(grid, EditorTool.Paint);
        session.BrushRadius = 3f;

        var lines = new RecordingLines();
        EditorOverlays.AddBrushPreview(lines, session, new RaycastHit(new Int3(4, 0, 4), Face.PosY, 1f));

        Assert.Equal(29, lines.Fills);
    }

    /// <summary>
    /// A post on a floor, cut where it stands on it: the ring goes round the post, not round the
    /// floor's bounds and not round the floor either, which ends at the plane rather than crossing it.
    /// </summary>
    [Fact]
    public void TheCutPreviewRingsOnlyWhatItCuts()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 6; x++)
        {
            for (int z = 0; z < 6; z++)
            {
                grid.SetVoxel(x, 0, z, Palette.WhiteIndex);
            }
        }

        for (int y = 1; y < 5; y++)
        {
            grid.SetVoxel(0, y, 0, Palette.WhiteIndex);
        }

        EditorSession session = SessionWith(grid, EditorTool.LoopCut);
        session.PreviewCutPlane = new CutPlane(Axis.Y, 1);

        var lines = new RecordingLines();
        EditorOverlays.AddCutPreview(lines, session);

        Assert.Equal(1, lines.Fills);
        Assert.Equal(4, lines.Strokes);
        Assert.All(lines.FillPoints, point => Assert.Equal(1f, point.Y, 4));
        Assert.All(lines.FillPoints, point => Assert.InRange(point.X, 0f, 1f));
    }
}

/// <summary>Dragging a sun's or a spot's aim line onto the model to point it there.</summary>
public class LightAimTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    private readonly EditorSession _session = new();
    private readonly FlyCamera _camera = new() { Position = new Vector3(14f, 14f, 14f) };
    private readonly LightAimInteraction _aim;
    private readonly SceneLight _sun;

    public LightAimTests()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    grid.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "cube");
        _session.ReplaceScene(scene, projectPath: null);
        _camera.LookAt(new Vector3(2f, 2f, 2f));

        _sun = _session.AddLight(LightKind.Directional, new Vector3(-4f, 10f, 2f), -Vector3.UnitY);
        _session.ClearLightSelection();
        _aim = new LightAimInteraction(_session);
    }

    private Vector2 ScreenOf(Vector3 world)
    {
        Assert.True(_camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    private static bool Shown(SceneLight light) => true;

    private Vector2 Handle() => ScreenOf(EditorOverlays.AimHandle(_sun, _camera)!.Value);

    [Fact]
    public void DroppingTheLineOnTheModelPointsTheLightAtThatSpot()
    {
        var spot = new Vector3(3.5f, 4f, 0.5f);

        Assert.True(_aim.OnPress(Handle(), Viewport, _camera, Shown));
        _aim.OnDrag(ScreenOf(spot), Viewport, _camera);

        Vector3 expected = Vector3.Normalize(spot - _sun.Position);
        Assert.True(Vector3.Dot(expected, _sun.Direction) > 0.9995f, $"shines along {_sun.Direction}, wanted {expected}");
        Assert.Equal(_sun.Id, _session.SelectedLightId);

        _aim.OnRelease();
        Assert.Equal("Aim Sun", _session.History.NextUndoName);

        _session.Undo();
        Assert.True(Vector3.Dot(-Vector3.UnitY, _sun.Direction) > 0.9999f);
    }

    [Fact]
    public void OffTheModelTheLineFallsOnTheGround()
    {
        var ground = new Vector3(-6f, 0f, 9f);

        Assert.True(_aim.OnPress(Handle(), Viewport, _camera, Shown));
        _aim.OnDrag(ScreenOf(ground), Viewport, _camera);

        Assert.True(Vector3.Dot(Vector3.Normalize(ground - _sun.Position), _sun.Direction) > 0.9995f);
    }

    [Fact]
    public void EscapePutsTheLightBack()
    {
        Assert.True(_aim.OnPress(Handle(), Viewport, _camera, Shown));
        _aim.OnDrag(ScreenOf(new Vector3(3.5f, 4f, 0.5f)), Viewport, _camera);
        _aim.Cancel();

        Assert.True(Vector3.Dot(-Vector3.UnitY, _sun.Direction) > 0.9999f);
        Assert.False(_aim.IsAiming);
        Assert.False(_session.History.NextUndoName?.StartsWith("Aim", StringComparison.Ordinal) ?? false);
    }

    [Fact]
    public void APressAwayFromTheLineIsLeftForTheGizmo()
    {
        Assert.False(_aim.OnPress(Handle() + new Vector2(60f, 0f), Viewport, _camera, Shown));
        Assert.False(_aim.IsAiming);
    }

    /// <summary>The part of the line on the icon is the icon's: a press there is for the move gizmo.</summary>
    [Fact]
    public void TheLineDoesNotGrabRightAtTheLight()
    {
        Assert.False(_aim.OnPress(ScreenOf(_sun.Position + (_sun.Direction * 0.05f)), Viewport, _camera, Shown));
    }

    [Fact]
    public void AHiddenLightsLineCannotBeTaken()
    {
        Assert.False(_aim.OnPress(Handle(), Viewport, _camera, _ => false));
    }

    [Fact]
    public void ABulbHasNothingToAim()
    {
        SceneLight bulb = _session.AddLight(LightKind.Point, new Vector3(0f, 6f, 0f), -Vector3.UnitY);

        Assert.Null(EditorOverlays.AimHandle(bulb, _camera));
        Assert.NotNull(EditorOverlays.AimHandle(_sun, _camera));
    }
}
