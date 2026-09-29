using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Import;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>The stencil (Fullreleaseplan 4.5): an image stretched over a box on the view, painted onto the faces seen through it.</summary>
public class StencilTests
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    /// <summary>A 4 × 4 × 1 wall facing the camera, and a 2 × 1 image: red on the left, see-through on the right.</summary>
    private static (EditorSession Session, VoxelObject Wall, FlyCamera Camera) Wall()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                grid.SetVoxel(x, y, 0, Palette.WhiteIndex);
            }
        }

        var scene = new VoxelScene();
        VoxelObject wall = scene.Add(grid, ObjectTransform.Identity, "Wall");
        var session = new EditorSession { ActiveTool = EditorTool.Paint, PaintMode = PaintMode.Stencil };
        session.ReplaceScene(scene, projectPath: null);
        session.Pattern = PatternSource.FromImage("half", new DecodedImage(2, 1, [new Color32(255, 0, 0), new Color32(0, 0, 0, 0)]));

        var camera = new FlyCamera { Position = new Vector3(2f, 2f, 12f) };
        camera.LookAt(new Vector3(2f, 2f, 1f));
        return (session, wall, camera);
    }

    private static Vector2 ScreenOf(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    [Fact]
    public void OnlyTheFacesSeenThroughTheBoxAreFound()
    {
        (_, VoxelObject wall, FlyCamera camera) = Wall();
        Vector2 min = ScreenOf(camera, new Vector3(0f, 4f, 1f));
        Vector2 max = ScreenOf(camera, new Vector3(4f, 0f, 1f));

        var faces = StencilProjection.FacesSeenIn(wall, camera, Viewport, min, max).ToList();

        // The sixteen front faces; the back of the wall and the sides are not seen head on.
        Assert.Equal(16, faces.Count);
        Assert.All(faces, f => Assert.Equal(Face.PosZ, f.Face));
        Assert.All(faces, f => Assert.InRange(f.U, 0f, 1f));
    }

    [Fact]
    public void TheImageIsPaintedWhereItIsOpaqueAndNotWhereItIsSeeThrough()
    {
        (EditorSession session, VoxelObject wall, FlyCamera camera) = Wall();
        Vector2 min = ScreenOf(camera, new Vector3(0f, 4f, 1f));
        Vector2 max = ScreenOf(camera, new Vector3(4f, 0f, 1f));

        Assert.True(session.ProjectPattern(StencilProjection.FacesSeenIn(wall, camera, Viewport, min, max)));

        byte red = session.Scene.Palette.Nearest(new Color32(255, 0, 0));
        Assert.Equal(red, wall.Grid.GetFaceColor(new Int3(0, 1, 0), Face.PosZ));
        Assert.Equal(Palette.WhiteIndex, wall.Grid.GetFaceColor(new Int3(3, 1, 0), Face.PosZ));
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void WithNoImageLoadedNothingIsPainted()
    {
        (EditorSession session, _, _) = Wall();
        session.Pattern = null;

        Assert.False(session.ProjectPattern([(Int3.Zero, Face.PosZ, 0.1f, 0.1f)]));
    }
}
