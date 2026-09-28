using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Every key the editor answers to, on one sheet over the viewport — F1, or Help in the menu bar.
/// Menus show the keys for what is in them; the camera, the numpad and the modifier drags are in no
/// menu at all, and this is the one place they are all written down.
/// </summary>
public static class ShortcutSheet
{
    public static readonly (string Group, (string Keys, string Does)[] Entries)[] Groups =
    [
        ("Tools",
        [
            ("Q", "Transform"),
            ("W", "Extrude"),
            ("E", "Paint"),
            ("R", "Loop Cut"),
            ("V", "View - the camera only"),
            ("F", "The tool's other mode"),
            ("X", "Cycle paint mode, axes, new object"),
        ]),
        ("Edit",
        [
            ("Ctrl+Z", "Undo"),
            ("Ctrl+Y, Ctrl+Shift+Z", "Redo"),
            ("Ctrl+C / X / V", "Copy, cut, paste"),
            ("Shift+D", "Duplicate"),
            ("Delete", "Delete"),
            ("H", "Hide, or switch a light off"),
            ("Alt+H", "Show everything"),
            ("F2", "Rename"),
            ("Enter", "Keep an extrude"),
            ("Esc", "Cancel, or let go"),
        ]),
        ("File",
        [
            ("Ctrl+N", "New level"),
            ("Ctrl+O", "Open"),
            ("Ctrl+S", "Save"),
            ("Ctrl+Shift+S", "Save as"),
            ("Ctrl+E", "Export mesh"),
        ]),
        ("View",
        [
            ("Middle drag", "Orbit"),
            ("Shift+Middle drag", "Pan"),
            ("Wheel, Ctrl+Middle", "Zoom"),
            ("Right drag", "Look - W A S D, Q E fly"),
            ("Numpad 1 / 3 / 7", "Front, right, top"),
            ("Ctrl+Numpad", "The opposite side"),
            ("Numpad 9", "Turn round"),
            ("Numpad 5", "Orthographic"),
            ("Numpad 2 4 6 8", "Step the view round"),
            ("Numpad .", "Frame the focused object"),
            ("Home", "Frame the level"),
            ("G / D", "Grid, measurements"),
            ("N", "Sidebar"),
        ]),
        ("Paint",
        [
            ("Ctrl+Scroll", "Brush size"),
            ("Shift+drag", "A line"),
            ("Ctrl+drag", "A box"),
            ("Alt+click", "Pick a colour"),
        ]),
        ("Help",
        [
            ("F1", "This sheet"),
        ]),
    ];

    private const float KeyColumn = 150f;

    public static bool IsOpen { get; private set; }

    public static void Toggle() => IsOpen = !IsOpen;

    public static void Close() => IsOpen = false;

    public static void Draw(Vector2 screen)
    {
        if (!IsOpen)
        {
            return;
        }

        ImGui.SetNextWindowPos(screen * 0.5f, ImGuiCond.Always, new Vector2(0.5f));
        ImGui.SetNextWindowBgAlpha(0.97f);

        bool open = true;
        ImGui.Begin(
            "Keyboard shortcuts###shortcut-sheet",
            ref open,
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove);

        // Three groups a column: the camera's list is the long one, so it gets a column with less else in it.
        if (ImGui.BeginTable("##shortcut-columns", 2, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableNextColumn();
            DrawGroups(0, 3);
            ImGui.TableNextColumn();
            DrawGroups(3, Groups.Length);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("F1 or Esc closes this.");

        bool clickedAway = ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);

        ImGui.End();

        if (!open || clickedAway)
        {
            Close();
        }
    }

    private static void DrawGroups(int from, int to)
    {
        for (int g = from; g < to; g++)
        {
            (string group, (string Keys, string Does)[] entries) = Groups[g];

            ImGui.SeparatorText(group);

            foreach ((string keys, string does) in entries)
            {
                // A gap measured from the key's own width: an absolute position would be counted from
                // the table column's start a second time in every column but the first.
                ImGui.TextColored(Theme.Highlight, keys);
                ImGui.SameLine(0f, MathF.Max(KeyColumn - ImGui.CalcTextSize(keys).X, 12f));
                ImGui.TextUnformatted(does);
            }

            ImGui.Spacing();
        }

        ImGui.Dummy(new Vector2(KeyColumn + 190f, 0f));
    }
}
