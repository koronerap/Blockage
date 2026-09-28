using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The outliner: every object in the level, then every light, one row each. Loop Cut, Extrude's
/// Create sub-mode and Duplicate all produce objects, so without a list the level can quietly grow
/// pieces the designer never sees named anywhere.
///
/// An object's row is an eye, a name and a voxel count; a light's is a switch, a name and its kind.
/// Click to focus an object or pick a light, double-click or F2 to rename, right-click for the rest.
/// Hovering an object's row outlines it in the viewport, and a light's lights up its icon, so a name
/// can be matched to a shape without guessing.
/// </summary>
public static class ObjectListPanel
{
    /// <summary>Between rows. Tighter than the theme's spacing: a list reads as one thing, not as separate controls.</summary>
    private const float RowGap = 2f;

    private static int _renamingId;
    private static string _renameBuffer = string.Empty;
    private static bool _renameJustStarted;
    private static int _lastFocusId;
    private static int _lastLightId;

    /// <summary>The object or light whose row the mouse is over, for the viewport to mark. 0 for none.</summary>
    public static int HoveredId { get; private set; }

    /// <summary>Filters in the Outliner's header: a level with many lights can hide them to find its objects, and back.</summary>
    public static bool ShowObjects { get; set; } = true;

    public static bool ShowLights { get; set; } = true;

    public static void StartRename(VoxelObject o) => StartRename(o.Id, o.Name);

    public static void StartRename(SceneLight light) => StartRename(light.Id, light.Name);

    private static void StartRename(int id, string name)
    {
        _renamingId = id;
        _renameBuffer = name;
        _renameJustStarted = true;
    }

