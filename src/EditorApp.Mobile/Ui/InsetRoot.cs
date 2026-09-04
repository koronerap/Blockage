using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace EditorApp.Mobile.Ui;

/// <param name="Left">Pixels along each edge that the system's own bars are sitting on.</param>
public readonly record struct EdgeInsets(int Left, int Top, int Right, int Bottom);

/// <summary>
/// The root the whole editor sits in, and the only thing that knows where the system's bars are.
///
/// The viewport is meant to run edge to edge — a phone has no screen to spare and a 3D view is the
/// one thing that benefits from every pixel. But the navigation bar sits on top of that, and in
/// landscape it takes a strip down one side, which is exactly where the last button in the tool bar
/// ends up. So the surface keeps the whole window and the interface is held clear of the bars.
///
/// Which side the strip lands on depends on how the phone is being held, so it cannot be a constant.
/// </summary>
public sealed class InsetRoot(Context context) : FrameLayout(context)
{
    /// <summary>Raised whenever the system bars move — including on the first layout.</summary>
    public event Action<EdgeInsets>? InsetsChanged;

    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets is not null)
        {
            InsetsChanged?.Invoke(Measure(insets));
        }

        // Passed on rather than consumed: the children are positioned around the bars by padding,
        // and anything added later should still get the chance to see them.
        return base.OnApplyWindowInsets(insets);
    }

    private static EdgeInsets Measure(WindowInsets insets)
    {
        // OperatingSystem rather than Build.VERSION: the platform analyser understands this one, and
        // an unguarded call to an API the minimum version does not have is exactly the mistake worth
        // having checked.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            Insets bars = insets.GetInsets(WindowInsets.Type.SystemBars());
            return new EdgeInsets(bars.Left, bars.Top, bars.Right, bars.Bottom);
        }

#pragma warning disable CA1422  // The typed accessors above only exist from API 30.
        return new EdgeInsets(
            insets.SystemWindowInsetLeft,
            insets.SystemWindowInsetTop,
            insets.SystemWindowInsetRight,
            insets.SystemWindowInsetBottom);
#pragma warning restore CA1422
    }
}
