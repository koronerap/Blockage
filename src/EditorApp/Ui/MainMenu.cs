using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>The menu bar and the status line under it.</summary>
public static class MainMenu
{
    private static void DrawObjectSelect(EditorSession session)
    {
        bool anything = session.Scene.Objects.Count > 0 || session.Scene.Lights.Count > 0;
        if (ImGui.MenuItem("All", Shortcut.Of(EditorAction.SelectAll), false, anything))
        {
            session.SelectAll();
        }

        if (ImGui.MenuItem("None", Shortcut.Of(EditorAction.DeselectAll), false, session.SelectedCount > 0))
        {
            session.DeselectAll();
        }

        if (ImGui.MenuItem("Invert", Shortcut.Of(EditorAction.InvertSelection), false, anything))
        {
            session.InvertSelection();
        }
    }

    /// <summary>The Select menu inside an object: its voxels.</summary>
    private static void DrawVoxelSelect(EditorSession session)
    {
        bool any = !session.VoxelSelection.IsEmpty;
        if (ImGui.MenuItem("All", Shortcut.Of(EditorAction.SelectAll)))
        {
            session.SelectAllVoxels();
        }

        if (ImGui.MenuItem("None", Shortcut.Of(EditorAction.DeselectAll), false, any))
        {
            session.DeselectAllVoxels();
        }

        if (ImGui.MenuItem("Invert", Shortcut.Of(EditorAction.InvertSelection)))
        {
            session.InvertVoxelSelection();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("More", Shortcut.Of(EditorAction.GrowSelection), false, any))
        {
            session.GrowVoxelSelection();
        }

        if (ImGui.MenuItem("Less", Shortcut.Of(EditorAction.ShrinkSelection), false, any))
        {
            session.ShrinkVoxelSelection();
        }

        ImGui.Separator();
        foreach ((VoxelSelectMode mode, string name) in new[] { (VoxelSelectMode.Box, "Box"), (VoxelSelectMode.Wand, "Wand"), (VoxelSelectMode.Colour, "By Colour") })
        {
            if (ImGui.MenuItem(name, null, session.VoxelSelectMode == mode))
            {
                session.VoxelSelectMode = mode;
                session.ActiveTool = EditorTool.Select;
            }
        }
    }

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
            // Blender's File > New: a template to start from, the cube first as Ctrl+N makes it.
            if (ImGui.BeginMenu("New"))
            {
                foreach (LevelTemplate template in LevelTemplates.All)
                {
                    string shortcut = template == LevelTemplate.Cube ? Shortcut.Of(EditorAction.NewLevel) : string.Empty;
                    if (ImGui.MenuItem(LevelTemplates.NameOf(template), shortcut))
                    {
                        project.NewProject(template);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(LevelTemplates.DescriptionOf(template));
                    }
                }

                ImGui.EndMenu();
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

            // Blender's quit.blend: the level as the editor last closed on it, saved or not.
            if (ImGui.MenuItem("Recover Last Session", null, false, project.LastSessionFound is not null))
            {
                project.RecoverLastSession();
            }

            if (project.LastSessionFound is { } last && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{last.ProjectName}, as Blockage last closed on it.");
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

            // The level in front: the one beside it comes forward, or with none, a new one.
            if (ImGui.MenuItem("Close Level", Shortcut.Of(EditorAction.CloseLevel)))
            {
                view.CloseLevel();
            }

            ImGui.Separator();

            // Blender's Append: objects from another level, brought in where they stood there.
            if (ImGui.MenuItem("Append..."))
            {
                AppendDialog.Show();
            }

            if (ImGui.MenuItem("Prop Library"))
            {
                AddMenu.OpenPropLibrary();
            }

            ImGui.Separator();

            // Blender's File > Import: another program's file, as a level of its own.
            if (ImGui.BeginMenu("Import"))
            {
                if (ImGui.MenuItem("MagicaVoxel (.vox)...", Shortcut.Of(EditorAction.ImportVox)))
                {
                    project.ImportVox();
                }

                ImGui.Separator();

                // Into the level in front, where the view looks.
                if (ImGui.MenuItem("Image as a Sprite (.png)...", Shortcut.Of(EditorAction.ImportSprite)))
                {
                    ImageImportDialog.Show(ImageImport.Sprite);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Pixel art stood up in voxels, each pixel as deep as you like.");
                }

                if (ImGui.MenuItem("Heightmap as Terrain (.png)...", Shortcut.Of(EditorAction.ImportHeightmap)))
                {
                    ImageImportDialog.Show(ImageImport.Heightmap);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Ground as high as the image is bright, dressed in grass, earth and stone.");
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Export"))
            {
                if (ImGui.MenuItem("Mesh (OBJ, glTF)...", Shortcut.Of(EditorAction.Export)))
                {
                    export.Show();
                }

                if (ImGui.MenuItem("MagicaVoxel (.vox)...", Shortcut.Of(EditorAction.ExportVox)))
                {
                    project.ExportVox();
                }

                // Kept apart from the mesh export on purpose: nothing here is meshed and no texture
                // is written. The voxels themselves go out, in the layout Mimicraft's own decoders read.
                if (ImGui.MenuItem("Mimicraft (.character, .weapons)..."))
                {
                    mimicraft.Show();
                }

                ImGui.EndMenu();
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

            if (ImGui.MenuItem("Undo History...", Shortcut.Of(EditorAction.UndoHistory), HistoryWindow.IsOpen))
            {
                HistoryWindow.Toggle();
            }

            ImGui.Separator();

            // Blender's Menu Search: every command, found by typing part of its name.
            if (ImGui.MenuItem("Search...", Shortcut.Of(EditorAction.Search)))
            {
                CommandSearch.Open();
            }

            ImGui.Separator();

            // What a copy takes is worth saying before it is taken: the selection, or everything.
            string what = session.CopiesSelection ? "Faces" : session.SelectedObjects.Skip(1).Any() ? "Objects" : "Object";
            bool copies = session.CopiesSelection || session.SelectedObjects.Any(o => !o.IsEmpty);

            if (ImGui.MenuItem($"Copy {what}", Shortcut.Of(EditorAction.Copy), false, copies))
            {
                ClipboardActions.Copy(session, ReportLog.Shared);
            }

            if (ImGui.MenuItem($"Cut {what}", Shortcut.Of(EditorAction.Cut), false, copies))
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

        // Blender's Select menu: everything, nothing, the rest — of the objects, or inside one, of its voxels.
        if (ImGui.BeginMenu("Select"))
        {
            if (session.InEditMode)
            {
                DrawVoxelSelect(session);
            }
            else
            {
                DrawObjectSelect(session);
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Select Tool", Shortcut.Of(EditorAction.ToolSelect), session.ActiveTool == EditorTool.Select))
            {
                session.ActiveTool = EditorTool.Select;
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Object"))
        {
            ObjectMenu.DrawItems(session, view.Camera);
            ImGui.EndMenu();
        }

        // Shapes, props and lights: set down where the view is looking. Shift+A opens the same list
        // at the mouse, and sets what is picked down there.
        if (ImGui.BeginMenu("Add"))
        {
            AddMenu.DrawItems(at: null);
            ImGui.EndMenu();
        }

        // Blender's: the level made into a picture.
        if (ImGui.BeginMenu("Render"))
        {
            if (ImGui.MenuItem("Render Image", Shortcut.Of(EditorAction.RenderImage)))
            {
                view.RenderImage();
            }

            // Which camera a render is seen from: one of the level's, or the view.
            if (ImGui.BeginMenu("Render From"))
            {
                if (ImGui.MenuItem("The View", string.Empty, session.Scene.ActiveCameraId == 0))
                {
                    session.SetActiveCamera(0);
                }

                foreach (Core.Scene.SceneCamera camera in session.Scene.Cameras)
                {
                    if (ImGui.MenuItem(camera.Name, string.Empty, session.Scene.ActiveCameraId == camera.Id))
                    {
                        session.SetActiveCamera(camera.Id);
                    }
                }

                ImGui.EndMenu();
            }

            if (ImGui.MenuItem("Turntable, Sprites and Cameras..."))
            {
                view.RenderOutputs();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("The level turning, as a GIF, PNGs or an MP4; a sheet of sprites from all round it; a picture from every camera.");
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Save Viewport Image..."))
            {
                view.SaveViewportImage();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("The view as it is now, without the grid, overlays or gizmos, to a PNG.");
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("Welcome Screen"))
            {
                WelcomeScreen.Open();
            }

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

        if (ImGui.MenuItem("Walk", Shortcut.Of(EditorAction.WalkMode)))
        {
            view.Walk();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Walk through the level at the size of a player: W A S D, Space to jump, Shift to run, Tab to fly. Enter keeps the view there, Esc goes back.");
        }

        if (ImGui.MenuItem("Quad View", Shortcut.Of(EditorAction.ToggleQuadView), view.Viewport.Quad))
        {
            view.Viewport.Quad = !view.Viewport.Quad;
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Camera", Shortcut.Of(EditorAction.ViewCamera)))
        {
            view.ViewCamera();
        }

        if (ImGui.MenuItem("Move Camera to View", Shortcut.Of(EditorAction.CameraToView)))
        {
            view.CameraToView();
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
