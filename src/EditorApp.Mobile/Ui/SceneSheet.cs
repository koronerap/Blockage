using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Mobile.Tools;
using EditorApp.Rendering;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// Everything about the level and the way it is being looked at, on one page.
///
/// On the desktop this is four docked panels — the level, the objects, the light and the view menu —
/// which a phone has no room to keep open. They are the same controls, gathered where they can have
/// the width they need and cost nothing while they are closed.
/// </summary>
public sealed class SceneSheet : FrameLayout, IPage
{
    private readonly LinearLayout _body;
    private readonly ScrollView _scroll;

    public SceneSheet(Context context)
        : base(context)
    {
        SetBackgroundColor(Style.PanelSolid);

        var page = new LinearLayout(context) { Orientation = Orientation.Vertical };
        int pad = Style.Dp(context, 12f);
        page.SetPadding(pad, pad, pad, 0);

        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(context) { Text = "Level" };
        title.SetTextColor(Style.Text);
        title.SetTextSize(ComplexUnitType.Sp, 14f);
        header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var close = new IconButtonView(context, Resource.Drawable.ic_close, "Close");
        close.Click += (_, _) => Dismissed?.Invoke();
        header.AddView(close);
        page.AddView(header);

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

    public event Action<bool>? GridChanged;

    public event Action<ShadingMode>? ShadingChanged;

    /// <summary>Azimuth and elevation in degrees, intensity and ambient as fractions.</summary>
    public event Action<float, float>? LightAngleChanged;

    public event Action<float, float>? LightStrengthChanged;

    public event Action? LightReset;

    public event Action<float>? VoxelSizeChanged;

    public event Action<RotateDirection>? RotateRequested;

    public event Action? FrameRequested;

    public event Action<int>? ObjectChosen;

    public event Action<int>? ObjectVisibilityToggled;

    public event Action<int>? ObjectDeleteRequested;

    /// <summary>
    /// Renaming matters more here than it looks. A Mimicraft character writes each object's name as
    /// the rig slot it fills, so a level whose objects are still called "Object 1" exports a file
    /// that is valid and fits nothing.
    /// </summary>
    public event Action<ObjectState>? ObjectRenameRequested;

    public void Populate(SceneState state)
    {
        Context context = Context!;
        _body.RemoveAllViews();

        Heading("View");
        Row(
            Toggle(Resource.Drawable.ic_grid, "Ground grid", state.GridVisible, () => GridChanged?.Invoke(!state.GridVisible)),
            Space(),
            Toggle(Resource.Drawable.ic_lit, "Lit", state.Lighting.IsLit, () => ShadingChanged?.Invoke(ShadingMode.Lit)),
            Toggle(Resource.Drawable.ic_unlit, "Unlit", !state.Lighting.IsLit, () => ShadingChanged?.Invoke(ShadingMode.Unlit)),
            Space(),
            Toggle(Resource.Drawable.ic_frame, "Frame the level", false, () => FrameRequested?.Invoke()));

        if (state.Lighting.IsLit)
        {
            Heading("Light");
            Row(
                Stepper(
                    $"{state.Lighting.Azimuth:0}°",
                    "Direction",
                    () => LightAngleChanged?.Invoke(Wrap(state.Lighting.Azimuth - 15f), state.Lighting.Elevation),
                    () => LightAngleChanged?.Invoke(Wrap(state.Lighting.Azimuth + 15f), state.Lighting.Elevation)),
                Space(),
                Stepper(
                    $"{state.Lighting.Elevation:0}° up",
                    "Height",
                    () => LightAngleChanged?.Invoke(state.Lighting.Azimuth, Math.Clamp(state.Lighting.Elevation - 5f, 0f, 90f)),
                    () => LightAngleChanged?.Invoke(state.Lighting.Azimuth, Math.Clamp(state.Lighting.Elevation + 5f, 0f, 90f))));

            Row(
                Stepper(
                    $"key {state.Lighting.Intensity:0.00}",
                    "Light strength",
                    () => LightStrengthChanged?.Invoke(MathF.Max(0f, state.Lighting.Intensity - 0.05f), state.Lighting.Ambient),
                    () => LightStrengthChanged?.Invoke(MathF.Min(1.5f, state.Lighting.Intensity + 0.05f), state.Lighting.Ambient)),
                Space(),
                Stepper(
                    $"fill {state.Lighting.Ambient:0.00}",
                    "Ambient floor",
                    () => LightStrengthChanged?.Invoke(state.Lighting.Intensity, MathF.Max(0f, state.Lighting.Ambient - 0.05f)),
                    () => LightStrengthChanged?.Invoke(state.Lighting.Intensity, MathF.Min(1f, state.Lighting.Ambient + 0.05f))),
                Space(),
                Toggle(Resource.Drawable.ic_rotate, "Reset the light", false, () => LightReset?.Invoke()));

            Note("The light is a way of looking at the level, not part of it. It is never saved and "
                + "never reaches an export.");
        }

        Heading("Voxel size of the focused object");
        Row(Stepper(
            $"{state.VoxelSize:0.###} units",
            "Voxel size",
            () => VoxelSizeChanged?.Invoke(state.VoxelSize / 2f),
            () => VoxelSizeChanged?.Invoke(state.VoxelSize * 2f)));

        Note(state.Extent is { } extent
            ? $"The level is {extent.X:0.##} × {extent.Y:0.##} × {extent.Z:0.##} units in the game."
            : "The level is empty.");

        Heading("Turn the focused object");
        Row(
            Toggle(Resource.Drawable.ic_rotate_left, "Turn left", false, () => RotateRequested?.Invoke(RotateDirection.Left)),
            Toggle(Resource.Drawable.ic_rotate_right, "Turn right", false, () => RotateRequested?.Invoke(RotateDirection.Right)),
            Space(),
            Toggle(Resource.Drawable.ic_tip_up, "Tip up", false, () => RotateRequested?.Invoke(RotateDirection.Up)),
            Toggle(Resource.Drawable.ic_tip_down, "Tip down", false, () => RotateRequested?.Invoke(RotateDirection.Down)));

        Note("Turns the voxels themselves, a quarter at a time. Nothing to do with the axes an "
            + "export is written in.");

        Heading($"{state.Objects.Count} object(s), {state.SolidCount:N0} voxels");

        foreach (ObjectState o in state.Objects)
        {
            _body.AddView(ObjectRow(context, o, state.Objects.Count));
        }
    }

    private static float Wrap(float degrees) => (degrees + 360f) % 360f;

    private View ObjectRow(Context context, ObjectState o, int total)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(0, Style.Dp(context, 3f), 0, Style.Dp(context, 3f));

        var eye = new IconButtonView(context, o.Visible ? Resource.Drawable.ic_visible : Resource.Drawable.ic_hidden, "Show or hide")
        {
            Chosen = o.Visible,
        };
        eye.Click += (_, _) => ObjectVisibilityToggled?.Invoke(o.Id);
        row.AddView(eye);

        var name = new TextView(context)
        {
            Text = $"{o.Name}   ·   {o.Voxels:N0} voxels",
            Background = Style.RoundedFill(context, o.Focused ? Style.Accent : Style.ButtonIdle),
            Gravity = GravityFlags.CenterVertical,
        };

        name.SetTextColor(o.Focused ? Style.PanelSolid : Style.Text);
        name.SetTextSize(ComplexUnitType.Sp, 13f);
        name.SetPadding(Style.Dp(context, 12f), 0, Style.Dp(context, 12f), 0);
        name.SetMinimumHeight(Style.Dp(context, Style.TouchTargetDp));
        name.Click += (_, _) => ObjectChosen?.Invoke(o.Id);

        var layout = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        layout.SetMargins(Style.Dp(context, 6f), 0, Style.Dp(context, 6f), 0);
        row.AddView(name, layout);

        var rename = new IconButtonView(context, Resource.Drawable.ic_rename, "Rename this object");
        rename.Click += (_, _) => ObjectRenameRequested?.Invoke(o);
        row.AddView(rename);

        // The last object cannot go: a scene with nothing in it has no grid to edit and no focus to
        // hold, and the session would have to invent one back immediately.
        var remove = new IconButtonView(context, Resource.Drawable.ic_delete, "Delete this object")
        {
            Available = total > 1,
        };

        remove.Click += (_, _) => ObjectDeleteRequested?.Invoke(o.Id);
        row.AddView(remove);

        return row;
    }

