using Android.Content;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using EditorApp.Ui;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// The bar along the bottom: which tool is in hand, undo and redo, the colour, the files, and the
/// handle that opens the tool's own options.
///
/// One row of pictograms and nothing else. Words were costing a third of the screen's width in
/// landscape, and there are only nine things here — few enough to learn, and each one has its name
/// on a long press for the first day.
/// </summary>
public sealed class ToolBar : LinearLayout
{
    private static readonly (EditorTool Tool, Icons.Painter Icon, string Name)[] Tools =
    [
        (EditorTool.Transform, Icons.Move, "Move"),
        (EditorTool.Extrude, Icons.Extrude, "Extrude"),
        (EditorTool.Paint, Icons.Paint, "Paint"),
        (EditorTool.LoopCut, Icons.Cut, "Loop cut"),
    ];

    private readonly Dictionary<EditorTool, IconButtonView> _toolButtons = new();
    private readonly IconButtonView _undo;
    private readonly IconButtonView _redo;
    private readonly IconButtonView _options;
    private readonly View _colour;

    public ToolBar(Context context)
        : base(context)
    {
        Orientation = Orientation.Horizontal;
        SetGravity(GravityFlags.CenterVertical);
        SetBackgroundColor(Style.Panel);

        foreach ((EditorTool tool, Icons.Painter icon, string name) in Tools)
        {
            var button = new IconButtonView(context, icon, name);
            button.Click += (_, _) => ToolChosen?.Invoke(tool);

            _toolButtons.Add(tool, button);
            Add(button);
        }

        Gap();

        _undo = new IconButtonView(context, Icons.Undo, "Undo");
        _undo.Click += (_, _) => UndoRequested?.Invoke();
        Add(_undo);

        _redo = new IconButtonView(context, Icons.Redo, "Redo");
        _redo.Click += (_, _) => RedoRequested?.Invoke();
        Add(_redo);

        Gap();

        // The colour is its own colour — a pictogram of a swatch would be a picture of the thing
        // standing next to the thing.
        _colour = new View(context) { ContentDescription = "Colour", Clickable = true };
        _colour.Click += (_, _) => PaletteRequested?.Invoke();
        Add(_colour);

        var files = new IconButtonView(context, Icons.Files, "Levels");
        files.Click += (_, _) => FilesRequested?.Invoke();
        Add(files);

        // Pushed to the far end, where a thumb rests, and away from anything destructive.
        AddView(new Space(context), new LayoutParams(0, 0, 1f));

        _options = new IconButtonView(context, Icons.ChevronUp, "Tool options");
        _options.Click += (_, _) => OptionsToggled?.Invoke();
        Add(_options);
    }

    public event Action<EditorTool>? ToolChosen;

    public event Action? UndoRequested;

    public event Action? RedoRequested;

    public event Action? PaletteRequested;

    public event Action? FilesRequested;

    public event Action? OptionsToggled;

    private void Add(View view)
    {
        int side = Style.Dp(Context!, Style.IconButtonDp);
        var layout = new LayoutParams(side, side);
        layout.SetMargins(Style.Dp(Context!, 3f), 0, Style.Dp(Context!, 3f), 0);
        AddView(view, layout);
    }

    private void Gap() => AddView(new Space(Context!), new LayoutParams(Style.Dp(Context!, 12f), 0));

    public void Refresh(EditorTool active, bool canUndo, bool canRedo, Color32 colour, bool optionsOpen)
    {
        foreach ((EditorTool tool, IconButtonView button) in _toolButtons)
        {
            button.Chosen = tool == active;
        }

        _undo.Available = canUndo;
        _redo.Available = canRedo;

        _colour.Background = Style.RoundedFill(Context!, Style.ToAndroid(colour), Style.Accent);
        _options.Chosen = optionsOpen;
    }
}
