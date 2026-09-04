using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Mobile.Tools;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// Whatever the tool in hand can be told to do differently, shown only when asked for.
///
/// Closed by default, because the options are set once and then left alone for a long stretch of
/// modelling, and every row that is always there is a row the viewport does not get. The chevron at
/// the end of the tool bar opens it.
///
/// Numbers are steppers rather than sliders. A slider is a poor thing to aim a fingertip at, and
/// every number here — a brush radius, a colour match — is one you want an exact value of far more
/// often than a rough one.
/// </summary>
public sealed class OptionsBar : LinearLayout
{
    private readonly LinearLayout _controls;
    private readonly TextView _hint;

    public OptionsBar(Context context)
        : base(context)
    {
        Orientation = Orientation.Vertical;

        // The same floating panel the chrome groups use, so an opened option reads as one more of
        // them rather than as a bar that appeared.
        Background = Style.RoundedFill(context, Style.Panel, radiusDp: Style.PanelRadiusDp);

        int gap = Style.Dp(context, 6f);
        SetPadding(gap, gap, gap, gap);

        _controls = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _controls.SetGravity(GravityFlags.CenterVertical);
        AddView(_controls);

        // The one piece of prose left. A phone has no hover, so the sentence the desktop keeps in a
        // tooltip has nowhere else to go — and it is only on screen while the options are open.
        _hint = new TextView(context);
        _hint.SetTextColor(Style.TextDim);
        _hint.SetTextSize(ComplexUnitType.Sp, 10.5f);
        _hint.SetPadding(Style.Dp(context, 2f), Style.Dp(context, 4f), 0, 0);
        AddView(_hint);
    }

    public event Action<TransformMode>? TransformModeChosen;

    public event Action<TransformSpace>? TransformSpaceChosen;

    public event Action<ExtrudeSelectionMode>? ExtrudeSelectionModeChosen;

    public event Action<bool>? ExtrudeCreatesObjectChanged;

    /// <summary>Extrude by a number of whole steps, positive out and negative in.</summary>
    public event Action<int>? ExtrudeStepped;

    public event Action<PaintMode>? PaintModeChosen;

    public event Action<float>? BrushRadiusChanged;

    public event Action<int>? BucketThresholdChanged;

    public event Action<bool>? BucketWholeObjectChanged;

    public event Action<bool>? SamplerArmed;

    public void Refresh(ToolState state)
    {
        _controls.RemoveAllViews();

        switch (state.Tool)
        {
            case EditorTool.Transform:
                BuildTransform(state);
                break;

            case EditorTool.Extrude:
                BuildExtrude(state);
                break;

            case EditorTool.Paint:
                BuildPaint(state);
                break;

            case EditorTool.LoopCut:
                _hint.Text = state.HasCutPreview
                    ? "Tap the same place again to cut, or somewhere else to move the plane."
                    : "Tap a surface to place a cut plane. A second tap splits the object in two.";
                break;
        }
    }

    private void BuildTransform(ToolState state)
    {
        bool moving = state.TransformMode == TransformMode.Move;

        Choice(Resource.Drawable.ic_move, "Move", moving, () => TransformModeChosen?.Invoke(TransformMode.Move));
        Choice(Resource.Drawable.ic_rotate, "Rotate", !moving, () => TransformModeChosen?.Invoke(TransformMode.Rotate));

        Gap();

        // The edge hinge a rotation turns about is always in the object's own space, so the choice
        // has nothing to say while rotating.
        Choice(
            Resource.Drawable.ic_global, "Global space",
            state.TransformSpace == TransformSpace.Global,
            () => TransformSpaceChosen?.Invoke(TransformSpace.Global),
            moving);

        Choice(
            Resource.Drawable.ic_local, "Local space",
            state.TransformSpace == TransformSpace.Local,
            () => TransformSpaceChosen?.Invoke(TransformSpace.Local),
            moving);

        _hint.Text = moving
            ? "Drag an arrow to move the object. Movement snaps to whole voxels."
            : "Drag a ring to turn the object. Global and local mean the same thing here.";
    }

