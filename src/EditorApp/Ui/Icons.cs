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

    /// <summary>Transform: arrows out of a centre, the universal "move this" mark.</summary>
    public static void Move(IIconCanvas canvas, Vector2 centre, float r)
    {
        Span<Vector2> directions = [new(0f, -1f), new(0f, 1f), new(-1f, 0f), new(1f, 0f)];

        foreach (Vector2 direction in directions)
        {
            Vector2 tip = centre + (direction * r);
            canvas.Line(centre, tip);
            Arrowhead(canvas, tip, direction, r * 0.34f);
        }
    }

    /// <summary>Extrude: a face lifting off the surface it came from.</summary>
    public static void Extrude(IIconCanvas canvas, Vector2 centre, float r)
    {
        // The base surface.
        canvas.Line(
            centre + new Vector2(-r, r * 0.72f),
            centre + new Vector2(r, r * 0.72f));

        // The slab being pulled off it.
        canvas.Rect(
            centre + new Vector2(-r * 0.62f, -r * 0.1f),
            centre + new Vector2(r * 0.62f, r * 0.42f),
            2f);

        Vector2 tip = centre + new Vector2(0f, -r);
        canvas.Line(centre + new Vector2(0f, -r * 0.28f), tip);
        Arrowhead(canvas, tip, new Vector2(0f, -1f), r * 0.34f);
    }

    /// <summary>Paint: a brush dab.</summary>
    public static void Paint(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.FilledCircle(centre + new Vector2(0f, r * 0.22f), r * 0.5f);
        canvas.Line(
            centre + new Vector2(r * 0.28f, -r * 0.1f),
            centre + new Vector2(r * 0.78f, -r * 0.8f),
            1.6f);
    }

    /// <summary>Loop Cut: a plane splitting a body in two.</summary>
    public static void Cut(IIconCanvas canvas, Vector2 centre, float r)
    {
        canvas.Rect(
            centre + new Vector2(-r * 0.75f, -r * 0.72f),
            centre + new Vector2(r * 0.75f, r * 0.72f),
            2f);

        // A dashed line, because a solid one would read as an edge of the box rather than a cut.
        for (float x = -r; x < r; x += r * 0.32f)
        {
            canvas.Line(
                centre + new Vector2(x, 0f),
                centre + new Vector2(MathF.Min(x + (r * 0.18f), r), 0f));
        }
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

    /// <summary>Bucket: a tipped pail.</summary>
    public static void Bucket(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 topLeft = centre + new Vector2(-r * 0.72f, -r * 0.62f);
        Vector2 topRight = centre + new Vector2(r * 0.72f, -r * 0.62f);
        Vector2 bottomRight = centre + new Vector2(r * 0.36f, r * 0.72f);
        Vector2 bottomLeft = centre + new Vector2(-r * 0.36f, r * 0.72f);

        canvas.Line(topLeft, topRight);
        canvas.Line(topRight, bottomRight);
        canvas.Line(bottomRight, bottomLeft);
        canvas.Line(bottomLeft, topLeft);

        // A drip, so it is a bucket pouring rather than a plain trapezoid.
        canvas.FilledCircle(centre + new Vector2(r * 0.85f, r * 0.5f), r * 0.2f);
    }

    /// <summary>
    /// Eyedropper: a pipette. What the desktop does with Alt held, which a finger cannot do, so on
    /// a phone it is a mode that lasts exactly one tap.
    /// </summary>
    public static void Eyedropper(IIconCanvas canvas, Vector2 centre, float r)
    {
        // The barrel, running corner to corner.
        canvas.Line(
            centre + new Vector2(-r * 0.55f, r * 0.55f),
            centre + new Vector2(r * 0.4f, -r * 0.4f),
            1.6f);

        // The bulb at the top, and the drop at the tip.
        canvas.FilledCircle(centre + new Vector2(r * 0.6f, -r * 0.6f), r * 0.3f);
        canvas.FilledTriangle(
            centre + new Vector2(-r * 0.82f, r * 0.82f),
            centre + new Vector2(-r * 0.3f, r * 0.5f),
            centre + new Vector2(-r * 0.5f, r * 0.3f));
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
    public static void Zoom(IIconCanvas canvas, Vector2 centre, float r)
    {
        Vector2 lens = centre - new Vector2(r * 0.18f);

        canvas.Circle(lens, r * 0.56f);
        canvas.Line(lens + new Vector2(r * 0.4f), centre + new Vector2(r * 0.9f), 1.3f);
        canvas.Line(lens - new Vector2(r * 0.26f, 0f), lens + new Vector2(r * 0.26f, 0f), 0.8f);
        canvas.Line(lens - new Vector2(0f, r * 0.26f), lens + new Vector2(0f, r * 0.26f), 0.8f);
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
