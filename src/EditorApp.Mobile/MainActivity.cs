using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Core.Export.Mimicraft;
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
    private EditorChrome? _chrome;
    private PaletteSheet? _palette;
    private FilesSheet? _files;
    private SceneSheet? _scene;
    private MimicraftSheet? _mimicraft;
    private MimicraftOptions _mimicraftOptions = MimicraftOptions.Default;
    private FileActions? _actions;
    private ExportActions? _exports;
    private OptionsBar? _options;

    /// <summary>
    /// Whether the tool's options are showing. Closed to begin with: they are set once and then left
    /// alone, and a row that is always there is a row the viewport does not get.
    /// </summary>
    private bool _optionsOpen;

    /// <summary>The floating groups, so the system bars can be kept off all of them at once.</summary>
    private readonly List<View> _floating = [];

    /// <summary>The full-screen pages. Exactly one of them is ever visible.</summary>
    private readonly List<View> _pages = [];

    private LinearLayout? _corner;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _surface = new EditorSurfaceView(this);
        _surface.SessionChanged += Refresh;

        _options = new OptionsBar(this);
        _options.TransformModeChosen += mode => _surface.Configure(s => s.TransformMode = mode);
        _options.TransformSpaceChosen += space => _surface.Configure(s => s.TransformSpace = space);
        _options.ExtrudeSelectionModeChosen += mode => _surface.Configure(s => s.ExtrudeSelectionMode = mode);
        _options.ExtrudeCreatesObjectChanged += on => _surface.Configure(s => s.ExtrudeCreatesObject = on);
        _options.ExtrudeStepped += steps => _surface.StepExtrude(steps);
        _options.PaintModeChosen += mode => _surface.Configure(s => s.PaintMode = mode);
        _options.BrushRadiusChanged += radius => _surface.Configure(s => s.BrushRadius = radius);
        _options.BucketThresholdChanged += value => _surface.Configure(s => s.BucketThreshold = value);
        _options.BucketWholeObjectChanged += on => _surface.Configure(s => s.BucketWholeObject = on);
        _options.SamplerArmed += armed => _surface.ArmSampler(armed);

        _chrome = new EditorChrome(this);
        _chrome.ToolChosen += tool => _surface.SetTool(tool);
        _chrome.UndoRequested += () => _surface.Undo();
        _chrome.RedoRequested += () => _surface.Redo();
        _chrome.PaletteRequested += ShowPalette;
        _chrome.SceneRequested += ShowScene;
        _chrome.OptionsToggled += () =>
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
        _palette.Dismissed += () => ShowOnly(null);

        _scene = new SceneSheet(this) { Visibility = ViewStates.Gone };
        _scene.Dismissed += () => ShowOnly(null);
        _scene.GridChanged += on => { _surface.SetGridVisible(on); ShowScene(); };
        _scene.ShadingChanged += mode => { _surface.ConfigureLighting(l => l.Mode = mode); ShowScene(); };
        _scene.LightAngleChanged += (azimuth, elevation) =>
        {
            _surface.ConfigureLighting(l => { l.Azimuth = azimuth; l.Elevation = elevation; });
            ShowScene();
        };
        _scene.LightStrengthChanged += (intensity, ambient) =>
        {
            _surface.ConfigureLighting(l => { l.Intensity = intensity; l.Ambient = ambient; });
            ShowScene();
        };
        _scene.LightReset += () => { _surface.ConfigureLighting(l => l.ResetAngles()); ShowScene(); };
        _scene.VoxelSizeChanged += size => { _surface.Configure(s => s.SetVoxelSize(size)); ShowScene(); };
        _scene.RotateRequested += direction => { _surface.Configure(s => s.RotateFocus(direction)); ShowScene(); };
        _scene.FrameRequested += () => { _surface.FrameLevel(); ShowOnly(null); };
        _scene.ObjectChosen += id => { _surface.Configure(s => s.TryFocus(id)); ShowScene(); };
        _scene.ObjectVisibilityToggled += id => { _surface.ToggleObjectVisible(id); ShowScene(); };
        _scene.ObjectDeleteRequested += id => { _surface.Configure(s => s.DeleteObject(id)); ShowScene(); };
        _scene.ObjectRenameRequested += o => AskForName("Rename object", o.Name, name =>
        {
            _surface.RenameObject(o.Id, name);
            ShowScene();
        });

        _mimicraft = new MimicraftSheet(this) { Visibility = ViewStates.Gone };
        _mimicraft.Dismissed += () => ShowOnly(_files);
        _mimicraft.OptionsChanged += options => { _mimicraftOptions = options; ShowMimicraft(); };
        _mimicraft.ExportRequested += options =>
        {
            _mimicraftOptions = options;
            ShowOnly(null);
            _exports?.Start(
                options.Target == MimicraftTarget.Character ? ExportKind.Character : ExportKind.Weapon,
                options);
        };

        // Private storage: emptied if the app is uninstalled, which is exactly why Export sits
        // beside Save rather than somewhere further in.
        var store = new LevelStore(Path.Combine(FilesDir!.AbsolutePath, "levels"));
        _actions = new FileActions(this, _surface, store);

        _files = new FilesSheet(this) { Visibility = ViewStates.Gone };
        _files.Dismissed += () => ShowOnly(null);
        _files.NewRequested += () => _actions.New();
        _files.SaveRequested += () => _actions.Save();
        _files.SaveAsRequested += () => _actions.SaveAs();
        _files.ImportRequested += () => _actions.Import();
        _files.ExportRequested += () => _actions.Export();

        _exports = new ExportActions(this, _surface, CacheDir!.AbsolutePath);
        _files.MeshExportRequested += zip =>
            _exports.Start(zip ? ExportKind.ObjZip : ExportKind.Glb);
        _files.MimicraftRequested += ShowMimicraft;
        _files.OpenRequested += level => _actions.Open(level);
        _files.DeleteRequested += level => _actions.Delete(level);

        // The library only changes through these actions, so this is the one place the list can go
        // stale without anyone noticing.
        _actions.LibraryChanged += () => ShowFiles(store);
        _chrome.FilesRequested += () => ShowFiles(store);

        // The options open upwards out of the handle that asked for them, rather than pushing a
        // second bar across the whole screen.
        _corner = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var optionsLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent);

        optionsLayout.SetMargins(0, 0, 0, Style.Dp(this, 6f));
        _corner.AddView(_options, optionsLayout);
        _corner.AddView(_chrome.Handles, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent));

        InsetRoot root = new(this);
        root.InsetsChanged += ApplyInsets;
        root.AddView(_surface, Fill());

        Float(root, _chrome.Title, GravityFlags.Top | GravityFlags.Start);
        Float(root, _chrome.Actions, GravityFlags.Top | GravityFlags.End);
        Float(root, _chrome.Tools_, GravityFlags.CenterVertical | GravityFlags.Start);
        Float(root, _corner, GravityFlags.Bottom | GravityFlags.Start);
        // Order here is z-order, and one page opening over another that is still visible is exactly
        // the bug ShowOnly exists to stop.
        foreach (View page in new View[] { _palette, _scene, _files, _mimicraft })
        {
            root.AddView(page, Fill());
            _pages.Add(page);
        }

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
        // Each group is pushed off the edges it is actually against, so a bar down one side moves
        // only what that side holds.
        int margin = Style.Dp(this, 8f);

        foreach (View group in _floating)
        {
            if (group.LayoutParameters is FrameLayout.LayoutParams layout)
            {
                layout.SetMargins(
                    insets.Left + margin,
                    insets.Top + margin,
                    insets.Right + margin,
                    insets.Bottom + margin);

                group.LayoutParameters = layout;
            }
        }

        // The pages already carry their own margin inside; all they need from here is the bars.
        _palette?.SetPadding(insets.Left, insets.Top, insets.Right, insets.Bottom);
        _scene?.SetPadding(insets.Left, insets.Top, insets.Right, insets.Bottom);
        _mimicraft?.SetPadding(insets.Left, insets.Top, insets.Right, insets.Bottom);
        _files?.SetPadding(insets.Left, insets.Top, insets.Right, insets.Bottom);
    }

    /// <summary>Places one floating group against an edge, clear of the system bars.</summary>
    private void Float(ViewGroup root, View group, GravityFlags gravity)
    {
        int margin = Style.Dp(this, 8f);
        var layout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = gravity,
        };

        layout.SetMargins(margin, margin, margin, margin);
        root.AddView(group, layout);
        _floating.Add(group);
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
        ShowOnly(_palette);
    }

    private void ShowScene()
    {
        if (_surface is null || _scene is null)
        {
            return;
        }

        _scene.Populate(_surface.SceneState);
        ShowOnly(_scene);
    }

    private void ShowMimicraft()
    {
        if (_surface is null || _mimicraft is null || _exports is null)
        {
            return;
        }

        _mimicraft.Populate(
            _mimicraftOptions,
            _exports.Problems(_mimicraftOptions.Target),
            _surface.ProjectName);

        ShowOnly(_mimicraft);
    }

    /// <summary>
    /// Brings one page up and puts every other one away. Pages are full-screen and opaque, so two
    /// visible at once means the one underneath is simply unreachable.
    /// </summary>
    private void ShowOnly(View? page)
    {
        // Opening is not the same moment as refreshing. A page rebuilt because a choice on it
        // changed has to stay where it was, or every tap throws the controls out from under the
        // finger; a page being opened starts at its top.
        bool opening = page is { Visibility: not ViewStates.Visible };

        foreach (View other in _pages)
        {
            other.Visibility = ReferenceEquals(other, page) ? ViewStates.Visible : ViewStates.Gone;
        }

        if (opening && page is IPage scrollable)
        {
            scrollable.ResetScroll();
        }
    }

    /// <summary>A one-line text prompt. Renaming and Save as both need exactly this and no more.</summary>
    private void AskForName(string title, string suggestion, Action<string> accepted)
    {
        var input = new EditText(this) { Text = suggestion };
        input.SetSelection(suggestion.Length);

        new AlertDialog.Builder(this)
            .SetTitle(title)!
            .SetView(input)!
            .SetPositiveButton("OK", (_, _) => accepted(input.Text ?? string.Empty))!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ShowFiles(LevelStore store)
    {
        if (_surface is null || _files is null)
        {
            return;
        }

        _files.Populate(store.List(), _surface.ProjectName, _surface.HasUnsavedChanges);
        ShowOnly(_files);
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
        if (_actions?.OnPicked(requestCode, resultCode, data) != true)
        {
            _exports?.OnPicked(requestCode, resultCode, data);
        }
    }
#pragma warning restore CA1422

    private void Refresh()
    {
        if (_surface is not null && _chrome is not null)
        {
            _chrome.Refresh(
                _surface.ActiveTool,
                _surface.CanUndo,
                _surface.CanRedo,
                _surface.ActiveColor,
                _optionsOpen,
                _surface.ProjectName,
                _surface.HasUnsavedChanges);

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
        foreach (View page in _pages)
        {
            if (page.Visibility == ViewStates.Visible)
            {
                ShowOnly(null);
                return;
            }
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
