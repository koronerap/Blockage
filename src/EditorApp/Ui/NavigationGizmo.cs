using System.Numerics;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Blender's navigation gizmo, in the viewport's top-right corner: the world axes as they lie from
/// here, with a ball on each end. Click a ball to look along that axis from its side; click the one
/// already facing you to see the other side. Drag anywhere on it to orbit.
///
/// Under it, a column of buttons for what a mouse without a middle button — or a touchpad — cannot
/// otherwise reach: the projection, zoom, pan, and framing the level.
/// </summary>
public sealed class NavigationGizmo
{
    public const float Radius = 44f;

    public const float BallRadius = 8.5f;

    /// <summary>How far the gizmo sits in from the viewport's corner.</summary>
    private const float Margin = 12f;

    private const float ButtonGap = 8f;

    /// <summary>Pixels of cursor travel before a press on the disc stops being a click and becomes an orbit.</summary>
    private const float DragThreshold = 3f;

    /// <summary>Pixels of vertical drag on the zoom button that make one wheel step.</summary>
    public const float ZoomDragPixelsPerStep = 20f;

    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoNavFocus
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>
    /// The six ends of the three axes, and which view each one stands for: the ball on +X is the
    /// view from +X, which is the right-hand view.
    /// </summary>
    private static readonly (Vector3 Axis, AlignedView From, string Label, Vector4 Colour, bool Positive)[] Ends =
    [
        (Vector3.UnitX, AlignedView.Right, "X", Theme.AxisX, true),
        (Vector3.UnitY, AlignedView.Top, "Y", Theme.AxisY, true),
        (Vector3.UnitZ, AlignedView.Front, "Z", Theme.AxisZ, true),
        (-Vector3.UnitX, AlignedView.Left, "-X", Theme.AxisX, false),
        (-Vector3.UnitY, AlignedView.Bottom, "-Y", Theme.AxisY, false),
        (-Vector3.UnitZ, AlignedView.Back, "-Z", Theme.AxisZ, false),
    ];

    /// <summary>The ball the press started on, or -1: a click only counts if it ends where it began.</summary>
    private int _pressed = -1;

    private bool _orbiting;

    /// <summary>Where the disc is centred for a given viewport.</summary>
    public static Vector2 Centre(ViewportRect viewport) => new(
        viewport.Position.X + viewport.Size.X - Margin - Radius,
        viewport.Position.Y + Margin + Radius);

    /// <summary>The top of the button column under the disc.</summary>
    public static Vector2 ButtonColumnTop(ViewportRect viewport) =>
        Centre(viewport) + new Vector2(0f, Radius + ButtonGap);

    /// <summary>
    /// Where the ball on the end of an axis sits: the axis carried into the camera's own frame, with
    /// the depth dropped. Drawing and hit-testing both use this, so they cannot disagree.
    /// </summary>
    public static Vector2 BallPosition(FlyCamera camera, Vector3 axis, Vector2 centre)
    {
        var direction = new Vector2(Vector3.Dot(axis, camera.Right), -Vector3.Dot(axis, camera.Up));
        return centre + (direction * (Radius - BallRadius));
    }

    /// <summary>
    /// The view the ball under a point stands for, or null. Where two overlap — looking straight
    /// along an axis puts both its ends in the middle — the one nearer the viewer wins, as it is the
    /// one drawn on top.
    /// </summary>
    public static AlignedView? ViewAt(FlyCamera camera, Vector2 centre, Vector2 point)
    {
        int index = BallAt(camera, centre, point);
        return index < 0 ? null : Ends[index].From;
    }

    /// <summary>
    /// Looks from the chosen side — or, if the view is already looking from there, from the other
    /// one. The ball facing you is the way round to the back, the way Blender's gizmo does it.
    /// </summary>
    public static void AlignTo(FlyCamera camera, AlignedView view) =>
        camera.Align(camera.CurrentAlignedView() == view ? FlyCamera.Opposite(view) : view);

