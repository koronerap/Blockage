using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _surface = new EditorSurfaceView(this);
        _surface.SessionChanged += Refresh;

        _toolBar = new ToolBar(this);
        _toolBar.ToolChosen += tool => _surface.SetTool(tool);
        _toolBar.UndoRequested += () => _surface.Undo();
        _toolBar.RedoRequested += () => _surface.Redo();
        _toolBar.PaletteRequested += ShowPalette;

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

        FrameLayout root = new(this);
        root.AddView(_surface, Fill());
        root.AddView(
            _toolBar,
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
                _surface.ActiveColor);
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
