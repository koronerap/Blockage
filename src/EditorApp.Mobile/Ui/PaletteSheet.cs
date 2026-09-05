using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Voxels;
using EditorApp.Mobile.Tools;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// The palette, as its own page.
///
/// A phone has no room for a panel that is always there, and colour is not something adjusted
/// continuously while modelling — it is chosen, then used for a while. So it gets the whole screen
/// for the moment it is needed and none of it the rest of the time.
///
/// Three things live here and they are genuinely different. Choosing from the library changes which
/// entry is active. Dragging the sliders puts a new colour in a working slot, which becomes a swatch
/// only if it is kept. Recolouring changes what the active slot holds, repainting every voxel
/// already using it — the one destructive action on the page, and the reason it says so.
///
/// Sliders here, steppers everywhere else: a brush radius is a number you want exactly, and a colour
/// is one you drag until it looks right.
/// </summary>
public sealed class PaletteSheet : FrameLayout, IPage
{
    private readonly LinearLayout _body;
    private readonly ScrollView _scroll;

    // Created once and re-attached on every rebuild. The sliders especially: rebuilding the page
    // while a finger is on one would take it away mid-drag.
    private readonly View _preview;
    private readonly TextView _readout;
    private readonly SeekBar[] _channels = new SeekBar[3];
    private readonly TextView _save;
    private readonly TextView _forget;
    private readonly TextView _recolour;

    /// <summary>Set while the sliders are being filled in, so echoing a value back is not a pick.</summary>
    private bool _settingUp;

    private TextView? _note;

    /// <summary>
    /// The slot Recolour acts on: the last one chosen from a grid, not whatever is active now.
    ///
    /// Dragging a slider moves the active colour into a working slot by design — that is what makes
    /// picking a colour different from editing the palette. Without remembering the grid choice,
    /// Recolour could only ever act on the working slot, which is nobody's colour and holds nothing
    /// worth recolouring.
    /// </summary>
    private int _slotUnderEdit = 1;

    public PaletteSheet(Context context)
        : base(context)
    {
        // Opaque, unlike the chrome: this is a page, and a level showing faintly through a grid of
        // colours makes both harder to read.
        SetBackgroundColor(Style.PanelSolid);

        var page = new LinearLayout(context) { Orientation = Orientation.Vertical };
        int pad = Style.Dp(context, 12f);
        page.SetPadding(pad, pad, pad, 0);

        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(context) { Text = "Colour" };
        title.SetTextColor(Style.Text);
        title.SetTextSize(ComplexUnitType.Sp, 14f);
        header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var close = new IconButtonView(context, Resource.Drawable.ic_close, "Close");
        close.Click += (_, _) => Dismissed?.Invoke();
        header.AddView(close);
        page.AddView(header);

        _preview = new View(context);

        _readout = new TextView(context);
        _readout.SetTextColor(Style.TextDim);
        _readout.SetTextSize(ComplexUnitType.Sp, 11.5f);

        for (int channel = 0; channel < 3; channel++)
        {
            SeekBar bar = new(context) { Max = 255 };
            bar.ProgressChanged += (_, _) => Picked();

            // The page is only rebuilt once the finger leaves. Which slot the colour landed in, and
            // whether it can be kept, are answers that belong to the end of a drag rather than to
            // every pixel of it.
            bar.StopTrackingTouch += (_, _) => PickingFinished?.Invoke();

            _channels[channel] = bar;
        }

        _save = Style.Button(context, "Save as swatch");
        _save.Click += (_, _) => SaveRequested?.Invoke();

        _forget = Style.Button(context, "Forget");
        _forget.Click += (_, _) => ForgetRequested?.Invoke();

        _recolour = Style.Button(context, "Recolour slot");
        _recolour.Click += (_, _) => RecolourRequested?.Invoke(_slotUnderEdit, Read());

        _body = new LinearLayout(context) { Orientation = Orientation.Vertical };
        _body.SetPadding(0, pad, 0, pad);

        _scroll = new ScrollView(context);
        _scroll.AddView(_body);
        page.AddView(_scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        AddView(page, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        // Swallow touches so a tap meant for a swatch never reaches the viewport behind it.
        Clickable = true;
    }

    public event Action<byte>? ColorChosen;

    /// <summary>A colour dragged out on the sliders, live.</summary>
    public event Action<Color32>? ColorPicked;

    public event Action? SaveRequested;

    public event Action? ForgetRequested;

    /// <summary>The slot to change, and what to change it to.</summary>
    public event Action<int, Color32>? RecolourRequested;

    /// <summary>The finger has left a slider, so the page can be rebuilt around where it landed.</summary>
    public event Action? PickingFinished;

    public event Action? Dismissed;

    /// <summary>
    /// Rebuilds the page. Every part of it reads from the same snapshot, and a half-updated palette
    /// would be harder to follow than a rebuilt one.
    /// </summary>
    public void Populate(PaletteState state)
    {
        Context context = Context!;
        _body.RemoveAllViews();

        // Opening the page on a slot makes that the one Recolour would act on, until a grid choice
        // says otherwise.
        if (!state.ActiveIsWorking)
        {
            _slotUnderEdit = state.ActiveIndex;
        }

        Color32 active = state.Active;

        Heading("Picked");
        _preview.Background = Style.RoundedFill(context, Style.ToAndroid(active), Style.Accent);
        Detach(_preview);
        Detach(_readout);

        _readout.Text = $"   #{active.R:X2}{active.G:X2}{active.B:X2}    ·    slot {state.ActiveIndex}";

        var top = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);
        top.AddView(_preview, new LinearLayout.LayoutParams(
            Style.Dp(context, 52f), Style.Dp(context, 52f)));
        top.AddView(_readout);
        _body.AddView(top);

        _settingUp = true;

        byte[] values = [active.R, active.G, active.B];
        string[] names = ["Red", "Green", "Blue"];

        for (int channel = 0; channel < 3; channel++)
        {
            SeekBar bar = _channels[channel];
            Detach(bar);
            bar.Progress = values[channel];
            bar.ContentDescription = names[channel];

            _body.AddView(bar, new LinearLayout.LayoutParams(
                Style.Dp(context, 320f), ViewGroup.LayoutParams.WrapContent));
        }

        _settingUp = false;

        var actions = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        actions.SetPadding(0, Style.Dp(context, 8f), 0, 0);

        foreach (TextView button in new[] { _save, _forget, _recolour })
        {
            Detach(button);

            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent);

            layout.SetMargins(0, 0, Style.Dp(context, 8f), 0);
            actions.AddView(button, layout);
        }

        _body.AddView(actions);

        _note = new TextView(context);
        _note.SetTextColor(Style.TextDim);
        _note.SetTextSize(ComplexUnitType.Sp, 10.5f);
        _note.SetPadding(0, Style.Dp(context, 4f), 0, 0);
        _body.AddView(_note);

        Refresh(state);

        if (state.Saved.Length > 0)
        {
            Heading("Saved");
            Grid(context, state, state.Saved);
        }

        Heading("Library");
        Grid(context, state, [.. Enumerable
            .Range(1, Palette.CustomStart - 1)
            .Where(index => state.Colors[index].A != 0)]);
    }

