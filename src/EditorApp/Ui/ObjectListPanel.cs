using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The scene's objects. Loop Cut and Extrude's Create sub-mode both produce them, so without a list
/// the level can quietly grow pieces the designer never sees named anywhere.
/// </summary>
public static class ObjectListPanel
{
    private static int _renamingId;
    private static string _renameBuffer = string.Empty;

    public static void Draw(EditorSession session)
    {
        VoxelScene scene = session.Scene;

        foreach (VoxelObject o in scene.Objects.ToArray())
        {
            ImGui.PushID(o.Id);

            DrawVisibilityToggle(o);
            ImGui.SameLine();

            if (_renamingId == o.Id)
            {
                DrawRenameField(o);
            }
            else
            {
                DrawNameRow(session, o);
            }

            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"{scene.Objects.Count} object(s), {scene.SolidCount:N0} voxels");
    }

    private static void DrawVisibilityToggle(VoxelObject o)
    {
        // A hidden object is skipped by picking and by export, so this is a real edit, not a view
        // preference — worth the width it takes.
        bool visible = o.Visible;
        if (ImGui.Checkbox("##visible", ref visible))
        {
            o.Visible = visible;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Visible. Hidden objects are not picked and not exported.");
        }
    }

    private static void DrawNameRow(EditorSession session, VoxelObject o)
    {
        bool focused = o.Id == session.Scene.FocusId;

        if (focused)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        }

        if (ImGui.Selectable($"{o.Name}##name", focused, ImGuiSelectableFlags.AllowDoubleClick))
        {
            session.TryFocus(o.Id);

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _renamingId = o.Id;
                _renameBuffer = o.Name;
            }
        }

        if (focused)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            Vector3 position = o.Transform.Position;
            ImGui.SetTooltip(
                $"{o.Grid.SolidCount:N0} voxels\nat {position.X:0.##}, {position.Y:0.##}, {position.Z:0.##}\n\nDouble-click to rename.");
        }

        DrawContextMenu(session, o);
    }

    private static void DrawContextMenu(EditorSession session, VoxelObject o)
    {
        if (!ImGui.BeginPopupContextItem("##object-menu"))
        {
            return;
        }

        if (ImGui.MenuItem("Rename"))
        {
            _renamingId = o.Id;
            _renameBuffer = o.Name;
        }

        if (ImGui.MenuItem("Focus"))
        {
            session.TryFocus(o.Id);
        }

        ImGui.Separator();

        // Refused for the last object: with no Place tool, an empty scene is a dead end.
        bool isLast = session.Scene.Objects.Count <= 1;
        ImGui.BeginDisabled(isLast);
        if (ImGui.MenuItem("Delete"))
        {
            session.DeleteObject(o.Id);
        }

        ImGui.EndDisabled();

        if (isLast && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("The last object cannot be deleted — there would be nothing to extrude from.");
        }

        ImGui.EndPopup();
    }

    private static void DrawRenameField(VoxelObject o)
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.SetKeyboardFocusHere();

        string buffer = _renameBuffer;
        if (ImGui.InputText("##rename", ref buffer, 64, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            o.Name = buffer.Trim().Length > 0 ? buffer.Trim() : o.Name;
            _renamingId = 0;
        }
        else
        {
            _renameBuffer = buffer;
        }

        if (ImGui.IsItemDeactivated())
        {
            _renamingId = 0;
        }
    }
}
