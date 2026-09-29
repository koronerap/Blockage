using System.Numerics;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Blender's Quick Favorites (Fullreleaseplan 7.7): the commands you choose, in a menu at the mouse
/// on Shift+Q — and a window to choose them in, every command listed by where it belongs.
/// </summary>
public static class QuickFavorites
{
    private const string PopupId = "##quick-favorites";

    /// <summary>What a new set of preferences starts with.</summary>
    public static readonly string[] Defaults =
    [
        "edit.duplicate", "edit.duplicatelinked", "edit.collection", "view.section", "view.quad", "view.walk", "view.render",
    ];

    private static bool _openRequested;
    private static Vector2 _at;
    private static bool _editing;

    public static void Open(Vector2 at)
    {
        _at = at;
        _openRequested = true;
    }

    public static void Draw(Preferences preferences, Action<EditorAction> run, Action save)
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        ImGui.SetNextWindowPos(_at, ImGuiCond.Appearing);
        if (ImGui.BeginPopup(PopupId))
        {
            ImGui.TextDisabled("Quick Favorites");
            ImGui.Separator();

            List<ActionInfo> chosen = [.. preferences.Favorites
                .Select(id => EditorActions.All.FirstOrDefault(info => info.Id == id))
                .Where(info => info.Id is not null)];

            if (chosen.Count == 0)
            {
                ImGui.TextDisabled("None chosen yet.");
            }

            foreach (ActionInfo info in chosen)
            {
                if (ImGui.MenuItem(info.Name, Shortcut.Of(info.Action)))
                {
                    run(info.Action);
                }
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Choose Favorites..."))
            {
                _editing = true;
            }

            ImGui.EndPopup();
        }

        DrawEditor(preferences, save);
    }

    private static void DrawEditor(Preferences preferences, Action save)
    {
        if (!_editing)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 22f, ImGui.GetFontSize() * 28f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Quick Favorites", ref _editing))
        {
            ImGui.End();
            return;
        }

        ImGui.TextDisabled("The commands Shift+Q shows at the mouse.");
        bool changed = false;
        foreach (string category in EditorActions.Categories)
        {
            if (!ImGui.CollapsingHeader(category, ImGuiTreeNodeFlags.DefaultOpen))
            {
                continue;
            }

            foreach (ActionInfo info in EditorActions.All.Where(info => info.Category == category))
            {
                bool on = preferences.Favorites.Contains(info.Id);
                if (ImGui.Checkbox($"{info.Name}##{info.Id}", ref on))
                {
                    preferences.Favorites = on
                        ? [.. preferences.Favorites, info.Id]
                        : [.. preferences.Favorites.Where(id => id != info.Id)];
                    changed = true;
                }
            }
        }

        ImGui.End();
        if (changed)
        {
            save();
        }
    }
}
