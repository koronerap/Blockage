using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Operations on the focused object's voxels as a whole: quarter turns and mirrors.
///
/// One list, drawn in two places — the Object menu in the menu bar, and the dropdown in the header —
/// so the two can never disagree about what is on offer. They used to be four loose buttons in the
/// header, which cost the width of four buttons at every moment for something done a few times a
/// session.
///
/// All of these change the voxels rather than the transform. A transform rotation is only a
/// placement; anything that reads the voxels — the exporters, a format with no field for rotation —
/// still sees the model the way it was built. These change what was built, exactly, because a
/// quarter turn or a mirror of a cubic lattice is a permutation and nothing is resampled.
/// </summary>
public static class ObjectMenu
{
    private static readonly (string Label, RotateDirection Direction)[] Turns =
    [
        ("Turn left", RotateDirection.Left),
        ("Turn right", RotateDirection.Right),
        ("Tip up", RotateDirection.Up),
        ("Tip down", RotateDirection.Down),
    ];

    private static readonly (string Label, Axis Axis)[] Flips =
    [
        ("Flip X  (left and right)", Axis.X),
        ("Flip Y  (top and bottom)", Axis.Y),
        ("Flip Z  (front and back)", Axis.Z),
    ];

    /// <summary>The items themselves, for whichever menu or popup is open around them.</summary>
    public static void DrawItems(EditorSession session)
    {
        bool hasVoxels = session.Scene.Focus is { IsEmpty: false };

        ImGui.TextDisabled(session.Scene.Focus?.Name ?? "No object");
        ImGui.Separator();

        foreach ((string label, RotateDirection direction) in Turns)
        {
            if (ImGui.MenuItem(label, null, false, hasVoxels))
            {
                session.RotateFocus(direction);
            }
        }

        ImGui.Separator();

        foreach ((string label, Axis axis) in Flips)
        {
            if (ImGui.MenuItem(label, null, false, hasVoxels))
            {
                session.FlipFocus(axis);
            }
        }

        // The axes are the object's own. Said once here rather than left to be discovered on a
        // turned object, where "X" and the screen's left and right no longer agree.
        ImGui.Separator();
        ImGui.TextDisabled("Along the object's own axes.");
    }
}
