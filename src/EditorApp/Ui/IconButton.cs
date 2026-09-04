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
    public static bool Draw(string id, Icons.Painter painter, bool active, string tooltip, float size)
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

        Vector4 tint = active || hovered ? Theme.Text : Theme.TextDim;
        var canvas = new ImGuiIconCanvas(
            ImGui.GetWindowDrawList(),
            ImGui.ColorConvertFloat4ToU32(tint),
            Thickness);

        painter(canvas, topLeft + new Vector2(size * 0.5f), size * 0.3f);

        if (hovered && tooltip.Length > 0)
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }
}