    public void Draw(FlyCamera camera, ViewportRect viewport, Action frameAll)
    {
        Vector2 centre = Centre(viewport);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 4f));

        DrawDisc(camera, centre);
        DrawButtons(camera, viewport, frameAll);

        ImGui.PopStyleVar(2);
    }

    private void DrawDisc(FlyCamera camera, Vector2 centre)
    {
        ImGui.SetNextWindowPos(centre - new Vector2(Radius));
        ImGui.SetNextWindowSize(new Vector2(Radius * 2f));
        ImGui.Begin("##navigation", Flags);

        ImGui.InvisibleButton("##orbit", new Vector2(Radius * 2f));

        ImGuiIOPtr io = ImGui.GetIO();
        bool active = ImGui.IsItemActive();
        bool overDisc = ImGui.IsItemHovered() && Vector2.Distance(io.MousePos, centre) <= Radius;
        int hovered = overDisc || active ? BallAt(camera, centre, io.MousePos) : -1;

        if (ImGui.IsItemActivated())
        {
            _pressed = hovered;
            _orbiting = false;
        }

        if (active)
        {
            if (!_orbiting && ImGui.IsMouseDragging(ImGuiMouseButton.Left, DragThreshold))
            {
                _orbiting = true;
            }

            if (_orbiting)
            {
                camera.Orbit(io.MouseDelta);
            }
        }

        if (ImGui.IsItemDeactivated())
        {
            if (!_orbiting && _pressed >= 0 && hovered == _pressed)
            {
                AlignTo(camera, Ends[_pressed].From);
            }

            _pressed = -1;
            _orbiting = false;
        }

        if (ImGui.IsItemHovered() && !active)
        {
            ImGui.SetTooltip(hovered >= 0
                ? $"View from {Ends[hovered].Label}  ({Ends[hovered].From})"
                : "Drag to orbit, click an axis to look along it");
        }

        Paint(camera, centre, highlight: overDisc || active, hovered);

        ImGui.End();
    }

    private static void Paint(FlyCamera camera, Vector2 centre, bool highlight, int hovered)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        if (highlight)
        {
            drawList.AddCircleFilled(centre, Radius, Colour(Theme.SurfaceRaised with { W = 0.45f }), 48);
        }

        // Far ends first, so the nearer end of each axis is drawn over the further.
        Span<int> order = [0, 1, 2, 3, 4, 5];
        Vector3 forward = camera.Forward;
        order.Sort((a, b) => Vector3.Dot(Ends[b].Axis, forward).CompareTo(Vector3.Dot(Ends[a].Axis, forward)));

        foreach (int i in order)
        {
            (Vector3 axis, _, string label, Vector4 colour, bool positive) = Ends[i];
            Vector2 at = BallPosition(camera, axis, centre);

            if (positive)
            {
                drawList.AddLine(centre, at, Colour(colour), 2f);
                drawList.AddCircleFilled(at, BallRadius, Colour(colour));
            }
            else
            {
                // The negative ends are rings, not solid: they say where the axis would come out the
                // other side without competing with the three that name it.
                drawList.AddCircleFilled(at, BallRadius, Colour(colour with { W = 0.28f }));
                drawList.AddCircle(at, BallRadius, Colour(colour with { W = 0.75f }), 0, 1.2f);
            }

            if (i == hovered)
            {
                drawList.AddCircle(at, BallRadius + 1.5f, Colour(Theme.Text), 0, 1.5f);
            }

            // A negative end only names itself when pointed at, as in Blender: six letters at once
            // are harder to read than three.
            if (positive || i == hovered)
            {
                Vector2 size = ImGui.CalcTextSize(label);
                drawList.AddText(at - (size * 0.5f), Colour(positive ? Theme.Background : Theme.Text), label);
            }
        }
    }

    /// <summary>
    /// Their own window, sized to the buttons: a window takes the mouse wherever its rectangle is, and
    /// one stretched to the disc's width would leave a strip of viewport either side that ignored clicks.
    /// </summary>
    private static void DrawButtons(FlyCamera camera, ViewportRect viewport, Action frameAll)
    {
        float button = ImGui.GetFrameHeight();
        ImGuiIOPtr io = ImGui.GetIO();

        ImGui.SetNextWindowPos(ButtonColumnTop(viewport) - new Vector2(button * 0.5f, 0f));
        ImGui.Begin("##navigation-buttons", Flags | ImGuiWindowFlags.AlwaysAutoResize);

        ImGui.PushStyleColor(ImGuiCol.Button, Theme.SurfaceRaised with { W = 0.82f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.ControlHovered with { W = 0.92f });

        if (IconButton.Toggle(
                "projection",
                (Icons.Perspective, "Perspective"),
                (Icons.Orthographic, "Orthographic"),
                camera.Orthographic,
                Shortcut.Of(EditorAction.ToggleOrthographic),
                button))
        {
            camera.Orthographic = !camera.Orthographic;
        }

        // Zoom and pan act while held rather than on a click: the button is a handle to drag.
        IconButton.Draw("zoom", Icons.Zoom, active: false, "Zoom  -  drag up and down\nWheel, or Ctrl + middle drag", button);
        if (ImGui.IsItemActive())
        {
            camera.Zoom(-io.MouseDelta.Y / ZoomDragPixelsPerStep);
        }

        IconButton.Draw("pan", Icons.Pan, active: false, "Pan  -  drag\nShift + middle drag", button);
        if (ImGui.IsItemActive())
        {
            camera.Pan(io.MouseDelta, camera.PivotDistance, viewport.Size);
        }

        if (IconButton.Draw("frame", Icons.FrameAll, active: false, $"Frame the whole level{Shortcut.Hint(EditorAction.FrameLevel)}", button))
        {
            frameAll();
        }

        ImGui.PopStyleColor(2);
        ImGui.End();
    }

    private static int BallAt(FlyCamera camera, Vector2 centre, Vector2 point)
    {
        int found = -1;
        float nearest = float.MaxValue;
        Vector3 forward = camera.Forward;

        for (int i = 0; i < Ends.Length; i++)
        {
            if (Vector2.Distance(BallPosition(camera, Ends[i].Axis, centre), point) > BallRadius + 1f)
            {
                continue;
            }

            // Smaller is nearer: an end pointing back at the viewer runs against the view direction.
            float depth = Vector3.Dot(Ends[i].Axis, forward);
            if (depth < nearest)
            {
                nearest = depth;
                found = i;
            }
        }

        return found;
    }

    private static uint Colour(Vector4 value) => ImGui.ColorConvertFloat4ToU32(value);
}
