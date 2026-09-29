using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Draws an <see cref="Icons"/> pictogram through ImGui, and wraps one in a button.
///
/// This is the only part of the icon set that knows what ImGui is. Everything about what an icon
/// looks like lives in <see cref="Icons"/>, which the Android head compiles in as well.
/// </summary>
public sealed class ImGuiIconCanvas(ImDrawListPtr drawList, uint colour, float thickness) : IIconCanvas
{
    public void Line(Vector2 from, Vector2 to, float width = 1f) =>
        drawList.AddLine(from, to, colour, thickness * width);

    public void Polyline(ReadOnlySpan<Vector2> points, float width = 1f)
    {
        drawList.PathClear();

        foreach (Vector2 point in points)
        {
            drawList.PathLineTo(point);
        }

        drawList.PathStroke(colour, ImDrawFlags.None, thickness * width);
    }

    public void Rect(Vector2 min, Vector2 max, float rounding = 0f, float width = 1f) =>
        drawList.AddRect(min, max, colour, rounding, ImDrawFlags.None, thickness * width);

    public void FilledRect(Vector2 min, Vector2 max, float rounding = 0f) =>
        drawList.AddRectFilled(min, max, colour, rounding);

    public void Circle(Vector2 centre, float radius, float width = 1f) =>
        drawList.AddCircle(centre, radius, colour, 20, thickness * width);

    public void FilledCircle(Vector2 centre, float radius) =>
        drawList.AddCircleFilled(centre, radius, colour);

    public void FilledTriangle(Vector2 a, Vector2 b, Vector2 c) =>
        drawList.AddTriangleFilled(a, b, c, colour);
}

public static class IconButton
{
    private const float Thickness = 1.6f;

    /// <summary>
    /// A square button that paints an icon over itself. The button carries the interaction and the
    /// theming; the painter only has to know its own shape.
    /// </summary>
    /// <param name="hasAlternatives">
    /// Marks the button with a small triangle in its lower-right corner — Blender's sign that a button
    /// stands for one of several choices and a click will offer or switch to the others.
    /// </param>
    public static bool Draw(
        string id,
        Icons.Painter painter,
        bool active,
        string tooltip,
        float size,
        bool hasAlternatives = false)
    {
        Vector2 topLeft = ImGui.GetCursorScreenPos();

        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
        }

        bool clicked = ImGui.Button($"##{id}", new Vector2(size, size));
        bool hovered = ImGui.IsItemHovered();

        if (active)
        {
            ImGui.PopStyleColor(3);
        }

        // Over the accent the icon is light whatever the theme; elsewhere it follows the text.
        Vector4 tint = active ? Theme.TextOnAccent : hovered ? Theme.Text : Theme.TextDim;
        uint colour = ImGui.ColorConvertFloat4ToU32(tint);
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        painter(new ImGuiIconCanvas(drawList, colour, Thickness), topLeft + new Vector2(size * 0.5f), size * 0.3f);

        if (hasAlternatives)
        {
            // Tucked into the corner and small enough not to compete with the icon, which is the
            // thing actually being read.
            Vector2 corner = topLeft + new Vector2(size - 3f, size - 3f);
            float leg = MathF.Max(size * 0.16f, 4f);
            drawList.AddTriangleFilled(corner, corner - new Vector2(leg, 0f), corner - new Vector2(0f, leg), colour);
        }

        if (hovered && tooltip.Length > 0)
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }

    /// <summary>
    /// A two-way switch as one button. The icon is the state it is in now, and a click takes the
    /// other one.
    ///
    /// One button rather than a pair: two side by side spend twice the width to show one bit, and
    /// the half that is not lit carries no information the tooltip cannot. Returns true on the frame
    /// the state changes.
    /// </summary>
    public static bool Toggle(
        string id,
        (Icons.Painter Icon, string Name) first,
        (Icons.Painter Icon, string Name) second,
        bool isSecond,
        string shortcut,
        float size)
    {
        (Icons.Painter icon, string name) = isSecond ? second : first;
        string other = isSecond ? first.Name : second.Name;
        string key = shortcut.Length > 0 ? $"  ({shortcut})" : string.Empty;

        return Draw(id, icon, active: false, $"{name}\nClick for {other}{key}", size, hasAlternatives: true);
    }

    /// <summary>
    /// One of several choices as one button: the icon is the current choice, and a click opens the
    /// list. For three or more, where a switch would have to cycle blind through options the user
    /// cannot see.
    /// </summary>
    public static void Choice(
        string id,
        (Icons.Painter Icon, string Name)[] options,
        int selected,
        string shortcut,
        float size,
        Action<int> onChange)
    {
        (Icons.Painter icon, string name) = options[selected];
        string key = shortcut.Length > 0 ? $"  ({shortcut} cycles)" : string.Empty;

        if (Draw(id, icon, active: false, $"{name}{key}", size, hasAlternatives: true))
        {
            ImGui.OpenPopup($"##{id}-choices");
        }

        if (!ImGui.BeginPopup($"##{id}-choices"))
        {
            return;
        }

        float row = ImGui.GetFrameHeight();

        // A selectable with no visible label has no width of its own to size the popup by, so the
        // widest name decides it.
        float width = 0f;
        foreach ((_, string optionName) in options)
        {
            width = MathF.Max(width, ImGui.CalcTextSize(optionName).X);
        }

        width += row + 14f;

        for (int i = 0; i < options.Length; i++)
        {
            (Icons.Painter optionIcon, string optionName) = options[i];

            // The label is indented and the icon painted into the gap, since a selectable has no
            // slot of its own for a picture.
            if (ImGui.Selectable($"##{id}-option{i}", i == selected, ImGuiSelectableFlags.None, new Vector2(width, row)))
            {
                if (i != selected)
                {
                    onChange(i);
                }
            }

            Vector2 min = ImGui.GetItemRectMin();
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            uint text = ImGui.GetColorU32(ImGuiCol.Text);

            optionIcon(new ImGuiIconCanvas(drawList, text, Thickness), min + new Vector2(row * 0.5f), row * 0.3f);
            drawList.AddText(
                min + new Vector2(row + 6f, (row - ImGui.GetTextLineHeight()) * 0.5f),
                text,
                optionName);
        }

        ImGui.EndPopup();
    }
}
