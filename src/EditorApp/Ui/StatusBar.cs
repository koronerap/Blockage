using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The bottom strip: what the mouse does right now, what the cursor is over, what is focused, and
/// what a running drag is doing. Live readouts belong here rather than in a panel, because the eye
/// is already near the model.
/// </summary>
public static class StatusBar
{
    private const float HintGap = 14f;

    public static void Draw(ShellContext context)
    {
        EditorSession session = context.Session;

        ImGui.AlignTextToFramePadding();

        DrawHints(context);
        ImGui.SameLine(0f, 28f);

        if (context.Hover is { } hit)
        {
            ImGui.TextDisabled($"{hit.Voxel.X}, {hit.Voxel.Y}, {hit.Voxel.Z}");
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"· {hit.Face}");
        }

        // What is selected, and how big it is. The cursor's own coordinates answer "where am I";
        // this answers "how much have I got", which is the question a selection actually raises.
        //
        // Only while Extrude is the tool, for the same reason the outline is only drawn then: a
        // selection is kept when the tool is left so coming back finds it, and reporting a size
        // for something no longer on screen is worse than saying nothing.
        if (session.ActiveTool == EditorTool.Extrude
            && session.Selection is { IsEmpty: false } selection
            && selection.TryGetBounds(out Int3 min, out Int3 max))
        {
            Int3 size = max - min + Int3.One;

            ImGui.SameLine(0f, 20f);
            ImGui.TextColored(Theme.Highlight, $"{size.X} x {size.Y} x {size.Z}");
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"· {selection.Count:N0} faces");
        }

        if (session.SelectedLight is { } light)
        {
            ImGui.SameLine(0f, 20f);
            ImGui.TextColored(Theme.Highlight, light.Name);
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"· {light.Kind} light{(light.Visible ? string.Empty : ", off")}");
        }
        else if (session.Scene.Focus is { } focus)
        {
            ImGui.SameLine(0f, 20f);
            ImGui.TextColored(Theme.Highlight, focus.Name);
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"· {focus.Grid.SolidCount:N0} voxels");
        }

        // A drag's own number, in the one place the user is already looking.
        if (context.DragReadout.Length > 0)
        {
            ImGui.SameLine(0f, 20f);
            ImGui.TextColored(Theme.Highlight, context.DragReadout);
        }

        if (session.History.CanUndo)
        {
            ImGui.SameLine(0f, 20f);
            ImGui.TextDisabled($"Last: {session.History.NextUndoName}");
        }

        DrawRightAligned(context);
    }

    /// <summary>
    /// Blender's hint strip: a small mouse with the button that matters filled in, and what it does.
    /// It changes with the tool and with the modifier keys held, so what Shift would do is on screen
    /// the moment Shift is pressed.
    /// </summary>
    private static void DrawHints(ShellContext context)
    {
        if (!context.Preferences.MouseHints)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        IReadOnlyList<MouseHint> hints = MouseHints.For(context.Session, context.Looking, io.KeyShift, ControlKey.IsHeld(io), io.KeyAlt);

        float height = ImGui.GetFrameHeight();
        uint colour = ImGui.GetColorU32(ImGuiCol.TextDisabled);

        for (int i = 0; i < hints.Count; i++)
        {
            MouseHint hint = hints[i];

            if (i > 0)
            {
                ImGui.SameLine(0f, HintGap);
            }

            if (hint.Keys.Length > 0)
            {
                ImGui.TextDisabled(hint.Keys);
                ImGui.SameLine(0f, hint.Icon is null ? 6f : 2f);
            }

            if (hint.Icon is { } icon)
            {
                // Room for the icon, then the icon painted into it: a text line has no slot for a picture.
                ImGui.Dummy(new Vector2(height * 0.72f, height));
                Vector2 centre = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
                icon(new ImGuiIconCanvas(ImGui.GetWindowDrawList(), colour, 1.2f), centre, height * 0.3f);
                ImGui.SameLine(0f, 3f);
            }

            ImGui.TextDisabled(hint.Action);
        }
    }

    /// <summary>
    /// The latest report, at the right-hand end, fading out when its time is up. The frame and mesh
    /// counters that used to sit here are in the Statistics overlay.
    /// </summary>
    private static void DrawRightAligned(ShellContext context)
    {
        _ = context;
        ReportLog reports = ReportLog.Shared;
        if (reports.Current is not { } report)
        {
            return;
        }

        float width = ImGui.CalcTextSize(report.Text).X;
        float x = ImGui.GetWindowWidth() - width - ImGui.GetStyle().WindowPadding.X;

        // On a narrow window the readouts win: a report that would print over them waits its turn.
        float used = ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X;
        if (x < used + 20f)
        {
            return;
        }

        Vector4 colour = report.Kind switch
        {
            ReportKind.Error => Theme.Danger,
            ReportKind.Warning => Theme.Highlight,
            _ => Theme.Text with { W = 0.8f },
        };

        ImGui.SameLine(x);
        ImGui.TextColored(colour with { W = colour.W * reports.Opacity }, report.Text);
    }
}
