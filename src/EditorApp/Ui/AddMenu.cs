using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>What was picked from the Add menu: one shape, prop or light.</summary>
public readonly record struct AddChoice(ShapeKind? Shape = null, PropKind? Prop = null, LightKind? Light = null);

/// <summary>
/// Blender's Shift+A: a new shape, a prop or a light, from a menu at the mouse — and the same list
/// under Add in the menu bar, the Outliner's plus and the viewport's right-click. What is picked is
/// set down where the menu was opened over the level, or where the view looks when it was opened
/// elsewhere; the application does that, taking the choice from here.
/// </summary>
public static class AddMenu
{
    private const string PopupId = "##add-menu";

    private static readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = [];

    private static bool _openRequested;
    private static Vector2 _at;
    private static (AddChoice Choice, Vector2? At)? _pending;

    public static bool IsOpen { get; private set; }

    /// <summary>Where an entry was drawn last, by its name, for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max)? ItemRect(string name) => Rects.TryGetValue(name, out var rect) ? rect : null;

    /// <summary>Asks for the menu at a point on screen — the mouse, for Shift+A — where what is chosen will go.</summary>
    public static void Open(Vector2 at)
    {
        _at = at;
        _openRequested = true;
    }

    /// <summary>Picks something as though from the menu: for the search, which has no menu of its own open.</summary>
    public static void Choose(AddChoice choice, Vector2? at) => _pending = (choice, at);

    /// <summary>What was chosen since last asked, and the point on screen it was chosen for; null for the middle of the view.</summary>
    public static (AddChoice Choice, Vector2? At)? TakePending()
    {
        (AddChoice, Vector2?)? pending = _pending;
        _pending = null;
        return pending;
    }

    /// <summary>The Shift+A menu, while it is open.</summary>
    public static void DrawPopup()
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

        ImGui.TextDisabled("Add");
        ImGui.Separator();
        DrawItems(_at);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>The three lists, for whichever menu they are drawn in. <paramref name="at"/> is where what is chosen goes on screen.</summary>
    public static void DrawItems(Vector2? at)
    {
        if (IconMenu.Begin(Icons.ObjectTab, "Mesh"))
        {
            foreach (ShapeKind shape in Shapes.All)
            {
                if (Entry(IconFor(shape), Shapes.NameOf(shape)))
                {
                    Choose(new AddChoice(Shape: shape), at);
                }
            }

            ImGui.EndMenu();
        }

        Remember("Mesh");

        if (IconMenu.Begin(Icons.PropCrate, "Prop"))
        {
            foreach (PropKind prop in PropPresets.All)
            {
                if (Entry(IconFor(prop), PropPresets.NameOf(prop)))
                {
                    Choose(new AddChoice(Prop: prop), at);
                }
            }

            ImGui.EndMenu();
        }

        Remember("Prop");

        if (IconMenu.Begin(Icons.LightPoint, "Light"))
        {
            foreach ((LightKind kind, string label) in LightMenu.Kinds)
            {
                if (Entry(Icons.For(kind), label))
                {
                    Choose(new AddChoice(Light: kind), at);
                }
            }

            ImGui.EndMenu();
        }

        Remember("Light");
    }

    private static bool Entry(Icons.Painter icon, string name)
    {
        bool chosen = IconMenu.Item(icon, name);
        Rects[name] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        return chosen;
    }

    /// <summary>A submenu's own entry, once its list is closed again: ending a window puts back the last item before it.</summary>
    private static void Remember(string name) => Rects[name] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

    public static Icons.Painter IconFor(ShapeKind shape) => shape switch
    {
        ShapeKind.Voxel => Icons.TemplateVoxel,
        ShapeKind.Plane => Icons.TemplateGround,
        ShapeKind.Wall => Icons.ShapeWall,
        ShapeKind.Sphere => Icons.ShapeSphere,
        ShapeKind.Cylinder => Icons.ShapeCylinder,
        ShapeKind.Cone => Icons.ShapeCone,
        ShapeKind.Pyramid => Icons.ShapePyramid,
        ShapeKind.Torus => Icons.ShapeTorus,
        ShapeKind.Stairs => Icons.ShapeStairs,
        ShapeKind.Arch => Icons.ShapeArch,
        _ => Icons.ObjectTab,
    };

    public static Icons.Painter IconFor(PropKind prop) => prop switch
    {
        PropKind.Barrel => Icons.PropBarrel,
        PropKind.Table => Icons.PropTable,
        PropKind.Chair => Icons.PropChair,
        PropKind.Tree => Icons.PropTree,
        PropKind.Fence => Icons.PropFence,
        _ => Icons.PropCrate,
    };

    /// <summary>The key that opens the menu, for tooltips that point to it.</summary>
    public static string Hint => Shortcut.Hint(EditorAction.AddMenu);
}
