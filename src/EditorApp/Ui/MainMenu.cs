using System.Numerics;
using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>The menu bar and the status line under it.</summary>
public static class MainMenu
{
    /// <summary>Draws the menu bar and returns its height, so the shell can lay out beneath it.</summary>
    public static float Draw(EditorSession session, ProjectController project, ExportController export, Action onExit)
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

            ImGui.Separator();

            if (ImGui.MenuItem("Exit"))
            {
                onExit();
            }

            ImGui.EndMenu();
        }

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

        DrawStatus(project);

        float height = ImGui.GetWindowSize().Y;
        ImGui.EndMainMenuBar();
        return height;
    }

    private static void DrawRecentMenu(ProjectController project)
    {
        IReadOnlyList<string> recent = project.Recent.Paths;

        if (!ImGui.BeginMenu("Open Recent", recent.Count > 0))
        {
            return;
        }

        foreach (string path in recent)
        {
            if (ImGui.MenuItem(Path.GetFileName(path)))
            {
                project.OpenRecent(path);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(path);
            }
        }

        ImGui.EndMenu();
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
