using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The outliner: every object in the level, one row each. Loop Cut, Extrude's Create sub-mode and
/// Duplicate all produce objects, so without a list the level can quietly grow pieces the designer
/// never sees named anywhere.
///
/// A row is an eye, a name and a voxel count. Click to focus, double-click or F2 to rename, right-click
/// for the rest. Hovering a row outlines its object in the viewport, so a name can be matched to a
/// shape without guessing.
/// </summary>
public static class ObjectListPanel
{
    /// <summary>Rows shown before the list scrolls, so a level of many pieces cannot push the palette off the bottom.</summary>
    private const int MaxVisibleRows = 10;

    /// <summary>Between rows. Tighter than the theme's spacing: a list reads as one thing, not as separate controls.</summary>
    private const float RowGap = 2f;

    private static int _renamingId;
    private static string _renameBuffer = string.Empty;
    private static bool _renameJustStarted;
    private static int _lastFocusId;

    /// <summary>The object whose row the mouse is over, for the viewport to outline. 0 for none.</summary>
    public static int HoveredId { get; private set; }

    /// <summary>
    /// True while a rename has been asked for from outside the panel — F2, or the Object menu — and
    /// the panel may be folded away. The shell opens the section so the field can be seen.
    /// </summary>
    public static bool WantsToBeOpen => _renameJustStarted;

    public static void StartRename(VoxelObject o)
    {
        _renamingId = o.Id;
        _renameBuffer = o.Name;
        _renameJustStarted = true;
    }

    public static void Draw(EditorSession session, FlyCamera camera)
    {
        HoveredId = 0;
        VoxelScene scene = session.Scene;

        float row = ImGui.GetFrameHeight();
        int rows = Math.Clamp(scene.Objects.Count, 1, MaxVisibleRows);

        // Focus changed from elsewhere — a click in the viewport, a duplicate — so the list follows
        // it to wherever the row is.
        bool focusMoved = scene.FocusId != _lastFocusId;
        _lastFocusId = scene.FocusId;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, RowGap));

        if (ImGui.BeginChild("##outliner", new Vector2(-1f, (rows * (row + RowGap)) - RowGap)))
        {
            foreach (VoxelObject o in scene.Objects.ToArray())
            {
                ImGui.PushID(o.Id);
                DrawRow(session, camera, o, row, focusMoved);
                ImGui.PopID();
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        DrawFooter(session);
    }

    private static void DrawRow(EditorSession session, FlyCamera camera, VoxelObject o, float row, bool focusMoved)
    {
        bool focused = o.Id == session.Scene.FocusId;

        DrawEye(session, o, row);
        ImGui.SameLine();

        if (_renamingId == o.Id)
        {
            DrawRenameField(session, o);
            return;
        }

        if (focused)
        {
            // The focused object is the one every tool acts on, so it is the one thing in the
            // interface that earns the accent.
            ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent);
        }

        // The label is drawn by hand after the selectable, so the voxel count can sit at the right
        // edge and the name can dim when the object is hidden.
        if (ImGui.Selectable("##name", focused, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(0f, row)))
        {
            session.ChooseObject(o.Id);

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                StartRename(o);
            }
        }

        if (focused)
        {
            ImGui.PopStyleColor();

            if (focusMoved)
            {
                ImGui.SetScrollHereY(0.5f);
            }
        }

        if (ImGui.IsItemHovered())
        {
            HoveredId = o.Id;
            ImGui.SetTooltip(o.Visible
                ? "Double-click or F2 to rename, right-click for more."
                : "Hidden - not drawn, picked or exported.");
        }

        DrawLabel(o, focused);
        DrawContextMenu(session, camera, o);
    }

    /// <summary>
    /// Flat, not a raised button: a column of them down the list would read as a stack of controls
    /// rather than as a property of each row. An invisible button with the icon painted on, too,
    /// because a framed button lowers the text line it sits on, and the whole row grows by that much.
    /// </summary>
    private static void DrawEye(EditorSession session, VoxelObject o, float row)
    {
        bool clicked = ImGui.InvisibleButton("##eye", new Vector2(row));
        bool hovered = ImGui.IsItemHovered();

        Vector2 min = ImGui.GetItemRectMin();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        if (hovered)
        {
            drawList.AddRectFilled(min, min + new Vector2(row), ImGui.GetColorU32(ImGuiCol.ButtonHovered), 4f);
        }

        Vector4 tint = hovered ? Theme.Text : o.Visible ? Theme.TextDim : Theme.TextDim with { W = 0.5f };
        Icons.Painter icon = o.Visible ? Icons.Eye : Icons.EyeClosed;
        icon(new ImGuiIconCanvas(drawList, ImGui.ColorConvertFloat4ToU32(tint), 1.4f), min + new Vector2(row * 0.5f), row * 0.3f);

        if (hovered)
        {
            ImGui.SetTooltip(o.Visible ? "Hide  (H)" : "Show  (H)");
        }

        if (clicked)
        {
            session.SetObjectVisible(o.Id, !o.Visible);
        }
    }

    private static void DrawLabel(VoxelObject o, bool focused)
    {
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float y = min.Y + ((max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f);

        Vector4 nameColour = !o.Visible ? Theme.TextDim with { W = 0.6f } : focused ? Theme.Text : Theme.Text with { W = 0.9f };
        string count = $"{o.Grid.SolidCount:N0}";
        float countWidth = ImGui.CalcTextSize(count).X;

        // Clipped short of the count, so a long name runs under nothing rather than over the number.
        float padding = ImGui.GetStyle().FramePadding.X;
        Vector2 clipMax = new(max.X - countWidth - (padding * 3f), max.Y);
        drawList.PushClipRect(min, clipMax, true);
        drawList.AddText(new Vector2(min.X + padding, y), ImGui.ColorConvertFloat4ToU32(nameColour), o.Name);
        drawList.PopClipRect();

        drawList.AddText(
            new Vector2(max.X - countWidth - padding, y),
            ImGui.ColorConvertFloat4ToU32(Theme.TextDim with { W = focused ? 0.9f : 0.6f }),
            count);
    }

    private static void DrawContextMenu(EditorSession session, FlyCamera camera, VoxelObject o)
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

    private static void DrawRenameField(EditorSession session, VoxelObject o)
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
                session.RenameObject(o.Id, _renameBuffer);
            }

            _renamingId = 0;
        }
    }

    private static void DrawFooter(EditorSession session)
    {
        VoxelScene scene = session.Scene;
        int hidden = scene.Objects.Count(o => !o.Visible);

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(hidden > 0
            ? $"{scene.Objects.Count} objects, {hidden} hidden · {scene.SolidCount:N0} voxels"
            : $"{scene.Objects.Count} object(s) · {scene.SolidCount:N0} voxels");

        if (hidden > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Show all"))
            {
                session.ShowAllObjects();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Alt+H");
            }
        }
    }
}
