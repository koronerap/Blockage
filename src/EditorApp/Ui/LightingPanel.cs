using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// How the level is lit: the viewport's Lit / Unlit switch, the level's ambient floor, and adding
/// lights. The lights themselves are in the outliner, each with its own settings.
///
/// Lights used to be one direction set here, and not part of the level. They are things in the level
/// now — saved with it, several of them, sun and point and spot — and still never exported.
/// </summary>
public static class LightingPanel
{
    public static void DrawContent(ShellContext context)
    {
        EditorSession session = context.Session;
        SceneLighting lighting = context.View.Lighting;

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

        ImGui.BeginDisabled(!lit);

        float ambient = session.Scene.Ambient;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##ambient", ref ambient, 0f, 1f, "Ambient  %.2f"))
        {
            session.SetAmbient(ambient);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Light every face gets whichever way it points.\nWithout it, faces no light reaches go black.");
        }

        IReadOnlyList<SceneLight> lights = session.Scene.Lights;
        int off = lights.Count(l => !l.Visible);
        ImGui.TextDisabled(lights.Count == 0
            ? "No lights - only the ambient."
            : $"{lights.Count} light(s){(off > 0 ? $", {off} off" : string.Empty)}");

        if (context.Renderer.DroppedLights > 0)
        {
            ImGui.TextColored(
                Theme.Highlight,
                $"{context.Renderer.DroppedLights} not drawn - {LightUniforms.MaxLights} at most at once.");
        }

        ImGui.EndDisabled();

        DrawAddButtons(session);
        ImGui.TextDisabled("Saved with the level, never exported.");
    }

    private static void DrawAddButtons(EditorSession session)
    {
        float button = ImGui.GetFrameHeight();

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Add");

        foreach ((LightKind kind, string label) in LightMenu.Kinds)
        {
            ImGui.SameLine();
            if (IconButton.Draw($"add-{kind}", Icons.For(kind), active: false, $"Add a {label.ToLowerInvariant()}", button))
            {
                LightMenu.Add(session, kind);
            }
        }
    }
}
