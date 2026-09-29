using EditorApp.Input;

namespace EditorApp.Ui;

/// <summary>
/// The key an action is on right now, for a menu item or a tooltip. Read from the active keymap
/// every time it is drawn, so a key changed in Preferences is the key the menus show — a label
/// written out by hand would go on showing the old one.
/// </summary>
public static class Shortcut
{
    /// <summary>The action's first key, or empty when it has none.</summary>
    public static string Of(EditorAction action) => Keymap.Active.Label(action);

    /// <summary>The key in brackets, for the end of a tooltip; nothing when there is no key.</summary>
    public static string Hint(EditorAction action) => Of(action) is { Length: > 0 } key ? $"  ({key})" : string.Empty;

    /// <summary>Every key an action is on, joined for the shortcut sheet.</summary>
    public static string All(EditorAction action) => string.Join(",  ", Keymap.Active.Bindings(action));

    /// <summary>
    /// The Transform tool's key. A keymap can put Move and Rotate on keys of their own instead of the
    /// tool as a whole — the Default does, as G and R — and then those are what to show.
    /// </summary>
    public static string ForTransform()
    {
        if (Of(EditorAction.ToolTransform) is { Length: > 0 } tool)
        {
            return tool;
        }

        string move = Of(EditorAction.ToolMove);
        string rotate = Of(EditorAction.ToolRotate);
        return (move, rotate) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{move} move, {rotate} rotate",
            ({ Length: > 0 }, _) => move,
            (_, { Length: > 0 }) => rotate,
            _ => string.Empty,
        };
    }
}
