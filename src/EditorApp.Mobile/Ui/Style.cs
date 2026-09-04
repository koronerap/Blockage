using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Voxels;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// The handful of colours and measurements the interface is built from, and the two or three widgets
/// it is built out of.
///
/// There is no layout XML and no AppCompat. The interface is a row of buttons and a grid of colours
/// over a GL surface, and a resource pipeline for that would be more to keep in step than it would
/// be worth.
/// </summary>
public static class Style
{
    /// <summary>Panel background. Dark enough to read against the lightest part of the viewport.</summary>
    public static readonly Color Panel = Color.Argb(235, 30, 33, 38);

    /// <summary>The same surface with nothing behind it, for a page rather than a bar.</summary>
    public static readonly Color PanelSolid = Color.Argb(255, 30, 33, 38);

    public static readonly Color Text = Color.Argb(255, 226, 230, 236);

    public static readonly Color TextDim = Color.Argb(255, 150, 157, 167);

    /// <summary>The selected tool, and the ring around the selected colour.</summary>
    public static readonly Color Accent = Color.Argb(255, 96, 165, 250);

    public static readonly Color ButtonIdle = Color.Argb(255, 48, 53, 61);

    /// <summary>
    /// A touch target's minimum side, in dp. Below about this a target is missed often enough to be
    /// noticed, and every button here is at least this big.
    /// </summary>
    public const float TouchTargetDp = 48f;

    /// <summary>
    /// One icon button's side. Smaller than the touch-target floor above because a row of them is
    /// spaced apart, so the gap between two buttons belongs to neither and a miss lands on nothing
    /// rather than on the wrong thing.
    /// </summary>
    public const float IconButtonDp = 44f;

    public static int Dp(Context context, float dp) =>
        (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, dp, context.Resources!.DisplayMetrics);

    /// <summary>A rounded rectangle, optionally ringed. Used for buttons and colour swatches alike.</summary>
    public static GradientDrawable RoundedFill(Context context, Color fill, Color? ring = null)
    {
        var shape = new GradientDrawable();
        shape.SetShape(ShapeType.Rectangle);
        shape.SetColor(fill.ToArgb());
        shape.SetCornerRadius(Dp(context, 8f));

        if (ring is { } outline)
        {
            shape.SetStroke(Dp(context, 2f), outline);
        }

        return shape;
    }

    /// <summary>A text button. A TextView rather than a Button, so it carries no platform theming.</summary>
    public static TextView Button(Context context, string label)
    {
        var view = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
        };

        view.SetTextSize(ComplexUnitType.Sp, 12f);
        view.SetTextColor(TextDim);
        view.SetPadding(Dp(context, 10f), 0, Dp(context, 10f), 0);
        view.Background = RoundedFill(context, ButtonIdle);
        view.SetMinimumWidth(Dp(context, 56f));
        view.SetMinimumHeight(Dp(context, TouchTargetDp));

        return view;
    }

    /// <summary>Shows a button as chosen, or not.</summary>
    public static void SetSelected(Context context, TextView button, bool selected)
    {
        button.Background = RoundedFill(context, selected ? Accent : ButtonIdle);
        button.SetTextColor(selected ? Color.Argb(255, 16, 20, 26) : TextDim);
    }

    /// <summary>Dims a button that currently does nothing, rather than hiding it.</summary>
    public static void SetEnabledLook(TextView button, bool enabled)
    {
        button.Enabled = enabled;
        button.Alpha = enabled ? 1f : 0.35f;
    }

    public static Color ToAndroid(Color32 color) => Color.Argb(color.A, color.R, color.G, color.B);
}
