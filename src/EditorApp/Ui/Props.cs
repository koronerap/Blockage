using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Blender's property layout for the Properties tabs: a label on the left, right-aligned against a
/// column, and the value filling the rest of the row. The eye runs down the labels to find a setting
/// and across to its value — where a value with its name printed inside a full-width bar has to be
/// read whole every time.
///
/// Rarely used settings go in sections that start closed; sections are slim, a chevron and a word,
/// rather than the full-width bars they replace.
/// </summary>
public static class Props
{
    /// <summary>Share of the row the labels take, as in Blender's split layout.</summary>
    private const float LabelShare = 0.4f;

    /// <summary>Between a label and its value.</summary>
    private const float LabelGap = 8f;

    private static readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = [];

    /// <summary>
    /// Where the value of a row was drawn in the last frame, by its id. What lets a test aim at a
    /// field the way a hand would, without knowing the layout's arithmetic.
    /// </summary>
    public static (Vector2 Min, Vector2 Max)? RectOf(string id) => Rects.TryGetValue(id, out var rect) ? rect : null;

    /// <summary>
    /// Starts a row: the label right-aligned in the left column, and the cursor left at the value
    /// column with the next item set to fill it.
    /// </summary>
    public static void Label(string label)
    {
        float start = ImGui.GetCursorPosX();
        float width = ImGui.GetContentRegionAvail().X;
        float column = MathF.Floor(width * LabelShare);

        ImGui.AlignTextToFramePadding();

        if (label.Length > 0)
        {
            float text = ImGui.CalcTextSize(label).X;
            ImGui.SetCursorPosX(start + MathF.Max(column - text - LabelGap, 0f));
            ImGui.TextColored(Theme.Text with { W = 0.72f }, label);
            ImGui.SameLine(start + column);
        }
        else
        {
            ImGui.SetCursorPosX(start + column);
        }

        ImGui.SetNextItemWidth(-1f);
    }

    private static void Remember(string id) => Rects[id] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

    public static bool Float(string label, string id, ref float value, float speed, float min, float max, string format = "%.3g")
    {
        Label(label);
        bool changed = ImGui.DragFloat($"##{id}", ref value, speed, min, max, format, ImGuiSliderFlags.AlwaysClamp);
        Remember(id);
        return changed;
    }

    public static bool Slider(string label, string id, ref float value, float min, float max, string format)
    {
        Label(label);
        bool changed = ImGui.SliderFloat($"##{id}", ref value, min, max, format, ImGuiSliderFlags.AlwaysClamp);
        Remember(id);
        return changed;
    }

    public static bool Int(string label, string id, ref int value, float speed, int min, int max, string format)
    {
        Label(label);
        bool changed = ImGui.DragInt($"##{id}", ref value, speed, min, max, format, ImGuiSliderFlags.AlwaysClamp);
        Remember(id);
        return changed;
    }

    /// <summary>A switch with its name beside it in the value column, as Blender lays out a checkbox.</summary>
    public static bool Check(string label, string id, string text, ref bool value)
    {
        Label(label);
        bool changed = ImGui.Checkbox($"{text}##{id}", ref value);
        Remember(id);
        return changed;
    }

    /// <summary>A read-only value.</summary>
    public static void Value(string label, string text)
    {
        Label(label);
        ImGui.TextUnformatted(text);
    }

