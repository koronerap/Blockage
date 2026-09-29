using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Menu entries with an icon in front, as Blender's menus have them. ImGui's own items take text
/// only, so the label is given room at the front and the icon is painted there — everything else,
/// hover, keys, closing, stays ImGui's.
/// </summary>
public static class IconMenu
{
    private static float _padFor = -1f;
    private static string _pad = string.Empty;

    /// <summary>An item; true when chosen.</summary>
    public static bool Item(Icons.Painter icon, string label, string? shortcut = null, bool selected = false, bool enabled = true)
    {
        (ImDrawListPtr drawList, Vector2 at) = (ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos());
        bool chosen = ImGui.MenuItem(Pad() + label, shortcut, selected, enabled);
        Paint(drawList, at, icon, enabled);
        return chosen;
    }

    /// <summary>A submenu; true while it is open, when the caller draws it and closes it with <c>ImGui.EndMenu</c>.</summary>
    public static bool Begin(Icons.Painter icon, string label, bool enabled = true)
    {
        // Painted from where the entry starts, not from its rectangle: once open, the last item is
        // the submenu's own window.
        (ImDrawListPtr drawList, Vector2 at) = (ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos());
        bool open = ImGui.BeginMenu(Pad() + label, enabled);
        Paint(drawList, at, icon, enabled);
        return open;
    }

    private static void Paint(ImDrawListPtr drawList, Vector2 at, Icons.Painter icon, bool enabled)
    {
        float line = ImGui.GetTextLineHeight();
        Vector4 colour = enabled ? Theme.Text with { W = 0.85f } : Theme.TextDim with { W = 0.5f };
        icon(new ImGuiIconCanvas(drawList, ImGui.ColorConvertFloat4ToU32(colour), 1.2f), at + new Vector2(line * 0.55f, line * 0.5f), line * 0.42f);
    }

    /// <summary>Spaces as wide as an icon and the gap after it, worked out once for each size of text.</summary>
    private static string Pad()
    {
        float line = ImGui.GetTextLineHeight();
        if (line != _padFor)
        {
            float space = MathF.Max(ImGui.CalcTextSize(" ").X, 1f);
            _pad = new string(' ', (int)MathF.Ceiling(line * 1.35f / space));
            _padFor = line;
        }

        return _pad;
    }
}