    // ---- Small pieces the page is built from ---------------------------------------------------

    private void Heading(string text)
    {
        var view = new TextView(Context!) { Text = text };
        view.SetTextColor(Style.Text);
        view.SetTextSize(ComplexUnitType.Sp, 12f);
        view.SetPadding(0, Style.Dp(Context!, 14f), 0, Style.Dp(Context!, 6f));
        _body.AddView(view);
    }

    private void Note(string text)
    {
        var view = new TextView(Context!) { Text = text };
        view.SetTextColor(Style.TextDim);
        view.SetTextSize(ComplexUnitType.Sp, 10.5f);
        view.SetPadding(0, Style.Dp(Context!, 4f), 0, 0);
        _body.AddView(view);
    }

    private void Row(params View[] children)
    {
        var row = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        // Rows of forty-dip buttons touch each other without this, and two steppers stacked up read
        // as one tall control rather than two.
        row.SetPadding(0, Style.Dp(Context!, 3f), 0, Style.Dp(Context!, 3f));

        foreach (View child in children)
        {
            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent);

            layout.SetMargins(Style.Dp(Context!, 3f), 0, Style.Dp(Context!, 3f), 0);
            row.AddView(child, layout);
        }

        _body.AddView(row);
    }

    private View Toggle(int icon, string name, bool chosen, Action clicked)
    {
        var button = new IconButtonView(Context!, icon, name) { Chosen = chosen };
        button.Click += (_, _) => clicked();
        return button;
    }

    private View Stepper(string value, string name, Action down, Action up)
    {
        var group = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        group.SetGravity(GravityFlags.CenterVertical);

        group.AddView(Toggle(Resource.Drawable.ic_remove, $"{name}: less", false, down));

        var label = new TextView(Context!) { Text = value, Gravity = GravityFlags.Center };
        label.SetTextColor(Style.Text);
        label.SetTextSize(ComplexUnitType.Sp, 11.5f);
        label.SetMinimumWidth(Style.Dp(Context!, 84f));
        group.AddView(label);

        group.AddView(Toggle(Resource.Drawable.ic_add, $"{name}: more", false, up));
        return group;
    }

    private View Space() => new Space(Context!)
    {
        LayoutParameters = new LinearLayout.LayoutParams(Style.Dp(Context!, 12f), 0),
    };

    /// <summary>A page opens at its top, however far down it was left last time.</summary>
    public void ResetScroll() => _scroll.ScrollTo(0, 0);
}
