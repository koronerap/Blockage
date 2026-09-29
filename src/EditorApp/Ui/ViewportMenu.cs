using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>What the viewport's right-click menu reaches.</summary>
public sealed class ViewportMenuActions
{
    public required EditorSession Session { get; init; }

    /// <summary>Does a keymap action, exactly as its key would.</summary>
    public required Action<EditorAction> Run { get; init; }

    public required ViewportSettings Viewport { get; init; }
}

/// <summary>
/// The viewport's right-click menu, as Blender's object context menu is: short, and about what was
/// under the cursor — an object, a light, or nothing, when it is about the view and adding things.
/// Every entry does what its key does, and shows the key.
/// </summary>
public static class ViewportMenu
{
    private const string PopupId = "##viewport-menu";

    private static readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = [];

    private static bool _openRequested;
    private static Vector2 _at;
    private static int _objectId;
    private static int _lightId;

    public static bool IsOpen { get; private set; }

    /// <summary>Where an entry was drawn last, by its label, for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max)? ItemRect(string label) => Rects.TryGetValue(label, out var rect) ? rect : null;

    /// <summary>
    /// Asks for the menu at a point on screen, for what was there: an object's id, a light's, or
    /// neither. The host has already picked it, so the entries act on it.
    /// </summary>
    public static void Open(Vector2 at, int objectId, int lightId)
    {
        _at = at;
        _objectId = objectId;
        _lightId = lightId;
        _openRequested = true;
    }

    public static void Draw(ViewportMenuActions actions)
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        ImGui.SetNextWindowPos(_at, ImGuiCond.Appearing);
        IsOpen = ImGui.BeginPopup(PopupId);
        if (!IsOpen)
        {
            return;
        }

        Rects.Clear();
        VoxelScene scene = actions.Session.Scene;

        if (scene.FindLight(_lightId) is { } light)
        {
            DrawLight(actions, light);
        }
        else if (scene.Find(_objectId) is { } target)
        {
            DrawObject(actions, target);
        }
        else
        {
            DrawNothing(actions);
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static void DrawObject(ViewportMenuActions actions, VoxelObject target)
    {
        EditorSession session = actions.Session;
        bool hasVoxels = !target.IsEmpty;
        bool several = session.SelectedCount > 1;

        // The commands act on the selection, which the click made this object part of.
        ImGui.TextDisabled(several ? ObjectMenu.SelectionSummary(session) : target.Name);
        ImGui.Separator();

        Entry(actions, "Duplicate", EditorAction.Duplicate, hasVoxels);
        Entry(actions, "Copy", EditorAction.Copy, hasVoxels);
        Entry(actions, "Cut", EditorAction.Cut, hasVoxels);
        Entry(actions, "Paste", EditorAction.Paste, session.Clipboard is not null);

        ImGui.Separator();
        Entry(actions, "Rename", EditorAction.Rename);
        Entry(actions, "Hide", EditorAction.Hide);
        Entry(actions, "Lock", EditorAction.Lock);

        ImGui.Separator();
        if (several)
        {
            Entry(actions, "Parent to Active", EditorAction.SetParent, session.SelectedLightId == 0);
            Entry(actions, "Join into Active", EditorAction.Join, session.SelectedLightId == 0 && session.SelectedObjects.Count() > 1);
        }
        else
        {
            ParentMenu.DrawSubmenu(session, target);
            ObjectMenu.DrawJoinMenu(session, target);
        }

        Entry(actions, "Subdivide", EditorAction.Subdivide, session.SelectedObjects.Any(o => session.SubdivideProblem(o) is null));

        if (ImGui.BeginMenu("Turn and Mirror", hasVoxels))
        {
            ObjectMenu.DrawTurns(session);
            ImGui.EndMenu();
        }

        ImGui.Separator();
        Entry(actions, "Frame", EditorAction.FrameFocused);
        DrawAdd();

        ImGui.Separator();
        Entry(actions, "Delete", EditorAction.Delete, session.SelectedCount > 0);
    }

    private static void DrawLight(ViewportMenuActions actions, SceneLight light)
    {
        EditorSession session = actions.Session;

        ImGui.TextDisabled(light.Name);
        ImGui.Separator();

        Entry(actions, "Duplicate", EditorAction.Duplicate);
        Entry(actions, "Rename", EditorAction.Rename);
        Entry(actions, light.Visible ? "Switch Off" : "Switch On", EditorAction.Hide);
        Entry(actions, "Lock", EditorAction.Lock);

        ImGui.Separator();
        ParentMenu.DrawSubmenu(session, light);
        DrawAdd();

        ImGui.Separator();
        Entry(actions, "Delete", EditorAction.Delete);
    }

    /// <summary>Nothing under the cursor: what to add there, and the view.</summary>
    private static void DrawNothing(ViewportMenuActions actions)
    {
        EditorSession session = actions.Session;
        VoxelScene scene = session.Scene;

        AddMenu.DrawItems(_at);
        Entry(actions, "Paste", EditorAction.Paste, session.Clipboard is not null);

        ImGui.Separator();
        bool anything = scene.Objects.Count > 0 || scene.Lights.Count > 0;
        Entry(actions, "Select All", EditorAction.SelectAll, anything);
        Entry(actions, "Select None", EditorAction.DeselectAll, session.SelectedCount > 0);
        Entry(actions, "Invert Selection", EditorAction.InvertSelection, anything);

        ImGui.Separator();
        Entry(actions, "Frame All", EditorAction.FrameLevel);
        Entry(actions, "Show All", EditorAction.ShowAll, scene.Objects.Any(o => !o.Visible) || scene.Lights.Any(l => !l.Visible));
        Entry(actions, "Unlock All", EditorAction.UnlockAll, scene.Objects.Any(o => o.Locked) || scene.Lights.Any(l => l.Locked));

        ImGui.Separator();
        Entry(actions, "Snapping", EditorAction.ToggleSnap, selected: session.Snap.Enabled);
        Entry(actions, "X-Ray", EditorAction.ToggleXRay, selected: actions.Viewport.XRay);

        ImGui.Separator();
        Entry(actions, "Search...", EditorAction.Search);
    }

    /// <summary>The Add menu's lists, for the point the menu was opened at. No icon: nothing round it has one.</summary>
    private static void DrawAdd()
    {
        if (ImGui.BeginMenu("Add"))
        {
            AddMenu.DrawItems(_at);
            ImGui.EndMenu();
        }

        Rects["Add"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    private static void Entry(ViewportMenuActions actions, string label, EditorAction action, bool enabled = true, bool selected = false)
    {
        if (ImGui.MenuItem(label, Shortcut.Of(action), selected, enabled))
        {
            actions.Run(action);
        }

        Rects[label] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }
}
