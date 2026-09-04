using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using EditorApp.Core.Editing;
using EditorApp.Mobile.Tools;

namespace EditorApp.Mobile.Ui;

/// <summary>
/// The row above the tool bar: whatever the tool in hand can be told to do differently.
///
/// The desktop puts these in a strip under the menu with tooltips explaining each one. A phone has
/// no tooltip and no hover, so the same options are shown as buttons that say what they are, with a
/// line of plain text where the desktop would have waited for the cursor to rest.
///
/// Numbers are steppers rather than sliders. A slider is a poor thing to aim a fingertip at, and
/// every number here — a brush radius, a colour match — is one the user wants an exact value of far
/// more often than a rough one.
/// </summary>
public sealed class OptionsBar : LinearLayout
{
    private readonly LinearLayout _controls;
    private readonly TextView _hint;

    public OptionsBar(Context context)
        : base(context)
    {
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Style.Panel);

        int gap = Style.Dp(context, 6f);
        SetPadding(gap, gap, gap, gap);

        _controls = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _controls.SetGravity(GravityFlags.CenterVertical);
        AddView(_controls);

        _hint = new TextView(context);
        _hint.SetTextColor(Style.TextDim);
        _hint.SetTextSize(ComplexUnitType.Sp, 11f);
        _hint.SetPadding(Style.Dp(context, 2f), Style.Dp(context, 4f), 0, 0);
        AddView(_hint);
    }

    public event Action<TransformMode>? TransformModeChosen;

    public event Action<TransformSpace>? TransformSpaceChosen;

    public event Action<bool>? ExtrudeCreatesObjectChanged;

    /// <summary>Extrude by a number of whole steps, positive out and negative in.</summary>
    public event Action<int>? ExtrudeStepped;

    public event Action<PaintMode>? PaintModeChosen;

    public event Action<float>? BrushRadiusChanged;

    public event Action<int>? BucketThresholdChanged;

    public event Action<bool>? BucketWholeObjectChanged;

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
        Segmented(
            [("Move", state.TransformMode == TransformMode.Move),
             ("Rotate", state.TransformMode == TransformMode.Rotate)],
            index => TransformModeChosen?.Invoke((TransformMode)index));

        Gap();

        bool moving = state.TransformMode == TransformMode.Move;
        Segmented(
            [("Global", state.TransformSpace == TransformSpace.Global),
             ("Local", state.TransformSpace == TransformSpace.Local)],
            index => TransformSpaceChosen?.Invoke((TransformSpace)index),
            enabled: moving);

        // The edge hinge a rotation turns about is always in the object's own space, so the choice
        // has nothing to say while rotating.
        _hint.Text = moving
            ? "Drag an arrow to move the object. Movement snaps to whole voxels."
            : "Drag a ring to turn the object. Global and local mean the same thing here.";
    }

    private void BuildExtrude(ToolState state)
    {
        Toggle("New object", state.ExtrudeCreatesObject,
            value => ExtrudeCreatesObjectChanged?.Invoke(value));

        Gap();

        bool hasSelection = state.SelectedFaces > 0;
        Button("Pull out", () => ExtrudeStepped?.Invoke(1), hasSelection);
        Button("Push in", () => ExtrudeStepped?.Invoke(-1), hasSelection);

        _hint.Text = hasSelection
            ? $"{state.SelectedFaces} face(s) selected — drag the arrow, or step one voxel at a time."
            : "Tap a surface to select the whole flat patch it belongs to.";
    }

    private void BuildPaint(ToolState state)
    {
        bool bucket = state.PaintMode == PaintMode.Bucket;

        Segmented(
            [("Brush", !bucket), ("Bucket", bucket)],
            index => PaintModeChosen?.Invoke(index == 0 ? PaintMode.Brush : PaintMode.Bucket));

        Gap();

        if (bucket)
        {
            Toggle("Whole object", state.BucketWholeObject,
                value => BucketWholeObjectChanged?.Invoke(value));

            Gap();

            // A whole-object fill does not spread, so how far it would have spread means nothing.
            Stepper(
                state.BucketThreshold == 0 ? "Match: exact" : $"Match: {state.BucketThreshold}",
                () => BucketThresholdChanged?.Invoke(Math.Max(0, state.BucketThreshold - 8)),
                () => BucketThresholdChanged?.Invoke(Math.Min(128, state.BucketThreshold + 8)),
                enabled: !state.BucketWholeObject);

            _hint.Text = state.BucketWholeObject
                ? "Recolours every voxel of the object, including faces nothing can see yet."
                : "Fills the connected surface under the tap. Match widens what counts as the same colour.";
            return;
        }

        Stepper(
            state.BrushRadius < 0.5f ? "Radius: one face" : $"Radius: {state.BrushRadius:0.0}",
            () => BrushRadiusChanged?.Invoke(MathF.Max(0f, state.BrushRadius - 0.5f)),
            () => BrushRadiusChanged?.Invoke(MathF.Min(12f, state.BrushRadius + 0.5f)));

        _hint.Text = "Tap a face to paint it. Dragging turns the model, so paint one tap at a time.";
    }

    private void Segmented((string Label, bool Selected)[] items, Action<int> chosen, bool enabled = true)
    {
        for (int i = 0; i < items.Length; i++)
        {
            int index = i;
            TextView button = Style.Button(Context!, items[i].Label);
            Style.SetSelected(Context!, button, items[i].Selected);
            Style.SetEnabledLook(button, enabled);
            button.Click += (_, _) => chosen(index);
            Add(button);
        }
    }

    private void Toggle(string label, bool on, Action<bool> changed)
    {
        TextView button = Style.Button(Context!, label);
        Style.SetSelected(Context!, button, on);
        button.Click += (_, _) => changed(!on);
        Add(button);
    }

    private void Button(string label, Action clicked, bool enabled)
    {
        TextView button = Style.Button(Context!, label);
        Style.SetEnabledLook(button, enabled);
        button.Click += (_, _) =>
        {
            if (enabled)
            {
                clicked();
            }
        };

        Add(button);
    }

    private void Stepper(string label, Action down, Action up, bool enabled = true)
    {
        TextView less = Style.Button(Context!, "−");
        Style.SetEnabledLook(less, enabled);
        less.Click += (_, _) =>
        {
            if (enabled)
            {
                down();
            }
        };

        Add(less);

        var value = new TextView(Context!) { Text = label, Gravity = GravityFlags.Center };
        value.SetTextColor(enabled ? Style.Text : Style.TextDim);
        value.SetTextSize(ComplexUnitType.Sp, 12f);
        value.SetPadding(Style.Dp(Context!, 10f), 0, Style.Dp(Context!, 10f), 0);
        value.SetMinimumWidth(Style.Dp(Context!, 110f));
        Add(value);

        TextView more = Style.Button(Context!, "+");
        Style.SetEnabledLook(more, enabled);
        more.Click += (_, _) =>
        {
            if (enabled)
            {
                up();
            }
        };

        Add(more);
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
