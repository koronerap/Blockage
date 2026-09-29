using System.Numerics;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The right-hand end of the viewport header, laid out as Blender's: Gizmos and Overlays, each a
/// switch with a popover beside it; X-Ray; and the three shadings with theirs. They belong to the
/// viewport rather than to any tool, so they keep their place whichever tool is in hand.
/// </summary>
public static class ViewportHeader
{
    /// <summary>The narrow button that opens a popover beside a switch.</summary>
    private const float DropShare = 0.6f;

    private const float Pair = 1f;

    private const float GroupGap = 10f;

    /// <summary>How wide the whole group is, so the header can put it against its right edge.</summary>
    public static float Width(float button) =>
        (button + Pair + (button * DropShare)) * 2f   // gizmos, overlays
        + (GroupGap * 0.5f)                            // between those two
        + GroupGap                                     // between them and X-Ray
        + button                                       // X-Ray
        + GroupGap
        + (button * 3f) + (Pair * 2f) + Pair + (button * DropShare);   // shading

    public static void Draw(ViewActions view, float button)
    {
        ViewportSettings v = view.Viewport;

        if (IconButton.Draw("gizmos", Icons.Gizmo, v.Gizmos, v.Gizmos ? $"Gizmos shown{Shortcut.Hint(EditorAction.ToggleGizmos)}" : "Gizmos hidden", button))
        {
            v.Gizmos = !v.Gizmos;
        }

        ImGui.SameLine(0f, Pair);
        Dropdown("gizmos", "Which gizmos", button, () => DrawGizmos(v));

        ImGui.SameLine(0f, GroupGap * 0.5f);
        if (IconButton.Draw("overlays", Icons.Overlays, v.Overlays, v.Overlays ? $"Overlays shown{Shortcut.Hint(EditorAction.ToggleOverlays)}" : $"Overlays hidden{Shortcut.Hint(EditorAction.ToggleOverlays)}", button))
        {
            v.Overlays = !v.Overlays;
        }

        ImGui.SameLine(0f, Pair);
        Dropdown("overlays", "Which overlays", button, () => OverlaysMenu.DrawItems(view));

        ImGui.SameLine(0f, GroupGap);
        if (IconButton.Draw("xray", Icons.XRay, v.XRay, $"X-Ray  -  see through the model{Shortcut.Hint(EditorAction.ToggleXRay)}", button))
        {
            v.XRay = !v.XRay;
        }

        ImGui.SameLine(0f, GroupGap);
        Shading(v, ShadingMode.Wireframe, Icons.ShadingWire, $"Wireframe  -  the voxel lattice alone{Shortcut.Hint(EditorAction.ToggleWireframe)}", button);
        ImGui.SameLine(0f, Pair);
        Shading(v, ShadingMode.Unlit, Icons.ShadingSolid, "Solid  -  a fixed shade per face, as exported", button);
        ImGui.SameLine(0f, Pair);
        Shading(v, ShadingMode.Lit, Icons.Lit, "Lit  -  by the level's own lights", button);
        ImGui.SameLine(0f, Pair);
        Dropdown("shading", "Shading options", button, () => DrawShading(v));
    }

    private static void Shading(ViewportSettings v, ShadingMode mode, Icons.Painter icon, string tooltip, float button)
    {
        if (IconButton.Draw($"shading-{mode}", icon, v.Shading == mode, tooltip, button))
        {
            v.Shading = mode;
        }
    }

    /// <summary>A narrow button with a chevron, opening a popover drawn by <paramref name="items"/>.</summary>
    private static void Dropdown(string id, string tooltip, float button, Action items)
    {
        Vector2 size = new(MathF.Round(button * DropShare), button);
        Vector2 min = ImGui.GetCursorScreenPos();

        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        if (ImGui.Button($"##{id}-more", size))
        {
            ImGui.OpenPopup($"##{id}-popover");
        }

        ImGui.PopStyleColor();

        bool hovered = ImGui.IsItemHovered();
        uint colour = ImGui.ColorConvertFloat4ToU32(hovered ? Theme.Text : Theme.TextDim);
        Icons.ChevronDown(new ImGuiIconCanvas(ImGui.GetWindowDrawList(), colour, 1.4f), min + (size * 0.5f), size.X * 0.34f);

        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        if (ImGui.BeginPopup($"##{id}-popover"))
        {
            items();
            ImGui.EndPopup();
        }
    }

