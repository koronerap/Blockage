using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using EditorApp.Mobile.Files;
using EditorApp.Mobile.Rendering;
using EditorApp.Mobile.Ui;

namespace EditorApp.Mobile;

/// <summary>
/// The one screen the editor lives on: a GL surface with the interface over it as ordinary Android
/// views. No layout XML — the hierarchy is small enough that building it here keeps it in one
/// readable place, and a resource file would only add a second one to keep in step.
/// </summary>
[Activity(
    Label = "Blockage",
    MainLauncher = true,

    // No title bar and no status bar. A viewport this size cannot spare the height, and the app has
    // nothing to say in a title bar that the screen does not already show.
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",

    // A viewport wants width, and locking the orientation also keeps the GL context off the rotation
    // path entirely while the renderer is still young. Worth revisiting once the surface survives
    // being recreated.
    ScreenOrientation = ScreenOrientation.UserLandscape,

    // Handle these rather than being torn down and rebuilt for them.
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.KeyboardHidden
        | ConfigChanges.UiMode
        | ConfigChanges.Density)]
public sealed class MainActivity : Activity
{
    private EditorSurfaceView? _surface;
    private ToolBar? _toolBar;
    private PaletteSheet? _palette;
    private FilesSheet? _files;
    private FileActions? _actions;
    private OptionsBar? _options;

    /// <summary>
    /// Whether the tool's options are showing. Closed to begin with: they are set once and then left
    /// alone, and a row that is always there is a row the viewport does not get.
    /// </summary>
    private bool _optionsOpen;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _surface = new EditorSurfaceView(this);
        _surface.SessionChanged += Refresh;

        _options = new OptionsBar(this);
        _options.TransformModeChosen += mode => _surface.Configure(s => s.TransformMode = mode);
        _options.TransformSpaceChosen += space => _surface.Configure(s => s.TransformSpace = space);
        _options.ExtrudeCreatesObjectChanged += on => _surface.Configure(s => s.ExtrudeCreatesObject = on);
        _options.ExtrudeStepped += steps => _surface.StepExtrude(steps);
        _options.PaintModeChosen += mode => _surface.Configure(s => s.PaintMode = mode);
        _options.BrushRadiusChanged += radius => _surface.Configure(s => s.BrushRadius = radius);
        _options.BucketThresholdChanged += value => _surface.Configure(s => s.BucketThreshold = value);
        _options.BucketWholeObjectChanged += on => _surface.Configure(s => s.BucketWholeObject = on);

        _toolBar = new ToolBar(this);
        _toolBar.ToolChosen += tool => _surface.SetTool(tool);
        _toolBar.UndoRequested += () => _surface.Undo();
        _toolBar.RedoRequested += () => _surface.Redo();
        _toolBar.PaletteRequested += ShowPalette;
        _toolBar.OptionsToggled += () =>
        {
            _optionsOpen = !_optionsOpen;
            Refresh();
        };

        _palette = new PaletteSheet(this) { Visibility = ViewStates.Gone };
        _palette.ColorChosen += index =>
        {
            _surface.SetColorIndex(index);
            _palette.SetSelected(_surface.PaletteSnapshot(), index);
        };
        _palette.Dismissed += () => _palette.Visibility = ViewStates.Gone;

        // Private storage: emptied if the app is uninstalled, which is exactly why Export sits
        // beside Save rather than somewhere further in.
        var store = new LevelStore(Path.Combine(FilesDir!.AbsolutePath, "levels"));
        _actions = new FileActions(this, _surface, store);

        _files = new FilesSheet(this) { Visibility = ViewStates.Gone };
        _files.Dismissed += () => _files.Visibility = ViewStates.Gone;
        _files.NewRequested += () => _actions.New();
        _files.SaveRequested += () => _actions.Save();
        _files.SaveAsRequested += () => _actions.SaveAs();
        _files.ImportRequested += () => _actions.Import();
        _files.ExportRequested += () => _actions.Export();
        _files.OpenRequested += level => _actions.Open(level);
        _files.DeleteRequested += level => _actions.Delete(level);

