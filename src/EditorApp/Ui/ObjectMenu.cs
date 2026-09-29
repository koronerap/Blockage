using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Operations on the focused object as a whole: duplicating, renaming, hiding and deleting it,
/// subdividing it, and quarter turns and mirrors of its voxels.
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

    /// <summary>Pastes as a new object and hands it to the Transform tool, the same as a duplicate.</summary>
    public static VoxelObject? Paste(EditorSession session, FlyCamera camera)
    {
        if (session.Paste(camera.Right) is not { } pasted)
        {
            return null;
        }

        session.ActiveTool = EditorTool.Transform;
        session.TransformMode = TransformMode.Move;
        return pasted;
    }

    /// <summary>
    /// Subdivides the focused object and says what came of it in the status bar — the count is the
    /// part worth knowing, since it goes up eightfold each time.
    /// </summary>
    public static void SubdivideFocus(EditorSession session, ReportLog log)
    {
        if (session.Scene.Focus is not { } focus)
        {
            return;
        }

        if (session.SubdivideProblem(focus) is { } problem)
        {
            log.Post($"Cannot subdivide {focus.Name}. {problem}", ReportKind.Warning);
            return;
        }

        int before = focus.Grid.SolidCount;
        if (session.SubdivideFocus())
        {
            log.Post($"Subdivided {focus.Name}: {before:N0} voxels became {focus.Grid.SolidCount:N0}, each {focus.VoxelSize:0.####} units.");
        }
    }

    /// <summary>What Subdivide does, for the menu item and the button alike.</summary>
    public const string SubdivideTip =
        "Cuts every voxel into 2 × 2 × 2 of half the size.\n"
        + "The object keeps its size and place in the world and its colours;\n"
        + "it holds eight times the voxels, room for finer detail. Ctrl+Z undoes it.";

    /// <summary>
    /// "Join into", listing every other object. The ones it cannot join are shown but greyed, with
    /// the reason on hover — a missing entry would leave the user guessing why.
    /// </summary>
    public static void DrawJoinMenu(EditorSession session, VoxelObject source)
    {
        if (!ImGui.BeginMenu("Join Into", session.Scene.Objects.Count > 1))
        {
            return;
        }

        foreach (VoxelObject target in session.Scene.Objects)
        {
            if (target.Id == source.Id)
            {
                continue;
            }

            string? problem = session.JoinProblem(source.Id, target.Id);
            if (ImGui.MenuItem($"{target.Name}##join-{target.Id}", null, false, problem is null))
            {
                ClipboardActions.Join(session, source, target, ReportLog.Shared);
            }

            if (problem is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(problem);
            }
        }

        ImGui.EndMenu();
    }

    /// <summary>The items themselves, for whichever menu or popup is open around them.</summary>
    public static void DrawItems(EditorSession session, FlyCamera camera)
    {
        VoxelScene scene = session.Scene;
        bool hasVoxels = scene.Focus is { IsEmpty: false };

        ImGui.TextDisabled(scene.Focus?.Name ?? "No object");
        ImGui.Separator();

        if (ImGui.MenuItem("Duplicate", Shortcut.Of(EditorAction.Duplicate), false, hasVoxels))
        {
            Duplicate(session, camera);
        }

        if (ImGui.MenuItem("Rename", Shortcut.Of(EditorAction.Rename), false, scene.Focus is not null) && scene.Focus is { } focus)
        {
            ObjectListPanel.StartRename(focus);
        }

        if (ImGui.MenuItem("Hide", Shortcut.Of(EditorAction.Hide), false, scene.Focus is { Visible: true }))
        {
            session.SetObjectVisible(scene.FocusId, false);
        }

        if (ImGui.MenuItem("Show All", Shortcut.Of(EditorAction.ShowAll), false, scene.Objects.Any(o => !o.Visible)))
        {
            session.ShowAllObjects();
        }

        // Locking the focused object moves focus on; the Outliner's padlock is the way back.
        if (ImGui.MenuItem("Lock", Shortcut.Of(EditorAction.Lock), false, scene.Focus is { Locked: false }) && scene.Focus is { } locked)
        {
            session.SetObjectLocked(locked.Id, true);
        }

        if (ImGui.MenuItem("Unlock All", Shortcut.Of(EditorAction.UnlockAll), false, scene.Objects.Any(o => o.Locked) || scene.Lights.Any(l => l.Locked)))
        {
            session.UnlockAll();
        }

        if (ImGui.MenuItem("Delete", Shortcut.Of(EditorAction.Delete), false, scene.Objects.Count > 1))
        {
            session.DeleteObject(scene.FocusId);
        }

        if (scene.Focus is { } joined)
        {
            DrawJoinMenu(session, joined);
        }

        ImGui.Separator();

        string? subdivideProblem = session.SubdivideProblem(scene.Focus);
        if (ImGui.MenuItem("Subdivide", Shortcut.Of(EditorAction.Subdivide), false, subdivideProblem is null))
        {
            SubdivideFocus(session, ReportLog.Shared);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(subdivideProblem ?? SubdivideTip);
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
