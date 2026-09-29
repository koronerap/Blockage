using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;


/// <summary>
/// A picked light's settings, in the Object tab where an object's would be. Laid out like the rest of
/// Properties: the kind of light as a row of buttons, what it gives off, the shape of a spot, where it
/// points, and where it is.
///
/// Each gesture is one undo step — a slider dragged, a colour picked, a value typed — however many
/// frames it changed the light across. The light changes live, so its effect on the model is seen
/// while dragging; what reaches history is the setting before and the setting after.
/// </summary>
public static class LightPropertiesPanel
{
    private const string ColourPopup = "##light-colour";

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

        ObjectPropertiesPanel.DrawName(light.Id, light.Name, Icons.For(light.Kind), name => session.RenameLight(light.Id, name));

        LightState state = light.State;
        LightState edited = state;

        if (Props.Section("Light"))
        {
            int kind = Props.Choice(
                "Type",
                "light-kind",
                [(Icons.LightSun, "Sun"), (Icons.LightPoint, "Point"), (Icons.LightSpot, "Spot")],
                (int)light.Kind);
            edited = edited with { Kind = (LightKind)kind };

            edited = DrawColour(light, edited);

            float intensity = light.Intensity;
            if (Props.Float("Intensity", "intensity", ref intensity, 0.01f, 0f, SceneLight.MaxIntensity, "%.2f"))
            {
                edited = edited with { Intensity = intensity };
            }

            if (light.Kind != LightKind.Directional)
            {
                float range = light.Range;
                if (Props.Float("Reach", "range", ref range, 0.1f, SceneLight.MinRange, SceneLight.MaxRange, "%.1f"))
                {
                    edited = edited with { Range = range };
                }

                Tooltip("How far the light gets, in world units. It fades out smoothly to nothing there.");
            }

            bool on = light.Visible;
            if (Props.Check(string.Empty, "light-on", "Shines", ref on))
            {
                session.SetLightVisible(light.Id, on);
            }

            Tooltip($"Off lights stay in the level but light nothing.{Shortcut.Hint(EditorAction.Hide)}");
        }

        if (light.Kind == LightKind.Spot && Props.Section("Spot"))
        {
            float angle = light.SpotAngle;
            if (Props.Slider("Cone", "angle", ref angle, SceneLight.MinSpotAngle, SceneLight.MaxSpotAngle, "%.0f°"))
            {
                edited = edited with { SpotAngle = angle };
            }

            float blend = light.SpotBlend;
            if (Props.Slider("Soft edge", "blend", ref blend, 0f, 1f, "%.2f"))
            {
                edited = edited with { SpotBlend = blend };
            }
        }

        // A point light shines every way, so there is nothing to aim.
        if (light.Kind != LightKind.Point && Props.Section("Direction"))
        {
            (float azimuth, float elevation) = SceneLight.AnglesOf(light.Direction);
            bool aimed = false;

            aimed |= Props.Slider("From", "azimuth", ref azimuth, 0f, 360f, "%.0f°");
            Tooltip("The compass bearing the light comes from.");

            aimed |= Props.Slider("Height", "elevation", ref elevation, -90f, 90f, "%.0f°");
            Tooltip("How high it comes from: 90 is straight overhead.");

            if (aimed)
            {
                Quaternion rotation = SceneLight.Aiming(SceneLight.ShiningFrom(azimuth, elevation));
                edited = edited with { Transform = edited.Transform with { Rotation = rotation } };
            }

            Props.Label(string.Empty);
            DrawCompass(-light.Direction, light.Visible);
        }

        if (Props.Section("Transform"))
        {
            Vector3 position = light.Position;
            if (Props.Vector("Location", "light-location", ref position, 0.05f))
            {
                edited = edited with { Transform = edited.Transform with { Position = position } };
            }

            if (light.Kind == LightKind.Directional)
            {
                Tooltip("Where its icon stands. A sun lights everything alike wherever it is.");
            }
        }

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
    }

    /// <summary>A swatch as wide as the value column; a click opens the picker.</summary>
    private static LightState DrawColour(SceneLight light, LightState edited)
    {
        Props.Label("Colour");

        Vector3 colour = light.Colour;
        if (ImGui.ColorButton(
                "##light-colour-swatch",
                new Vector4(colour, 1f),
                ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip,
                new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight())))
        {
            ImGui.OpenPopup(ColourPopup);
        }

        if (ImGui.BeginPopup(ColourPopup))
        {
            ImGui.SetNextItemWidth(220f);
            if (ImGui.ColorPicker3("##light-colour-picker", ref colour, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.DisplayRGB))
            {
                edited = edited with { Colour = colour };
            }

            ImGui.EndPopup();
        }

        return edited;
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
