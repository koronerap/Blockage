using System.Numerics;

namespace EditorApp.Ui;

/// <summary>
/// The editor's pictograms, described as shapes rather than loaded from an icon font.
///
/// A font would mean shipping a binary, matching its glyph ranges to an atlas, and living with
/// whatever pictograms it happens to contain — and half of these have no equivalent in any general
/// set: an extrude, a loop cut, a face selection. These are a handful of shapes for a fixed set of
/// tools, they scale to any button size, and they take their colour from whatever draws them.
///
/// Nothing here knows what a draw list or a canvas is. That is what lets the desktop and the phone
/// show the same icon rather than two drawings of the same idea.
/// </summary>
public static class Icons
{
    /// <summary>Signature every icon shares: a canvas, a centre, and how big to be.</summary>
    public delegate void Painter(IIconCanvas canvas, Vector2 centre, float radius);

    // ---- Tools -------------------------------------------------------------------------------
    //
    // The four tools share one drawing: solid heads and faces, strokes of one weight, and the two
    // that act on the model in three dimensions — Extrude and Loop Cut — drawn as the same small
    // isometric cube, so they read as a family and as operations on a block. The first set drew each
    // tool its own way, in flat outlines, and at button size they read as a stamp, a lollipop and a
    // battery.

    /// <summary>Transform: four solid-headed arrows out of a centre, the universal "move this".</summary>
    public static void Move(IIconCanvas canvas, Vector2 centre, float r)
    {
        Span<Vector2> directions = [new(0f, -1f), new(0f, 1f), new(-1f, 0f), new(1f, 0f)];

        foreach (Vector2 direction in directions)
        {
            Vector2 tip = centre + (direction * r);
            canvas.Line(centre + (direction * r * 0.12f), tip - (direction * r * 0.3f));
            Arrowhead(canvas, tip, direction, r * 0.46f);
        }

        canvas.FilledRect(centre - new Vector2(r * 0.14f), centre + new Vector2(r * 0.14f));
    }

    /// <summary>
    /// Extrude: a block with its top face chosen — filled, as a selection is — and an arrow rising
    /// well clear of it. The face and the pull, which is the whole of the tool.
    /// </summary>
    public static void Extrude(IIconCanvas canvas, Vector2 centre, float r)
    {
        Block block = IsoBlock(centre + new Vector2(0f, r * 0.2f), r * 0.74f, r * 0.4f);

        // The chosen face, then the block's visible edges over it.
        canvas.FilledTriangle(block.Top, block.Right, block.Front);
        canvas.FilledTriangle(block.Top, block.Front, block.Left);
        DrawBlock(canvas, block, 1f);

        Vector2 tip = new(centre.X, centre.Y - r);
        canvas.Line(new Vector2(centre.X, block.Top.Y + (r * 0.1f)), tip + new Vector2(0f, r * 0.4f), 1.25f);
        Arrowhead(canvas, tip, new Vector2(0f, -1f), r * 0.5f);
    }

    /// <summary>
    /// Paint: a brush, handle up and to the right, bristles to a point at the lower left — the way a
    /// brush tool has been drawn since the first paint programs, and why it is recognised at a glance.
    /// </summary>
    public static void Paint(IIconCanvas canvas, Vector2 centre, float r)
    {
        var along = Vector2.Normalize(new Vector2(-1f, 1f));
        var across = new Vector2(-along.Y, along.X);

        Vector2 handleEnd = centre - (along * r * 0.92f);
        Vector2 ferrule = centre - (along * r * 0.02f);
        Vector2 bristles = centre + (along * r * 0.24f);
        Vector2 point = centre + (along * r * 1.08f);

        canvas.Line(handleEnd, ferrule, 1.1f);
        canvas.FilledCircle(handleEnd, r * 0.1f);
        canvas.Line(ferrule, bristles, 2.8f);

        // The bristles: full where they leave the ferrule, drawn to a point, bent a touch the way a
        // loaded brush lies on the paper.
        float wide = r * 0.3f;
        Vector2 belly = bristles + (along * r * 0.34f) + (across * r * 0.06f);
        canvas.FilledTriangle(bristles + (across * wide), bristles - (across * wide), belly);
        canvas.FilledTriangle(bristles + (across * wide), belly, point);
        canvas.FilledTriangle(bristles - (across * wide), belly, point);
        canvas.FilledCircle(belly, wide * 0.92f);
    }

    /// <summary>
    /// Loop Cut: the same block, with a ring round its middle drawn heavy and running out past its
    /// sides — the section the cut goes through, as the tool's own preview rings it, and as Blender
    /// draws its loop cut.
    /// </summary>
    public static void Cut(IIconCanvas canvas, Vector2 centre, float r)
    {
        Block block = IsoBlock(centre + new Vector2(0f, -r * 0.34f), r * 0.8f, r * 1.02f);
        DrawBlock(canvas, block, 0.75f);

        Vector2 down = new(0f, block.Height * 0.52f);
        Vector2 left = block.Left + down;
        Vector2 front = block.Front + down;
        Vector2 right = block.Right + down;
        // Along the block's own edges, from constants rather than from the corners: at size zero
        // the corners coincide, and a direction taken from them would be NaN.
        Vector2 overhang = IsoLeftward * r * 0.3f;
        Vector2 overhangRight = new Vector2(-IsoLeftward.X, IsoLeftward.Y) * r * 0.3f;

        canvas.Polyline([left + overhang, front, right + overhangRight], 2f);
    }

