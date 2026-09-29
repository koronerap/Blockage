using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The tools, floating over the top-left of the viewport: Select first, as in Blender, then the four
/// that change the level. View is reachable by its shortcut but has no button, because the camera
/// already works from inside every other tool.
/// </summary>
public static class ToolColumn
{
    private const float ButtonSize = 34f;

    private static readonly (EditorTool Tool, Icons.Painter Icon, string Name, string Help)[] Tools =
    [
        (EditorTool.Select, Icons.Select, "Select", "Click to pick what the tools work on,\ndrag for a box. Shift adds, Ctrl takes away."),
        (EditorTool.Transform, Icons.Move, "Transform", "Move and rotate what is selected."),
        (EditorTool.Extrude, Icons.Extrude, "Extrude", "Select a surface, then drag its arrow.\nOut adds voxels, in deletes them."),
        (EditorTool.Paint, Icons.Paint, "Paint", "Recolor existing, visible voxels.\nNever creates or deletes."),
        (EditorTool.LoopCut, Icons.Cut, "Loop Cut", "Split the model at a grid plane\ninto two independent objects."),
    ];

    /// <summary>The key a tool is on in the active keymap, empty when it has none.</summary>
    public static string KeyFor(EditorTool tool) => tool switch
    {
        EditorTool.Select => Shortcut.Of(EditorAction.ToolSelect),
        EditorTool.Transform => Shortcut.ForTransform(),
        EditorTool.Extrude => Shortcut.Of(EditorAction.ToolExtrude),
        EditorTool.Paint => Shortcut.Of(EditorAction.ToolPaint),
        EditorTool.LoopCut => Shortcut.Of(EditorAction.ToolLoopCut),
        _ => Shortcut.Of(EditorAction.ToolView),
    };

    /// <summary>A tool's icon, name and key — the Tool tab and its tab button show the same.</summary>
    public static (Icons.Painter Icon, string Name, string Shortcut) Describe(EditorTool tool)
    {
        foreach ((EditorTool candidate, Icons.Painter icon, string name, _) in Tools)
        {
            if (candidate == tool)
            {
                return (icon, name, KeyFor(tool));
            }
        }

        return (Icons.ViewTool, "View", KeyFor(EditorTool.View));
    }

    /// <summary>Where the colour button was drawn last frame — for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max) ColourButtonRect { get; private set; }

    public static void Draw(EditorSession session, PalettePanel palette)
    {
        // Translucent, since there is no panel behind them any more: the model shows through a
        // little, so the buttons read as floating over the scene rather than as holes cut in it.
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.SurfaceRaised with { W = 0.82f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.ControlHovered with { W = 0.92f });

        foreach ((EditorTool tool, Icons.Painter icon, string name, string help) in Tools)
        {
            string key = KeyFor(tool);
            if (IconButton.Draw(
                    tool.ToString(),
                    icon,
                    session.ActiveTool == tool,
                    key.Length > 0 ? $"{name}  ({key})\n\n{help}" : $"{name}\n\n{help}",
                    ButtonSize))
            {
                session.ActiveTool = tool;
            }
        }

        ImGui.PopStyleColor(2);

        DrawColour(session, palette);
    }

    /// <summary>
    /// The colour in hand, under the tools — as MagicaVoxel and the phone keep it — opening the palette
    /// in a popover. Choosing a colour used to mean finding the Palette section and scrolling to it.
    /// </summary>
    private static void DrawColour(EditorSession session, PalettePanel palette)
    {
        ImGui.Dummy(new Vector2(ButtonSize, 6f));

        Core.Voxels.Color32 colour = session.Scene.Palette[session.ActiveColorIndex];
        if (ImGui.ColorButton(
                "##active-colour",
                colour.ToVector4(),
                ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip,
                new Vector2(ButtonSize, ButtonSize)))
        {
            ImGui.OpenPopup("##quick-palette");
        }

        ColourButtonRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Colour  -  index {session.ActiveColorIndex}  {colour}\nClick for the palette");
        }

        if (ImGui.BeginPopup("##quick-palette"))
        {
            if (palette.DrawQuickPalette(session))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
