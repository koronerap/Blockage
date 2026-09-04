using Android.Content;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Export.Mimicraft;

namespace EditorApp.Mobile.Ui;

/// <param name="Id">
/// The rig id for a character, or the weapon id for a weapon. What the game looks the model up by,
/// which is why it is asked for rather than taken from the filename.
/// </param>
public readonly record struct MimicraftOptions(
    MimicraftTarget Target,
    MimicraftUpAxis Up,
    int TurnDegrees,
    string Id)
{
    public static readonly MimicraftOptions Default =
        new(MimicraftTarget.Character, MimicraftUpAxis.Y, 0, string.Empty);

    public MimicraftOrientation Orientation => new(Up, TurnDegrees);
}

/// <summary>
/// Saving for the game, as its own page.
///
/// It exists because a Mimicraft file cannot be written from defaults. Which way the model was built
/// is not in the level and nothing could infer it — a model lying on its side is not a bug in the
/// conversion — and the id the game looks the model up by is not the filename. The desktop asks for
/// both; writing them silently from a phone would produce files that load and fit nothing.
///
/// The problems are listed before anything is written, all of them, not just the first.
/// </summary>
public sealed class MimicraftSheet : FrameLayout, IPage
{
    private static readonly (MimicraftUpAxis Axis, string Label)[] Axes =
        [(MimicraftUpAxis.Y, "Y up"), (MimicraftUpAxis.Z, "Z up"), (MimicraftUpAxis.X, "X up")];

    private readonly LinearLayout _body;
    private readonly ScrollView _scroll;
    private readonly EditText _id;

    private MimicraftOptions _options = MimicraftOptions.Default;

    public MimicraftSheet(Context context)
        : base(context)
    {
        SetBackgroundColor(Style.PanelSolid);

        var page = new LinearLayout(context) { Orientation = Orientation.Vertical };
        int pad = Style.Dp(context, 12f);
        page.SetPadding(pad, pad, pad, 0);

        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(context) { Text = "Save for Mimicraft" };
        title.SetTextColor(Style.Text);
        title.SetTextSize(ComplexUnitType.Sp, 14f);
        header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var close = new IconButtonView(context, Resource.Drawable.ic_close, "Close");
        close.Click += (_, _) => Dismissed?.Invoke();
        header.AddView(close);
        page.AddView(header);

        _id = new EditText(context)
        {
            InputType = InputTypes.ClassText,
            Hint = "Rig id",
        };

        _id.SetTextColor(Style.Text);
        _id.SetTextSize(ComplexUnitType.Sp, 13f);
        _id.TextChanged += (_, _) => _options = _options with { Id = _id.Text ?? string.Empty };

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

        Clickable = true;
    }

    public event Action? Dismissed;

    /// <summary>Asked whenever a choice changes, so the problem list can be checked again.</summary>
    public event Action<MimicraftOptions>? OptionsChanged;

    public event Action<MimicraftOptions>? ExportRequested;

    public MimicraftOptions Options => _options;