    /// <summary>View: a camera — the tool that only ever moves the camera.</summary>
    public static void ViewTool(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Rect(centre + new Vector2(-r * 0.9f, -r * 0.45f), centre + new Vector2(r * 0.9f, r * 0.72f), r * 0.18f);
        canvas.FilledRect(centre + new Vector2(-r * 0.36f, -r * 0.72f), centre + new Vector2(r * 0.2f, -r * 0.45f), r * 0.08f);
        canvas.Circle(centre + new Vector2(0f, r * 0.14f), r * 0.34f);
        canvas.FilledCircle(centre + new Vector2(0f, r * 0.14f), r * 0.14f);
    }

    /// <summary>From a block's front corner towards its left one: the slope of every receding edge.</summary>
    private static readonly Vector2 IsoLeftward = Vector2.Normalize(new Vector2(-1f, -0.5f));

    /// <summary>The corners of a block seen from above one of its vertical edges.</summary>
    private readonly record struct Block(Vector2 Top, Vector2 Right, Vector2 Front, Vector2 Left, float Height);

    /// <param name="topCentre">Where the middle of the top face is.</param>
    /// <param name="halfWidth">Half the block's width on screen; the top face is half as tall as it is wide.</param>
    /// <param name="height">How far the sides run down.</param>
    private static Block IsoBlock(Vector2 topCentre, float halfWidth, float height) => new(
        topCentre - new Vector2(0f, halfWidth * 0.5f),
        topCentre + new Vector2(halfWidth, 0f),
        topCentre + new Vector2(0f, halfWidth * 0.5f),
        topCentre - new Vector2(halfWidth, 0f),
        height);

    /// <summary>The nine edges of a block that can be seen from above it.</summary>
    private static void DrawBlock(IIconCanvas canvas, Block block, float width)
    {
        Vector2 down = new(0f, block.Height);

        canvas.Polyline([block.Top, block.Right, block.Front, block.Left, block.Top], width);
        canvas.Polyline([block.Left, block.Left + down, block.Front + down, block.Right + down, block.Right], width);
        canvas.Line(block.Front, block.Front + down, width);
    }

    // ---- Tool options ------------------------------------------------------------------------

    /// <summary>Rotate: a circular arrow.</summary>
    public static void Rotate(IIconCanvas canvas, Vector2 centre, float r)
    {
        Arc(canvas, centre, r * 0.72f, Vector2.One, 0.6f, 5.2f, 24);

        Vector2 end = centre + (new Vector2(MathF.Cos(5.2f), MathF.Sin(5.2f)) * r * 0.72f);
        Arrowhead(canvas, end, new Vector2(MathF.Sin(5.2f), -MathF.Cos(5.2f)), r * 0.36f);
    }

    /// <summary>Global space: a globe.</summary>
    public static void Global(IIconCanvas canvas, Vector2 centre, float r)
    {
        float radius = r * 0.78f;
        canvas.Circle(centre, radius);
        canvas.Line(centre + new Vector2(-radius, 0f), centre + new Vector2(radius, 0f));

        // Two meridians, drawn as ellipses so the circle reads as a sphere.
        Span<Vector2> meridian = stackalloc Vector2[25];

        for (float squash = 0.35f; squash <= 0.75f; squash += 0.4f)
        {
            for (int i = 0; i <= 24; i++)
            {
                float a = i / 24f * MathF.Tau;
                meridian[i] = centre + new Vector2(MathF.Sin(a) * radius * squash, MathF.Cos(a) * radius);
            }

            canvas.Polyline(meridian, 0.8f);
        }
    }

    /// <summary>Local space: a small set of axes.</summary>
    public static void Local(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 origin = centre + new Vector2(-r * 0.3f, r * 0.5f);

        Span<Vector2> directions = [new(0f, -1f), new(1f, -0.32f), new(-0.86f, 0.28f)];
        foreach (Vector2 direction in directions)
        {
            Vector2 tip = origin + (direction * r * 1.05f);
            canvas.Line(origin, tip);
            Arrowhead(canvas, tip, Vector2.Normalize(direction), r * 0.3f);
        }
    }

    /// <summary>Box selection: a marquee.</summary>
    public static void BoxSelect(IIconCanvas canvas, Vector2 centre, float r)
    {
        float step = r * 0.4f;
        for (float t = -r; t < r; t += step)
        {
            float end = MathF.Min(t + (step * 0.55f), r);
            canvas.Line(centre + new Vector2(t, -r), centre + new Vector2(end, -r));
            canvas.Line(centre + new Vector2(t, r), centre + new Vector2(end, r));
            canvas.Line(centre + new Vector2(-r, t), centre + new Vector2(-r, end));
            canvas.Line(centre + new Vector2(r, t), centre + new Vector2(r, end));
        }
    }

    /// <summary>Face selection: one whole filled face.</summary>
    public static void FaceSelect(IIconCanvas canvas, Vector2 centre, float r) =>
        canvas.FilledRect(centre - new Vector2(r * 0.8f), centre + new Vector2(r * 0.8f), 2f);

    /// <summary>Brush: a soft dab with a ring, matching the radius control it sits beside.</summary>
    public static void Brush(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.FilledCircle(centre, r * 0.36f);
        canvas.Circle(centre, r * 0.82f, 0.8f);
    }

