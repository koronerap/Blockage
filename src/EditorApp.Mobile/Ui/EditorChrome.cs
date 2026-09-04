using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// Everything that is always on screen, and as little of it as possible.
///
/// One full-width bar along the bottom was costing a band of the viewport at every moment, most of
/// it empty. This is the same buttons in four small groups that float over the level instead, each
/// one where the thing it does belongs:
///
/// - the tools down the left, where a thumb reaches while the other hand holds the phone
/// - undo, redo and the two pages at the top right, away from anything used mid-stroke
/// - the level's name at the top left, which is the one thing that is read rather than pressed
/// - the colour and the options handle at the bottom left, next to the tools they belong to
///
/// Everything else is behind a page or behind the chevron. The top of the screen was empty before
/// and is now carrying the half of this that is not part of drawing.
/// </summary>
public sealed class EditorChrome
{
    private static readonly (EditorTool Tool, int Icon, string Name)[] Tools =
    [
        (EditorTool.Transform, Resource.Drawable.ic_move, "Move"),
        (EditorTool.Extrude, Resource.Drawable.ic_extrude, "Extrude"),
        (EditorTool.Paint, Resource.Drawable.ic_paint, "Paint"),
        (EditorTool.LoopCut, Resource.Drawable.ic_cut, "Loop cut"),
    ];

    private readonly Dictionary<EditorTool, IconButtonView> _toolButtons = new();
    private readonly IconButtonView _undo;
    private readonly IconButtonView _redo;
    private readonly IconButtonView _options;
    private readonly SwatchView _colour;
    private readonly TextView _title;

    public EditorChrome(Context context)
    {
        Tools_ = Group(context, Orientation.Vertical);

        foreach ((EditorTool tool, int icon, string name) in Tools)
        {
            var button = new IconButtonView(context, icon, name);
            button.Click += (_, _) => ToolChosen?.Invoke(tool);

            _toolButtons.Add(tool, button);
            Add(context, Tools_, button);
        }

        Actions = Group(context, Orientation.Horizontal);

        _undo = new IconButtonView(context, Resource.Drawable.ic_undo, "Undo");
        _undo.Click += (_, _) => UndoRequested?.Invoke();
        Add(context, Actions, _undo);

        _redo = new IconButtonView(context, Resource.Drawable.ic_redo, "Redo");
        _redo.Click += (_, _) => RedoRequested?.Invoke();
        Add(context, Actions, _redo);

        Actions.AddView(new Space(context), new LinearLayout.LayoutParams(Style.Dp(context, 10f), 0));

        var scene = new IconButtonView(context, Resource.Drawable.ic_tune, "Level and view");
        scene.Click += (_, _) => SceneRequested?.Invoke();
        Add(context, Actions, scene);

        var files = new IconButtonView(context, Resource.Drawable.ic_folder, "Levels");
        files.Click += (_, _) => FilesRequested?.Invoke();
        Add(context, Actions, files);

        Handles = Group(context, Orientation.Horizontal);

        _colour = new SwatchView(context);
        _colour.Click += (_, _) => PaletteRequested?.Invoke();
        Add(context, Handles, _colour);

        _options = new IconButtonView(context, Resource.Drawable.ic_expand_less, "Tool options");
        _options.Click += (_, _) => OptionsToggled?.Invoke();
        Add(context, Handles, _options);

        // Read, never pressed, so it gets no panel behind it and none of the width.
        _title = new TextView(context);
        _title.SetTextColor(Style.TextDim);
        _title.SetTextSize(ComplexUnitType.Sp, 12f);
        _title.SetPadding(Style.Dp(context, 4f), 0, 0, 0);
        Title = _title;
    }

    /// <summary>The four tools, down the left edge.</summary>
    public LinearLayout Tools_ { get; }

    /// <summary>Undo, redo and the two pages, at the top right.</summary>
    public LinearLayout Actions { get; }

    /// <summary>The colour and the options chevron, at the bottom left.</summary>
    public LinearLayout Handles { get; }

    /// <summary>The level's name, at the top left.</summary>
    public View Title { get; }

    public event Action<EditorTool>? ToolChosen;

    public event Action? UndoRequested;

    public event Action? RedoRequested;

    public event Action? PaletteRequested;

    public event Action? SceneRequested;

    public event Action? FilesRequested;

    public event Action? OptionsToggled;

    private static LinearLayout Group(Context context, Orientation orientation)
    {
        var group = new LinearLayout(context) { Orientation = orientation };
        group.SetGravity(GravityFlags.Center);

        int pad = Style.Dp(context, 4f);
        group.SetPadding(pad, pad, pad, pad);
        group.Background = Style.RoundedFill(context, Style.Panel, radiusDp: Style.PanelRadiusDp);

        return group;
    }

    private static void Add(Context context, LinearLayout group, View view)
    {
        int side = Style.Dp(context, Style.IconButtonDp);
        var layout = new LinearLayout.LayoutParams(side, side);

        int gap = Style.Dp(context, 2f);
        if (group.Orientation == Orientation.Vertical)
        {
            layout.SetMargins(0, gap, 0, gap);
        }
        else
        {
            layout.SetMargins(gap, 0, gap, 0);
        }

        group.AddView(view, layout);
    }

    public void Refresh(
        EditorTool active,
        bool canUndo,
        bool canRedo,
        Color32 colour,
        bool optionsOpen,
        string name,
        bool unsaved)
    {
        foreach ((EditorTool tool, IconButtonView button) in _toolButtons)
        {
            button.Chosen = tool == active;
        }

        _undo.Available = canUndo;
        _redo.Available = canRedo;
        _colour.Show(Style.ToAndroid(colour));
        _options.Chosen = optionsOpen;

        // A dot rather than the word: a phone has no title bar to spell it out in, and the mark is
        // only ever asked one question — is there anything unsaved.
        _title.Text = unsaved ? name + " •" : name;
    }
}