    private void BuildExtrude(ToolState state)
    {
        bool box = state.ExtrudeSelectionMode == ExtrudeSelectionMode.Box;

        Choice(Resource.Drawable.ic_box_select, "Drag out a rectangle", box,
            () => ExtrudeSelectionModeChosen?.Invoke(ExtrudeSelectionMode.Box));
        Choice(Resource.Drawable.ic_face_select, "Take the whole flat patch", !box,
            () => ExtrudeSelectionModeChosen?.Invoke(ExtrudeSelectionMode.Face));

        Gap();

        Choice(
            Resource.Drawable.ic_new_object, "Extrude into a new object",
            state.ExtrudeCreatesObject,
            () => ExtrudeCreatesObjectChanged?.Invoke(!state.ExtrudeCreatesObject));

        Gap();

        bool hasSelection = state.SelectedFaces > 0;
        Choice(Resource.Drawable.ic_remove, "Push in one voxel", false, () => ExtrudeStepped?.Invoke(-1), hasSelection);
        Label(hasSelection ? $"{state.SelectedFaces} faces" : "nothing");
        Choice(Resource.Drawable.ic_add, "Pull out one voxel", false, () => ExtrudeStepped?.Invoke(1), hasSelection);

        _hint.Text = hasSelection
            ? "Drag the arrow, or step one voxel at a time."
            : box
                ? "Drag across a surface to select a rectangle of it. Drag off the model to turn the view."
                : "Tap a surface to take the whole flat patch it belongs to.";
    }

    private void BuildPaint(ToolState state)
    {
        bool bucket = state.PaintMode == PaintMode.Bucket;

        Choice(Resource.Drawable.ic_brush, "Brush", !bucket, () => PaintModeChosen?.Invoke(PaintMode.Brush));
        Choice(Resource.Drawable.ic_bucket, "Bucket fill", bucket, () => PaintModeChosen?.Invoke(PaintMode.Bucket));

        Gap();

        Choice(
            Resource.Drawable.ic_eyedropper, "Take a colour from the model",
            state.SamplerArmed,
            () => SamplerArmed?.Invoke(!state.SamplerArmed));

        Gap();

        if (bucket)
        {
            Choice(
                Resource.Drawable.ic_pattern, "Recolour the whole object",
                state.BucketWholeObject,
                () => BucketWholeObjectChanged?.Invoke(!state.BucketWholeObject));

            Gap();

            // A whole-object fill does not spread, so how far it would have spread means nothing.
            bool spreads = !state.BucketWholeObject;
            Stepper(
                state.BucketThreshold == 0 ? "exact" : $"±{state.BucketThreshold}",
                "Colour match",
                () => BucketThresholdChanged?.Invoke(Math.Max(0, state.BucketThreshold - 8)),
                () => BucketThresholdChanged?.Invoke(Math.Min(128, state.BucketThreshold + 8)),
                spreads);

            _hint.Text = state.SamplerArmed
                ? "The next tap takes that face's colour instead of filling with it."
                : state.BucketWholeObject
                    ? "Recolours every voxel of the object, including faces nothing can see yet."
                    : "Fills the connected surface under the tap. Match widens what counts as the same colour.";
            return;
        }

        Stepper(
            state.BrushRadius < 0.5f ? "1 face" : $"{state.BrushRadius:0.0}",
            "Brush radius",
            () => BrushRadiusChanged?.Invoke(MathF.Max(0f, state.BrushRadius - 0.5f)),
            () => BrushRadiusChanged?.Invoke(MathF.Min(12f, state.BrushRadius + 0.5f)));

        _hint.Text = state.SamplerArmed
            ? "The next tap takes that face's colour instead of painting it."
            : "Drag across the model to paint. Drag off it to turn the view.";
    }

    private void Choice(
        int icon,
        string name,
        bool chosen,
        Action clicked,
        bool available = true)
    {
        var button = new IconButtonView(Context!, icon, name) { Chosen = chosen, Available = available };
        button.Click += (_, _) => clicked();
        Add(button);
    }

    private void Stepper(string value, string name, Action down, Action up, bool available = true)
    {
        Choice(Resource.Drawable.ic_remove, $"{name}: less", false, down, available);
        Label(value, available);
        Choice(Resource.Drawable.ic_add, $"{name}: more", false, up, available);
    }

    private void Label(string text, bool available = true)
    {
        var view = new TextView(Context!) { Text = text, Gravity = GravityFlags.Center };
        view.SetTextColor(available ? Style.Text : Style.TextDim);
        view.SetTextSize(ComplexUnitType.Sp, 11.5f);
        view.SetMinimumWidth(Style.Dp(Context!, 62f));
        Add(view);
    }

    private void Add(View view)
    {
        var layout = new LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent);

        layout.SetMargins(Style.Dp(Context!, 3f), 0, Style.Dp(Context!, 3f), 0);
        _controls.AddView(view, layout);
    }

    private void Gap() => _controls.AddView(
        new Space(Context!),
        new LayoutParams(Style.Dp(Context!, 14f), 0));
}
