using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Operations on the selected objects as a whole: duplicating, renaming, hiding and deleting them,
/// subdividing them, and quarter turns and mirrors of their voxels — each one undo step, however
/// many are selected.
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
    /// Duplicates everything selected and hands the copies to the Transform tool, ready to be dragged
    /// to where they go — the nearest thing to Blender's Shift+D, which starts moving them at once.
    /// </summary>
    public static void Duplicate(EditorSession session, FlyCamera camera)
    {
        bool made = session.InEditMode
            ? session.DuplicateSelectedVoxels(camera.Right)
            : session.DuplicateSelected(camera.Right).Count > 0;

        if (made)
        {
            session.ActiveTool = EditorTool.Transform;
            session.TransformMode = TransformMode.Move;
        }
    }

    /// <summary>
    /// Alt+D: linked copies of what is selected — sharing their voxels with the originals, so an
    /// edit to one is an edit to all — taken straight into a move, as a duplicate is.
    /// </summary>
    public static void DuplicateLinked(EditorSession session, FlyCamera camera)
    {
        if (session.InEditMode)
        {
            ReportLog.Shared.Post("Linked copies are of whole objects - Tab back to Object Mode first.", ReportKind.Warning);
            return;
        }

        if (session.DuplicateSelectedLinked(camera.Right).Count > 0)
        {
            session.ActiveTool = EditorTool.Transform;
            session.TransformMode = TransformMode.Move;
        }
    }

    /// <summary>Tab: into the active object, or out of it, saying why when it cannot.</summary>
    public static void ToggleEditMode(EditorSession session, ReportLog log)
    {
        if (session.ToggleEditMode())
        {
            return;
        }

        log.Post(
            session.SelectedLightId != 0 ? "A light has no voxels to edit - choose an object."
            : session.Scene.Focus is { Locked: true } locked ? $"{locked.Name} is locked - unlock it to edit it."
            : "Select an object to edit first.",
            ReportKind.Warning);
    }

    /// <summary>P: the chosen voxels into an object of their own.</summary>
    public static void Separate(EditorSession session, ReportLog log)
    {
        if (!session.InEditMode)
        {
            log.Post("P separates chosen voxels - Tab into the object first.", ReportKind.Warning);
            return;
        }

        int count = session.VoxelSelection.Count;
        if (session.SeparateSelectedVoxels() is { } piece)
        {
            log.Post($"Separated {count:N0} voxels into {piece.Name}.");
        }
        else
        {
            log.Post("Choose some voxels to separate first.", ReportKind.Warning);
        }
    }

    /// <summary>The chosen voxels in the colour in hand.</summary>
    public static void Fill(EditorSession session, ReportLog log)
    {
        if (!session.InEditMode || session.VoxelSelection.IsEmpty)
        {
            log.Post("Fill colours chosen voxels - Tab into an object and choose some.", ReportKind.Warning);
            return;
        }

        session.FillSelectedVoxels();
    }

    /// <summary>Locks everything selected, saying where to undo it.</summary>
    public static void LockSelected(EditorSession session, ReportLog log)
    {
        string? name = session.SelectedCount == 1 ? Selected(session).First().Name : null;
        if (session.LockSelected() is > 0 and var locked)
        {
            log.Post($"Locked {name ?? $"{locked} things"}. Unlock in the Outliner, or Alt+L for everything.");
        }
    }

    /// <summary>Ctrl+P with several selected: the rest go under the active object.</summary>
    public static void ParentSelected(EditorSession session, ReportLog log)
    {
        if (session.SelectedLightId != 0 || session.Scene.Focus is not { } parent)
        {
            log.Post("The active one has to be an object to be a parent - click the one to parent to last.", ReportKind.Warning);
            return;
        }

        int parented = session.ParentSelectedToActive();
        log.Post(parented > 0
            ? $"Parented {parented} to {parent.Name}."
            : $"Nothing more could go under {parent.Name}.", parented > 0 ? ReportKind.Info : ReportKind.Warning);
    }

    /// <summary>Ctrl+J: the selected objects' voxels go into the active object's.</summary>
    public static void JoinSelected(EditorSession session, ReportLog log)
    {
        if (session.SelectedLightId != 0 || session.Scene.Focus is not { } target || session.SelectedObjects.Count() < 2)
        {
            log.Post("Select two objects or more to join, the one to join into last.", ReportKind.Warning);
            return;
        }

        int joined = session.JoinSelectedIntoActive(out int refused);
        string skipped = refused > 0 ? $" {refused} could not: {(refused == 1 ? "it is" : "they are")} not on its lattice." : string.Empty;
        log.Post(joined > 0 ? $"Joined {joined} into {target.Name}.{skipped}" : $"Nothing joined into {target.Name}.{skipped}", refused > 0 ? ReportKind.Warning : ReportKind.Info);
    }

    /// <summary>A boolean of the other selected objects into the active one, saying what came of it.</summary>
    public static void Boolean(EditorSession session, BooleanOperation operation, ReportLog log)
    {
        string target = session.Scene.Focus?.Name ?? string.Empty;
        int used = session.BooleanSelected(operation, out string? problem);
        if (used == 0)
        {
            log.Post(problem ?? "Nothing to do.", ReportKind.Warning);
            return;
        }

        string verb = operation switch
        {
            BooleanOperation.Union => "Added",
            BooleanOperation.Difference => "Cut",
            _ => "Intersected",
        };
        string what = used == 1 ? "one object" : $"{used} objects";
        log.Post(operation == BooleanOperation.Intersect ? $"{verb} {target} with {what}." : $"{verb} {what} {(operation == BooleanOperation.Union ? "into" : "from")} {target}.");
    }

    /// <summary>
    /// Booleans with the other selected objects, and the filters and resampling of whole volumes —
    /// the menu's Volume part, shared by the menu bar, the right click and the Outliner.
    /// </summary>
    public static void DrawVolumeMenus(EditorSession session)
    {
        bool several = session.SelectedObjects.Skip(1).Any() && session.SelectedLightId == 0;
        if (ImGui.BeginMenu("Boolean", several && !session.InEditMode))
        {
            if (ImGui.MenuItem("Union"))
            {
                Boolean(session, BooleanOperation.Union, ReportLog.Shared);
            }

            if (ImGui.MenuItem("Difference"))
            {
                Boolean(session, BooleanOperation.Difference, ReportLog.Shared);
            }

            if (ImGui.MenuItem("Intersect"))
            {
                Boolean(session, BooleanOperation.Intersect, ReportLog.Shared);
            }

            ImGui.Separator();
            bool keep = session.BooleanKeepsOthers;
            if (ImGui.MenuItem("Keep the Others", null, ref keep))
            {
                session.BooleanKeepsOthers = keep;
            }

            ImGui.TextDisabled("Into the active object, from the rest selected.");
            ImGui.EndMenu();
        }

        if (!several && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Select the objects to use, then the one to change last.");
        }

        bool any = session.InEditMode || session.SelectedObjects.Any(o => !o.IsEmpty);
        if (ImGui.BeginMenu("Colours", any))
        {
            if (ImGui.MenuItem($"Replace {session.ActiveColorIndex} with {session.SecondaryColorIndex}"))
            {
                int changed = session.ReplaceColour(session.ActiveColorIndex, session.SecondaryColorIndex);
                ReportLog.Shared.Post(changed > 0 ? $"Replaced the colour on {changed:N0} voxels." : "Nothing is that colour.", changed > 0 ? ReportKind.Info : ReportKind.Warning);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("The colour in hand, everywhere in what is selected, becomes the second colour.\nSwap the two in the Paint tool's header.");
            }

            if (ImGui.MenuItem("Adjust Colours..."))
            {
                ColourAdjustWindow.Open();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Volume", any))
        {
            if (ImGui.MenuItem($"Hollow  (walls {session.HollowThickness} thick)"))
            {
                session.HollowSelected();
            }

            if (ImGui.MenuItem("Thicken"))
            {
                session.ThickenSelected();
            }

            if (ImGui.MenuItem("Thin"))
            {
                session.ThinSelected();
            }

            if (ImGui.MenuItem("Smooth"))
            {
                session.SmoothSelected();
            }

            if (ImGui.MenuItem($"Remove Loose Pieces  (under {session.LooseMinimum})"))
            {
                session.RemoveLooseSelected();
            }

            if (ImGui.MenuItem("Fill Enclosed"))
            {
                session.FillEnclosedSelected();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Fills what the model closes off from outside, in the colour in hand.");
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Halve Resolution"))
            {
                session.HalveSelected();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Subdivide's reverse: every 2 x 2 x 2 voxels become one of twice the size,\nin the colour most of them had. The object keeps its size and place.");
            }

            if (ImGui.MenuItem($"Scale x{session.ScaleFactor:0.##}"))
            {
                session.ScaleSelected();
            }

            ImGui.TextDisabled("The amounts are in Properties, under Volume.");
            ImGui.EndMenu();
        }
    }

    /// <summary>The selected objects and lights, objects first.</summary>
    private static IEnumerable<IPlaceable> Selected(EditorSession session) =>
        session.SelectedObjects.Cast<IPlaceable>().Concat(session.SelectedLights);

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

    /// <summary>Subdivides every selected object that can be, as one step, saying what came of it.</summary>
    public static void SubdivideSelected(EditorSession session, ReportLog log)
    {
        List<VoxelObject> chosen = [.. session.SelectedObjects];
        if (chosen.Count == 0)
        {
            return;
        }

        if (chosen.Count == 1 && session.SubdivideProblem(chosen[0]) is { } problem)
        {
            log.Post($"Cannot subdivide {chosen[0].Name}. {problem}", ReportKind.Warning);
            return;
        }

        int before = chosen.Sum(o => o.Grid.SolidCount);
        if (session.SubdivideSelected() is > 0 and var done)
        {
            int after = chosen.Sum(o => o.Grid.SolidCount);
            log.Post(chosen.Count == 1
                ? $"Subdivided {chosen[0].Name}: {before:N0} voxels became {after:N0}, each {chosen[0].VoxelSize:0.####} units."
                : $"Subdivided {done} of {chosen.Count}: {before:N0} voxels became {after:N0}.");
        }
        else
        {
            log.Post("None of the selected can be subdivided.", ReportKind.Warning);
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

    /// <summary>What is selected, in a few words: its name, how many, or that nothing is.</summary>
    public static string SelectionSummary(EditorSession session) => session.SelectedCount switch
    {
        0 => "Nothing selected",
        1 => Selected(session).First().Name,
        int count => $"{count} selected",
    };

    /// <summary>Inside an object: what can be done to the voxels chosen in it.</summary>
    public static void DrawVoxelItems(EditorSession session, FlyCamera camera)
    {
        VoxelSelection chosen = session.VoxelSelection;
        bool any = !chosen.IsEmpty;

        ImGui.TextDisabled(any ? $"{session.EditObject?.Name}  ·  {chosen.Count:N0} voxels" : $"{session.EditObject?.Name}  ·  no voxels chosen");
        ImGui.Separator();

        if (ImGui.MenuItem("Select All", Shortcut.Of(EditorAction.SelectAll)))
        {
            session.SelectAllVoxels();
        }

        if (ImGui.MenuItem("Select None", Shortcut.Of(EditorAction.DeselectAll), false, any))
        {
            session.DeselectAllVoxels();
        }

        if (ImGui.MenuItem("Invert", Shortcut.Of(EditorAction.InvertSelection)))
        {
            session.InvertVoxelSelection();
        }

        if (ImGui.MenuItem("Grow", Shortcut.Of(EditorAction.GrowSelection), false, any))
        {
            session.GrowVoxelSelection();
        }

        if (ImGui.MenuItem("Shrink", Shortcut.Of(EditorAction.ShrinkSelection), false, any))
        {
            session.ShrinkVoxelSelection();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Duplicate", Shortcut.Of(EditorAction.Duplicate), false, any))
        {
            Duplicate(session, camera);
        }

        if (ImGui.MenuItem("Fill with Colour", Shortcut.Of(EditorAction.FillSelection), false, any))
        {
            Fill(session, ReportLog.Shared);
        }

        if (ImGui.MenuItem("Separate", Shortcut.Of(EditorAction.Separate), false, any))
        {
            Separate(session, ReportLog.Shared);
        }

        if (ImGui.MenuItem("Delete", Shortcut.Of(EditorAction.Delete), false, any))
        {
            session.DeleteSelectedVoxels();
        }

        ImGui.Separator();
        DrawTurns(session);

        ImGui.Separator();
        if (ImGui.MenuItem("Back to Object Mode", Shortcut.Of(EditorAction.ToggleEditMode)))
        {
            session.ExitEditMode();
        }
    }

    /// <summary>The items themselves, for whichever menu or popup is open around them.</summary>
    public static void DrawItems(EditorSession session, FlyCamera camera)
    {
        if (session.InEditMode)
        {
            DrawVoxelItems(session, camera);
            return;
        }

        VoxelScene scene = session.Scene;
        bool any = session.SelectedCount > 0;
        List<VoxelObject> objects = [.. session.SelectedObjects];

        ImGui.TextDisabled(SelectionSummary(session));
        ImGui.Separator();

        if (ImGui.MenuItem("Duplicate", Shortcut.Of(EditorAction.Duplicate), false, any))
        {
            Duplicate(session, camera);
        }

        if (ImGui.MenuItem("Duplicate Linked", Shortcut.Of(EditorAction.DuplicateLinked), false, objects.Count > 0))
        {
            DuplicateLinked(session, camera);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Copies that share their voxels with the originals: an edit to one is an edit to all.");
        }

        if (scene.Focus is { } shared && objects.Contains(shared) && session.UsersOf(shared) > 1
            && ImGui.MenuItem("Make Single User"))
        {
            session.MakeSingleUser(shared.Id);
        }

        IPlaceable? active = (IPlaceable?)session.SelectedLight ?? scene.Focus;
        if (ImGui.MenuItem("Rename", Shortcut.Of(EditorAction.Rename), false, active is not null) && active is not null)
        {
            ObjectListPanel.StartRename(active);
        }

        if (ImGui.MenuItem("Hide", Shortcut.Of(EditorAction.Hide), false, any))
        {
            session.HideSelected();
        }

        if (ImGui.MenuItem("Show All", Shortcut.Of(EditorAction.ShowAll), false, scene.Objects.Any(o => !o.Visible)))
        {
            session.ShowAllObjects();
        }

        // Locked, they are let go; the Outliner's padlocks are the way back.
        if (ImGui.MenuItem("Lock", Shortcut.Of(EditorAction.Lock), false, any))
        {
            LockSelected(session, ReportLog.Shared);
        }

        if (ImGui.MenuItem("Unlock All", Shortcut.Of(EditorAction.UnlockAll), false, scene.Objects.Any(o => o.Locked) || scene.Lights.Any(l => l.Locked)))
        {
            session.UnlockAll();
        }

        if (ImGui.MenuItem("Delete", Shortcut.Of(EditorAction.Delete), false, any))
        {
            session.DeleteSelected();
        }

        // Several: into the active one, Blender's Ctrl+J and Ctrl+P. One: a list to choose from.
        if (objects.Count > 1)
        {
            if (ImGui.MenuItem("Join into Active", Shortcut.Of(EditorAction.Join), false, session.SelectedLightId == 0))
            {
                JoinSelected(session, ReportLog.Shared);
            }
        }
        else if (objects.Count == 1)
        {
            DrawJoinMenu(session, objects[0]);
        }

        if (session.SelectedCount > 1)
        {
            if (ImGui.MenuItem("Parent to Active", Shortcut.Of(EditorAction.SetParent), false, session.SelectedLightId == 0))
            {
                ParentSelected(session, ReportLog.Shared);
            }
        }
        else if (Selected(session).FirstOrDefault() is { } only)
        {
            ParentMenu.DrawSubmenu(session, only);
        }

        ImGui.Separator();
        DrawVolumeMenus(session);

        string? subdivideProblem = objects.Count == 0
            ? "Nothing is selected."
            : objects.Any(o => session.SubdivideProblem(o) is null) ? null : session.SubdivideProblem(objects[0]);
        if (ImGui.MenuItem("Subdivide", Shortcut.Of(EditorAction.Subdivide), false, subdivideProblem is null))
        {
            SubdivideSelected(session, ReportLog.Shared);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(subdivideProblem ?? SubdivideTip);
        }

        ImGui.Separator();

        DrawTurns(session);
    }

    /// <summary>The quarter turns and the mirrors of the selected objects' voxels — or of the chosen voxels, in Edit Mode.</summary>
    public static void DrawTurns(EditorSession session)
    {
        bool voxels = session.InEditMode;
        bool hasVoxels = voxels ? !session.VoxelSelection.IsEmpty : session.SelectedObjects.Any(o => !o.IsEmpty);

        foreach ((string label, RotateDirection direction) in Turns)
        {
            if (ImGui.MenuItem(label, null, false, hasVoxels))
            {
                if (voxels)
                {
                    // Right is a quarter about the object's up, Up a quarter about its X, as the object turns do.
                    (Axis axis, int turns) = direction switch
                    {
                        RotateDirection.Right => (Axis.Y, 1),
                        RotateDirection.Left => (Axis.Y, -1),
                        RotateDirection.Up => (Axis.X, 1),
                        _ => (Axis.X, -1),
                    };
                    session.RotateSelectedVoxels(axis, turns);
                }
                else
                {
                    session.RotateSelected(direction);
                }
            }
        }

        ImGui.Separator();

        foreach ((string label, Axis axis) in Flips)
        {
            if (ImGui.MenuItem(label, null, false, hasVoxels))
            {
                if (voxels)
                {
                    session.MirrorSelectedVoxels(axis);
                }
                else
                {
                    session.FlipSelected(axis);
                }
            }
        }

        // The axes are the object's own. Said once here rather than left to be discovered on a
        // turned object, where "X" and the screen's left and right no longer agree.
        ImGui.Separator();
        ImGui.TextDisabled("Along the object's own axes.");
    }
}
