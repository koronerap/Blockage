using System.Numerics;
using EditorApp.Ui;

namespace EditorApp.Tests;

/// <summary>
/// An <see cref="IIconCanvas"/> that draws nothing and remembers every point it was asked to touch.
/// </summary>
internal sealed class RecordingCanvas : IIconCanvas
{
    public List<Vector2> Points { get; } = [];

    public int Shapes { get; private set; }

    public void Line(Vector2 from, Vector2 to, float width = 1f)
    {
        Points.Add(from);
        Points.Add(to);
        Shapes++;
    }

    public void Polyline(ReadOnlySpan<Vector2> points, float width = 1f)
    {
        foreach (Vector2 point in points)
        {
            Points.Add(point);
        }

        Shapes++;
    }

    public void Rect(Vector2 min, Vector2 max, float rounding = 0f, float width = 1f)
    {
        Points.Add(min);
        Points.Add(max);
        Shapes++;
    }

    public void FilledRect(Vector2 min, Vector2 max, float rounding = 0f)
    {
        Points.Add(min);
        Points.Add(max);
        Shapes++;
    }

    public void Circle(Vector2 centre, float radius, float width = 1f) => Round(centre, radius);

    public void FilledCircle(Vector2 centre, float radius) => Round(centre, radius);

    private void Round(Vector2 centre, float radius)
    {
        Points.Add(centre - new Vector2(radius));
        Points.Add(centre + new Vector2(radius));
        Shapes++;
    }

    public void FilledTriangle(Vector2 a, Vector2 b, Vector2 c)
    {
        Points.Add(a);
        Points.Add(b);
        Points.Add(c);
        Shapes++;
    }
}

/// <summary>
/// The icon set is now shared with the Android head, which means a mistake in one of these shapes
/// shows up in two places at once and can only be seen by eye in either. These are the things worth
/// asking without eyes.
/// </summary>
public class IconTests
{
    public static TheoryData<string, Icons.Painter> All() => new()
    {
        { nameof(Icons.Move), Icons.Move },
        { nameof(Icons.Extrude), Icons.Extrude },
        { nameof(Icons.Paint), Icons.Paint },
        { nameof(Icons.Cut), Icons.Cut },
        { nameof(Icons.ViewTool), Icons.ViewTool },
        { nameof(Icons.Lock), Icons.Lock },
        { nameof(Icons.Unlocked), Icons.Unlocked },
        { nameof(Icons.Rotate), Icons.Rotate },
        { nameof(Icons.Global), Icons.Global },
        { nameof(Icons.Local), Icons.Local },
        { nameof(Icons.BoxSelect), Icons.BoxSelect },
        { nameof(Icons.FaceSelect), Icons.FaceSelect },
        { nameof(Icons.Brush), Icons.Brush },
        { nameof(Icons.Bucket), Icons.Bucket },
        { nameof(Icons.Eyedropper), Icons.Eyedropper },
        { nameof(Icons.Pattern), Icons.Pattern },
        { nameof(Icons.Grid), Icons.Grid },
        { nameof(Icons.Measure), Icons.Measure },
        { nameof(Icons.RotateRight), Icons.RotateRight },
        { nameof(Icons.RotateLeft), Icons.RotateLeft },
        { nameof(Icons.RotateUp), Icons.RotateUp },
        { nameof(Icons.RotateDown), Icons.RotateDown },
        { nameof(Icons.Lit), Icons.Lit },
        { nameof(Icons.Unlit), Icons.Unlit },
        { nameof(Icons.Undo), Icons.Undo },
        { nameof(Icons.Redo), Icons.Redo },
        { nameof(Icons.Adjust), Icons.Adjust },
        { nameof(Icons.Files), Icons.Files },
        { nameof(Icons.ChevronUp), Icons.ChevronUp },
        { nameof(Icons.ChevronDown), Icons.ChevronDown },
        { nameof(Icons.Plus), Icons.Plus },
        { nameof(Icons.Minus), Icons.Minus },
        { nameof(Icons.Close), Icons.Close },
        { nameof(Icons.Perspective), Icons.Perspective },
        { nameof(Icons.Orthographic), Icons.Orthographic },
        { nameof(Icons.FrameAll), Icons.FrameAll },
        { nameof(Icons.Pan), Icons.Pan },
        { nameof(Icons.Zoom), Icons.Zoom },
        { nameof(Icons.MouseLeft), Icons.MouseLeft },
        { nameof(Icons.MouseMiddle), Icons.MouseMiddle },
        { nameof(Icons.MouseRight), Icons.MouseRight },
        { nameof(Icons.MouseWheel), Icons.MouseWheel },
        { nameof(Icons.Eye), Icons.Eye },
        { nameof(Icons.EyeClosed), Icons.EyeClosed },
        { nameof(Icons.LightSun), Icons.LightSun },
        { nameof(Icons.LightPoint), Icons.LightPoint },
        { nameof(Icons.LightSpot), Icons.LightSpot },
        { nameof(Icons.ObjectTab), Icons.ObjectTab },
        { nameof(Icons.WorldTab), Icons.WorldTab },
        { nameof(Icons.PaletteTab), Icons.PaletteTab },
        { nameof(Icons.ReferenceTab), Icons.ReferenceTab },
        { nameof(Icons.Overlays), Icons.Overlays },
        { nameof(Icons.NewObject), Icons.NewObject },
        { nameof(Icons.Magnet), Icons.Magnet },
        { nameof(Icons.SnapIncrement), Icons.SnapIncrement },
        { nameof(Icons.SnapCorner), Icons.SnapCorner },
        { nameof(Icons.SnapEdge), Icons.SnapEdge },
        { nameof(Icons.SnapSurface), Icons.SnapSurface },
        { nameof(Icons.ShadingWire), Icons.ShadingWire },
        { nameof(Icons.ShadingSolid), Icons.ShadingSolid },
        { nameof(Icons.XRay), Icons.XRay },
        { nameof(Icons.Gizmo), Icons.Gizmo },
        { nameof(Icons.ChevronRight), Icons.ChevronRight },
        { nameof(Icons.Search), Icons.Search },
        { nameof(Icons.NewFile), Icons.NewFile },
        { nameof(Icons.Document), Icons.Document },
        { nameof(Icons.Folder), Icons.Folder },
        { nameof(Icons.Recover), Icons.Recover },
        { nameof(Icons.Repository), Icons.Repository },
        { nameof(Icons.Keyboard), Icons.Keyboard },
        { nameof(Icons.TemplateCube), Icons.TemplateCube },
        { nameof(Icons.TemplateVoxel), Icons.TemplateVoxel },
        { nameof(Icons.TemplateGround), Icons.TemplateGround },
        { nameof(Icons.TemplateRoom), Icons.TemplateRoom },
        { nameof(Icons.ShapeWall), Icons.ShapeWall },
        { nameof(Icons.ShapeSphere), Icons.ShapeSphere },
        { nameof(Icons.ShapeCylinder), Icons.ShapeCylinder },
        { nameof(Icons.ShapeCone), Icons.ShapeCone },
        { nameof(Icons.ShapePyramid), Icons.ShapePyramid },
        { nameof(Icons.ShapeTorus), Icons.ShapeTorus },
        { nameof(Icons.ShapeStairs), Icons.ShapeStairs },
        { nameof(Icons.ShapeArch), Icons.ShapeArch },
        { nameof(Icons.PropCrate), Icons.PropCrate },
        { nameof(Icons.PropBarrel), Icons.PropBarrel },
        { nameof(Icons.PropTable), Icons.PropTable },
        { nameof(Icons.PropChair), Icons.PropChair },
        { nameof(Icons.PropTree), Icons.PropTree },
        { nameof(Icons.PropFence), Icons.PropFence },
    };