        // The library only changes through these actions, so this is the one place the list can go
        // stale without anyone noticing.
        _actions.LibraryChanged += () => ShowFiles(store);
        _toolBar.FilesRequested += () => ShowFiles(store);

        // The two bars travel together at the bottom, so the options for a tool are next to the
        // button that chose it rather than at the far end of the screen.
        var bars = new LinearLayout(this) { Orientation = Orientation.Vertical };
        bars.AddView(_options, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));
        bars.AddView(_toolBar, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        InsetRoot root = new(this);
        root.InsetsChanged += ApplyInsets;
        root.AddView(_surface, Fill());
        root.AddView(
            bars,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Bottom,
            });
        root.AddView(_palette, Fill());
        root.AddView(_files, Fill());

        SetContentView(root);
        Refresh();
    }

    /// <summary>
    /// Holds the interface clear of the system's bars. The GL surface keeps the whole window — a
    /// viewport wants every pixel — but a button underneath the navigation bar cannot be pressed,
    /// and in landscape that bar takes the side the last button is on.
    /// </summary>
    private void ApplyInsets(EdgeInsets insets)
    {
        int gap = Style.Dp(this, 6f);

        _toolBar?.SetPadding(insets.Left + gap, gap, insets.Right + gap, insets.Bottom + gap);
        _options?.SetPadding(insets.Left + gap, gap, insets.Right + gap, gap);

        // The pages already carry their own margin inside; all they need from here is the bars.
        _palette?.SetPadding(insets.Left, insets.Top, insets.Right, insets.Bottom);
        _files?.SetPadding(insets.Left, insets.Top, insets.Right, insets.Bottom);
    }

    private static FrameLayout.LayoutParams Fill() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

    private void ShowPalette()
    {
        if (_surface is null || _palette is null)
        {
            return;
        }

        Color32[] colors = _surface.PaletteSnapshot();
        _palette.Populate(colors, _surface.ActiveColorIndex);
        _palette.Visibility = ViewStates.Visible;
    }

    private void ShowFiles(LevelStore store)
    {
        if (_surface is null || _files is null)
        {
            return;
        }

        _files.Populate(store.List(), _surface.ProjectName, _surface.HasUnsavedChanges);
        _files.Visibility = ViewStates.Visible;
    }

    /// <summary>
    /// The document picker's answer. Deprecated in favour of the AndroidX result APIs, which this
    /// app does not carry — it has no AndroidX at all, and one intent in each direction does not
    /// justify the dependency.
    /// </summary>
#pragma warning disable CA1422
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        _actions?.OnPicked(requestCode, resultCode, data);
    }
#pragma warning restore CA1422

    private void Refresh()
    {
        if (_surface is not null && _toolBar is not null)
        {
            _toolBar.Refresh(
                _surface.ActiveTool,
                _surface.CanUndo,
                _surface.CanRedo,
                _surface.ActiveColor,
                _optionsOpen);

            if (_options is not null)
            {
                _options.Visibility = _optionsOpen ? ViewStates.Visible : ViewStates.Gone;

                if (_optionsOpen)
                {
                    _options.Refresh(_surface.State);
                }
            }
        }
    }

    /// <summary>
    /// The palette is a page, so the back gesture should close it before leaving the app.
    ///
    /// Marked obsolete from Android 33, where the replacement is an OnBackInvokedCallback — but that
    /// only runs for an app that has opted into predictive back in its manifest, which this one has
    /// not. Until it does, this override is still the path the system takes.
    /// </summary>
#pragma warning disable CA1422
    public override void OnBackPressed()
    {
        if (_palette is { Visibility: ViewStates.Visible })
        {
            _palette.Visibility = ViewStates.Gone;
            return;
        }

        if (_files is { Visibility: ViewStates.Visible })
        {
            _files.Visibility = ViewStates.Gone;
            return;
        }

        base.OnBackPressed();
    }
#pragma warning restore CA1422

    protected override void OnPause()
    {
        base.OnPause();
        _surface?.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _surface?.OnResume();
    }
}
