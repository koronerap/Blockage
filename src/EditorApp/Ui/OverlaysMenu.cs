using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// What is drawn over the scene — Blender's "Viewport Overlays", in its sections: guides, objects,
/// geometry. The header's switch beside it hides them all at once; the tools' own marks — the
/// selection, the hovered face, the cut ring — stay, since they are how the tools are used.
/// </summary>
public static class OverlaysMenu
{
    /// <summary>The list itself, as checkboxes that leave the popover open for the next.</summary>
    public static void DrawItems(ViewActions view)
    {
        ViewportSettings v = view.Viewport;

        ImGui.TextDisabled("Viewport Overlays");
        ImGui.Separator();

        ImGui.BeginDisabled(!v.Overlays);

        ImGui.TextDisabled("Guides");
        Item("Floor", Shortcut.Of(EditorAction.ToggleGrid), v.Grid, on => v.Grid = on);

        ImGui.SameLine(0f, 14f);
        ImGui.TextUnformatted("Axes");
        ImGui.SameLine();
        AxisBox("X", v.AxisX, on => v.AxisX = on);
        ImGui.SameLine();
        AxisBox("Y", v.AxisY, on => v.AxisY = on);
        ImGui.SameLine();
        AxisBox("Z", v.AxisZ, on => v.AxisZ = on);

        Item("Text info", string.Empty, v.TextInfo, on => v.TextInfo = on);
        Item("Statistics", string.Empty, view.StatisticsVisible(), _ => view.ToggleStatistics());
        Item("Measurements", Shortcut.Of(EditorAction.ToggleMeasurements), v.Measurements, on => v.Measurements = on);

        ImGui.Spacing();
        ImGui.TextDisabled("Objects");
        Item("Light icons", string.Empty, v.LightIcons, on => v.LightIcons = on);
        Item("Relationship lines", string.Empty, v.RelationshipLines, on => v.RelationshipLines = on);
        Item("Origins", string.Empty, v.Origins, on => v.Origins = on);
        Item("Focus highlight", string.Empty, v.FocusHighlight, on => v.FocusHighlight = on);

        ImGui.Spacing();
        ImGui.TextDisabled("Geometry");
        Item("Wireframe", string.Empty, v.Wireframe, on => v.Wireframe = on);
        ImGui.SameLine(170f);
        ImGui.BeginDisabled(!v.Wireframe);
        float opacity = v.WireframeOpacity;
        ImGui.SetNextItemWidth(110f);
        if (ImGui.SliderFloat("##wire-opacity", ref opacity, 0.05f, 1f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
        {
            v.WireframeOpacity = opacity;
        }

        ImGui.EndDisabled();
        Item("Mirror planes", string.Empty, v.MirrorPlanes, on => v.MirrorPlanes = on);

        ImGui.EndDisabled();

        if (!v.Overlays)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("All hidden - the switch beside this is off.");
        }
    }

    /// <summary>
    /// A checkbox rather than a menu item: switching one off is often followed by the next, and a
    /// menu item closes the list on every click.
    /// </summary>
    private static void Item(string name, string shortcut, bool on, Action<bool> set)
    {
        if (ImGui.Checkbox(name, ref on))
        {
            set(on);
        }

        if (shortcut.Length > 0)
        {
            ImGui.SameLine(170f);
            ImGui.TextDisabled(shortcut);
        }
    }

    /// <summary>A small toggle in the axis's own colour, as Blender's X Y Z buttons are.</summary>
    private static void AxisBox(string axis, bool on, Action<bool> set)
    {
        System.Numerics.Vector4 colour = axis switch { "X" => Theme.AxisX, "Y" => Theme.AxisY, _ => Theme.AxisZ };

        ImGui.PushStyleColor(ImGuiCol.Button, on ? colour with { W = 0.85f } : Theme.Control);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colour);
        ImGui.PushStyleColor(ImGuiCol.Text, on ? Theme.TextOnAccent : Theme.TextDim);

        if (ImGui.Button($"{axis}##axis-{axis}", new System.Numerics.Vector2(ImGui.GetFrameHeight())))
        {
            set(!on);
        }

        ImGui.PopStyleColor(3);
    }
}
