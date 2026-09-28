using System.Numerics;
using EditorApp.Core.Editing;
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
            if (ImGui.MenuItem("New", "Ctrl+N"))
            {
                project.NewProject();
            }

            if (ImGui.MenuItem("Open...", "Ctrl+O"))
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

            if (ImGui.MenuItem("Save", "Ctrl+S"))
            {
                project.Save();
            }

            if (ImGui.MenuItem("Save As...", "Ctrl+Shift+S"))
            {
                project.SaveAs();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Export mesh...", "Ctrl+E"))
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
            if (ImGui.MenuItem($"Undo {session.History.NextUndoName ?? string.Empty}", "Ctrl+Z", false, session.History.CanUndo))
            {
                session.Undo();
            }

            if (ImGui.MenuItem($"Redo {session.History.NextRedoName ?? string.Empty}", "Ctrl+Y", false, session.History.CanRedo))
            {
                session.Redo();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Object"))
        {
            ObjectMenu.DrawItems(session, view.Camera);
            ImGui.EndMenu();
        }

        DrawStatus(project);

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

        if (ImGui.MenuItem("Frame Level", "Home"))
        {
            view.FrameLevel();
        }

        if (ImGui.MenuItem("Frame Focused Object", "Numpad ."))
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

        if (ImGui.MenuItem("Orthographic", "Numpad 5", view.Camera.Orthographic))
        {
            view.Camera.Orthographic = !view.Camera.Orthographic;
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Ground Grid", "G", view.GridVisible()))
        {
            view.ToggleGrid();
        }

        if (ImGui.MenuItem("Measurements", "D", view.MeasurementsVisible()))
        {
            view.ToggleMeasurements();
        }

        ImGui.Separator();

        // One checkable item, the same single switch the header carries.
        if (ImGui.MenuItem("Lit Shading", null, view.Lighting.IsLit))
        {
            view.Lighting.Mode = view.Lighting.IsLit ? ShadingMode.Unlit : ShadingMode.Lit;
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

        (AlignedView View, string Keys)[] views =
        [
            (AlignedView.Front, "Numpad 1"),
            (AlignedView.Back, "Ctrl+Numpad 1"),
            (AlignedView.Right, "Numpad 3"),
            (AlignedView.Left, "Ctrl+Numpad 3"),
            (AlignedView.Top, "Numpad 7"),
            (AlignedView.Bottom, "Ctrl+Numpad 7"),
        ];

        foreach ((AlignedView view, string keys) in views)
        {
            if (ImGui.MenuItem(view.ToString(), keys, current == view))
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

    private static void DrawStatus(ProjectController project)
    {
        if (project.StatusMessage.Length == 0)
        {
            return;
        }

        // Right-aligned so it never fights the menus for space.
        float width = ImGui.CalcTextSize(project.StatusMessage).X;
        ImGui.SameLine(ImGui.GetWindowWidth() - width - 16f);

        if (project.IsError)
        {
            ImGui.TextColored(Theme.Danger, project.StatusMessage);
        }
        else
        {
            ImGui.TextDisabled(project.StatusMessage);
        }
    }
}
