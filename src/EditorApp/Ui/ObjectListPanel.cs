using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Input;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The outliner: every object in the level, then every light, one row each. Loop Cut, Extrude's
/// Create sub-mode and Duplicate all produce objects, so without a list the level can quietly grow
/// pieces the designer never sees named anywhere.
///
/// A row is laid out the way Blender's is: what kind of thing it is as an icon just left of its name,
/// and the switches stacked at the right edge — lock, then the eye (for a light, its on switch). An
/// object's voxel count sits dim between the name and the switches.
///
/// Click to focus an object or pick a light, double-click or F2 to rename, right-click for the rest.
/// Hovering an object's row outlines it in the viewport, and a light's lights up its icon, so a name
/// can be matched to a shape without guessing.
///
/// Children are listed under their parents, indented, behind an arrow that folds them away. Drag a
/// row onto an object's row to make it that object's child, as in Blender; drag it below the list
/// to free it.
/// </summary>
public static class ObjectListPanel
{
    /// <summary>Between rows. Tighter than the theme's spacing: a list reads as one thing, not as separate controls.</summary>
    private const float RowGap = 2f;

    /// <summary>Between the switches at the end of a row.</summary>
    private const float ToggleGap = 1f;

    private const string RowPayload = "blockage-row";

    private static readonly Dictionary<(int Id, string Toggle), (Vector2 Min, Vector2 Max)> ToggleRects = [];

    private static readonly Dictionary<int, (Vector2 Min, Vector2 Max)> RowRects = [];

    /// <summary>Parents whose children are folded away.</summary>
    private static readonly HashSet<int> Folded = [];

    /// <summary>Where a row's switch — "lock" or "eye" — or its "fold" arrow was drawn last frame, for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max)? ToggleRect(int id, string toggle) =>
        ToggleRects.TryGetValue((id, toggle), out var rect) ? rect : null;

    /// <summary>Where a row's name was drawn last frame — what a click or a drag takes hold of.</summary>
    public static (Vector2 Min, Vector2 Max)? RowRect(int id) => RowRects.TryGetValue(id, out var rect) ? rect : null;

    /// <summary>Where the space below the rows was drawn last frame: a row dropped there is freed from its parent.</summary>
    public static (Vector2 Min, Vector2 Max)? DropSpaceRect { get; private set; }

    /// <summary>Whether a parent's children are folded away in the list.</summary>
    public static bool IsFolded(int id) => Folded.Contains(id);

    /// <summary>
    /// Whether any row is under another, so the list is drawn as a tree. A level with none is drawn
    /// as the plain list it always was, without a column of arrows that are never there.
    /// </summary>
    private static bool _tree;

    /// <summary>The row being dragged, and what letting go of it where it is would do.</summary>
    private static int _draggedId;
    private static int _draggedFrame = -1;
    private static string? _dropHint;

    private static int _renamingId;
    private static string _renameBuffer = string.Empty;
    private static bool _renameJustStarted;
    private static int _lastFocusId;
    private static int _lastLightId;

    /// <summary>The object or light whose row the mouse is over, for the viewport to mark. 0 for none.</summary>
    public static int HoveredId { get; private set; }

    /// <summary>For another list that names objects — the Parent menu — to mark the one under the mouse the same way.</summary>
    public static void MarkHovered(int id) => HoveredId = id;

    /// <summary>Filters in the Outliner's header: a level with many lights can hide them to find its objects, and back.</summary>
    public static bool ShowObjects { get; set; } = true;

    public static bool ShowLights { get; set; } = true;

    /// <summary>A rename asked for and not yet begun — the shell shows the sidebar, if hidden, so it can be.</summary>
    public static bool RenamePending => _renameJustStarted;

    public static void StartRename(VoxelObject o) => StartRename(o.Id, o.Name);

    public static void StartRename(SceneLight light) => StartRename(light.Id, light.Name);

    public static void StartRename(IPlaceable thing) => StartRename(thing.Id, thing.Name);

    public static void StartRename(int id, string name)
    {
        _renamingId = id;
        _renameBuffer = name;
        _renameJustStarted = true;
    }

    /// <summary>The rows in the order they were last drawn, top to bottom, for a Shift-click's range.</summary>
    private static List<int> _shownOrder = [];

