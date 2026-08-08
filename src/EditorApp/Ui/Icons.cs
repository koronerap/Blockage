using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Icons drawn with ImGui's draw list rather than loaded from an icon font.
///
/// A font would mean shipping a binary, matching its glyph ranges to the atlas, and living with
/// whatever pictograms it happens to contain. These are a handful of shapes for a fixed set of
/// tools, they scale to any button size, and they take their colour from the theme.
/// </summary>
public static class Icons
{
    /// <summary>Signature every icon painter shares: centre, half-size, colour.</summary>
    public delegate void Painter(ImDrawListPtr drawList, Vector2 centre, float radius, uint colour);

    private const float Thickness = 1.6f;

    // ---- Tools -------------------------------------------------------------------------------

    /// <summary>Transform: arrows out of a centre, the universal "move this" mark.</summary>
    public static void Move(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        Span<Vector2> directions = [new(0f, -1f), new(0f, 1f), new(-1f, 0f), new(1f, 0f)];

        foreach (Vector2 direction in directions)
        {
            Vector2 tip = centre + direction * r;
            drawList.AddLine(centre, tip, colour, Thickness);
            Arrowhead(drawList, tip, direction, r * 0.34f, colour);
        }
    }

    /// <summary>Extrude: a face lifting off the surface it came from.</summary>
    public static void Extrude(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        // The base surface.
        drawList.AddLine(
            centre + new Vector2(-r, r * 0.72f),
            centre + new Vector2(r, r * 0.72f),
            colour,
            Thickness);

        // The slab being pulled off it.
        drawList.AddRect(
            centre + new Vector2(-r * 0.62f, -r * 0.1f),
            centre + new Vector2(r * 0.62f, r * 0.42f),
            colour,
            2f,
            ImDrawFlags.None,
            Thickness);

        Vector2 tip = centre + new Vector2(0f, -r);
        drawList.AddLine(centre + new Vector2(0f, -r * 0.28f), tip, colour, Thickness);
        Arrowhead(drawList, tip, new Vector2(0f, -1f), r * 0.34f, colour);
    }

    /// <summary>Paint: a brush dab.</summary>
    public static void Paint(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        drawList.AddCircleFilled(centre + new Vector2(0f, r * 0.22f), r * 0.5f, colour);
        drawList.AddLine(
            centre + new Vector2(r * 0.28f, -r * 0.1f),
            centre + new Vector2(r * 0.78f, -r * 0.8f),
            colour,
            Thickness * 1.6f);
    }

    /// <summary>Loop Cut: a plane splitting a body in two.</summary>
    public static void Cut(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        drawList.AddRect(
            centre + new Vector2(-r * 0.75f, -r * 0.72f),
            centre + new Vector2(r * 0.75f, r * 0.72f),
            colour,
            2f,
            ImDrawFlags.None,
            Thickness);

        // A dashed line, because a solid one would read as an edge of the box rather than a cut.
        for (float x = -r; x < r; x += r * 0.32f)
        {
            drawList.AddLine(
                centre + new Vector2(x, 0f),
                centre + new Vector2(MathF.Min(x + r * 0.18f, r), 0f),
                colour,
                Thickness);
        }
    }

    // ---- Tool options ------------------------------------------------------------------------

    /// <summary>Rotate: a circular arrow.</summary>
    public static void Rotate(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        drawList.PathArcTo(centre, r * 0.72f, 0.6f, 5.2f, 24);
        drawList.PathStroke(colour, ImDrawFlags.None, Thickness);

        Vector2 end = centre + new Vector2(MathF.Cos(5.2f), MathF.Sin(5.2f)) * r * 0.72f;
        Arrowhead(drawList, end, new Vector2(MathF.Sin(5.2f), -MathF.Cos(5.2f)), r * 0.36f, colour);
    }

    /// <summary>Global space: a globe.</summary>
    public static void Global(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        float radius = r * 0.78f;
        drawList.AddCircle(centre, radius, colour, 20, Thickness);
        drawList.AddLine(centre + new Vector2(-radius, 0f), centre + new Vector2(radius, 0f), colour, Thickness);

        // Two meridians, drawn as ellipses so the circle reads as a sphere.
        for (float squash = 0.35f; squash <= 0.75f; squash += 0.4f)
        {
            drawList.PathClear();
            for (int i = 0; i <= 24; i++)
            {
                float a = i / 24f * MathF.Tau;
                drawList.PathLineTo(centre + new Vector2(MathF.Sin(a) * radius * squash, MathF.Cos(a) * radius));
            }

            drawList.PathStroke(colour, ImDrawFlags.None, Thickness * 0.8f);
        }
    }

    /// <summary>Local space: a small set of axes.</summary>
    public static void Local(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        Vector2 origin = centre + new Vector2(-r * 0.3f, r * 0.5f);

        Span<Vector2> directions = [new(0f, -1f), new(1f, -0.32f), new(-0.86f, 0.28f)];
        foreach (Vector2 direction in directions)
        {
            Vector2 tip = origin + direction * r * 1.05f;
            drawList.AddLine(origin, tip, colour, Thickness);
            Arrowhead(drawList, tip, Vector2.Normalize(direction), r * 0.3f, colour);
        }
    }