    /// <summary>The Outliner's header and list, filling the space it is given; the list scrolls within it.</summary>
    public static void Draw(EditorSession session, FlyCamera camera, Vector2 size)
    {
        HoveredId = 0;
        VoxelScene scene = session.Scene;

        float row = ImGui.GetFrameHeight();
        float headerHeight = DrawHeader(session);

        // Focus or the picked light changed from elsewhere — a click in the viewport, a duplicate —
        // so the list follows it to wherever the row is.
        bool focusMoved = scene.FocusId != _lastFocusId;
        bool lightMoved = session.SelectedLightId != _lastLightId;
        _lastFocusId = scene.FocusId;
        _lastLightId = session.SelectedLightId;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, RowGap));

        if (ImGui.BeginChild("##outliner", new Vector2(-1f, MathF.Max(size.Y - headerHeight, row))))
        {
            if (ShowObjects)
            {
                foreach (VoxelObject o in scene.Objects.ToArray())
                {
                    ImGui.PushID(o.Id);
                    DrawObjectRow(session, camera, o, row, focusMoved);
                    ImGui.PopID();
                }
            }

            if (ShowLights)
            {
                foreach (SceneLight light in scene.Lights.ToArray())
                {
                    ImGui.PushID(light.Id);
                    DrawLightRow(session, camera, light, row, lightMoved);
                    ImGui.PopID();
                }
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// One slim row: the title, and on the right the filters, "show everything" when anything is
    /// hidden, and adding a light. Returns how much height it took.
    /// </summary>
    private static float DrawHeader(EditorSession session)
    {
        float start = ImGui.GetCursorPosY();
        float button = ImGui.GetFrameHeight() - 4f;
        float spacing = 2f;

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Outliner");

        bool anyHidden = session.Scene.Objects.Any(o => !o.Visible) || session.Scene.Lights.Any(l => !l.Visible);
        int buttons = anyHidden ? 4 : 3;
        float width = (buttons * button) + ((buttons - 1) * spacing);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - width);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(spacing, 0f));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);

        if (anyHidden)
        {
            if (IconButton.Draw("show-all", Icons.Eye, active: false, "Show everything hidden  (Alt+H)", button))
            {
                session.ShowAllObjects();
                foreach (SceneLight light in session.Scene.Lights)
                {
                    session.SetLightVisible(light.Id, true);
                }
            }

            ImGui.SameLine();
        }

        // Lit only while filtering: the ordinary state, everything listed, should not look like a setting.
        if (IconButton.Draw("filter-objects", Icons.ObjectTab, !ShowObjects, ShowObjects ? "Objects shown - click to hide them here" : "Objects hidden here - click to list them", button))
        {
            ShowObjects = !ShowObjects;
        }

        ImGui.SameLine();
        if (IconButton.Draw("filter-lights", Icons.LightPoint, !ShowLights, ShowLights ? "Lights shown - click to hide them here" : "Lights hidden here - click to list them", button))
        {
            ShowLights = !ShowLights;
        }

        ImGui.SameLine();
        if (IconButton.Draw("add-light", Icons.Plus, active: false, "Add a light", button, hasAlternatives: true))
        {
            ImGui.OpenPopup("##outliner-add");
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        if (ImGui.BeginPopup("##outliner-add"))
        {
            LightMenu.DrawItems(session);
            ImGui.EndPopup();
        }

        return ImGui.GetCursorPosY() - start;
    }

    private static void DrawObjectRow(EditorSession session, FlyCamera camera, VoxelObject o, float row, bool focusMoved)
    {
        bool focused = o.Id == session.Scene.FocusId;

        // While a light is picked, the focused object is only the one the voxel tools would act on,
        // so it keeps its mark but gives up the accent to the light.
        bool emphasised = focused && session.SelectedLightId == 0;

        DrawEye(o.Visible, row, o.Visible ? "Hide  (H)" : "Show  (H)", out bool toggled);
        if (toggled)
        {
            session.SetObjectVisible(o.Id, !o.Visible);
        }

        ImGui.SameLine();

        if (_renamingId == o.Id)
        {
            DrawRenameField(session, o.Id);
            return;
        }

        if (DrawSelectable(focused, emphasised, row))
        {
            session.ChooseObject(o.Id);

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                StartRename(o);
            }
        }

        if (emphasised && focusMoved)
        {
            ImGui.SetScrollHereY(0.5f);
        }

        if (ImGui.IsItemHovered())
        {
            HoveredId = o.Id;
            ImGui.SetTooltip(o.Visible
                ? "Double-click or F2 to rename, right-click for more."
                : "Hidden - not drawn, picked or exported.");
        }

        DrawLabel(o.Name, o.Visible, emphasised, $"{o.Grid.SolidCount:N0}", null);
        DrawObjectMenu(session, camera, o);
    }

    private static void DrawLightRow(EditorSession session, FlyCamera camera, SceneLight light, float row, bool lightMoved)
    {
        bool picked = light.Id == session.SelectedLightId;

        DrawEye(light.Visible, row, light.Visible ? "Switch off  (H)" : "Switch on  (H)", out bool toggled);
        if (toggled)
        {
            session.SetLightVisible(light.Id, !light.Visible);
        }

        ImGui.SameLine();

        if (_renamingId == light.Id)
        {
            DrawRenameField(session, light.Id);
            return;
        }

        if (DrawSelectable(picked, picked, row))
        {
            session.SelectLight(light.Id);

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                StartRename(light);
            }
        }

        if (picked && lightMoved)
        {
            ImGui.SetScrollHereY(0.5f);
        }

        if (ImGui.IsItemHovered())
        {
            HoveredId = light.Id;
            ImGui.SetTooltip(light.Visible
                ? "A light - move and aim it with the Transform tool."
                : "Switched off - lights nothing.");
        }

        DrawLabel(light.Name, light.Visible, picked, null, Icons.For(light.Kind));
        DrawLightMenu(session, camera, light);
    }

    private static bool DrawSelectable(bool selected, bool emphasised, float row)
    {
        // The accent is for the one thing the tools act on; anything else selected gets the plain mark.
        if (emphasised)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent);
        }

        // The label is drawn by hand after the selectable, so what sits at the right edge can be a
        // number or an icon and the name can dim when switched off.
        bool clicked = ImGui.Selectable("##name", selected, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(0f, row));

        if (emphasised)
        {
            ImGui.PopStyleColor();
        }

        return clicked;
    }

    /// <summary>
    /// Flat, not a raised button: a column of them down the list would read as a stack of controls
    /// rather than as a property of each row. An invisible button with the icon painted on, too,
    /// because a framed button lowers the text line it sits on, and the whole row grows by that much.
    /// </summary>
    private static void DrawEye(bool open, float row, string tooltip, out bool clicked)
    {
        clicked = ImGui.InvisibleButton("##eye", new Vector2(row));
        bool hovered = ImGui.IsItemHovered();

        Vector2 min = ImGui.GetItemRectMin();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        if (hovered)
        {
            drawList.AddRectFilled(min, min + new Vector2(row), ImGui.GetColorU32(ImGuiCol.ButtonHovered), 4f);
        }

        Vector4 tint = hovered ? Theme.Text : open ? Theme.TextDim : Theme.TextDim with { W = 0.5f };
        Icons.Painter icon = open ? Icons.Eye : Icons.EyeClosed;
        icon(new ImGuiIconCanvas(drawList, ImGui.ColorConvertFloat4ToU32(tint), 1.4f), min + new Vector2(row * 0.5f), row * 0.3f);

        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static void DrawLabel(string name, bool visible, bool emphasised, string? count, Icons.Painter? icon)
    {
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float height = max.Y - min.Y;
        float y = min.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f);
        float padding = ImGui.GetStyle().FramePadding.X;

        Vector4 nameColour = !visible ? Theme.TextDim with { W = 0.6f } : emphasised ? Theme.Text : Theme.Text with { W = 0.9f };
        uint trailing = ImGui.ColorConvertFloat4ToU32(Theme.TextDim with { W = emphasised ? 0.9f : 0.6f });

        float trailingWidth = count is not null ? ImGui.CalcTextSize(count).X : height * 0.7f;

        // Clipped short of what trails it, so a long name runs under nothing rather than over it.
        var clipMax = new Vector2(max.X - trailingWidth - (padding * 3f), max.Y);
        drawList.PushClipRect(min, clipMax, true);
        drawList.AddText(new Vector2(min.X + padding, y), ImGui.ColorConvertFloat4ToU32(nameColour), name);
        drawList.PopClipRect();

        if (count is not null)
        {
            drawList.AddText(new Vector2(max.X - trailingWidth - padding, y), trailing, count);
        }
        else if (icon is not null)
        {
            var centre = new Vector2(max.X - padding - (trailingWidth * 0.5f), min.Y + (height * 0.5f));
            icon(new ImGuiIconCanvas(drawList, trailing, 1.2f), centre, height * 0.28f);
        }
    }

    private static void DrawObjectMenu(EditorSession session, FlyCamera camera, VoxelObject o)
    {
        if (!ImGui.BeginPopupContextItem("##object-menu"))
        {
            return;
        }

        if (ImGui.MenuItem("Rename", "F2"))
        {
            StartRename(o);
        }

        // Duplicating works on the focused object, so the row's object becomes it first.
        if (ImGui.MenuItem("Duplicate", "Shift+D", false, !o.IsEmpty) && session.ChooseObject(o.Id))
        {
            ObjectMenu.Duplicate(session, camera);
        }

        if (ImGui.MenuItem(o.Visible ? "Hide" : "Show", "H"))
        {
            session.SetObjectVisible(o.Id, !o.Visible);
        }

        ObjectMenu.DrawJoinMenu(session, o);

        ImGui.Separator();

        // Refused for the last object: with no Place tool, an empty scene is a dead end.
        bool isLast = session.Scene.Objects.Count <= 1;
        if (ImGui.MenuItem("Delete", "Del", false, !isLast))
        {
            session.DeleteObject(o.Id);
        }

        if (isLast && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("The last object cannot be deleted - there would be nothing to extrude from.");
        }

        ImGui.EndPopup();
    }

    private static void DrawLightMenu(EditorSession session, FlyCamera camera, SceneLight light)
    {
        if (!ImGui.BeginPopupContextItem("##light-menu"))
        {
            return;
        }

        if (ImGui.MenuItem("Rename", "F2"))
        {
            StartRename(light);
        }

        if (ImGui.MenuItem("Duplicate", "Shift+D"))
        {
            LightMenu.Duplicate(session, camera, light);
        }

        if (ImGui.MenuItem(light.Visible ? "Switch off" : "Switch on", "H"))
        {
            session.SetLightVisible(light.Id, !light.Visible);
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Delete", "Del"))
        {
            session.DeleteLight(light.Id);
        }

        ImGui.EndPopup();
    }

    private static void DrawRenameField(EditorSession session, int id)
    {
        ImGui.SetNextItemWidth(-1f);

        // Only on the frame the rename starts. Taking focus every frame would pull it straight back
        // from wherever the user clicked to finish.
        if (_renameJustStarted)
        {
            ImGui.SetKeyboardFocusHere();
            _renameJustStarted = false;
        }

        ImGui.InputText("##rename", ref _renameBuffer, 64, ImGuiInputTextFlags.AutoSelectAll);

        // Enter or clicking away keeps the new name, as in Blender; Escape keeps the old one. ImGui
        // already puts the old text back on Escape; it is checked here too, so the rule does not rest
        // on a detail of the widget. A blank name is refused by the session and the old one stays.
        if (ImGui.IsItemDeactivated())
        {
            if (!ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                if (session.Scene.FindLight(id) is not null)
                {
                    session.RenameLight(id, _renameBuffer);
                }
                else
                {
                    session.RenameObject(id, _renameBuffer);
                }
            }

            _renamingId = 0;
        }
    }
}