    [Theory]
    [MemberData(nameof(All))]
    public void EveryIconDrawsSomething(string name, Icons.Painter painter)
    {
        var canvas = new RecordingCanvas();

        painter(canvas, new Vector2(100f, 100f), 20f);

        Assert.True(canvas.Shapes > 0, $"{name} drew nothing at all.");
    }

    /// <summary>
    /// Both heads call a painter with a radius of three tenths of the button's side, so the button
    /// gives it five thirds of that radius in every direction. An icon that reaches past it is
    /// clipped on one screen and overlaps its neighbour on the other.
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public void NoIconReachesOutsideItsButton(string name, Icons.Painter painter)
    {
        var centre = new Vector2(100f, 100f);
        const float Radius = 20f;
        const float Limit = Radius * 5f / 3f;

        var canvas = new RecordingCanvas();
        painter(canvas, centre, Radius);

        foreach (Vector2 point in canvas.Points)
        {
            Vector2 offset = point - centre;
            Assert.True(
                MathF.Abs(offset.X) <= Limit && MathF.Abs(offset.Y) <= Limit,
                $"{name} reaches {offset} from its centre, past the {Limit} the button allows.");
        }
    }

    /// <summary>
    /// A painter is called with whatever size the button happens to be, and a bar can be laid out
    /// before it has one. Zero has to come back as nothing drawn rather than as NaN.
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public void AnIconAtZeroSizeProducesNoNonsense(string name, Icons.Painter painter)
    {
        var canvas = new RecordingCanvas();

        painter(canvas, new Vector2(50f, 50f), 0f);

        foreach (Vector2 point in canvas.Points)
        {
            Assert.True(
                float.IsFinite(point.X) && float.IsFinite(point.Y),
                $"{name} produced {point} at zero size.");
        }
    }

    /// <summary>
    /// The icons scale rather than being drawn at a fixed size, so doubling the radius has to double
    /// how far every part of the shape sits from the centre.
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public void AnIconScalesWithItsRadius(string name, Icons.Painter painter)
    {
        var centre = new Vector2(100f, 100f);

        var small = new RecordingCanvas();
        var large = new RecordingCanvas();
        painter(small, centre, 10f);
        painter(large, centre, 20f);

        Assert.Equal(small.Points.Count, large.Points.Count);

        for (int i = 0; i < small.Points.Count; i++)
        {
            Vector2 expected = centre + ((small.Points[i] - centre) * 2f);
            Assert.True(
                Vector2.Distance(expected, large.Points[i]) < 0.01f,
                $"{name} point {i} moved to {large.Points[i]}, not the {expected} scaling would give.");
        }
    }
}
