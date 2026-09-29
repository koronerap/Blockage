using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>The menu bar and the status line under it.</summary>
public static class MainMenu
{
    /// <summary>Draws the menu bar and returns its height, so the shell can lay out beneath it.</summary>
    public static float Draw(
        EditorSession session,
        ProjectController project,
        ExportController export,
        MimicraftController mimicraft,
        ViewActions view,
        Action onExit)
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return 0f;
        }

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New", Shortcut.Of(EditorAction.NewLevel)))
            {
                project.NewProject();
            }

            if (ImGui.MenuItem("Open...", Shortcut.Of(EditorAction.Open)))
            {
                project.OpenProject();
            }

            DrawRecentMenu(project);

            // Only enabled when an earlier session left autosaved work behind. Offered once at
            // start; this is the way back to it after "Decide later".
            if (ImGui.MenuItem("Recover Unsaved Work...", null, false, project.CanRecover))
            {
                project.OfferRecovery();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Save", Shortcut.Of(EditorAction.Save)))
            {
                project.Save();
            }

            if (ImGui.MenuItem("Save As...", Shortcut.Of(EditorAction.SaveAs)))
            {
                project.SaveAs();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Export mesh...", Shortcut.Of(EditorAction.Export)))
            {
                export.Show();
            }

            // Kept apart from the mesh export on purpose: nothing here is meshed and no texture is
            // written. The voxels themselves go out, in the layout Mimicraft's own decoders read.
            if (ImGui.MenuItem("Save for Mimicraft..."))
            {
                mimicraft.Show();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Exit"))
            {
                onExit();
            }

            ImGui.EndMenu();
        }

        DrawViewMenu(view);

        if (ImGui.BeginMenu("Edit"))
        {
            if (ImGui.MenuItem($"Undo {session.History.NextUndoName ?? string.Empty}", Shortcut.Of(EditorAction.Undo), false, session.History.CanUndo))
            {
                session.Undo();
            }

            if (ImGui.MenuItem($"Redo {session.History.NextRedoName ?? string.Empty}", Shortcut.Of(EditorAction.Redo), false, session.History.CanRedo))
            {
                session.Redo();
            }

            ImGui.Separator();

            // What a copy takes is worth saying before it is taken: the selection, or everything.
            string what = session.CopiesSelection ? "Selection" : "Object";

            if (ImGui.MenuItem($"Copy {what}", Shortcut.Of(EditorAction.Copy), false, session.Scene.Focus is { IsEmpty: false }))
            {
                ClipboardActions.Copy(session, ReportLog.Shared);
            }

            if (ImGui.MenuItem($"Cut {what}", Shortcut.Of(EditorAction.Cut), false, session.Scene.Focus is { IsEmpty: false }))
            {
                ClipboardActions.Cut(session, ReportLog.Shared);
            }

            string paste = session.Clipboard is { } clipboard ? $"Paste {clipboard.Grid.SolidCount:N0} voxels" : "Paste";
            if (ImGui.MenuItem(paste, Shortcut.Of(EditorAction.Paste), false, session.Clipboard is not null))
            {
                ClipboardActions.Paste(session, view.Camera, ReportLog.Shared);
            }

            ImGui.Separator();

            // Where Blender keeps it: the settings of the editor, not of the level.
            if (ImGui.MenuItem("Preferences...", Shortcut.Of(EditorAction.Preferences)))
            {
                PreferencesWindow.Open();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Object"))
        {
            ObjectMenu.DrawItems(session, view.Camera);
            ImGui.EndMenu();
        }

        // Lights are the only thing there is to add: voxels come from extruding what is already there.
        if (ImGui.BeginMenu("Add"))
        {
            LightMenu.DrawItems(session);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("Keyboard Shortcuts", Shortcut.Of(EditorAction.ShortcutSheet), ShortcutSheet.IsOpen))
            {
                ShortcutSheet.Toggle();
            }

            ImGui.EndMenu();
        }

        float height = ImGui.GetWindowSize().Y;
        ImGui.EndMainMenuBar();
        return height;
    }

    private static void DrawViewMenu(ViewActions view)
    {
        if (!ImGui.BeginMenu("View"))
        {
            return;
        }

        if (ImGui.MenuItem("Frame Level", Shortcut.Of(EditorAction.FrameLevel)))
        {
            view.FrameLevel();
        }

        if (ImGui.MenuItem("Frame Focused Object", Shortcut.Of(EditorAction.FrameFocused)))
        {
            view.FrameFocused();
        }

        if (ImGui.MenuItem("Move View to Center"))
        {
            view.LookAtCenter();
        }

        if (ImGui.MenuItem("Reset Camera"))
        {
            view.ResetCamera();
        }

        ImGui.Separator();

        DrawAlignMenu(view.Camera);

        if (ImGui.MenuItem("Orthographic", Shortcut.Of(EditorAction.ToggleOrthographic), view.Camera.Orthographic))
        {
            view.Camera.Orthographic = !view.Camera.Orthographic;
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Ground Grid", Shortcut.Of(EditorAction.ToggleGrid), view.GridVisible()))
        {
            view.ToggleGrid();
        }

        if (ImGui.MenuItem("Measurements", Shortcut.Of(EditorAction.ToggleMeasurements), view.MeasurementsVisible()))
        {
            view.ToggleMeasurements();
        }

        if (ImGui.MenuItem("Statistics", null, view.StatisticsVisible()))
        {
            view.ToggleStatistics();
        }

        if (ImGui.MenuItem("Sidebar", Shortcut.Of(EditorAction.ToggleSidebar), view.SidebarVisible()))
        {
            view.ToggleSidebar();
        }

        ImGui.Separator();

        // The header's shading, as three checkable items.
        if (ImGui.MenuItem("Wireframe", Shortcut.Of(EditorAction.ToggleWireframe), view.Viewport.Shading == ShadingMode.Wireframe))
        {
            view.Viewport.Shading = ShadingMode.Wireframe;
        }

        if (ImGui.MenuItem("Solid", null, view.Viewport.Shading == ShadingMode.Unlit))
        {
            view.Viewport.Shading = ShadingMode.Unlit;
        }

        if (ImGui.MenuItem("Lit", null, view.Viewport.Shading == ShadingMode.Lit))
        {
            view.Viewport.Shading = ShadingMode.Lit;
        }

        if (ImGui.MenuItem("X-Ray", Shortcut.Of(EditorAction.ToggleXRay), view.Viewport.XRay))
        {
            view.Viewport.XRay = !view.Viewport.XRay;
        }

        ImGui.EndMenu();
    }

    /// <summary>The six views along the axes, with Blender's numpad keys beside them.</summary>
    private static void DrawAlignMenu(FlyCamera camera)
    {
        if (!ImGui.BeginMenu("Align View"))
        {
            return;
        }

        AlignedView? current = camera.CurrentAlignedView();

        (AlignedView View, EditorAction Action)[] views =
        [
            (AlignedView.Front, EditorAction.ViewFront),
            (AlignedView.Back, EditorAction.ViewBack),
            (AlignedView.Right, EditorAction.ViewRight),
            (AlignedView.Left, EditorAction.ViewLeft),
            (AlignedView.Top, EditorAction.ViewTop),
            (AlignedView.Bottom, EditorAction.ViewBottom),
        ];

        foreach ((AlignedView view, EditorAction action) in views)
        {
            if (ImGui.MenuItem(view.ToString(), Shortcut.Of(action), current == view))
            {
                camera.Align(view);
            }
        }

        ImGui.EndMenu();
    }

    private static void DrawRecentMenu(ProjectController project)
    {
        IReadOnlyList<string> recent = project.Recent.Paths;

        if (!ImGui.BeginMenu("Open Recent", recent.Count > 0))
        {
            return;
        }

        string? chosen = null;

        foreach (string path in recent)
        {
            if (ImGui.MenuItem(Path.GetFileName(path)))
            {
                chosen = path;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(path);
            }
        }

        ImGui.EndMenu();

        // Opened out here, not where it was clicked. Loading a level replaces the scene and drops
        // the renderer's buffers, which is not something to start halfway through drawing the menu
        // that asked for it.
        if (chosen is not null)
        {
            project.OpenRecent(chosen);
        }
    }
}
