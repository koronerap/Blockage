using Android.Content;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// The bar along the bottom: which tool is in hand, undo and redo, and the way into the palette.
///
/// It sits at the bottom because that is the part of a phone a thumb reaches without regripping,
/// and it is the only chrome over the viewport — everything else the editor can do is either a
/// gesture or behind the palette button.
/// </summary>
public sealed class ToolBar : LinearLayout
{
    private static readonly (EditorTool Tool, string Label)[] Tools =
    [
        (EditorTool.Transform, "Move"),
        (EditorTool.Extrude, "Extrude"),
        (EditorTool.Paint, "Paint"),
        (EditorTool.LoopCut, "Cut"),
    ];

    private readonly Dictionary<EditorTool, TextView> _toolButtons = new();
    private readonly TextView _undo;
    private readonly TextView _redo;
    private readonly TextView _palette;

    public ToolBar(Context context)
        : base(context)
    {
        Orientation = Orientation.Horizontal;
        SetGravity(GravityFlags.Center);
        SetBackgroundColor(Style.Panel);

        int gap = Style.Dp(context, 6f);
        SetPadding(gap, gap, gap, gap);

        foreach ((EditorTool tool, string label) in Tools)
        {
            TextView button = Style.Button(context, label);
            button.Click += (_, _) => ToolChosen?.Invoke(tool);

            _toolButtons.Add(tool, button);
            Add(button, gap, weight: 1f);
        }

        // A gap, so undo is never hit while reaching for a tool.
        var spacer = new Space(context);
        AddView(spacer, new LayoutParams(Style.Dp(context, 10f), 0));

        _undo = Style.Button(context, "Undo");
        _undo.Click += (_, _) => UndoRequested?.Invoke();
        Add(_undo, gap);

        _redo = Style.Button(context, "Redo");
        _redo.Click += (_, _) => RedoRequested?.Invoke();
        Add(_redo, gap);

        _palette = Style.Button(context, "Colour");
        _palette.Click += (_, _) => PaletteRequested?.Invoke();
        Add(_palette, gap);
    }

    public event Action<EditorTool>? ToolChosen;

    public event Action? UndoRequested;

    public event Action? RedoRequested;

    public event Action? PaletteRequested;

    private void Add(View view, int gap, float weight = 0f)
    {
        var layout = new LayoutParams(
            weight > 0f ? 0 : ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent,
            weight);

        layout.SetMargins(gap / 2, 0, gap / 2, 0);
        AddView(view, layout);
    }

    /// <summary>
    /// Brings the bar up to date with the session. Undo and redo are dimmed rather than removed when
    /// there is nothing to undo — a button that comes and goes is harder to aim at than a dull one.
    /// </summary>
    public void Refresh(EditorTool active, bool canUndo, bool canRedo, Color32 color)
    {
        foreach ((EditorTool tool, TextView button) in _toolButtons)
        {
            Style.SetSelected(Context!, button, tool == active);
        }

        Style.SetEnabledLook(_undo, canUndo);
        Style.SetEnabledLook(_redo, canRedo);

        // The swatch is the button: what colour is loaded matters more often than the word does.
        _palette.Background = Style.RoundedFill(Context!, Style.ToAndroid(color), Style.Accent);
        _palette.SetTextColor(Luminance(color) > 140 ? Style.Panel : Style.Text);
    }

    /// <summary>Rec. 601 luma — enough to decide whether a label reads better dark or light.</summary>
    private static int Luminance(Color32 color) =>
        ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000;
}