    /// <summary>A value the reader should notice — a warning, a limit reached.</summary>
    public static void Note(string label, string text, Vector4 colour)
    {
        Label(label);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(colour, text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>
    /// Three rows for one vector: the name on the first, just the axis letters under it — Blender's
    /// "Location X / Y / Z". Returns true when any of them changed.
    /// </summary>
    public static bool Vector(string label, string id, ref Vector3 value, float speed, string format = "%.3g")
    {
        bool changed = false;

        Label($"{label} X");
        changed |= ImGui.DragFloat($"##{id}-x", ref value.X, speed, 0f, 0f, format);
        Remember($"{id}-x");

        Label("Y");
        changed |= ImGui.DragFloat($"##{id}-y", ref value.Y, speed, 0f, 0f, format);
        Remember($"{id}-y");

        Label("Z");
        changed |= ImGui.DragFloat($"##{id}-z", ref value.Z, speed, 0f, 0f, format);
        Remember($"{id}-z");

        return changed;
    }

    /// <summary>
    /// A choice laid out as a row of buttons, the chosen one lit — for a handful of options that define
    /// what the panel is about, where a dropdown would hide the others. Returns the choice.
    /// </summary>
    public static int Choice(string label, string id, ReadOnlySpan<(Icons.Painter? Icon, string Name)> options, int selected)
    {
        Label(label);

        float available = ImGui.GetContentRegionAvail().X;
        const float Gap = 2f;
        float width = (available - (Gap * (options.Length - 1))) / options.Length;
        float height = ImGui.GetFrameHeight();
        int chosen = selected;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Gap, ImGui.GetStyle().ItemSpacing.Y));

        for (int i = 0; i < options.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            bool on = i == selected;
            if (on)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHovered);
            }

            (Icons.Painter? icon, string name) = options[i];

            // The text is drawn by hand so an icon can sit in front of it.
            if (ImGui.Button($"##{id}-{i}", new Vector2(width, height)))
            {
                chosen = i;
            }

            Remember($"{id}-{i}");
            DrawButtonFace(icon, name, on);

            if (on)
            {
                ImGui.PopStyleColor(2);
            }
        }

        ImGui.PopStyleVar();
        return chosen;
    }

    private static void DrawButtonFace(Icons.Painter? icon, string name, bool on)
    {
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint colour = ImGui.ColorConvertFloat4ToU32(on ? Theme.Text : Theme.Text with { W = 0.8f });

        float height = max.Y - min.Y;
        float iconSize = icon is null ? 0f : height * 0.62f;
        float text = ImGui.CalcTextSize(name).X;

        // Too narrow for both: the icon alone says it, and the name is in the tooltip.
        bool showText = text + iconSize + 12f <= max.X - min.X;
        float content = (showText ? text : 0f) + iconSize + (icon is not null && showText ? 5f : 0f);
        float x = min.X + (((max.X - min.X) - content) * 0.5f);

        if (icon is not null)
        {
            icon(new ImGuiIconCanvas(drawList, colour, 1.3f), new Vector2(x + (iconSize * 0.5f), min.Y + (height * 0.5f)), iconSize * 0.48f);
            x += iconSize + 5f;
        }

        if (showText)
        {
            drawList.AddText(new Vector2(x, min.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f)), colour, name);
        }
        else if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(name);
        }
    }

    /// <summary>
    /// A slim, collapsible section: a chevron and its title on a faint bar, the contents not indented.
    /// Returns whether it is open; nothing needs closing afterwards.
    /// </summary>
    public static bool Section(string title, bool openByDefault = true)
    {
        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.SurfaceRaised with { W = 0.55f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.ControlHovered with { W = 0.6f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.ControlHovered with { W = 0.75f });

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Framed
            | ImGuiTreeNodeFlags.NoTreePushOnOpen
            | ImGuiTreeNodeFlags.SpanAvailWidth
            | (openByDefault ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);

        bool open = ImGui.TreeNodeEx(title, flags);

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        return open;
    }

    /// <summary>Buttons in the value column, sharing its width.</summary>
    public static int Buttons(string label, string id, params string[] names)
    {
        Label(label);

        float available = ImGui.GetContentRegionAvail().X;
        const float Gap = 4f;
        float width = (available - (Gap * (names.Length - 1))) / names.Length;
        int pressed = -1;

        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, Gap);
            }

            if (ImGui.Button($"{names[i]}##{id}-{i}", new Vector2(width, 0f)))
            {
                pressed = i;
            }

            Remember($"{id}-{i}");
        }

        return pressed;
    }
}
