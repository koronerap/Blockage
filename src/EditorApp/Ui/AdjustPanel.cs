using System.Numerics;
using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Blender's Adjust Last Operation: once a shape is added, its numbers at the viewport's bottom left,
/// to change for as long as nothing else has been done. Each change remakes the shape where it
/// stands, as part of the one step that added it; anything else done, and the panel is gone.
/// </summary>
public static class AdjustPanel
{
    private static bool _folded;

    /// <summary>How tall it was drawn last, so what stacks over it can make room. Zero when not shown.</summary>
    public static float Height { get; private set; }

    /// <param name="bottom">The panel's bottom edge, on screen: over whatever is already in the viewport's corner.</param>
    public static void Draw(EditorSession session, Vector2 left, float bottom)
    {
        Height = 0f;

        if (session.LastShape is not { } shape)
        {
            return;
        }

        ShapeKind kind = shape.Settings.Kind;
        IReadOnlyList<ShapeField> fields = Shapes.FieldsOf(kind);
        if (fields.Count == 0 && !Shapes.CanBeHollow(kind))
        {
            return;
        }

        float width = MathF.Round(ImGui.GetFontSize() * 16f);
        ImGui.SetNextWindowPos(new Vector2(left.X, bottom), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowSize(new Vector2(width, 0f));
        ImGui.SetNextWindowBgAlpha(0.92f);

        const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar;

        if (ImGui.Begin("##adjust-last", Flags))
        {
            DrawHeader($"Add {Shapes.NameOf(kind)}");

            if (!_folded)
            {
                ShapeSettings settings = shape.Settings;

                // Lettering says something: what, first, typed straight in.
                if (kind == ShapeKind.Text)
                {
                    string text = settings.Text;
                    Props.Label("Text");
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputTextMultiline("##adjust-text", ref text, ShapeSettings.MaxTextLength, new System.Numerics.Vector2(-1f, ImGui.GetTextLineHeight() * 3.2f)))
                    {
                        settings = settings with { Text = text };
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Capitals, digits and the common marks, Turkish letters too. Enter starts a new line.");
                    }
                }

                for (int i = 0; i < fields.Count; i++)
                {
                    ShapeField field = fields[i];
                    int value = settings[i];
                    if (Props.Int(field.Name, $"adjust-{i}", ref value, 0.08f, field.Min, field.Max, "%d"))
                    {
                        settings = settings.With(i, value);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{field.Tooltip}\n{field.Min} to {field.Max}. Drag, or Ctrl+click to type.");
                    }
                }

                if (Shapes.CanBeHollow(kind))
                {
                    bool hollow = settings.Hollow;
                    if (Props.Check(string.Empty, "adjust-hollow", "Hollow", ref hollow))
                    {
                        settings = settings with { Hollow = hollow };
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Only a shell one voxel thick.");
                    }
                }

                if (!settings.Equals(shape.Settings))
                {
                    session.ReshapeLast(settings);
                }
            }

            Height = ImGui.GetWindowHeight();
        }

        ImGui.End();
    }

    /// <summary>A chevron and what was done, folding the numbers away when clicked, as Blender's does.</summary>
    private static void DrawHeader(string title)
    {
        float row = ImGui.GetFrameHeight();
        if (ImGui.InvisibleButton("##adjust-fold", new Vector2(ImGui.GetContentRegionAvail().X, row)))
        {
            _folded = !_folded;
        }

        Vector2 min = ImGui.GetItemRectMin();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint colour = ImGui.ColorConvertFloat4ToU32(ImGui.IsItemHovered() ? Theme.Text : Theme.Text with { W = 0.8f });

        Icons.Painter chevron = _folded ? Icons.ChevronRight : Icons.ChevronDown;
        chevron(new ImGuiIconCanvas(drawList, colour, 1.3f), min + new Vector2(row * 0.4f, row * 0.5f), row * 0.24f);
        drawList.AddText(min + new Vector2(row * 0.85f, (row - ImGui.GetTextLineHeight()) * 0.5f), colour, title);
    }
}