    /// <summary>Box selection: a marquee.</summary>
    public static void BoxSelect(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        float step = r * 0.4f;
        for (float t = -r; t < r; t += step)
        {
            float end = MathF.Min(t + step * 0.55f, r);
            drawList.AddLine(centre + new Vector2(t, -r), centre + new Vector2(end, -r), colour, Thickness);
            drawList.AddLine(centre + new Vector2(t, r), centre + new Vector2(end, r), colour, Thickness);
            drawList.AddLine(centre + new Vector2(-r, t), centre + new Vector2(-r, end), colour, Thickness);
            drawList.AddLine(centre + new Vector2(r, t), centre + new Vector2(r, end), colour, Thickness);
        }
    }

    /// <summary>Face selection: one whole filled face.</summary>
    public static void FaceSelect(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        drawList.AddRectFilled(centre - new Vector2(r * 0.8f), centre + new Vector2(r * 0.8f), colour, 2f);
    }

    /// <summary>Brush: a soft dab with a ring, matching the radius control it sits beside.</summary>
    public static void Brush(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        drawList.AddCircleFilled(centre, r * 0.36f, colour);
        drawList.AddCircle(centre, r * 0.82f, colour, 20, Thickness * 0.8f);
    }

    /// <summary>Bucket: a tipped pail.</summary>
    public static void Bucket(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        Vector2 topLeft = centre + new Vector2(-r * 0.72f, -r * 0.62f);
        Vector2 topRight = centre + new Vector2(r * 0.72f, -r * 0.62f);
        Vector2 bottomRight = centre + new Vector2(r * 0.36f, r * 0.72f);
        Vector2 bottomLeft = centre + new Vector2(-r * 0.36f, r * 0.72f);

        drawList.AddLine(topLeft, topRight, colour, Thickness);
        drawList.AddLine(topRight, bottomRight, colour, Thickness);
        drawList.AddLine(bottomRight, bottomLeft, colour, Thickness);
        drawList.AddLine(bottomLeft, topLeft, colour, Thickness);

        // A drip, so it is a bucket pouring rather than a plain trapezoid.
        drawList.AddCircleFilled(centre + new Vector2(r * 0.85f, r * 0.5f), r * 0.2f, colour);
    }

    /// <summary>Pattern: a chequer.</summary>
    public static void Pattern(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        float cell = r * 0.7f;
        Vector2 origin = centre - new Vector2(cell);

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                if ((x + y) % 2 != 0)
                {
                    continue;
                }

                Vector2 topLeft = origin + new Vector2(x * cell, y * cell);
                drawList.AddRectFilled(topLeft, topLeft + new Vector2(cell), colour);
            }
        }

        drawList.AddRect(origin, origin + new Vector2(cell * 2f), colour, 0f, ImDrawFlags.None, Thickness * 0.7f);
    }

    // ---- Overlays ----------------------------------------------------------------------------

    public static void Grid(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        for (int i = -1; i <= 1; i++)
        {
            float offset = i * r * 0.5f;
            drawList.AddLine(centre + new Vector2(offset, -r * 0.8f), centre + new Vector2(offset, r * 0.8f), colour, Thickness * 0.8f);
            drawList.AddLine(centre + new Vector2(-r * 0.8f, offset), centre + new Vector2(r * 0.8f, offset), colour, Thickness * 0.8f);
        }
    }

    /// <summary>Measurements: a ruler with ticks.</summary>
    public static void Measure(ImDrawListPtr drawList, Vector2 centre, float r, uint colour)
    {
        drawList.AddLine(centre + new Vector2(-r * 0.85f, r * 0.4f), centre + new Vector2(r * 0.85f, r * 0.4f), colour, Thickness);

        for (int i = 0; i < 4; i++)
        {
            float x = -r * 0.85f + i * r * 0.57f;
            float height = i % 2 == 0 ? r * 0.7f : r * 0.42f;
            drawList.AddLine(centre + new Vector2(x, r * 0.4f), centre + new Vector2(x, r * 0.4f - height), colour, Thickness * 0.8f);
        }
    }

    private static void Arrowhead(ImDrawListPtr drawList, Vector2 tip, Vector2 direction, float size, uint colour)
    {
        var side = new Vector2(-direction.Y, direction.X) * size * 0.55f;
        Vector2 back = tip - direction * size;

        drawList.AddTriangleFilled(tip, back + side, back - side, colour);
    }

    /// <summary>
    /// A square button that paints an icon over itself. The button carries the interaction and the
    /// theming; the painter only has to know its own shape.
    /// </summary>
    public static bool Button(string id, Painter painter, bool active, string tooltip, float size)
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

        Vector4 tint = active ? Theme.Text : hovered ? Theme.Text : Theme.TextDim;
        painter(
            ImGui.GetWindowDrawList(),
            topLeft + new Vector2(size * 0.5f),
            size * 0.3f,
            ImGui.ColorConvertFloat4ToU32(tint));

        if (hovered && tooltip.Length > 0)
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }
}
