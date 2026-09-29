using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// How the level is lit, in the World tab: the viewport's Lit / Unlit switch, the level's ambient
/// floor, and adding lights. The lights themselves are in the Outliner, each with its settings in the
/// Object tab when picked.
/// </summary>
public static class LightingPanel
{
    /// <param name="droppedLights">Lights switched on beyond what the shader can draw at once.</param>
    public static void DrawContent(EditorSession session, ViewportSettings viewport, int droppedLights)
    {
        int shown = viewport.Shading switch { ShadingMode.Lit => 0, ShadingMode.Unlit => 1, _ => 2 };
        int mode = Props.Choice(
            "Shading",
            "shading",
            [(Icons.Lit, "Lit"), (Icons.Unlit, "Solid"), (Icons.ShadingWire, "Wireframe")],
            shown);

        if (mode != shown)
        {
            viewport.Shading = mode switch { 0 => ShadingMode.Lit, 1 => ShadingMode.Unlit, _ => ShadingMode.Wireframe };
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Solid gives each face a fixed shade - how the exported mesh looks.");
        }

        ImGui.BeginDisabled(viewport.Shading != ShadingMode.Lit);

        float ambient = session.Scene.Ambient;
        if (Props.Slider("Ambient", "ambient", ref ambient, 0f, 1f, "%.2f"))
        {
            session.SetAmbient(ambient);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Light every face gets whichever way it points.\nWithout it, faces no light reaches go black.");
        }

        IReadOnlyList<SceneLight> lights = session.Scene.Lights;
        int off = lights.Count(l => !l.Visible);
        Props.Value("Lights", lights.Count == 0
            ? "none - only the ambient"
            : $"{lights.Count}{(off > 0 ? $", {off} off" : string.Empty)}");

        if (droppedLights > 0)
        {
            Props.Note(string.Empty, $"{droppedLights} not drawn - {LightUniforms.MaxLights} at most at once.", Theme.Highlight);
        }

        ImGui.EndDisabled();

        DrawAddButtons(session);
    }

    private static void DrawAddButtons(EditorSession session)
    {
        float button = ImGui.GetFrameHeight();

        Props.Label("Add");
        foreach ((LightKind kind, string label) in LightMenu.Kinds)
        {
            if (IconButton.Draw($"add-{kind}", Icons.For(kind), active: false, $"Add a {label.ToLowerInvariant()}  -  lights are saved with the level, never exported", button))
            {
                LightMenu.Add(session, kind);
            }

            ImGui.SameLine(0f, 4f);
        }

        ImGui.NewLine();
    }
}
