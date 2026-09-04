using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Color = Android.Graphics.Color;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// A square button showing one icon and nothing else.
///
/// The icons are Material Symbols, shipped as vector drawables. The desktop draws its own from
/// primitives — a set built for a windowed editor with tooltips and room to breathe — and those
/// shapes did not survive being shrunk into a phone's chrome. A phone is held close and judged on
/// how it looks, so here the drawing is left to a set made for exactly this.
///
/// Words cost width, and width is what a landscape phone has least of once the viewport has taken
/// its share. Every button says its name through <see cref="View.ContentDescription"/>, which is
/// both the accessibility label and what a long press shows.
/// </summary>
public sealed class IconButtonView : View
{
    private readonly Drawable? _icon;

    private bool _chosen;
    private bool _available = true;

    public IconButtonView(Context context, int iconResource, string description)
        : base(context)
    {
        _icon = context.Resources?.GetDrawable(iconResource, context.Theme)?.Mutate();

        ContentDescription = description;
        TooltipText = description;

        Clickable = true;
        Background = Style.RoundedFill(context, Style.ButtonIdle);

        Retint();
    }

    /// <summary>Shown as chosen: the accent behind it, and a dark mark over that.</summary>
    public bool Chosen
    {
        get => _chosen;
        set
        {
            _chosen = value;
            Background = Style.RoundedFill(Context!, value ? Style.Accent : Style.ButtonIdle);
            Retint();
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
            Alpha = value ? 1f : 0.32f;
        }
    }

    /// <summary>Drops the panel behind the button, for icons that sit straight on the viewport.</summary>
    public void MakeFlat() => Background = null;

    private void Retint()
    {
        _icon?.SetTint(_chosen ? Style.PanelSolid : Style.Text);
        Invalidate();
    }

    protected override void OnMeasure(int widthSpec, int heightSpec)
    {
        int side = Style.Dp(Context!, Style.IconButtonDp);
        SetMeasuredDimension(ResolveSize(side, widthSpec), ResolveSize(side, heightSpec));
    }

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas is null || _icon is null)
        {
            return;
        }

        base.OnDraw(canvas);

        // The mark sits inside the button rather than filling it, so a row of them reads as marks
        // rather than as a wall of boxes.
        int inset = Style.Dp(Context!, (Style.IconButtonDp - Style.IconGlyphDp) / 2f);
        _icon.SetBounds(inset, inset, Width - inset, Height - inset);
        _icon.Draw(canvas);
    }
}

/// <summary>
/// A colour, shown as itself. A pictogram of a swatch would be a picture of the thing standing next
/// to the thing.
/// </summary>
public sealed class SwatchView : View
{
    public SwatchView(Context context)
        : base(context)
    {
        ContentDescription = "Colour";
        Clickable = true;
    }

    public void Show(Color colour) => Background = Style.RoundedFill(Context!, colour, Style.Accent);

    protected override void OnMeasure(int widthSpec, int heightSpec)
    {
        int side = Style.Dp(Context!, Style.IconButtonDp);
        SetMeasuredDimension(ResolveSize(side, widthSpec), ResolveSize(side, heightSpec));
    }
}
