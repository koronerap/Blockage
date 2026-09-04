using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Mobile.Files;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// The level library, as its own page.
///
/// It replaces the desktop's file browser rather than porting it: Android gives a process no
/// directory to browse, so instead of paths this shows the levels the app itself holds, by name.
/// Bringing one in from elsewhere, or sending one out, goes through the system document picker —
/// the two buttons at the top.
/// </summary>
public sealed class FilesSheet : FrameLayout, IPage
{
    private readonly LinearLayout _list;
    private readonly ScrollView _scroll;
    private readonly TextView _current;

    public FilesSheet(Context context)
        : base(context)
    {
        SetBackgroundColor(Style.PanelSolid);

        var page = new LinearLayout(context) { Orientation = Orientation.Vertical };
        int pad = Style.Dp(context, 12f);
        page.SetPadding(pad, pad, pad, 0);

        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);

        _current = new TextView(context);
        _current.SetTextColor(Style.Text);
        _current.SetTextSize(ComplexUnitType.Sp, 14f);
        header.AddView(_current, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var close = new IconButtonView(context, Resource.Drawable.ic_close, "Close");
        close.Click += (_, _) => Dismissed?.Invoke();
        header.AddView(close);

        page.AddView(header);

        var actions = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        actions.SetPadding(0, pad, 0, pad);
        AddAction(context, actions, "New", () => NewRequested?.Invoke());
        AddAction(context, actions, "Save", () => SaveRequested?.Invoke());
        AddAction(context, actions, "Save as", () => SaveAsRequested?.Invoke());
        AddAction(context, actions, "Import", () => ImportRequested?.Invoke());
        AddAction(context, actions, "Export", () => ExportRequested?.Invoke());
        page.AddView(actions);

        var outputs = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        outputs.SetPadding(0, 0, 0, pad);
        AddAction(context, outputs, "Mesh .glb", () => MeshExportRequested?.Invoke(false));
        AddAction(context, outputs, "Mesh .obj (zip)", () => MeshExportRequested?.Invoke(true));
        AddAction(context, outputs, "For Mimicraft", () => MimicraftRequested?.Invoke());
        page.AddView(outputs);

        _list = new LinearLayout(context) { Orientation = Orientation.Vertical };

        _scroll = new ScrollView(context);
        _scroll.AddView(_list);
        page.AddView(_scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        AddView(page, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        // Swallow touches so nothing meant for this page reaches the viewport behind it.
        Clickable = true;
    }

    public event Action? Dismissed;

    public event Action? NewRequested;

    public event Action? SaveRequested;

    public event Action? SaveAsRequested;

    public event Action? ImportRequested;

    public event Action? ExportRequested;

    /// <summary>True for the zipped OBJ, false for the single-file GLB.</summary>
    public event Action<bool>? MeshExportRequested;

    public event Action? MimicraftRequested;

    public event Action<LevelEntry>? OpenRequested;

    public event Action<LevelEntry>? DeleteRequested;

    private static void AddAction(Context context, LinearLayout row, string label, Action clicked)
    {
        TextView button = Style.Button(context, label);
        button.Click += (_, _) => clicked();

        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, 0, Style.Dp(context, 8f), 0);

        row.AddView(button, layout);
    }

    public void Populate(IReadOnlyList<LevelEntry> levels, string currentName, bool unsaved)
    {
        Context context = Context!;
        _current.Text = unsaved ? currentName + " — unsaved" : currentName;

        _list.RemoveAllViews();

        if (levels.Count == 0)
        {
            var empty = new TextView(context)
            {
                Text = "No levels saved on this device yet. Save one, or import a .vxlevel.",
            };
            empty.SetTextColor(Style.TextDim);
            empty.SetTextSize(ComplexUnitType.Sp, 11.5f);
            empty.SetPadding(0, Style.Dp(context, 8f), 0, 0);
            _list.AddView(empty);
            return;
        }

        foreach (LevelEntry level in levels)
        {
            _list.AddView(Row(context, level));
        }
    }

    private View Row(Context context, LevelEntry level)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(0, Style.Dp(context, 4f), 0, Style.Dp(context, 4f));

        var label = new TextView(context)
        {
            Text = level.Name,
            Background = Style.RoundedFill(context, Style.ButtonIdle),
        };
        label.SetTextColor(Style.Text);
        label.SetTextSize(ComplexUnitType.Sp, 13f);
        label.SetPadding(Style.Dp(context, 12f), 0, Style.Dp(context, 12f), 0);
        label.SetMinimumHeight(Style.Dp(context, Style.TouchTargetDp));
        label.Gravity = GravityFlags.CenterVertical;
        label.Click += (_, _) => OpenRequested?.Invoke(level);

        row.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var detail = new TextView(context)
        {
            Text = $"{level.Modified:d MMM HH:mm} · {level.Bytes / 1024} KB",
        };
        detail.SetTextColor(Style.TextDim);
        detail.SetTextSize(ComplexUnitType.Sp, 11f);
        detail.SetPadding(Style.Dp(context, 12f), 0, Style.Dp(context, 12f), 0);
        row.AddView(detail);

        TextView delete = Style.Button(context, "Delete");
        delete.Click += (_, _) => DeleteRequested?.Invoke(level);
        row.AddView(delete);

        return row;
    }

    /// <summary>A page opens at its top, however far down it was left last time.</summary>
    public void ResetScroll() => _scroll.ScrollTo(0, 0);
}