    private static List<int> _drawingOrder = [];

    /// <summary>The Outliner's header and list, filling the space it is given; the list scrolls within it.</summary>
    public static void Draw(EditorSession session, FlyCamera camera, Vector2 size)
    {
        HoveredId = 0;
        _drawingOrder.Clear();

        // Only what is drawn this frame: a row folded away has no place to aim at.
        RowRects.Clear();
        ToggleRects.Clear();
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
            // Children are listed under their parents only while the parents are listed at all.
            _tree = ShowObjects
                && (scene.Objects.Any(o => scene.ParentOf(o) is not null) || (ShowLights && scene.Lights.Any(l => scene.ParentOf(l) is not null)));

            // Whatever was picked elsewhere is shown, even if it was folded away under its parent.
            if (focusMoved || lightMoved)
            {
                Reveal(scene, lightMoved && session.SelectedLightId != 0 ? session.SelectedLightId : scene.FocusId);
            }

            _dropHint = null;

            if (ShowObjects)
            {
                foreach (VoxelObject o in scene.Objects.ToArray())
                {
                    if (!_tree || scene.ParentOf(o) is null)
                    {
                        DrawObjectBranch(session, camera, o, row, focusMoved, lightMoved, 0);
                    }
                }
            }

            if (ShowLights)
            {
                foreach (SceneLight light in scene.Lights.ToArray())
                {
                    if (!_tree || scene.ParentOf(light) is null)
                    {
                        ImGui.PushID(light.Id);
                        DrawLightRow(session, camera, light, row, lightMoved, 0);
                        ImGui.PopID();
                    }
                }
            }

            if (ShowLights)
            {
                foreach (SceneCamera sceneCamera in scene.Cameras.ToArray())
                {
                    ImGui.PushID(sceneCamera.Id);
                    DrawCameraRow(session, sceneCamera, row);
                    ImGui.PopID();
                }
            }

            DrawDropSpace(session, row);
            DrawDragHint(session);
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        (_shownOrder, _drawingOrder) = (_drawingOrder, _shownOrder);
    }