    /// <summary>
    /// Bucket: a pail tipped over to the right, full of paint, with a drop leaving the lip — the fill,
    /// in the one picture every paint program uses for it.
    /// </summary>
    public static void Bucket(IIconCanvas canvas, Vector2 centre, float r)
    {
        // Upright, then turned a third of the way to pouring.
        float turn = 0.52f;
        float cos = MathF.Cos(turn);
        float sin = MathF.Sin(turn);
        Vector2 pivot = centre + new Vector2(-r * 0.2f, -r * 0.05f);

        Vector2 At(float x, float y) => pivot + new Vector2((x * cos) - (y * sin), (x * sin) + (y * cos)) * r;

        Vector2 rimLeft = At(-0.56f, -0.42f);
        Vector2 rimRight = At(0.56f, -0.42f);
        Vector2 baseRight = At(0.4f, 0.6f);
        Vector2 baseLeft = At(-0.4f, 0.6f);

        // The paint, filling the pail below its rim.
        Vector2 levelLeft = At(-0.5f, -0.12f);
        Vector2 levelRight = At(0.52f, -0.12f);
        canvas.FilledTriangle(levelLeft, levelRight, baseRight);
        canvas.FilledTriangle(levelLeft, baseRight, baseLeft);

        canvas.Polyline([rimLeft, baseLeft, baseRight, rimRight, rimLeft]);

        // The handle, arching over the open top.
        Span<Vector2> handle = stackalloc Vector2[9];
        for (int i = 0; i < handle.Length; i++)
        {
            float a = MathF.PI + (i / (float)(handle.Length - 1) * MathF.PI);
            handle[i] = At(MathF.Cos(a) * 0.5f, -0.42f + (MathF.Sin(a) * 0.42f));
        }

        canvas.Polyline(handle, 0.8f);

        // A drop falling from the lip.
        Vector2 drop = centre + new Vector2(r * 0.72f, r * 0.62f);
        canvas.FilledCircle(drop, r * 0.2f);
        canvas.FilledTriangle(drop + new Vector2(-r * 0.19f, -r * 0.05f), drop + new Vector2(r * 0.19f, -r * 0.05f), drop - new Vector2(0f, r * 0.42f));
    }

    /// <summary>
    /// Eyedropper: a pipette — bulb, the collar it is squeezed at, the glass, and a drop at the tip.
    /// What the desktop does with Alt held, which a finger cannot do, so on a phone it is a mode that
    /// lasts exactly one tap.
    /// </summary>
    public static void Eyedropper(IIconCanvas canvas, Vector2 centre, float r)
    {
        var along = Vector2.Normalize(new Vector2(-1f, 1f));
        var across = new Vector2(-along.Y, along.X);

        Vector2 bulb = centre - (along * r * 0.62f);
        Vector2 collar = centre - (along * r * 0.22f);
        Vector2 tip = centre + (along * r * 0.8f);

        canvas.FilledCircle(bulb, r * 0.3f);
        canvas.Line(bulb, collar, 2.2f);
        canvas.Line(collar - (across * r * 0.28f), collar + (across * r * 0.28f), 1.8f);
        canvas.Line(collar, tip - (along * r * 0.14f), 1.2f);
        canvas.FilledTriangle(tip, tip - (along * r * 0.3f) + (across * r * 0.1f), tip - (along * r * 0.3f) - (across * r * 0.1f));
    }

