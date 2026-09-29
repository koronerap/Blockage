using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Snapping, as Blender's header has it: a magnet that turns it on, and beside it a button showing
/// what it snaps to, opening the rest. Only in the Transform tool's header, the one tool that moves
/// things freely enough to need it.
/// </summary>
public static class SnapMenu
{
    private const string PopupId = "##snapping";

    private static readonly (SnapTarget Target, Icons.Painter Icon, string Name, string Help)[] Targets =
    [
        (SnapTarget.Increment, Icons.SnapIncrement, "Increment", "Whole voxels of the object being moved"),
        (SnapTarget.Corner, Icons.SnapCorner, "Corner", "The corners of other objects' boxes"),
        (SnapTarget.EdgeCentre, Icons.SnapEdge, "Edge centre", "The middles of their box edges"),
        (SnapTarget.Surface, Icons.SnapSurface, "Surface", "The face under the cursor: set it down there"),
    ];

    public static void DrawButtons(SnapSettings snap, float size)
    {
        string key = Shortcut.Hint(EditorAction.ToggleSnap);
        if (IconButton.Draw(
                "snap",
                Icons.Magnet,
                snap.Enabled,
                snap.Enabled
                    ? $"Snapping on{key}\nHold Shift while dragging to move freely"
                    : $"Snapping off{key}\nHold Shift while dragging to snap",
                size))
        {
            snap.Enabled = !snap.Enabled;
        }

        ImGui.SameLine(0f, 1f);

        if (IconButton.Draw("snap-to", IconFor(snap), active: false, $"Snap to: {Describe(snap)}", size, hasAlternatives: true))
        {
            ImGui.OpenPopup(PopupId);
        }

        if (ImGui.BeginPopup(PopupId))
        {
            DrawItems(snap);
            ImGui.EndPopup();
        }
    }

    /// <summary>The first target switched on stands for them all on the button, as Blender's does.</summary>
    private static Icons.Painter IconFor(SnapSettings snap)
    {
        foreach ((SnapTarget target, Icons.Painter icon, _, _) in Targets)
        {
            if (snap.Snaps(target))
            {
                return icon;
            }
        }

        return Icons.Magnet;
    }

    private static string Describe(SnapSettings snap)
    {
        string[] names = [.. Targets.Where(t => snap.Snaps(t.Target)).Select(t => t.Name.ToLowerInvariant())];
        return names.Length == 0 ? "nothing" : string.Join(", ", names);
    }

    /// <summary>The popover: what to snap to, with what, and to which kinds of change.</summary>
    public static void DrawItems(SnapSettings snap)
    {
        ImGui.TextDisabled("Snapping");
        ImGui.Separator();

        ImGui.TextUnformatted("Snap to");
        foreach ((SnapTarget target, Icons.Painter icon, string name, string help) in Targets)
        {
            bool on = snap.Snaps(target);
            if (ImGui.Checkbox($"{name}##snap-{target}", ref on))
            {
                snap.Set(target, on);
            }

            Tip(help);

            if (target == SnapTarget.Increment && snap.Snaps(SnapTarget.Increment))
            {
                ImGui.Indent();
                bool absolute = snap.AbsoluteGrid;
                if (ImGui.Checkbox("Absolute grid", ref absolute))
                {
                    snap.AbsoluteGrid = absolute;
                }

                Tip("On the world's lattice, rather than in whole steps from where the drag began.");
                ImGui.Unindent();
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Snap with");
        int based = (int)snap.Base;
        ImGui.RadioButton("Closest", ref based, (int)SnapBase.Closest);
        Tip("The moved thing's corner nearest the target, or its side that faces a surface.");
        ImGui.SameLine();
        ImGui.RadioButton("Center", ref based, (int)SnapBase.Center);
        Tip("The middle of its box.");
        ImGui.SameLine();
        ImGui.RadioButton("Origin", ref based, (int)SnapBase.Origin);
        Tip("Its origin.");
        snap.Base = (SnapBase)based;

        ImGui.Spacing();
        bool excludeLocked = snap.ExcludeLocked;
        if (ImGui.Checkbox("Exclude locked objects", ref excludeLocked))
        {
            snap.ExcludeLocked = excludeLocked;
        }

        bool align = snap.AlignToSurface;
        if (ImGui.Checkbox("Align rotation to surface", ref align))
        {
            snap.AlignToSurface = align;
        }

        Tip("Set down on a wall, it stands out from the wall.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Affect");
        bool move = snap.AffectMove;
        if (ImGui.Checkbox("Move", ref move))
        {
            snap.AffectMove = move;
        }

        ImGui.SameLine();
        bool rotate = snap.AffectRotate;
        if (ImGui.Checkbox("Rotate", ref rotate))
        {
            snap.AffectRotate = rotate;
        }

        float increment = snap.RotationIncrement;
        ImGui.SetNextItemWidth(140f);
        if (ImGui.SliderFloat("Rotation increment", ref increment, SnapSettings.MinRotationIncrement, SnapSettings.MaxRotationIncrement, "%.0f°", ImGuiSliderFlags.AlwaysClamp))
        {
            snap.RotationIncrement = MathF.Round(increment);
        }

        ImGui.Spacing();
        ImGui.TextDisabled(snap.Enabled ? "Hold Shift while dragging to move freely." : "Hold Shift while dragging to snap.");
    }

    private static void Tip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
