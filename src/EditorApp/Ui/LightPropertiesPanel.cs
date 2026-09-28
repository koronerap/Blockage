using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The picked light's settings, under the outliner where an object's voxel size would be.
///
/// Each gesture is one undo step — a slider dragged, a colour picked, a value typed — however many
/// frames it changed the light across. The light changes live, so its effect on the model is seen
/// while dragging; what reaches history is the setting before and the setting after.
/// </summary>
public static class LightPropertiesPanel
{
    private static SceneLight? _editing;
    private static LightState _before;

    /// <summary>Puts an unfinished edit into history — when the light is no longer the one shown, for one.</summary>
    public static void Flush(EditorSession session)
    {
        if (_editing is { } light)
        {
            session.PushLightEdit(light, _before, $"Edit {light.Name}");
        }

        _editing = null;
    }

    public static void DrawContent(EditorSession session, SceneLight light)
    {
        if (_editing is not null && _editing != light)
        {
            Flush(session);
        }

        LightState state = light.State;
        LightState edited = state;

        int kind = (int)light.Kind;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##kind", ref kind, "Sun\0Point\0Spot\0"))
        {
            edited = edited with { Kind = (LightKind)kind };
        }

        Vector3 colour = light.Colour;
        if (ImGui.ColorEdit3("##colour", ref colour, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            edited = edited with { Colour = colour };
        }

        ImGui.SameLine();
        float intensity = light.Intensity;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.DragFloat("##intensity", ref intensity, 0.01f, 0f, SceneLight.MaxIntensity, "Intensity  %.2f"))
        {
            edited = edited with { Intensity = intensity };
        }

        if (light.Kind != LightKind.Directional)
        {
            float range = light.Range;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.DragFloat("##range", ref range, 0.1f, SceneLight.MinRange, SceneLight.MaxRange, "Reach  %.1f"))
            {
                edited = edited with { Range = range };
            }

            Tooltip("How far the light gets, in world units. It fades out smoothly to nothing there.");
        }

        if (light.Kind == LightKind.Spot)
        {
            float angle = light.SpotAngle;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##angle", ref angle, SceneLight.MinSpotAngle, SceneLight.MaxSpotAngle, "Cone  %.0f deg"))
            {
                edited = edited with { SpotAngle = angle };
            }

            float blend = light.SpotBlend;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##blend", ref blend, 0f, 1f, "Soft edge  %.2f"))
            {
                edited = edited with { SpotBlend = blend };
            }
        }

        // A point light shines every way, so there is nothing to aim.
        if (light.Kind != LightKind.Point)
        {
            (float azimuth, float elevation) = SceneLight.AnglesOf(light.Direction);
            bool aimed = false;

            ImGui.SetNextItemWidth(-1f);
            aimed |= ImGui.SliderFloat("##azimuth", ref azimuth, 0f, 360f, "From  %.0f deg");
            Tooltip("The compass bearing the light comes from.");

            ImGui.SetNextItemWidth(-1f);
            aimed |= ImGui.SliderFloat("##elevation", ref elevation, -90f, 90f, "Height  %.0f deg");
            Tooltip("How high it comes from: 90 is straight overhead.");

            if (aimed)
            {
                Quaternion rotation = SceneLight.Aiming(SceneLight.ShiningFrom(azimuth, elevation));
                edited = edited with { Transform = edited.Transform with { Rotation = rotation } };
            }

            DrawCompass(-light.Direction, light.Visible);
        }