    /// <summary>Blender's "Viewport Gizmos", as far as there are gizmos here.</summary>
    public static void DrawGizmos(ViewportSettings v)
    {
        ImGui.TextDisabled("Viewport Gizmos");
        ImGui.Separator();

        ImGui.BeginDisabled(!v.Gizmos);
        Check("Navigate", "The axis ball and the view buttons in the corner.", v.NavigateGizmo, on => v.NavigateGizmo = on);
        Check("Active tools", "The Transform gizmo and the extrude arrow.\nA press on a selection still pulls it.", v.ToolGizmos, on => v.ToolGizmos = on);

        ImGui.Spacing();
        ImGui.TextDisabled("Lights");
        Check("Aim lines", "A sun's or a spot's line and the ring it is aimed by.", v.LightGizmos, on => v.LightGizmos = on);
        ImGui.EndDisabled();

        if (!v.Gizmos)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("All hidden - the switch beside this is off.");
        }
    }

    /// <summary>Blender's "Viewport Shading": how Solid lights, where colour comes from, what is behind, and X-Ray.</summary>
    public static void DrawShading(ViewportSettings v)
    {
        ImGui.TextDisabled("Viewport Shading");
        ImGui.Separator();

        if (v.Shading == ShadingMode.Unlit)
        {
            ImGui.TextUnformatted("Lighting");
            int lighting = (int)v.SolidLighting;
            ImGui.RadioButton("Studio", ref lighting, (int)SolidLighting.Studio);
            Tip("A fixed shade per face, the look an export has.");
            ImGui.SameLine();
            ImGui.RadioButton("Flat", ref lighting, (int)SolidLighting.Flat);
            Tip("No shading at all: every face its bare colour.");
            v.SolidLighting = (SolidLighting)lighting;
            ImGui.Spacing();
        }
        else if (v.Shading == ShadingMode.Lit)
        {
            ImGui.TextDisabled("Lit by the level's lights and ambient,\nset in the World tab and the Outliner.");
            ImGui.Spacing();
        }

        ImGui.TextUnformatted("Colour");
        int colour = (int)v.Colour;
        ImGui.RadioButton("Palette", ref colour, (int)ColourMode.Palette);
        ImGui.SameLine();
        ImGui.RadioButton("Single", ref colour, (int)ColourMode.Single);
        Tip("One colour for everything: the shape and nothing else.");
        ImGui.SameLine();
        ImGui.RadioButton("Random", ref colour, (int)ColourMode.Random);
        Tip("A colour for each object, to see which voxels belong to which.");
        v.Colour = (ColourMode)colour;

        if (v.Colour == ColourMode.Single)
        {
            Vector3 single = v.SingleColour;
            if (ImGui.ColorEdit3("##single-colour", ref single, ImGuiColorEditFlags.NoInputs))
            {
                v.SingleColour = single;
            }

            ImGui.SameLine();
            ImGui.TextDisabled("The colour");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Background");
        int background = (int)v.Background;
        ImGui.RadioButton("Theme", ref background, (int)BackgroundMode.Theme);
        ImGui.SameLine();
        ImGui.RadioButton("Custom", ref background, (int)BackgroundMode.Custom);
        v.Background = (BackgroundMode)background;

        if (v.Background == BackgroundMode.Custom)
        {
            ImGui.SameLine();
            Vector3 custom = v.BackgroundColour;
            if (ImGui.ColorEdit3("##background-colour", ref custom, ImGuiColorEditFlags.NoInputs))
            {
                v.BackgroundColour = custom;
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Options");
        Check("X-Ray", "See through the model: what is behind shows through it.", v.XRay, on => v.XRay = on);

        ImGui.SameLine();
        ImGui.BeginDisabled(!v.XRay);
        float alpha = v.XRayAlpha;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.SliderFloat("##xray-alpha", ref alpha, 0.05f, 0.95f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
        {
            v.XRayAlpha = alpha;
        }

        ImGui.EndDisabled();
        Tip("How solid the faces stay.");
    }

    private static void Check(string name, string tooltip, bool value, Action<bool> set)
    {
        if (ImGui.Checkbox(name, ref value))
        {
            set(value);
        }

        Tip(tooltip);
    }

    private static void Tip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(text);
        }
    }
}
