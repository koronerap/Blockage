using System.Numerics;
using Android.Content;
using Android.Graphics;
using Android.Views;
using EditorApp.Ui;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Path = Android.Graphics.Path;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// Draws an <see cref="Icons"/> pictogram onto an Android canvas.
///
/// The desktop's counterpart draws the same shapes through ImGui. Nothing about what an icon looks
/// like is decided here — only how a line, a circle and a triangle reach the screen.
/// </summary>
public sealed class AndroidIconCanvas(Canvas canvas, Color colour, float thickness) : IIconCanvas
{
    private readonly Paint _paint = new(PaintFlags.AntiAlias)
    {
        Color = colour,
        StrokeCap = Paint.Cap.Round,
        StrokeJoin = Paint.Join.Round,
    };

    private readonly Path _path = new();

    private Paint Stroke(float width)
    {
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = thickness * width;
        return _paint;
    }

    private Paint Fill()
    {
        _paint.SetStyle(Paint.Style.Fill);
        return _paint;
    }

    public void Line(Vector2 from, Vector2 to, float width = 1f) =>
        canvas.DrawLine(from.X, from.Y, to.X, to.Y, Stroke(width));

    public void Polyline(ReadOnlySpan<Vector2> points, float width = 1f)
    {
        if (points.Length < 2)
        {
            return;
        }

        _path.Reset();
        _path.MoveTo(points[0].X, points[0].Y);

        for (int i = 1; i < points.Length; i++)
        {
            _path.LineTo(points[i].X, points[i].Y);
        }

        canvas.DrawPath(_path, Stroke(width));
    }

    public void Rect(Vector2 min, Vector2 max, float rounding = 0f, float width = 1f)
    {
        Paint paint = Stroke(width);

        if (rounding > 0f)
        {
            canvas.DrawRoundRect(min.X, min.Y, max.X, max.Y, rounding, rounding, paint);
            return;
        }

        canvas.DrawRect(min.X, min.Y, max.X, max.Y, paint);
    }

    public void FilledRect(Vector2 min, Vector2 max, float rounding = 0f)
    {
        Paint paint = Fill();

        if (rounding > 0f)
        {
            canvas.DrawRoundRect(min.X, min.Y, max.X, max.Y, rounding, rounding, paint);
            return;
        }

        canvas.DrawRect(min.X, min.Y, max.X, max.Y, paint);
    }

    public void Circle(Vector2 centre, float radius, float width = 1f) =>
        canvas.DrawCircle(centre.X, centre.Y, radius, Stroke(width));

    public void FilledCircle(Vector2 centre, float radius) =>
        canvas.DrawCircle(centre.X, centre.Y, radius, Fill());

    public void FilledTriangle(Vector2 a, Vector2 b, Vector2 c)
    {
        _path.Reset();
        _path.MoveTo(a.X, a.Y);
        _path.LineTo(b.X, b.Y);
        _path.LineTo(c.X, c.Y);
        _path.Close();

        canvas.DrawPath(_path, Fill());
    }
}

/// <summary>
/// A square button showing one icon and nothing else.
///
/// Words cost width, and width is the one thing a phone in landscape has least of once the viewport
/// has taken its share. A pictogram at forty-four density-independent pixels is both a legible mark
/// and a target a thumb can hit.
/// </summary>
public sealed class IconButtonView : View
{
    private readonly Icons.Painter _painter;

    private bool _chosen;
    private bool _available = true;

    public IconButtonView(Context context, Icons.Painter painter, string description)
        : base(context)
    {
        _painter = painter;

        // Nothing on screen says what these are, so the only thing that can is the accessibility
        // label. It is also what a long press surfaces as a tooltip.
        ContentDescription = description;
        TooltipText = description;

        int side = Style.Dp(context, Style.IconButtonDp);
        SetMinimumWidth(side);
        SetMinimumHeight(side);

        Clickable = true;
        Background = Style.RoundedFill(context, Style.ButtonIdle);
    }

    /// <summary>Shown as chosen: the accent behind it, and a dark mark over that.</summary>
    public bool Chosen
    {
        get => _chosen;
        set
        {
            _chosen = value;
            Background = Style.RoundedFill(Context!, value ? Style.Accent : Style.ButtonIdle);
            Invalidate();
        }
    }

    /// <summary>Dimmed rather than removed: a button that comes and goes is hard to aim at.</summary>
    public bool Available
    {
        get => _available;
        set
        {
            _available = value;
            Enabled = value;
            Alpha = value ? 1f : 0.3f;
        }
    }

    protected override void OnMeasure(int widthSpec, int heightSpec)
    {
        int side = Style.Dp(Context!, Style.IconButtonDp);
        SetMeasuredDimension(
            ResolveSize(side, widthSpec),
            ResolveSize(side, heightSpec));
    }

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas is null)
        {
            return;
        }

        base.OnDraw(canvas);

        float side = MathF.Min(Width, Height);
        var centre = new Vector2(Width * 0.5f, Height * 0.5f);

        // The same proportions the desktop's icon button uses: the pictogram fills a little under a
        // third of the button, so a row of them reads as marks rather than as a wall.
        var paint = new AndroidIconCanvas(
            canvas,
            Chosen ? Style.PanelSolid : Style.Text,
            Style.Dp(Context!, 1f) * 1.6f);

        _painter(paint, centre, side * 0.3f);
    }
}
