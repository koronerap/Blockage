using System.Numerics;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Where the viewport's one directional light points. The mode switch itself lives in the header
/// beside the other viewport toggles; this is the detail behind it.
///
/// There is no way to add a second light, and that is the point: this exists to preview how a shape
/// catches light, not to light a scene. Anything more would be a lighting tool inside a level
/// editor, and the light is not saved with the level either way.
/// </summary>
public static class LightingPanel
{
    public static void DrawContent(SceneLighting lighting)
    {
        bool lit = lighting.IsLit;
        int mode = lit ? 0 : 1;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##mode", ref mode, "Lit\0Unlit\0"))
        {
            lighting.Mode = mode == 0 ? ShadingMode.Lit : ShadingMode.Unlit;
        }

        if (!lit)
        {
            ImGui.TextDisabled("Unlit: each face keeps a fixed shade.");
            ImGui.TextDisabled("This is how the exported mesh looks.");
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(!lit);

        float azimuth = lighting.Azimuth;
        float elevation = lighting.Elevation;
        float intensity = lighting.Intensity;
        float ambient = lighting.Ambient;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##azimuth", ref azimuth, 0f, 360f, "Direction  %.0f deg"))
        {
            lighting.Azimuth = azimuth;
        }

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##elevation", ref elevation, -20f, 90f, "Height  %.0f deg"))
        {
            // Slightly below the horizon is allowed: it is the quickest way to see which faces are
            // being carried entirely by the ambient floor.
            lighting.Elevation = elevation;
        }

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##intensity", ref intensity, 0f, 1f, "Intensity  %.2f"))
        {
            lighting.Intensity = intensity;
        }

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##ambient", ref ambient, 0f, 1f, "Ambient  %.2f"))
        {
            lighting.Ambient = ambient;
        }

        DrawDirectionPreview(lighting);

        if (ImGui.Button("Reset light", new Vector2(-1f, 0f)))
        {
            lighting.ResetAngles();
        }

        ImGui.EndDisabled();
    }

    /// <summary>
    /// A compass showing where the light comes from, seen from above. Two angles are hard to hold in
    /// your head at once; a dot on a circle is not.
    /// </summary>
    private static void DrawDirectionPreview(SceneLighting lighting)
    {
        float size = ImGui.GetFrameHeight() * 2.6f;
        Vector2 topLeft = ImGui.GetCursorScreenPos();

        // Reserved through a dummy so the layout accounts for something drawn by hand.
        ImGui.Dummy(new Vector2(size, size));

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 centre = topLeft + new Vector2(size * 0.5f);
        float radius = size * 0.42f;

        uint ring = ImGui.GetColorU32(Theme.Sunken);
        uint mark = ImGui.GetColorU32(lighting.IsLit ? Theme.Highlight : Theme.TextDisabled);

        drawList.AddCircleFilled(centre, radius, ring);
        drawList.AddCircle(centre, radius, ImGui.GetColorU32(ImGuiCol.Border));

        // The horizontal part of a unit direction is already shortened by the elevation, so using it
        // as-is pulls the dot towards the middle as the light rises — and puts it dead centre for a
        // light directly overhead, which is what being overhead looks like from above.
        Vector3 direction = lighting.Direction;
        Vector2 dot = centre + (new Vector2(direction.X, -direction.Z) * radius);

        drawList.AddLine(centre, dot, mark);
        drawList.AddCircleFilled(dot, 3.5f, mark);
    }
}