        Vector3 position = light.Position;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.DragFloat3("##position", ref position, 0.1f, 0f, 0f, "%.2f"))
        {
            edited = edited with { Transform = edited.Transform with { Position = position } };
        }

        Tooltip(light.Kind == LightKind.Directional
            ? "Where its icon stands. A sun lights everything alike wherever it is."
            : "Where the light is, in world units.");

        if (edited != state)
        {
            if (_editing is null)
            {
                _editing = light;
                _before = state;
            }

            light.Apply(edited);
        }

        // The gesture is over once nothing is held — a drag let go, a colour popup's pick made.
        if (_editing is not null && !ImGui.IsAnyItemActive())
        {
            Flush(session);
        }

        ImGui.TextDisabled("Lights the viewport only - never exported.");
    }

    /// <summary>
    /// A compass showing where the light comes from, seen from above. Two angles are hard to hold in
    /// your head at once; a dot on a circle is not.
    /// </summary>
    private static void DrawCompass(Vector3 towardsLight, bool on)
    {
        float size = ImGui.GetFrameHeight() * 2.6f;
        Vector2 topLeft = ImGui.GetCursorScreenPos();

        // Reserved through a dummy so the layout accounts for something drawn by hand.
        ImGui.Dummy(new Vector2(size, size));

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 centre = topLeft + new Vector2(size * 0.5f);
        float radius = size * 0.42f;

        uint mark = ImGui.GetColorU32(on ? Theme.Highlight : Theme.TextDisabled);

        drawList.AddCircleFilled(centre, radius, ImGui.GetColorU32(Theme.Sunken));
        drawList.AddCircle(centre, radius, ImGui.GetColorU32(ImGuiCol.Border));

        // The horizontal part of a unit direction is already shortened by the height, so using it
        // as-is pulls the dot towards the middle as the light rises — dead centre for a light
        // straight overhead, which is what being overhead looks like from above.
        Vector2 dot = centre + (new Vector2(towardsLight.X, -towardsLight.Z) * radius);

        drawList.AddLine(centre, dot, mark);
        drawList.AddCircleFilled(dot, 3.5f, mark);
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}

/// <summary>
/// Adding lights, from the Add menu and the Lighting section alike. Where a new light goes is
/// decided by what is being worked on: a lamp or a spot above the focused object, shining down on it;
/// a sun from the default bearing, far enough off to be out of the way.
/// </summary>
public static class LightMenu
{
    public static readonly (LightKind Kind, string Label)[] Kinds =
    [
        (LightKind.Directional, "Sun"),
        (LightKind.Point, "Point light"),
        (LightKind.Spot, "Spot light"),
    ];

    public static void DrawItems(EditorSession session)
    {
        foreach ((LightKind kind, string label) in Kinds)
        {
            if (ImGui.MenuItem(label))
            {
                Add(session, kind);
            }
        }
    }

    /// <summary>Adds a light, picks it, and hands it to the Transform tool to be put where it goes.</summary>
    public static SceneLight Add(EditorSession session, LightKind kind)
    {
        VoxelScene scene = session.Scene;

        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;
        bool found = (scene.Focus is { } focus && focus.TryGetWorldBounds(out min, out max))
            || scene.TryGetWorldBounds(out min, out max);

        Vector3 centre = found ? (min + max) * 0.5f : Vector3.Zero;
        float size = found ? MathF.Max((max - min).Length(), 1f) : 8f;

        SceneLight light;
        switch (kind)
        {
            case LightKind.Directional:
                Vector3 shining = SceneLight.ShiningFrom(SceneLight.SunAzimuth, SceneLight.SunElevation);
                light = session.AddLight(kind, centre - (shining * MathF.Max(size, 8f)), shining);
                break;

            case LightKind.Point:
                light = session.AddLight(kind, new Vector3(centre.X, max.Y + MathF.Max(size * 0.25f, 2f), centre.Z), -Vector3.UnitY);
                light.Range = MathF.Max(size * 1.5f, 10f);
                break;

            default:
                light = session.AddLight(kind, new Vector3(centre.X, max.Y + MathF.Max(size * 0.5f, 4f), centre.Z), -Vector3.UnitY);
                light.Range = MathF.Max(size * 2f, 12f);
                break;
        }

        session.ActiveTool = EditorTool.Transform;
        return light;
    }

    /// <summary>A copy of a light two units to the right on screen, picked.</summary>
    public static void Duplicate(EditorSession session, FlyCamera camera, SceneLight light)
    {
        Vector3 right = camera.Right;
        Vector3 offset = MathF.Abs(right.X) >= MathF.Abs(right.Z)
            ? new Vector3(MathF.Sign(right.X) * 2f, 0f, 0f)
            : new Vector3(0f, 0f, MathF.Sign(right.Z) * 2f);

        session.DuplicateLight(light.Id, offset);
    }
}