    public void Populate(MimicraftOptions options, IReadOnlyList<MimicraftProblem> problems, string levelName)
    {
        _options = options;
        _body.RemoveAllViews();

        Context context = Context!;

        Heading("What this is");
        Row(
            Text("Character", options.Target == MimicraftTarget.Character,
                () => Change(options with { Target = MimicraftTarget.Character })),
            Text("Weapon", options.Target == MimicraftTarget.Weapon,
                () => Change(options with { Target = MimicraftTarget.Weapon })));

        Note(options.Target == MimicraftTarget.Character
            ? "One piece per object, and each object's name is the rig slot it fills. Rename them on "
                + "the Level page."
            : "Every object flattened into one grid under a single weapon id.");

        Heading("Which way the model was built");
        var axes = new List<View>();
        foreach ((MimicraftUpAxis axis, string label) in Axes)
        {
            MimicraftUpAxis chosen = axis;
            axes.Add(Text(label, options.Up == axis, () => Change(options with { Up = chosen })));
        }

        Row([.. axes]);

        var turns = new List<View>();
        foreach (int turn in MimicraftOrientation.Turns)
        {
            int chosen = turn;
            turns.Add(Text($"{turn}°", options.TurnDegrees == turn,
                () => Change(options with { TurnDegrees = chosen })));
        }

        Row([.. turns]);

        Note("Handedness is handled either way — the editor is right-handed and Unity is not. This is "
            + "only about the model's own axes, which nothing in the file could guess.");

        Heading(options.Target == MimicraftTarget.Character ? "Rig id" : "Weapon id");
        _id.Hint = options.Target == MimicraftTarget.Character ? "Rig id" : "Weapon id";

        if ((_id.Text ?? string.Empty) != options.Id)
        {
            _id.Text = options.Id;
        }

        if (_id.Parent is ViewGroup previous)
        {
            previous.RemoveView(_id);
        }

        _body.AddView(_id, new LinearLayout.LayoutParams(
            Style.Dp(context, 260f),
            ViewGroup.LayoutParams.WrapContent));

        Note(options.Target == MimicraftTarget.Character
            ? $"What the game looks the character up by. Left empty it is written empty, which the "
                + $"reader accepts. The file itself will be called {levelName}."
            : "What the game looks the weapon up by. This one is not optional.");

        Heading(problems.Count == 0 ? "Ready" : $"{problems.Count} problem(s)");

        if (problems.Count == 0)
        {
            Note("Nothing is in the way. The file is written where you choose.");
        }
        else
        {
            foreach (MimicraftProblem problem in problems)
            {
                Note($"{problem.Subject} — {problem.Message}", Style.Warning);
            }
        }

        var export = Text("Write the file", chosen: false, () => ExportRequested?.Invoke(_options));
        export.Alpha = problems.Count == 0 ? 1f : 0.35f;
        export.Enabled = problems.Count == 0;

        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, Style.Dp(context, 10f), 0, 0);
        row.AddView(export);
        _body.AddView(row);
    }

    private void Change(MimicraftOptions options)
    {
        _options = options;
        OptionsChanged?.Invoke(options);
    }

    private void Heading(string text)
    {
        var view = new TextView(Context!) { Text = text };
        view.SetTextColor(Style.Text);
        view.SetTextSize(ComplexUnitType.Sp, 12f);
        view.SetPadding(0, Style.Dp(Context!, 14f), 0, Style.Dp(Context!, 6f));
        _body.AddView(view);
    }

    private void Note(string text, Android.Graphics.Color? colour = null)
    {
        var view = new TextView(Context!) { Text = text };
        view.SetTextColor(colour ?? Style.TextDim);
        view.SetTextSize(ComplexUnitType.Sp, 10.5f);
        view.SetPadding(0, Style.Dp(Context!, 3f), 0, 0);
        _body.AddView(view);
    }

    private void Row(params View[] children)
    {
        var row = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, Style.Dp(Context!, 3f), 0, Style.Dp(Context!, 3f));

        foreach (View child in children)
        {
            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent);

            layout.SetMargins(0, 0, Style.Dp(Context!, 6f), 0);
            row.AddView(child, layout);
        }

        _body.AddView(row);
    }

    /// <summary>
    /// A word rather than a pictogram. The chrome is icons because it is always on screen; a page is
    /// read once and these choices have no picture that would be clearer than their names.
    /// </summary>
    private TextView Text(string label, bool chosen, Action clicked)
    {
        TextView button = Style.Button(Context!, label);
        Style.SetSelected(Context!, button, chosen);
        button.Click += (_, _) => clicked();
        return button;
    }

    /// <summary>A page opens at its top, however far down it was left last time.</summary>
    public void ResetScroll() => _scroll.ScrollTo(0, 0);
}