    private void Grid(Context context, PaletteState state, IReadOnlyList<int> indices)
    {
        int side = Style.Dp(context, 40f);
        int gap = Style.Dp(context, 4f);

        // As many columns as the screen can hold rather than a fixed count. In landscape a fixed
        // eight would leave most of the width empty and the grid scrolling for no reason.
        int usable = context.Resources!.DisplayMetrics!.WidthPixels - (2 * Style.Dp(context, 12f));
        int columns = Math.Max(1, usable / (side + (2 * gap)));

        LinearLayout? row = null;
        int inRow = 0;

        foreach (int index in indices)
        {
            if (row is null || inRow == columns)
            {
                row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
                _body.AddView(row);
                inRow = 0;
            }

            var swatch = new View(context)
            {
                Background = Style.RoundedFill(
                    context,
                    Style.ToAndroid(state.Colors[index]),
                    index == state.ActiveIndex ? Style.Accent : null),
                ContentDescription = $"Colour {index}",
            };

            byte chosen = (byte)index;
            swatch.Click += (_, _) =>
            {
                _slotUnderEdit = chosen;
                ColorChosen?.Invoke(chosen);
            };

            var layout = new LinearLayout.LayoutParams(side, side);
            layout.SetMargins(gap, gap, gap, gap);
            row.AddView(swatch, layout);

            inRow++;
        }
    }

    /// <summary>
    /// Updates the parts that change while a slider is moving, and nothing else. The sliders and the
    /// grids are left exactly where they are — one because a finger is on it, the others because
    /// they have not changed.
    /// </summary>
    public void Refresh(PaletteState state)
    {
        Color32 active = state.Active;

        _preview.Background = Style.RoundedFill(Context!, Style.ToAndroid(active), Style.Accent);
        _readout.Text = $"   #{active.R:X2}{active.G:X2}{active.B:X2}    ·    slot {state.ActiveIndex}";

        // Only a colour nobody has kept yet can be kept, and only one that was kept can be given
        // back. Both are dimmed rather than hidden so the page does not change shape under a thumb.
        Style.SetEnabledLook(_save, state.ActiveIsWorking);
        Style.SetEnabledLook(_forget, state.ActiveIsSavedSwatch);

        _recolour.Text = $"Recolour slot {_slotUnderEdit}";

        if (_note is not null)
        {
            _note.Text = state.ActiveIsWorking
                ? $"A working colour: in use, but not kept. {state.FreeSlots} custom slot(s) free. "
                    + $"Recolour writes it into slot {_slotUnderEdit} instead, repainting every voxel "
                    + "already using that one."
                : $"Recolour changes what slot {_slotUnderEdit} holds, and repaints every voxel "
                    + "already using it.";
        }
    }

    /// <summary>A slider moved, so the colour under the sliders is now the active one.</summary>
    private void Picked()
    {
        if (!_settingUp)
        {
            ColorPicked?.Invoke(Read());
        }
    }

    private Color32 Read() => new(
        (byte)_channels[0].Progress,
        (byte)_channels[1].Progress,
        (byte)_channels[2].Progress);

    /// <summary>The picker's own views are reused across rebuilds, so they have to be let go first.</summary>
    private static void Detach(View view)
    {
        if (view.Parent is ViewGroup parent)
        {
            parent.RemoveView(view);
        }
    }

    private void Heading(string text)
    {
        var view = new TextView(Context!) { Text = text };
        view.SetTextColor(Style.Text);
        view.SetTextSize(ComplexUnitType.Sp, 12f);
        view.SetPadding(0, Style.Dp(Context!, 14f), 0, Style.Dp(Context!, 6f));
        _body.AddView(view);
    }

    /// <summary>A page opens at its top, however far down it was left last time.</summary>
    public void ResetScroll() => _scroll.ScrollTo(0, 0);
}
