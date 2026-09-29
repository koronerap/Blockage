using System.Numerics;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Every key the editor answers to, on one sheet over the viewport — F1, or Help in the menu bar.
/// Menus show the keys for what is in them; the camera, the numpad and the modifier drags are in no
/// menu at all, and this is the one place they are all written down.
///
/// Built from the active keymap each time it is drawn, so it lists the keys as they are — the preset's
/// or the user's own — and next to them the mouse gestures, which no keymap changes.
/// </summary>
public static class ShortcutSheet
{
    /// <summary>What the mouse does, which is the same whatever the keys: listed with the group it belongs to.</summary>
    private static readonly (string Group, (string Keys, string Does)[] Entries)[] Gestures =
    [
        ("Extrude",
        [
            ("Drag the selection", "Pull it out, push it in"),
            ("Shift+drag", "Add to the selection"),
            ("Alt+drag", "Take from the selection"),
        ]),
        ("View",
        [
            ("Middle drag", "Orbit"),
            ("Shift+Middle drag", "Pan"),
            ("Wheel, Ctrl+Middle", "Zoom"),
            ("Right drag", "Look - W A S D, Q E fly"),
        ]),
        ("Paint",
        [
            ("Ctrl+Scroll", "Brush size"),
            ("Shift+drag", "A line"),
            ("Ctrl+drag", "A box"),
            ("Alt+click", "Pick a colour"),
        ]),
        ("Lights",
        [
            ("Drag a sun's line", "Aim it at what it lands on"),
        ]),
    ];

    /// <summary>
    /// The sheet's groups as they stand: the keymap's categories with the keys bound in it, and the
    /// gestures. Tools, editing and Extrude down the left; the camera, the long one, on the right.
    /// </summary>
    public static IReadOnlyList<(string Group, (string Keys, string Does)[] Entries)> Groups
    {
        get
        {
            (string, (string, string)[]) FromKeymap(string category) =>
                (category, [.. EditorActions.All
                    .Where(a => a.Category == category && Keymap.Active.Bindings(a.Action).Count > 0)
                    .Select(a => (Shortcut.All(a.Action), a.Name))]);

            (string, (string, string)[]) Gesture(string group) => Gestures.First(g => g.Group == group);

            (string, (string, string)[]) ViewGroup()
            {
                (string name, (string, string)[] keys) = FromKeymap(EditorActions.View);
                return (name, [.. Gesture("View").Item2, .. keys]);
            }

            return
            [
                FromKeymap(EditorActions.Tools),
                FromKeymap(EditorActions.Edit),
                Gesture("Extrude"),
                FromKeymap(EditorActions.File),
                ViewGroup(),
                Gesture("Paint"),
                Gesture("Lights"),
                FromKeymap(EditorActions.Help),
            ];
        }
    }

    /// <summary>How many groups go down the left column.</summary>
    private const int LeftGroups = 4;

    private const float KeyColumn = 190f;

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
            $"Keyboard shortcuts  -  {Keymap.NameOf(Keymap.Active.Preset)}###shortcut-sheet",
            ref open,
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove);

        var groups = Groups;
        if (ImGui.BeginTable("##shortcut-columns", 2, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableNextColumn();
            DrawGroups(groups, 0, LeftGroups);
            ImGui.TableNextColumn();
            DrawGroups(groups, LeftGroups, groups.Count);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        string close = Shortcut.Of(EditorAction.ShortcutSheet);
        ImGui.TextDisabled($"{(close.Length > 0 ? close + " or " : string.Empty)}Esc closes this. The keys can be changed in Edit > Preferences > Keymap.");

        bool clickedAway = ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);

        ImGui.End();

        if (!open || clickedAway)
        {
            Close();
        }
    }

    private static void DrawGroups(IReadOnlyList<(string Group, (string Keys, string Does)[] Entries)> groups, int from, int to)
    {
        for (int g = from; g < to; g++)
        {
            (string group, (string Keys, string Does)[] entries) = groups[g];
            if (entries.Length == 0)
            {
                continue;
            }

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

        ImGui.Dummy(new Vector2(KeyColumn + 200f, 0f));
    }
}
