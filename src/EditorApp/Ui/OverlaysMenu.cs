using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// What is drawn over the scene, behind one button at the right of the header — Blender's Overlays
/// popover. Two of these used to be buttons of their own and the rest were in the View menu or not
/// switchable at all; now every overlay is in one list with its key beside it.
/// </summary>
public static class OverlaysMenu
{
    private const string PopupId = "##overlays";

    public static void DrawButton(ViewActions view, float size)
    {
        if (IconButton.Draw("overlays", Icons.Overlays, active: false, "Overlays  -  what is drawn over the scene", size, hasAlternatives: true))
        {
            ImGui.OpenPopup(PopupId);
        }

        if (ImGui.BeginPopup(PopupId))
        {
            DrawItems(view);
            ImGui.EndPopup();
        }
    }

    /// <summary>The list itself, as checkable items that leave the popup open for the next.</summary>
    public static void DrawItems(ViewActions view)
    {
        ImGui.TextDisabled("Overlays");
        ImGui.Separator();

        Item("Ground grid", "G", view.GridVisible(), view.ToggleGrid);
        Item("Measurements", "D", view.MeasurementsVisible(), view.ToggleMeasurements);
        Item("Light icons", string.Empty, view.LightIconsVisible(), view.ToggleLightIcons);
        Item("Mirror planes", string.Empty, view.MirrorPlanesVisible(), view.ToggleMirrorPlanes);
        Item("Statistics", string.Empty, view.StatisticsVisible(), view.ToggleStatistics);
    }

    /// <summary>
    /// A checkbox rather than a menu item: switching one off is often followed by the next, and a
    /// menu item closes the list on every click.
    /// </summary>
    private static void Item(string name, string shortcut, bool on, Action toggle)
    {
        if (ImGui.Checkbox(name, ref on))
        {
            toggle();
        }

        if (shortcut.Length > 0)
        {
            ImGui.SameLine(170f);
            ImGui.TextDisabled(shortcut);
        }
    }
}
