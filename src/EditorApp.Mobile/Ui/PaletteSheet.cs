using Android.Content;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Voxels;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// The palette, as its own page over the viewport.
///
/// A phone has no room for a panel that is always there, and colour is not something being adjusted
/// continuously while modelling — it is chosen, then used for a while. So it gets the whole screen
/// for the moment it is needed and none of it the rest of the time.
///
/// Empty custom slots are left out. There are sixty-four of them and, until one is saved, every one
/// looks the same as the last.
/// </summary>
public sealed class PaletteSheet : FrameLayout
{
    private readonly LinearLayout _rows;
    private readonly Dictionary<int, View> _swatches = new();

    public PaletteSheet(Context context)
        : base(context)
    {
        // Opaque, unlike the tool bar: this is a page, and a level showing faintly through a grid of
        // colours makes both harder to read.
        SetBackgroundColor(Style.PanelSolid);

        var page = new LinearLayout(context) { Orientation = Orientation.Vertical };
        page.SetPadding(Style.Dp(context, 12f), Style.Dp(context, 12f), Style.Dp(context, 12f), 0);

        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(context) { Text = "Colour" };
        title.SetTextColor(Style.Text);
        title.SetTextSize(Android.Util.ComplexUnitType.Sp, 16f);
        header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        TextView close = Style.Button(context, "Done");
        close.Click += (_, _) => Dismissed?.Invoke();
        header.AddView(close);

        page.AddView(header);

        _rows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        _rows.SetPadding(0, Style.Dp(context, 12f), 0, Style.Dp(context, 12f));

        var scroll = new ScrollView(context);
        scroll.AddView(_rows);
        page.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        AddView(page, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        // Swallow touches so a tap meant for a swatch never reaches the viewport behind it.
        Clickable = true;
    }

    public event Action<byte>? ColorChosen;

    public event Action? Dismissed;

    /// <summary>
    /// Rebuilds the grid. Called when the sheet opens rather than on every change, because the
    /// palette only moves when the user moves it.
    /// </summary>
    public void Populate(ReadOnlySpan<Color32> colors, byte selected)
    {
        _rows.RemoveAllViews();
        _swatches.Clear();

        Context context = Context!;
        int side = Style.Dp(context, Style.TouchTargetDp);
        int gap = Style.Dp(context, 4f);

        // As many columns as the screen can hold rather than a fixed count. In landscape a fixed
        // eight would leave most of the width empty and the grid scrolling for no reason.
        int usable = context.Resources!.DisplayMetrics!.WidthPixels - (2 * Style.Dp(context, 12f));
        int columns = Math.Max(1, usable / (side + (2 * gap)));

        LinearLayout? row = null;
        int inRow = 0;

        for (int index = 1; index < colors.Length; index++)
        {
            Color32 color = colors[index];

            // Index 0 is empty by definition, and an unsaved custom slot is transparent.
            if (color.A == 0)
            {
                continue;
            }

            if (row is null || inRow == columns)
            {
                row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
                _rows.AddView(row);
                inRow = 0;
            }

            var swatch = new View(context)
            {
                Background = Style.RoundedFill(
                    context,
                    Style.ToAndroid(color),
                    index == selected ? Style.Accent : null),
            };

            byte chosen = (byte)index;
            swatch.Click += (_, _) => ColorChosen?.Invoke(chosen);

            var layout = new LinearLayout.LayoutParams(side, side);
            layout.SetMargins(gap, gap, gap, gap);
            row.AddView(swatch, layout);

            _swatches.Add(index, swatch);
            inRow++;
        }
    }

    /// <summary>Moves the ring to a different colour without rebuilding the grid.</summary>
    public void SetSelected(ReadOnlySpan<Color32> colors, byte selected)
    {
        foreach ((int index, View swatch) in _swatches)
        {
            swatch.Background = Style.RoundedFill(
                Context!,
                Style.ToAndroid(colors[index]),
                index == selected ? Style.Accent : null);
        }
    }
}
