using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Operations on the focused object as a whole: duplicating, renaming, hiding and deleting it, and
/// quarter turns and mirrors of its voxels.
///
/// One list, drawn in two places — the Object menu in the menu bar, and the dropdown in the header —
/// so the two can never disagree about what is on offer. They used to be four loose buttons in the
/// header, which cost the width of four buttons at every moment for something done a few times a
/// session.
///
/// The turns and mirrors change the voxels rather than the transform. A transform rotation is only a
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

    /// <summary>
    /// Duplicates the focused object and hands the copy to the Transform tool, ready to be dragged to
    /// where it goes — the nearest thing to Blender's Shift+D, which starts moving the copy at once.
    /// </summary>
    public static void Duplicate(EditorSession session, FlyCamera camera)
    {
        if (session.DuplicateFocus(camera.Right) is not null)
        {
            session.ActiveTool = EditorTool.Transform;
            session.TransformMode = TransformMode.Move;
        }
    }

    /// <summary>The items themselves, for whichever menu or popup is open around them.</summary>
    public static void DrawItems(EditorSession session, FlyCamera camera)
    {
        VoxelScene scene = session.Scene;
        bool hasVoxels = scene.Focus is { IsEmpty: false };

        ImGui.TextDisabled(scene.Focus?.Name ?? "No object");
        ImGui.Separator();

        if (ImGui.MenuItem("Duplicate", "Shift+D", false, hasVoxels))
        {
            Duplicate(session, camera);
        }

        if (ImGui.MenuItem("Rename", "F2", false, scene.Focus is not null) && scene.Focus is { } focus)
        {
            ObjectListPanel.StartRename(focus);
        }

        if (ImGui.MenuItem("Hide", "H", false, scene.Focus is { Visible: true }))
        {
            session.SetObjectVisible(scene.FocusId, false);
        }

        if (ImGui.MenuItem("Show All", "Alt+H", false, scene.Objects.Any(o => !o.Visible)))
        {
            session.ShowAllObjects();
        }

        if (ImGui.MenuItem("Delete", "Del", false, scene.Objects.Count > 1))
        {
            session.DeleteObject(scene.FocusId);
        }

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