    /// <summary>Pattern: a chequer.</summary>
    public static void Pattern(IIconCanvas canvas, Vector2 centre, float r)
    {
        float cell = r * 0.7f;
        Vector2 origin = centre - new Vector2(cell);

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                if ((x + y) % 2 != 0)
                {
                    continue;
                }

                Vector2 topLeft = origin + new Vector2(x * cell, y * cell);
                canvas.FilledRect(topLeft, topLeft + new Vector2(cell));
            }
        }

        canvas.Rect(origin, origin + new Vector2(cell * 2f), 0f, 0.7f);
    }

    // ---- Snapping ----------------------------------------------------------------------------

    /// <summary>Snapping: a horseshoe magnet, the mark every editor uses for it.</summary>
    public static void Magnet(IIconCanvas canvas, Vector2 centre, float r)
    {
        const int Segments = 12;
        Span<Vector2> bend = stackalloc Vector2[Segments + 1];
        Vector2 middle = centre + new Vector2(0f, r * 0.12f);
        float radius = r * 0.56f;

        for (int i = 0; i <= Segments; i++)
        {
            float angle = i / (float)Segments * MathF.PI;
            bend[i] = middle + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }

        canvas.Polyline(bend, 1.5f);
        canvas.Line(middle + new Vector2(radius, 0f), middle + new Vector2(radius, -r * 0.62f), 1.5f);
        canvas.Line(middle - new Vector2(radius, 0f), middle + new Vector2(-radius, -r * 0.62f), 1.5f);

        // The poles, solid.
        float pole = r * 0.2f;
        canvas.FilledRect(middle + new Vector2(radius - pole, -r * 0.98f), middle + new Vector2(radius + pole, -r * 0.62f));
        canvas.FilledRect(middle + new Vector2(-radius - pole, -r * 0.98f), middle + new Vector2(-radius + pole, -r * 0.62f));
    }

    /// <summary>Snap to increments: a lattice of points.</summary>
    public static void SnapIncrement(IIconCanvas canvas, Vector2 centre, float r)
    {
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                canvas.FilledCircle(centre + (new Vector2(x, y) * r * 0.62f), r * 0.13f);
            }
        }
    }

    /// <summary>Snap to corners: a box with one corner marked.</summary>
    public static void SnapCorner(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Rect(centre - new Vector2(r * 0.7f), centre + new Vector2(r * 0.7f), 0f, 0.8f);
        canvas.FilledCircle(centre + new Vector2(r * 0.7f, -r * 0.7f), r * 0.28f);
    }

    /// <summary>Snap to edge middles: a box with the middle of an edge marked.</summary>
    public static void SnapEdge(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Rect(centre - new Vector2(r * 0.7f), centre + new Vector2(r * 0.7f), 0f, 0.8f);
        canvas.FilledCircle(centre + new Vector2(0f, -r * 0.7f), r * 0.28f);
    }

    /// <summary>Snap to surfaces: a block set down on a face.</summary>
    public static void SnapSurface(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.FilledRect(centre + new Vector2(-r * 0.95f, r * 0.45f), centre + new Vector2(r * 0.95f, r * 0.75f));
        canvas.Rect(centre + new Vector2(-r * 0.42f, -r * 0.55f), centre + new Vector2(r * 0.42f, r * 0.3f), 0f, 1f);
    }

    // ---- Overlays ----------------------------------------------------------------------------

    public static void Grid(IIconCanvas canvas, Vector2 centre, float r)
    {
        for (int i = -1; i <= 1; i++)
        {
            float offset = i * r * 0.5f;
            canvas.Line(centre + new Vector2(offset, -r * 0.8f), centre + new Vector2(offset, r * 0.8f), 0.8f);
            canvas.Line(centre + new Vector2(-r * 0.8f, offset), centre + new Vector2(r * 0.8f, offset), 0.8f);
        }
    }

    /// <summary>Measurements: a ruler with ticks.</summary>
    public static void Measure(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Line(centre + new Vector2(-r * 0.85f, r * 0.4f), centre + new Vector2(r * 0.85f, r * 0.4f));

        for (int i = 0; i < 4; i++)
        {
            float x = -r * 0.85f + (i * r * 0.57f);
            float height = i % 2 == 0 ? r * 0.7f : r * 0.42f;
            canvas.Line(
                centre + new Vector2(x, r * 0.4f),
                centre + new Vector2(x, (r * 0.4f) - height),
                0.8f);
        }
    }

    // ---- Rotation ------------------------------------------------------------------------------

    public static void RotateRight(IIconCanvas canvas, Vector2 centre, float r) =>
        Turn(canvas, centre, r, clockwise: true, vertical: false);

    public static void RotateLeft(IIconCanvas canvas, Vector2 centre, float r) =>
        Turn(canvas, centre, r, clockwise: false, vertical: false);

    public static void RotateUp(IIconCanvas canvas, Vector2 centre, float r) =>
        Turn(canvas, centre, r, clockwise: true, vertical: true);

    public static void RotateDown(IIconCanvas canvas, Vector2 centre, float r) =>
        Turn(canvas, centre, r, clockwise: false, vertical: true);

    /// <summary>
    /// A three-quarter arc with a head on it: the arrow says which way round, and the gap in the
    /// circle says it is a turn rather than a cycle.
    ///
    /// The vertical pair are the same arc seen edge-on, drawn as an ellipse squashed the other way,
    /// so tipping a model forwards reads differently from spinning it.
    /// </summary>
    private static void Turn(IIconCanvas canvas, Vector2 centre, float r, bool clockwise, bool vertical)
    {
        const int Segments = 18;
        float radius = r * 0.62f;

        // Squashed across for a spin, along for a tip: the same gesture from two points of view.
        Vector2 scale = vertical ? new Vector2(0.55f, 1f) : new Vector2(1f, 0.55f);

        // Three quarters of the way round, leaving the last quarter open for the head.
        float start = vertical ? -0.15f : 0.35f;
        float sweep = MathF.PI * 1.5f * (clockwise ? 1f : -1f);

        Vector2 At(float t)
        {
            float angle = start + (sweep * t);
            return centre + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * scale);
        }

        for (int i = 0; i < Segments; i++)
        {
            canvas.Line(At(i / (float)Segments), At((i + 1) / (float)Segments));
        }

        Vector2 tip = At(1f);
        Vector2 along = tip - At(0.94f);

        // A button can be laid out before it has a size, and normalising the last hair of an arc of
        // radius zero is how that arrives as NaN in a path the driver then has to make sense of.
        if (along.LengthSquared() < 1e-12f)
        {
            return;
        }

        Vector2 direction = Vector2.Normalize(along);
        Arrowhead(canvas, tip + (direction * r * 0.12f), direction, r * 0.42f);
    }

    /// <summary>Lit: a body with light falling on it.</summary>
    public static void Lit(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.FilledCircle(centre, r * 0.46f);

        // Rays on one side only. Spread evenly they would read as a sun, which is a light source;
        // gathered on the upper left they read as light arriving from somewhere.
        for (int i = 0; i < 3; i++)
        {
            float angle = (-0.62f + (i * 0.42f)) * MathF.PI;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            canvas.Line(centre + (direction * r * 0.68f), centre + (direction * r * 0.98f), 0.9f);
        }
    }

    /// <summary>Unlit: the same body, one flat tone, nothing shining on it.</summary>
    public static void Unlit(IIconCanvas canvas, Vector2 centre, float r) =>
        canvas.FilledCircle(centre, r * 0.46f);

    /// <summary>Wireframe shading: a ball drawn in its lines alone.</summary>
    public static void ShadingWire(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Circle(centre, r * 0.78f, 0.9f);
        Arc(canvas, centre, r * 0.78f, new Vector2(0.4f, 1f), 0f, MathF.Tau, 16);
        Arc(canvas, centre, r * 0.78f, new Vector2(1f, 0.36f), 0f, MathF.Tau, 16);
    }

    /// <summary>Solid shading: the same ball, filled, one flat tone.</summary>
    public static void ShadingSolid(IIconCanvas canvas, Vector2 centre, float r) =>
        canvas.FilledCircle(centre, r * 0.78f);

    /// <summary>
    /// X-Ray: one square over another, the front one only outlined, so the one behind shows through
    /// it — Blender's mark for seeing through.
    /// </summary>
    public static void XRay(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.FilledRect(centre + new Vector2(-r * 0.85f, -r * 0.85f), centre + new Vector2(r * 0.25f, r * 0.25f), r * 0.1f);
        canvas.Rect(centre + new Vector2(-r * 0.25f, -r * 0.25f), centre + new Vector2(r * 0.85f, r * 0.85f), r * 0.1f);
    }

    /// <summary>Gizmos: three arrows out of a corner, the handles the tools are driven by.</summary>
    public static void Gizmo(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 origin = centre + new Vector2(-r * 0.45f, r * 0.45f);
        Span<Vector2> directions = [new(0f, -1f), new(1f, 0f), Vector2.Normalize(new Vector2(0.72f, -0.72f))];

        foreach (Vector2 direction in directions)
        {
            Vector2 tip = origin + (direction * r * 1.15f);
            canvas.Line(origin, tip - (direction * r * 0.3f));
            Arrowhead(canvas, tip, direction, r * 0.42f);
        }
    }

    // ---- Navigation ----------------------------------------------------------------------------

    /// <summary>Perspective: a floor whose lines run together into the distance.</summary>
    public static void Perspective(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 farLeft = centre + new Vector2(-r * 0.42f, -r * 0.62f);
        Vector2 farRight = centre + new Vector2(r * 0.42f, -r * 0.62f);
        Vector2 nearRight = centre + new Vector2(r * 0.95f, r * 0.62f);
        Vector2 nearLeft = centre + new Vector2(-r * 0.95f, r * 0.62f);

        canvas.Polyline([farLeft, farRight, nearRight, nearLeft, farLeft]);
        canvas.Line(centre + new Vector2(0f, -r * 0.62f), centre + new Vector2(0f, r * 0.62f), 0.8f);

        // Above the middle rather than on it: rows further off sit closer together, which is most of
        // what reads as depth at this size.
        canvas.Line(centre + new Vector2(-r * 0.64f, -r * 0.1f), centre + new Vector2(r * 0.64f, -r * 0.1f), 0.8f);
    }

    /// <summary>Orthographic: the same floor with its lines kept parallel.</summary>
    public static void Orthographic(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Rect(centre + new Vector2(-r * 0.8f, -r * 0.62f), centre + new Vector2(r * 0.8f, r * 0.62f));
        canvas.Line(centre + new Vector2(0f, -r * 0.62f), centre + new Vector2(0f, r * 0.62f), 0.8f);
        canvas.Line(centre + new Vector2(-r * 0.8f, 0f), centre + new Vector2(r * 0.8f, 0f), 0.8f);
    }

    /// <summary>Frame all: corners closing in round something — "fit it to the view".</summary>
    public static void FrameAll(IIconCanvas canvas, Vector2 centre, float r)
    {
        float reach = r * 0.9f;
        float leg = r * 0.38f;

        Span<Vector2> corners = [new(-1f, -1f), new(1f, -1f), new(-1f, 1f), new(1f, 1f)];

        foreach (Vector2 corner in corners)
        {
            Vector2 at = centre + (corner * reach);
            canvas.Line(at, at - new Vector2(corner.X * leg, 0f));
            canvas.Line(at, at - new Vector2(0f, corner.Y * leg));
        }

        canvas.Rect(centre - new Vector2(r * 0.34f), centre + new Vector2(r * 0.34f));
    }

    /// <summary>
    /// Pan: four ways to slide. Heads only, no shafts — with shafts it would be the Transform tool's
    /// icon, and this moves the view, not the model.
    /// </summary>
    public static void Pan(IIconCanvas canvas, Vector2 centre, float r)
    {
        Span<Vector2> directions = [new(0f, -1f), new(0f, 1f), new(-1f, 0f), new(1f, 0f)];

        foreach (Vector2 direction in directions)
        {
            Arrowhead(canvas, centre + (direction * r * 0.95f), direction, r * 0.42f);
        }

        canvas.FilledCircle(centre, r * 0.2f);
    }

    /// <summary>Zoom: a magnifying glass with a plus in it.</summary>
    /// <summary>Search: a lens on its handle.</summary>
    public static void Search(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 lens = centre - new Vector2(r * 0.18f);

        canvas.Circle(lens, r * 0.56f);
        canvas.Line(lens + new Vector2(r * 0.4f), centre + new Vector2(r * 0.9f), 1.3f);
    }

    public static void Zoom(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 lens = centre - new Vector2(r * 0.18f);

        canvas.Circle(lens, r * 0.56f);
        canvas.Line(lens + new Vector2(r * 0.4f), centre + new Vector2(r * 0.9f), 1.3f);
        canvas.Line(lens - new Vector2(r * 0.26f, 0f), lens + new Vector2(r * 0.26f, 0f), 0.8f);
        canvas.Line(lens - new Vector2(0f, r * 0.26f), lens + new Vector2(0f, r * 0.26f), 0.8f);
    }

    // ---- Header --------------------------------------------------------------------------------

    /// <summary>Overlays: two circles over each other, Blender's mark for what is drawn over the scene.</summary>
    public static void Overlays(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Circle(centre - new Vector2(r * 0.28f, 0f), r * 0.58f);
        canvas.Circle(centre + new Vector2(r * 0.28f, 0f), r * 0.58f);
    }

    /// <summary>New object: a cube with a plus — what extruding into a new object makes.</summary>
    public static void NewObject(IIconCanvas canvas, Vector2 centre, float r)
    {
        ObjectTab(canvas, centre + new Vector2(-r * 0.18f, r * 0.14f), r * 0.78f);

        Vector2 plus = centre + new Vector2(r * 0.62f, -r * 0.62f);
        canvas.Line(plus - new Vector2(r * 0.3f, 0f), plus + new Vector2(r * 0.3f, 0f));
        canvas.Line(plus - new Vector2(0f, r * 0.3f), plus + new Vector2(0f, r * 0.3f));
    }

    // ---- Properties tabs -----------------------------------------------------------------------

    /// <summary>Object: a cube seen from above a corner — the thing itself.</summary>
    public static void ObjectTab(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 top = centre + new Vector2(0f, -r * 0.9f);
        Vector2 left = centre + new Vector2(-r * 0.8f, -r * 0.45f);
        Vector2 right = centre + new Vector2(r * 0.8f, -r * 0.45f);
        Vector2 middle = centre;
        Vector2 bottomLeft = centre + new Vector2(-r * 0.8f, r * 0.45f);
        Vector2 bottomRight = centre + new Vector2(r * 0.8f, r * 0.45f);
        Vector2 bottom = centre + new Vector2(0f, r * 0.9f);

        canvas.Polyline([top, right, middle, left, top]);
        canvas.Line(left, bottomLeft);
        canvas.Line(bottomLeft, bottom);
        canvas.Line(bottom, bottomRight);
        canvas.Line(bottomRight, right);
        canvas.Line(middle, bottom);
    }

    /// <summary>World: a globe — everything around the objects rather than any one of them.</summary>
    public static void WorldTab(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Circle(centre, r * 0.85f);
        Arc(canvas, centre, r * 0.85f, new Vector2(0.42f, 1f), 0f, MathF.Tau, 16);
        canvas.Line(centre - new Vector2(r * 0.85f, 0f), centre + new Vector2(r * 0.85f, 0f), 0.8f);
    }

    /// <summary>Palette: four swatches.</summary>
    public static void PaletteTab(IIconCanvas canvas, Vector2 centre, float r)
    {
        float gap = r * 0.12f;
        float side = r * 0.72f;

        canvas.FilledRect(centre + new Vector2(-side - gap, -side - gap), centre + new Vector2(-gap, -gap), r * 0.12f);
        canvas.Rect(centre + new Vector2(gap, -side - gap), centre + new Vector2(side + gap, -gap), r * 0.12f);
        canvas.Rect(centre + new Vector2(-side - gap, gap), centre + new Vector2(-gap, side + gap), r * 0.12f);
        canvas.FilledRect(centre + new Vector2(gap, gap), centre + new Vector2(side + gap, side + gap), r * 0.12f);
    }

    /// <summary>Reference: a picture in a frame — a guide to build against, not something built.</summary>
    public static void ReferenceTab(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Rect(centre - new Vector2(r * 0.9f, r * 0.72f), centre + new Vector2(r * 0.9f, r * 0.72f), r * 0.1f);
        canvas.Polyline(
        [
            centre + new Vector2(-r * 0.7f, r * 0.5f),
            centre + new Vector2(-r * 0.2f, -r * 0.15f),
            centre + new Vector2(r * 0.1f, r * 0.2f),
            centre + new Vector2(r * 0.35f, -r * 0.05f),
            centre + new Vector2(r * 0.7f, r * 0.5f),
        ]);
        canvas.FilledCircle(centre + new Vector2(r * 0.45f, -r * 0.4f), r * 0.14f);
    }

    // ---- Lights --------------------------------------------------------------------------------

    /// <summary>A sun: a disc with rays all the way round — a source, where Lit's rays on one side are light arriving.</summary>
    public static void LightSun(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Circle(centre, r * 0.36f);

        for (int i = 0; i < 8; i++)
        {
            float angle = i * (MathF.PI / 4f);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            canvas.Line(centre + (direction * r * 0.6f), centre + (direction * r * 0.95f), 0.9f);
        }
    }

    /// <summary>A point light: a bulb and its base.</summary>
    public static void LightPoint(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Circle(centre - new Vector2(0f, r * 0.22f), r * 0.5f);
        canvas.Line(centre + new Vector2(-r * 0.22f, r * 0.42f), centre + new Vector2(-r * 0.22f, r * 0.62f), 0.9f);
        canvas.Line(centre + new Vector2(r * 0.22f, r * 0.42f), centre + new Vector2(r * 0.22f, r * 0.62f), 0.9f);
        canvas.Line(centre + new Vector2(-r * 0.26f, r * 0.8f), centre + new Vector2(r * 0.26f, r * 0.8f), 0.9f);
    }

    /// <summary>A spot light: a cone opening downwards from where it hangs.</summary>
    public static void LightSpot(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 apex = centre - new Vector2(0f, r * 0.85f);
        Vector2 left = centre + new Vector2(-r * 0.7f, r * 0.5f);
        Vector2 right = centre + new Vector2(r * 0.7f, r * 0.5f);

        canvas.Line(apex, left);
        canvas.Line(apex, right);

        // The mouth, seen a little from above.
        Arc(canvas, centre + new Vector2(0f, r * 0.5f), r * 0.7f, new Vector2(1f, 0.32f), 0f, MathF.Tau, 16);
    }

    /// <summary>The icon for a kind of light.</summary>
    public static Painter For(Core.Scene.LightKind kind) => kind switch
    {
        Core.Scene.LightKind.Directional => LightSun,
        Core.Scene.LightKind.Point => LightPoint,
        _ => LightSpot,
    };

    // ---- Outliner ------------------------------------------------------------------------------

    /// <summary>Visible: an open eye.</summary>
    public static void Eye(IIconCanvas canvas, Vector2 centre, float r)
    {
        Lid(canvas, centre, r, upper: true);
        Lid(canvas, centre, r, upper: false);
        canvas.FilledCircle(centre, r * 0.3f);
    }

    /// <summary>
    /// Hidden: the eye shut — the lower lid with lashes. Shut rather than crossed out, which would
    /// read as "not allowed" when all it means is "not shown".
    /// </summary>
    public static void EyeClosed(IIconCanvas canvas, Vector2 centre, float r)
    {
        Lid(canvas, centre, r, upper: false);

        // The lid's own circle, so the lashes stand square to it.
        Vector2 middle = centre - new Vector2(0f, LidOffset * r);
        Span<float> angles = [MathF.PI / 2f - 0.55f, MathF.PI / 2f, MathF.PI / 2f + 0.55f];

        foreach (float angle in angles)
        {
            var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 root = middle + (outward * LidRadius * r);
            canvas.Line(root, root + (outward * r * 0.3f), 0.9f);
        }
    }

    /// <summary>Locked: a shut padlock.</summary>
    public static void Lock(IIconCanvas canvas, Vector2 centre, float r) =>
        Padlock(canvas, centre, r, open: false);

    /// <summary>Unlocked: the same padlock with its shackle lifted out on one side.</summary>
    public static void Unlocked(IIconCanvas canvas, Vector2 centre, float r) =>
        Padlock(canvas, centre, r, open: true);

    private static void Padlock(IIconCanvas canvas, Vector2 centre, float r, bool open)
    {
        float legs = r * 0.4f;
        float bodyTop = centre.Y - (r * 0.04f);

        canvas.FilledRect(
            new Vector2(centre.X - (r * 0.66f), bodyTop),
            new Vector2(centre.X + (r * 0.66f), centre.Y + (r * 0.8f)),
            r * 0.16f);

        // The shackle: a half circle over two legs. Open, it rides higher and its left leg stops
        // short of the body — the one detail that says "open" at twelve pixels.
        float lift = open ? r * 0.26f : 0f;
        var arcCentre = new Vector2(centre.X, centre.Y - (r * 0.4f) - lift);
        Arc(canvas, arcCentre, legs, Vector2.One, MathF.PI, MathF.Tau, 12);

        canvas.Line(arcCentre + new Vector2(legs, 0f), new Vector2(centre.X + legs, bodyTop));
        canvas.Line(
            arcCentre - new Vector2(legs, 0f),
            new Vector2(centre.X - legs, open ? arcCentre.Y + (r * 0.16f) : bodyTop));
    }

    // A lid is an arc of a circle through both corners of the eye, 0.9 either side of the centre,
    // bulging 0.55 away from it: that circle sits 0.4614 the other way with radius 1.0114.
    private const float LidOffset = 0.4614f;
    private const float LidRadius = 1.0114f;
    private const float LidSweep = 1.0971f;   // from the bottom (or top) of the circle to a corner

    private static void Lid(IIconCanvas canvas, Vector2 centre, float r, bool upper)
    {
        float sign = upper ? 1f : -1f;
        Vector2 middle = centre + new Vector2(0f, sign * LidOffset * r);
        float peak = upper ? -MathF.PI / 2f : MathF.PI / 2f;

        Arc(canvas, middle, LidRadius * r, Vector2.One, peak - LidSweep, peak + LidSweep, 14);
    }

    // ---- Mouse, for the hints along the bottom -------------------------------------------------

    public static void MouseLeft(IIconCanvas canvas, Vector2 centre, float r) =>
        Mouse(canvas, centre, r, MouseMark.Left);

    public static void MouseMiddle(IIconCanvas canvas, Vector2 centre, float r) =>
        Mouse(canvas, centre, r, MouseMark.Middle);

    public static void MouseRight(IIconCanvas canvas, Vector2 centre, float r) =>
        Mouse(canvas, centre, r, MouseMark.Right);

    /// <summary>The wheel turned rather than pressed: the middle button, with which way it rolls.</summary>
    public static void MouseWheel(IIconCanvas canvas, Vector2 centre, float r)
    {
        // Moved over to make room for the arrows, so the pair stays centred on the button.
        Vector2 body = centre - new Vector2(r * 0.3f, 0f);
        Mouse(canvas, body, r, MouseMark.Middle);

        float x = body.X + (r * 1.02f);
        Arrowhead(canvas, new Vector2(x, centre.Y - (r * 0.75f)), new Vector2(0f, -1f), r * 0.4f);
        Arrowhead(canvas, new Vector2(x, centre.Y + (r * 0.75f)), new Vector2(0f, 1f), r * 0.4f);
    }

    private enum MouseMark
    {
        Left,
        Middle,
        Right,
    }

    /// <summary>A mouse seen from above, with the button that matters filled in.</summary>
    private static void Mouse(IIconCanvas canvas, Vector2 centre, float r, MouseMark mark)
    {
        Vector2 min = centre - new Vector2(r * 0.62f, r * 0.95f);
        Vector2 max = centre + new Vector2(r * 0.62f, r * 0.95f);
        float split = centre.Y - (r * 0.12f);

        // Where the wheel sits between the two buttons.
        Vector2 wheelMin = new(centre.X - (r * 0.14f), min.Y + (r * 0.24f));
        Vector2 wheelMax = new(centre.X + (r * 0.14f), split - (r * 0.18f));

        // Filled first, so the outline lands on top of it. Inset and less rounded than the body, which
        // keeps its outer corner inside the body's curve instead of poking through it.
        Vector2 inset = new(r * 0.12f);
        switch (mark)
        {
            case MouseMark.Left:
                canvas.FilledRect(min + inset, new Vector2(centre.X, split), r * 0.3f);
                break;

            case MouseMark.Right:
                canvas.FilledRect(new Vector2(centre.X, min.Y + inset.Y), new Vector2(max.X - inset.X, split), r * 0.3f);
                break;

            default:
                canvas.FilledRect(wheelMin, wheelMax, r * 0.14f);
                break;
        }

        canvas.Rect(min, max, r * 0.6f);
        canvas.Line(new Vector2(min.X, split), new Vector2(max.X, split), 0.8f);
        canvas.Line(new Vector2(centre.X, min.Y), new Vector2(centre.X, wheelMin.Y), 0.8f);
        canvas.Line(new Vector2(centre.X, wheelMax.Y), new Vector2(centre.X, split), 0.8f);

        if (mark != MouseMark.Middle)
        {
            canvas.Rect(wheelMin, wheelMax, r * 0.14f, 0.8f);
        }
    }

    // ---- Shell ---------------------------------------------------------------------------------

    /// <summary>Undo: an arrow curving back on itself.</summary>
    public static void Undo(IIconCanvas canvas, Vector2 centre, float r) =>
        Reverse(canvas, centre, r, mirrored: false);

    public static void Redo(IIconCanvas canvas, Vector2 centre, float r) =>
        Reverse(canvas, centre, r, mirrored: true);

    private static void Reverse(IIconCanvas canvas, Vector2 centre, float r, bool mirrored)
    {
        float sign = mirrored ? -1f : 1f;

        // A half turn over the top, then a tail dropping away, so the arrow reads as going back to
        // something rather than merely round.
        Arc(canvas, centre + new Vector2(0f, r * 0.2f), r * 0.62f, new Vector2(sign, 1f), MathF.PI, MathF.Tau, 16);

        Vector2 tip = centre + new Vector2(-sign * r * 0.62f, r * 0.2f);
        canvas.Line(tip, tip + new Vector2(0f, r * 0.5f));
        Arrowhead(canvas, tip + new Vector2(0f, r * 0.62f), new Vector2(0f, 1f), r * 0.4f);
    }

    /// <summary>
    /// Adjust: sliders. Everything about the level and the way it is looked at lives behind this on
    /// the phone, where the desktop has four separate panels.
    /// </summary>
    public static void Adjust(IIconCanvas canvas, Vector2 centre, float r)
    {
        Span<float> rows = [-0.55f, 0f, 0.55f];
        Span<float> knobs = [0.3f, -0.35f, 0.15f];

        for (int i = 0; i < rows.Length; i++)
        {
            float y = centre.Y + (rows[i] * r);
            canvas.Line(new Vector2(centre.X - (r * 0.85f), y), new Vector2(centre.X + (r * 0.85f), y), 0.8f);
            canvas.FilledCircle(new Vector2(centre.X + (knobs[i] * r), y), r * 0.24f);
        }
    }

    /// <summary>Files: a stack of sheets.</summary>
    public static void Files(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Rect(centre + new Vector2(-r * 0.85f, -r * 0.5f), centre + new Vector2(r * 0.35f, r * 0.85f));
        canvas.Line(centre + new Vector2(-r * 0.5f, -r * 0.85f), centre + new Vector2(r * 0.7f, -r * 0.85f));
        canvas.Line(centre + new Vector2(r * 0.7f, -r * 0.85f), centre + new Vector2(r * 0.7f, r * 0.5f));
    }

    /// <summary>A chevron, for anything that opens and closes.</summary>
    public static void ChevronUp(IIconCanvas canvas, Vector2 centre, float r) =>
        Chevron(canvas, centre, r, up: true);

    public static void ChevronDown(IIconCanvas canvas, Vector2 centre, float r) =>
        Chevron(canvas, centre, r, up: false);

    /// <summary>Pointing right: something folded away, that opens downwards.</summary>
    public static void ChevronRight(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 point = centre + new Vector2(r * 0.45f, 0f);

        canvas.Line(centre + new Vector2(-r * 0.25f, -r * 0.7f), point);
        canvas.Line(point, centre + new Vector2(-r * 0.25f, r * 0.7f));
    }

    private static void Chevron(IIconCanvas canvas, Vector2 centre, float r, bool up)
    {
        float sign = up ? -1f : 1f;
        Vector2 point = centre + new Vector2(0f, sign * r * 0.45f);

        canvas.Line(centre + new Vector2(-r * 0.7f, -sign * r * 0.25f), point);
        canvas.Line(point, centre + new Vector2(r * 0.7f, -sign * r * 0.25f));
    }

    /// <summary>Plus and minus, for the steppers.</summary>
    public static void Plus(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Line(centre + new Vector2(-r * 0.7f, 0f), centre + new Vector2(r * 0.7f, 0f));
        canvas.Line(centre + new Vector2(0f, -r * 0.7f), centre + new Vector2(0f, r * 0.7f));
    }

    public static void Minus(IIconCanvas canvas, Vector2 centre, float r) =>
        canvas.Line(centre + new Vector2(-r * 0.7f, 0f), centre + new Vector2(r * 0.7f, 0f));

    /// <summary>Close: what a page is dismissed with.</summary>
    public static void Close(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Line(centre - new Vector2(r * 0.62f), centre + new Vector2(r * 0.62f));
        canvas.Line(
            centre + new Vector2(-r * 0.62f, r * 0.62f),
            centre + new Vector2(r * 0.62f, -r * 0.62f));
    }

    /// <summary>Samples an ellipse arc into a polyline, since a canvas only has to know lines.</summary>
    private static void Arc(
        IIconCanvas canvas,
        Vector2 centre,
        float radius,
        Vector2 scale,
        float from,
        float to,
        int segments)
    {
        Span<Vector2> points = stackalloc Vector2[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float angle = from + ((to - from) * i / segments);
            points[i] = centre + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * scale);
        }

        canvas.Polyline(points);
    }

    private static void Arrowhead(IIconCanvas canvas, Vector2 tip, Vector2 direction, float size)
    {
        Vector2 side = new Vector2(-direction.Y, direction.X) * size * 0.55f;
        Vector2 back = tip - (direction * size);

        canvas.FilledTriangle(tip, back + side, back - side);
    }
}