    /// <summary>
    /// A click on a row, as in Blender's outliner: alone it selects only that row's object or light;
    /// Ctrl adds it, makes it active, or — when it is the active one — lets it go; Shift selects every
    /// row from the active one to it.
    /// </summary>
    private static bool ClickRow(EditorSession session, int id)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (io.KeyShift
            && _shownOrder.IndexOf(session.ActiveId) is >= 0 and var from
            && _shownOrder.IndexOf(id) is >= 0 and var to)
        {
            session.SelectMany(
                _shownOrder.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1),
                io.KeyCtrl ? SelectionOperation.Add : SelectionOperation.Replace);
            return true;
        }

        if (io.KeyCtrl)
        {
            return session.ClickSelect(id, extend: true);
        }

        return session.Scene.Find(id) is not null ? session.ChooseObject(id) : session.SelectLight(id);
    }

    /// <summary>An object's row, then — unless folded — its children's, each a step further in.</summary>
    private static void DrawObjectBranch(EditorSession session, FlyCamera camera, VoxelObject o, float row, bool focusMoved, bool lightMoved, int depth)
    {
        VoxelScene scene = session.Scene;
        IPlaceable[] children = _tree ? [.. scene.ChildrenOf(o.Id).Where(c => c is VoxelObject || ShowLights)] : [];

        ImGui.PushID(o.Id);
        DrawObjectRow(session, camera, o, row, focusMoved, depth, children.Length > 0);
        ImGui.PopID();

        // A loop cannot be made, but a list that followed one would never end; the depth bounds it.
        if (children.Length == 0 || Folded.Contains(o.Id) || depth >= scene.Objects.Count)
        {
            return;
        }

        foreach (IPlaceable child in children)
        {
            switch (child)
            {
                case VoxelObject inner:
                    DrawObjectBranch(session, camera, inner, row, focusMoved, lightMoved, depth + 1);
                    break;

                case SceneLight light:
                    ImGui.PushID(light.Id);
                    DrawLightRow(session, camera, light, row, lightMoved, depth + 1);
                    ImGui.PopID();
                    break;
            }
        }
    }

    /// <summary>Unfolds every parent above something, so its row can be seen.</summary>
    private static void Reveal(VoxelScene scene, int id)
    {
        IPlaceable? step = scene.FindPlaceable(id);

        for (int hops = 0; step is not null && scene.ParentOf(step) is { } parent && hops <= scene.Objects.Count; hops++)
        {
            Folded.Remove(parent.Id);
            step = parent;
        }
    }

    private static float Indent(float row) => MathF.Round(row * 0.8f);

    /// <summary>
    /// The start of a row in a tree: its depth's indent, and the arrow that folds its children away —
    /// or the arrow's space, so names at one depth line up whether or not they have children.
    /// </summary>
    private static void DrawLead(int id, int depth, bool hasChildren, float row)
    {
        if (!_tree)
        {
            return;
        }

        float start = ImGui.GetCursorPosX() + (depth * Indent(row));

        if (!hasChildren)
        {
            ImGui.SetCursorPosX(start + Indent(row));
            return;
        }

        ImGui.SetCursorPosX(start);

        bool folded = Folded.Contains(id);
        if (ImGui.InvisibleButton("##fold", new Vector2(Indent(row), row)))
        {
            if (folded)
            {
                Folded.Remove(id);
            }
            else
            {
                Folded.Add(id);
            }
        }

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        ToggleRects[(id, "fold")] = (min, max);

        bool hovered = ImGui.IsItemHovered();
        uint colour = ImGui.ColorConvertFloat4ToU32(hovered ? Theme.Text : Theme.TextDim);
        Icons.Painter arrow = folded ? Icons.ChevronRight : Icons.ChevronDown;
        arrow(new ImGuiIconCanvas(ImGui.GetWindowDrawList(), colour, 1.3f), (min + max) * 0.5f, row * 0.26f);

        if (hovered)
        {
            ImGui.SetTooltip(folded ? "Show what is under it" : "Fold what is under it away");
        }

        ImGui.SameLine(0f, 0f);
    }

    /// <summary>A row's name can be dragged: onto an object to parent to it, below the list to be freed.</summary>
    private static void DragSource(int id)
    {
        // The tooltip is drawn once the rows are, when it is known what is under the mouse.
        if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
        {
            return;
        }

        _draggedId = id;
        _draggedFrame = ImGui.GetFrameCount();
        ImGui.SetDragDropPayload(RowPayload, IntPtr.Zero, 0);
        ImGui.EndDragDropSource();
    }

    /// <summary>An object's row takes a dragged row as its child, if it can be one.</summary>
    private static void DropOnto(EditorSession session, VoxelObject parent)
    {
        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        if (parent.Id != _draggedId && session.Scene.FindPlaceable(_draggedId) is { } dragged)
        {
            bool already = session.Scene.ParentOf(dragged)?.Id == parent.Id;
            string? problem = already ? $"{dragged.Name} is under {parent.Name} already." : session.ParentProblem(dragged.Id, parent.Id);
            _dropHint = problem ?? $"Parent {dragged.Name} to {parent.Name}";

            if (problem is null && Delivered(ImGui.AcceptDragDropPayload(RowPayload)))
            {
                ParentMenu.Parent(session, dragged, parent);
            }
        }

        ImGui.EndDragDropTarget();
    }

    /// <summary>The space below the rows, where a dragged row is let go to be freed from its parent.</summary>
    private static void DrawDropSpace(EditorSession session, float row)
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        ImGui.InvisibleButton("##drop-space", new Vector2(MathF.Max(available.X, 1f), MathF.Max(available.Y, row)));
        DropSpaceRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        if (session.Scene.FindPlaceable(_draggedId) is { } dragged && session.Scene.ParentOf(dragged) is { } parent)
        {
            _dropHint = $"Clear parent: free {dragged.Name} from {parent.Name}";

            if (Delivered(ImGui.AcceptDragDropPayload(RowPayload)))
            {
                ParentMenu.Clear(session, dragged);
            }
        }

        ImGui.EndDragDropTarget();
    }

    /// <summary>While a row is dragged, what letting go would do, beside the mouse.</summary>
    private static void DrawDragHint(EditorSession session)
    {
        if (_draggedFrame != ImGui.GetFrameCount() || session.Scene.FindPlaceable(_draggedId) is not { } dragged)
        {
            return;
        }

        ImGui.SetTooltip(_dropHint ?? $"{dragged.Name}\nDrop it on an object to make that its parent,\nor below the list to free it.");
    }

    private static unsafe bool Delivered(ImGuiPayloadPtr payload) => payload.NativePtr != null && payload.Delivery;

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
        bool anyLocked = session.Scene.Objects.Any(o => o.Locked) || session.Scene.Lights.Any(l => l.Locked);
        int buttons = 3 + (anyHidden ? 1 : 0) + (anyLocked ? 1 : 0);
        float width = (buttons * button) + ((buttons - 1) * spacing);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - width);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(spacing, 0f));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);

        if (anyHidden)
        {
            if (IconButton.Draw("show-all", Icons.Eye, active: false, $"Show everything hidden{Shortcut.Hint(EditorAction.ShowAll)}", button))
            {
                session.ShowAllObjects();
                foreach (SceneLight light in session.Scene.Lights)
                {
                    session.SetLightVisible(light.Id, true);
                }
            }

            ImGui.SameLine();
        }

        if (anyLocked)
        {
            if (IconButton.Draw("unlock-all", Icons.Unlocked, active: false, $"Unlock everything{Shortcut.Hint(EditorAction.UnlockAll)}", button))
            {
                session.UnlockAll();
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
        if (IconButton.Draw("add", Icons.Plus, active: false, $"Add a shape, prop or light{AddMenu.Hint}", button, hasAlternatives: true))
        {
            ImGui.OpenPopup("##outliner-add");
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        if (ImGui.BeginPopup("##outliner-add"))
        {
            AddMenu.DrawItems(at: null);
            ImGui.EndPopup();
        }

        return ImGui.GetCursorPosY() - start;
    }

    private static void DrawObjectRow(EditorSession session, FlyCamera camera, VoxelObject o, float row, bool focusMoved, int depth, bool hasChildren)
    {
        // Selected rows are marked, and the active one takes the accent. A hidden object cannot be
        // selected, but clicked it is still the one Properties shows, and is marked as that.
        bool active = o.Id == session.ActiveId;
        bool focused = session.IsSelected(o.Id) || (active && !o.Visible);
        bool emphasised = focused && active;
        _drawingOrder.Add(o.Id);
        DrawLead(o.Id, depth, hasChildren, row);
        float width = NameWidth(row);

        if (_renamingId == o.Id)
        {
            DrawRenameField(session, o.Id, width);
        }
        else
        {
            if (DrawSelectable(focused, emphasised, row, width))
            {
                // A locked object is not chosen by a click here either; the tooltip says why.
                if (ClickRow(session, o.Id) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    StartRename(o);
                }
            }

            RowRects[o.Id] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            DragSource(o.Id);
            DropOnto(session, o);

            if (emphasised && focusMoved)
            {
                ImGui.SetScrollHereY(0.5f);
            }

            if (ImGui.IsItemHovered())
            {
                HoveredId = o.Id;
                ImGui.SetTooltip(
                    !o.Visible ? "Hidden - not drawn, picked or exported."
                    : o.Locked ? "Locked - drawn and exported, but not picked or changed.\nUnlock it to work on it."
                    : "Ctrl-click adds, Shift-click takes a range.\nDouble-click or F2 to rename, right-click for more.");
            }

            DrawLabel(Icons.ObjectTab, o.Name, o.Visible, emphasised, $"{o.Grid.SolidCount:N0}");
            DrawObjectMenu(session, camera, o);
        }

        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        if (DrawToggle(o.Id, "lock", o.Locked ? Icons.Lock : Icons.Unlocked, o.Locked, row, o.Locked ? "Locked - click to unlock" : $"Lock - not picked or changed in the viewport{Shortcut.Hint(EditorAction.Lock)}"))
        {
            session.SetObjectLocked(o.Id, !o.Locked);
        }

        ImGui.SameLine(0f, ToggleGap);
        if (DrawToggle(o.Id, "eye", o.Visible ? Icons.Eye : Icons.EyeClosed, !o.Visible, row, o.Visible ? $"Hide{Shortcut.Hint(EditorAction.Hide)}" : $"Show{Shortcut.Hint(EditorAction.Hide)}"))
        {
            session.SetObjectVisible(o.Id, !o.Visible);
        }
    }

    private static void DrawLightRow(EditorSession session, FlyCamera camera, SceneLight light, float row, bool lightMoved, int depth)
    {
        bool picked = session.IsSelected(light.Id);
        bool active = picked && light.Id == session.SelectedLightId;
        _drawingOrder.Add(light.Id);
        DrawLead(light.Id, depth, hasChildren: false, row);
        float width = NameWidth(row);

        if (_renamingId == light.Id)
        {
            DrawRenameField(session, light.Id, width);
        }
        else
        {
            if (DrawSelectable(picked, active, row, width)
                && ClickRow(session, light.Id)
                && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                StartRename(light);
            }

            RowRects[light.Id] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            DragSource(light.Id);

            if (active && lightMoved)
            {
                ImGui.SetScrollHereY(0.5f);
            }

            if (ImGui.IsItemHovered())
            {
                HoveredId = light.Id;
                ImGui.SetTooltip(
                    !light.Visible ? "Switched off - lights nothing."
                    : light.Locked ? "Locked - still shines, but is not picked, moved or aimed."
                    : "A light - move and aim it with the Transform tool.");
            }

            DrawLabel(Icons.For(light.Kind), light.Name, light.Visible, active, null);
            DrawLightMenu(session, camera, light);
        }

        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        if (DrawToggle(light.Id, "lock", light.Locked ? Icons.Lock : Icons.Unlocked, light.Locked, row, light.Locked ? "Locked - click to unlock" : $"Lock - not picked, moved or aimed{Shortcut.Hint(EditorAction.Lock)}"))
        {
            session.SetLightLocked(light.Id, !light.Locked);
        }

        ImGui.SameLine(0f, ToggleGap);
        if (DrawToggle(light.Id, "eye", light.Visible ? Icons.Eye : Icons.EyeClosed, !light.Visible, row, light.Visible ? $"Switch off{Shortcut.Hint(EditorAction.Hide)}" : $"Switch on{Shortcut.Hint(EditorAction.Hide)}"))
        {
            session.SetLightVisible(light.Id, !light.Visible);
        }
    }

    /// <summary>
    /// A camera's row: picked by a click, renamed by a double one; its switch makes it the camera
    /// renders are seen from, and its menu looks through it or moves it to the view.
    /// </summary>
    private static void DrawCameraRow(EditorSession session, SceneCamera sceneCamera, float row)
    {
        bool picked = session.PickedCameraId == sceneCamera.Id;
        bool active = session.Scene.ActiveCameraId == sceneCamera.Id;
        DrawLead(sceneCamera.Id, 0, hasChildren: false, row);
        float width = NameWidth(row);

        if (_renamingId == sceneCamera.Id)
        {
            DrawRenameField(session, sceneCamera.Id, width);
        }
        else
        {
            if (DrawSelectable(picked, picked, row, width))
            {
                session.PickCamera(sceneCamera.Id);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    StartRename(sceneCamera.Id, sceneCamera.Name);
                }
            }

            RowRects[sceneCamera.Id] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (ImGui.IsItemHovered())
            {
                HoveredId = sceneCamera.Id;
                ImGui.SetTooltip($"A camera - look through it with{Shortcut.Hint(EditorAction.ViewCamera)} when renders are seen from it, or from its menu.");
            }

            DrawLabel(Icons.Camera, sceneCamera.Name, true, picked, null);
            DrawCameraMenu(session, sceneCamera);
        }

        // Two switches' room, as the other rows have: the render switch sits where their eye does.
        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X + row + ToggleGap);
        if (DrawToggle(sceneCamera.Id, "render", active ? Icons.CameraActive : Icons.Camera, active, row, active ? "Renders are seen from this camera - click to render the view instead" : "Render from this camera"))
        {
            session.SetActiveCamera(active ? 0 : sceneCamera.Id);
        }
    }

    private static void DrawCameraMenu(EditorSession session, SceneCamera sceneCamera)
    {
        if (!ImGui.BeginPopupContextItem("##camera-menu"))
        {
            return;
        }

        if (ImGui.IsWindowAppearing())
        {
            session.PickCamera(sceneCamera.Id);
        }

        if (ImGui.MenuItem("Look Through"))
        {
            CameraPropertiesPanel.LookThrough(sceneCamera);
        }

        if (ImGui.MenuItem("Move to View"))
        {
            CameraPropertiesPanel.MoveToView(sceneCamera);
        }

        bool active = session.Scene.ActiveCameraId == sceneCamera.Id;
        if (ImGui.MenuItem("Render from This Camera", string.Empty, active))
        {
            session.SetActiveCamera(active ? 0 : sceneCamera.Id);
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Rename", Shortcut.Of(EditorAction.Rename)))
        {
            StartRename(sceneCamera.Id, sceneCamera.Name);
        }

        if (ImGui.MenuItem("Delete"))
        {
            session.DeleteCamera(sceneCamera.Id);
        }

        ImGui.EndPopup();
    }

    /// <summary>What is left of a row for the name, once the switches have their place at the end.</summary>
    private static float NameWidth(float row) =>
        MathF.Max(ImGui.GetContentRegionAvail().X - (row * 2f) - ToggleGap - ImGui.GetStyle().ItemSpacing.X, row);

    private static bool DrawSelectable(bool selected, bool emphasised, float row, float width)
    {
        // The accent for the active one; the rest of the selection a faint wash of it, so it can be
        // told from the rows around it at a glance.
        ImGui.PushStyleColor(ImGuiCol.Header, emphasised ? Theme.Accent : Theme.Accent with { W = 0.32f });

        // The label is drawn by hand after the selectable, so the kind can sit as an icon before the
        // name and the name can dim when hidden. Narrower than the row: the switches at its end are
        // buttons of their own, and a selectable under them would take their clicks.
        bool clicked = ImGui.Selectable("##name", selected, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(width, row));
        ImGui.PopStyleColor();

        return clicked;
    }

    /// <summary>
    /// One of the switches at the end of a row. Flat, not a raised button: a column of them down the
    /// list would read as a stack of controls rather than as a property of each row. An invisible
    /// button with the icon painted on, too, because a framed button lowers the text line it sits on,
    /// and the whole row grows by that much.
    ///
    /// A switch in its ordinary state — shown, unlocked — is drawn faintly, so a list of fifty rows
    /// is not fifty rows of icons; one that is off the ordinary is drawn plainly, to be found.
    /// </summary>
    private static bool DrawToggle(int id, string toggle, Icons.Painter icon, bool unusual, float row, string tooltip)
    {
        bool clicked = ImGui.InvisibleButton($"##{toggle}", new Vector2(row));
        bool hovered = ImGui.IsItemHovered();

        Vector2 min = ImGui.GetItemRectMin();
        ToggleRects[(id, toggle)] = (min, ImGui.GetItemRectMax());
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        if (hovered)
        {
            drawList.AddRectFilled(min, min + new Vector2(row), ImGui.GetColorU32(ImGuiCol.ButtonHovered), 4f);
        }

        Vector4 tint = hovered ? Theme.Text
            : unusual ? Theme.Text with { W = 0.85f }
            : Theme.TextDim with { W = toggle == "eye" ? 0.8f : 0.35f };

        icon(new ImGuiIconCanvas(drawList, ImGui.ColorConvertFloat4ToU32(tint), 1.4f), min + new Vector2(row * 0.5f), row * 0.3f);

        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }

    /// <summary>The kind as an icon, the name after it, and a count — if there is one — at the far end.</summary>
    private static void DrawLabel(Icons.Painter kind, string name, bool visible, bool emphasised, string? count)
    {
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float height = max.Y - min.Y;
        float y = min.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f);
        float padding = ImGui.GetStyle().FramePadding.X;

        // Over the accent, light whatever the theme; elsewhere the theme's own text.
        Vector4 nameColour = !visible ? Theme.TextDim with { W = 0.6f } : emphasised ? Theme.TextOnAccent : Theme.Text with { W = 0.9f };
        uint dim = ImGui.ColorConvertFloat4ToU32(emphasised
            ? Theme.TextOnAccent with { W = 0.75f }
            : Theme.TextDim with { W = visible ? 0.75f : 0.45f });

        float iconRadius = height * 0.28f;
        var iconCentre = new Vector2(min.X + padding + iconRadius, min.Y + (height * 0.5f));
        kind(new ImGuiIconCanvas(drawList, dim, 1.2f), iconCentre, iconRadius);

        float nameStart = iconCentre.X + iconRadius + (padding * 0.75f);
        float countWidth = count is not null ? ImGui.CalcTextSize(count).X + padding : 0f;

        // Clipped short of the count, so a long name runs under nothing rather than over it.
        drawList.PushClipRect(new Vector2(nameStart, min.Y), new Vector2(max.X - countWidth - padding, max.Y), true);
        drawList.AddText(new Vector2(nameStart, y), ImGui.ColorConvertFloat4ToU32(nameColour), name);
        drawList.PopClipRect();

        if (count is not null)
        {
            drawList.AddText(new Vector2(max.X - countWidth, y), dim, count);
        }
    }

    private static void DrawObjectMenu(EditorSession session, FlyCamera camera, VoxelObject o)
    {
        if (!ImGui.BeginPopupContextItem("##object-menu"))
        {
            return;
        }

        // The menu acts on the selection, as the viewport's does; a row outside it becomes it.
        if (ImGui.IsWindowAppearing() && !session.IsSelected(o.Id))
        {
            session.ChooseObject(o.Id);
        }

        if (session.IsSelected(o.Id))
        {
            ObjectMenu.DrawItems(session, camera);
            ImGui.EndPopup();
            return;
        }

        // Hidden or locked, it cannot be selected: what can be done to the row itself.
        if (ImGui.MenuItem("Rename", Shortcut.Of(EditorAction.Rename)))
        {
            StartRename(o);
        }

        if (ImGui.MenuItem(o.Visible ? "Hide" : "Show", Shortcut.Of(EditorAction.Hide)))
        {
            session.SetObjectVisible(o.Id, !o.Visible);
        }

        if (ImGui.MenuItem(o.Locked ? "Unlock" : "Lock"))
        {
            session.SetObjectLocked(o.Id, !o.Locked);
        }

        ImGui.Separator();
        ParentMenu.DrawSubmenu(session, o);

        ImGui.Separator();

        if (ImGui.MenuItem("Delete", Shortcut.Of(EditorAction.Delete)))
        {
            session.DeleteObject(o.Id);
        }

        ImGui.EndPopup();
    }

    private static void DrawLightMenu(EditorSession session, FlyCamera camera, SceneLight light)
    {
        if (!ImGui.BeginPopupContextItem("##light-menu"))
        {
            return;
        }

        // As an object's row: one outside the selection becomes it.
        if (ImGui.IsWindowAppearing() && !session.IsSelected(light.Id))
        {
            session.SelectLight(light.Id);
        }

        if (ImGui.MenuItem("Rename", Shortcut.Of(EditorAction.Rename)))
        {
            StartRename(light);
        }

        if (ImGui.MenuItem("Duplicate", Shortcut.Of(EditorAction.Duplicate)))
        {
            if (session.IsSelected(light.Id))
            {
                ObjectMenu.Duplicate(session, camera);
            }
            else
            {
                LightMenu.Duplicate(session, camera, light);
            }
        }

        if (ImGui.MenuItem(light.Visible ? "Switch off" : "Switch on", Shortcut.Of(EditorAction.Hide)))
        {
            session.SetLightVisible(light.Id, !light.Visible);
        }

        if (ImGui.MenuItem(light.Locked ? "Unlock" : "Lock"))
        {
            session.SetLightLocked(light.Id, !light.Locked);
        }

        ImGui.Separator();
        ParentMenu.DrawSubmenu(session, light);

        ImGui.Separator();

        if (ImGui.MenuItem("Delete", Shortcut.Of(EditorAction.Delete)))
        {
            if (session.IsSelected(light.Id))
            {
                session.DeleteSelected();
            }
            else
            {
                session.DeleteLight(light.Id);
            }
        }

        ImGui.EndPopup();
    }

    private static void DrawRenameField(EditorSession session, int id, float width)
    {
        ImGui.SetNextItemWidth(width);

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
                else if (session.Scene.FindCamera(id) is not null)
                {
                    session.RenameCamera(id, _renameBuffer);
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
