using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Parenting from a menu: every object as a possible parent — the ones that cannot be greyed, with
/// the reason on hover — and Clear Parent. One list in four places, so they never disagree: a
/// submenu in the Outliner's row menu and in the Object menu, the Parent field in the Object tab,
/// and a popup at the mouse for Ctrl+P, Blender's key for it.
///
/// Blender parents the selected objects to the active one; with one object in hand at a time, the
/// parent is picked from the list instead. Either way the child stays where it is.
/// </summary>
public static class ParentMenu
{
    private const string PopupId = "##parent-to";

    private static readonly Dictionary<int, (Vector2 Min, Vector2 Max)> ItemRects = [];

    private static int _childId;
    private static bool _openRequested;

    /// <summary>
    /// Where the entry for a would-be parent was drawn last, for tests to aim at — 0 for "None" and
    /// "Clear Parent".
    /// </summary>
    public static (Vector2 Min, Vector2 Max)? ItemRect(int parentId) => ItemRects.TryGetValue(parentId, out var rect) ? rect : null;

    private static void Remember(int parentId) => ItemRects[parentId] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

    /// <summary>Asks for the popup at the mouse, for <paramref name="childId"/>; it opens on the next frame drawn.</summary>
    public static void Open(int childId)
    {
        _childId = childId;
        _openRequested = true;
    }

    /// <summary>The popup Ctrl+P opens, while it is open. Drawn above the shell, as the other popups are.</summary>
    public static void DrawPopup(EditorSession session)
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        if (!ImGui.BeginPopup(PopupId))
        {
            return;
        }

        if (session.Scene.FindPlaceable(_childId) is { } child)
        {
            ImGui.TextDisabled($"Parent {child.Name} to");
            ImGui.Separator();
            DrawTargets(session, child);
            ImGui.Separator();
            DrawClear(session, child);
        }
        else
        {
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>"Parent To" as a submenu, and "Clear Parent" after it — for the row and Object menus.</summary>
    public static void DrawSubmenu(EditorSession session, IPlaceable child)
    {
        if (ImGui.BeginMenu("Parent To", session.Scene.Objects.Any(o => o.Id != child.Id)))
        {
            DrawTargets(session, child);
            ImGui.EndMenu();
        }

        DrawClear(session, child);
    }

    /// <summary>The Parent field of the Object tab: the parent's name, a list of the others when opened.</summary>
    public static void DrawField(EditorSession session, IPlaceable child)
    {
        VoxelObject? current = session.Scene.ParentOf(child);

        if (Props.BeginCombo("Parent", "parent", current?.Name ?? "None"))
        {
            ItemRects.Clear();

            if (ImGui.Selectable("None", current is null))
            {
                Clear(session, child);
            }

            Remember(0);

            foreach (VoxelObject o in session.Scene.Objects)
            {
                if (o.Id == child.Id)
                {
                    continue;
                }

                string? problem = session.ParentProblem(child.Id, o.Id);
                ImGuiSelectableFlags flags = problem is null ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled;

                if (ImGui.Selectable($"{o.Name}##parent-{o.Id}", o.Id == current?.Id, flags))
                {
                    Parent(session, child, o);
                }

                Remember(o.Id);
                Hovered(o, problem);
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"What it moves with. It stays where it is when this changes.{Shortcut.Hint(EditorAction.SetParent)}");
        }
    }

    /// <summary>Every other object, as a menu item that makes it the parent.</summary>
    public static void DrawTargets(EditorSession session, IPlaceable child)
    {
        VoxelObject? current = session.Scene.ParentOf(child);
        ItemRects.Clear();

        foreach (VoxelObject o in session.Scene.Objects)
        {
            if (o.Id == child.Id)
            {
                continue;
            }

            string? problem = session.ParentProblem(child.Id, o.Id);
            if (ImGui.MenuItem($"{o.Name}##parent-{o.Id}", null, o.Id == current?.Id, problem is null))
            {
                Parent(session, child, o);
            }

            Remember(o.Id);
            Hovered(o, problem);
        }
    }

    private static void DrawClear(EditorSession session, IPlaceable child)
    {
        VoxelObject? current = session.Scene.ParentOf(child);

        if (ImGui.MenuItem("Clear Parent", Shortcut.Of(EditorAction.ClearParent), false, current is not null))
        {
            Clear(session, child);
        }

        Remember(0);

        if (current is not null && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Free it from {current.Name}, leaving it where it is.");
        }
    }

    /// <summary>A hovered entry outlines its object in the viewport, as a hovered Outliner row does.</summary>
    private static void Hovered(VoxelObject o, string? problem)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            return;
        }

        ObjectListPanel.MarkHovered(o.Id);

        if (problem is not null)
        {
            ImGui.SetTooltip(problem);
        }
    }

    /// <summary>Parents, and says so in the status bar — the only sign of it until the parent moves.</summary>
    public static bool Parent(EditorSession session, IPlaceable child, VoxelObject parent)
    {
        if (!session.SetParent(child.Id, parent.Id))
        {
            return false;
        }

        ReportLog.Shared.Post($"{child.Name} is now a child of {parent.Name} and moves with it.");
        return true;
    }

    public static bool Clear(EditorSession session, IPlaceable child)
    {
        if (session.Scene.ParentOf(child) is not { } former || !session.ClearParent(child.Id))
        {
            return false;
        }

        ReportLog.Shared.Post($"{child.Name} is no longer a child of {former.Name}.");
        return true;
    }
}
