using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Blender's M: what is selected, moved into a collection — one of the level's, listed nested as
/// they are, a new one made for it, or none, back at the top. At the mouse, and as a submenu of the
/// context menus.
/// </summary>
public static class CollectionMenu
{
    private const string PopupId = "##collection-menu";

    private static bool _openRequested;
    private static Vector2 _at;

    public static bool IsOpen { get; private set; }

    public static void Open(Vector2 at)
    {
        _at = at;
        _openRequested = true;
    }

    public static void DrawPopup(EditorSession session)
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

        ImGui.TextDisabled("Move to Collection");
        ImGui.Separator();
        DrawItems(session);
        ImGui.EndPopup();
    }

    /// <summary>"Move to Collection" in a context menu, when anything is selected.</summary>
    public static void DrawSubmenu(EditorSession session)
    {
        if (!ImGui.BeginMenu("Move to Collection"))
        {
            return;
        }

        DrawItems(session);
        ImGui.EndMenu();
    }

    private static void DrawItems(EditorSession session)
    {
        bool anything = session.SelectedObjects.Any() || session.SelectedLights.Any();
        if (!anything)
        {
            ImGui.TextDisabled("Nothing is selected.");
            return;
        }

        if (ImGui.MenuItem("New Collection"))
        {
            List<int> moving = [.. session.SelectedObjects.Select(o => o.Id), .. session.SelectedLights.Select(l => l.Id)];
            session.NewCollection(session.Scene.ActiveCollectionId, moving);
        }

        if (ImGui.MenuItem("None - at the top"))
        {
            session.MoveSelectedToCollection(0);
        }

        if (session.Scene.Collections.Count > 0)
        {
            ImGui.Separator();
            DrawLevel(session, 0, 0);
        }
    }

    private static void DrawLevel(EditorSession session, int parentId, int depth)
    {
        foreach (SceneCollection collection in session.Scene.CollectionsIn(parentId).ToList())
        {
            if (depth > session.Scene.Collections.Count)
            {
                return;
            }

            string indent = new(' ', depth * 3);
            if (IconMenu.Item(Icons.Collection, $"{indent}{collection.Name}##move-{collection.Id}"))
            {
                session.MoveSelectedToCollection(collection.Id);
            }

            DrawLevel(session, collection.Id, depth + 1);
        }
    }

    /// <summary>The key that opens it, for tooltips.</summary>
    public static string Hint => Shortcut.Hint(EditorAction.MoveToCollection);
}
