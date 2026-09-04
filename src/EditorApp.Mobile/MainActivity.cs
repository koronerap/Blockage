using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Mobile.Rendering;

namespace EditorApp.Mobile;

/// <summary>
/// The one screen the editor lives on: a GL surface with the interface drawn over it as ordinary
/// Android views. No layout XML — the hierarchy is small enough that building it here keeps it in
/// one readable place, and a resource file would only add a second one to keep in step.
/// </summary>
[Activity(
    Label = "Blockage",
    MainLauncher = true,

    // No title bar and no status bar. A viewport this size cannot spare the height, and the app
    // has nothing to say in a title bar that the screen does not already show.
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",

    // A viewport wants width, and locking the orientation also keeps the GL context off the
    // rotation path entirely while the renderer is still young. Worth revisiting once the surface
    // survives being recreated.
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
    private TextView? _reportView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _surface = new EditorSurfaceView(this);
        _surface.ReportChanged += UpdateReport;

        _reportView = new TextView(this)
        {
            Typeface = Typeface.Monospace,
            Text = _surface.Report,
        };
        _reportView.SetTextSize(ComplexUnitType.Sp, 11);
        _reportView.SetTextColor(Color.Argb(255, 226, 230, 236));
        _reportView.SetBackgroundColor(Color.Argb(150, 0, 0, 0));
        _reportView.SetPadding(24, 24, 24, 24);

        FrameLayout root = new(this);
        root.AddView(
            _surface,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        root.AddView(
            _reportView,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Start,
                LeftMargin = 32,
                TopMargin = 32,
            });

        SetContentView(root);
    }

    private void UpdateReport()
    {
        if (_reportView is not null && _surface is not null)
        {
            _reportView.Text = _surface.Report;
        }
    }

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
