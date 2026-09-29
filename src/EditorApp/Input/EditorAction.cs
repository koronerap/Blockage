namespace EditorApp.Input;

/// <summary>
/// Everything the keyboard can do. Mouse gestures — orbiting, dragging a gizmo, Shift to add to a
/// selection — are not here: they are how the tools work rather than shortcuts to them, and they
/// stay as they are whatever the keymap.
/// </summary>
public enum EditorAction
{
    // Tools
    ToolSelect,
    ToolTransform,
    ToolMove,
    ToolRotate,
    ToolExtrude,
    ToolPaint,
    ToolLoopCut,
    ToolSculpt,
    ToolView,
    ToolOtherMode,
    ToolCycleMode,
    ToggleSnap,
    ToggleEditMode,

    // Edit
    Undo,
    Redo,
    Copy,
    Cut,
    Paste,
    Duplicate,
    Delete,
    Hide,
    ShowAll,
    Lock,
    UnlockAll,
    Rename,
    Subdivide,
    SelectAll,
    DeselectAll,
    InvertSelection,
    GrowSelection,
    ShrinkSelection,
    Separate,
    FillSelection,
    Join,
    SetParent,
    ClearParent,
    Search,
    AddMenu,
    KeepExtrude,
    Cancel,

    // File
    NewLevel,
    Open,
    Save,
    SaveAs,
    Export,

    // View
    FrameLevel,
    FrameFocused,
    ViewFront,
    ViewBack,
    ViewRight,
    ViewLeft,
    ViewTop,
    ViewBottom,
    ViewTurnRound,
    ToggleOrthographic,
    OrbitLeft,
    OrbitRight,
    OrbitUp,
    OrbitDown,
    ToggleGrid,
    ToggleMeasurements,
    ToggleSidebar,
    ToggleOverlays,
    ToggleGizmos,
    ToggleXRay,
    ToggleWireframe,

    // Render
    RenderImage,

    // Help
    ShortcutSheet,
    Preferences,
}

/// <summary>What an action is called, where it is listed, and the name it is saved under.</summary>
/// <param name="Id">Saved in the preferences file. Never renamed once shipped: a saved binding would lose its action.</param>
public readonly record struct ActionInfo(EditorAction Action, string Id, string Name, string Category);

public static class EditorActions
{
    public const string Tools = "Tools";
    public const string Edit = "Edit";
    public const string File = "File";
    public const string View = "View";
    public const string Help = "Help";

    /// <summary>The categories in the order the keymap and the shortcut sheet list them.</summary>
    public static readonly string[] Categories = [Tools, Edit, File, View, Help];

    public static readonly ActionInfo[] All =
    [
        new(EditorAction.ToolSelect, "tool.select", "Select tool", Tools),
        new(EditorAction.ToolTransform, "tool.transform", "Transform tool", Tools),
        new(EditorAction.ToolMove, "tool.move", "Move (Transform)", Tools),
        new(EditorAction.ToolRotate, "tool.rotate", "Rotate (Transform)", Tools),
        new(EditorAction.ToolExtrude, "tool.extrude", "Extrude tool", Tools),
        new(EditorAction.ToolPaint, "tool.paint", "Paint tool", Tools),
        new(EditorAction.ToolLoopCut, "tool.loopcut", "Loop Cut tool", Tools),
        new(EditorAction.ToolSculpt, "tool.sculpt", "Sculpt tool", Tools),
        new(EditorAction.ToolView, "tool.view", "View tool - the camera only", Tools),
        new(EditorAction.ToolOtherMode, "tool.othermode", "The tool's other mode", Tools),
        new(EditorAction.ToolCycleMode, "tool.cyclemode", "Cycle paint mode, axes, new object", Tools),
        new(EditorAction.ToggleSnap, "tool.snap", "Snapping on or off", Tools),
        new(EditorAction.ToggleEditMode, "tool.editmode", "Edit Mode: into the object and out", Tools),

        new(EditorAction.Undo, "edit.undo", "Undo", Edit),
        new(EditorAction.Redo, "edit.redo", "Redo", Edit),
        new(EditorAction.Copy, "edit.copy", "Copy", Edit),
        new(EditorAction.Cut, "edit.cut", "Cut", Edit),
        new(EditorAction.Paste, "edit.paste", "Paste", Edit),
        new(EditorAction.Duplicate, "edit.duplicate", "Duplicate", Edit),
        new(EditorAction.Delete, "edit.delete", "Delete", Edit),
        new(EditorAction.Hide, "edit.hide", "Hide, or switch a light off", Edit),
        new(EditorAction.ShowAll, "edit.showall", "Show everything", Edit),
        new(EditorAction.Lock, "edit.lock", "Lock", Edit),
        new(EditorAction.UnlockAll, "edit.unlockall", "Unlock everything", Edit),
        new(EditorAction.Rename, "edit.rename", "Rename", Edit),
        new(EditorAction.Subdivide, "edit.subdivide", "Subdivide", Edit),
        new(EditorAction.SelectAll, "edit.selectall", "Select all", Edit),
        new(EditorAction.DeselectAll, "edit.deselectall", "Select none", Edit),
        new(EditorAction.InvertSelection, "edit.invertselection", "Invert the selection", Edit),
        new(EditorAction.GrowSelection, "edit.growselection", "Grow the voxel selection (Edit Mode)", Edit),
        new(EditorAction.ShrinkSelection, "edit.shrinkselection", "Shrink the voxel selection (Edit Mode)", Edit),
        new(EditorAction.Separate, "edit.separate", "Separate the chosen voxels into an object (Edit Mode)", Edit),
        new(EditorAction.FillSelection, "edit.fillselection", "Fill the chosen voxels with the colour (Edit Mode)", Edit),
        new(EditorAction.Join, "edit.join", "Join the selected into the active", Edit),
        new(EditorAction.SetParent, "edit.setparent", "Parent to... / to the active", Edit),
        new(EditorAction.ClearParent, "edit.clearparent", "Clear parent", Edit),
        new(EditorAction.Search, "edit.search", "Search for a command", Edit),
        new(EditorAction.AddMenu, "edit.add", "Add a shape, prop or light", Edit),
        new(EditorAction.KeepExtrude, "edit.keepextrude", "Keep an extrude", Edit),
        new(EditorAction.Cancel, "edit.cancel", "Cancel, or let go", Edit),

        new(EditorAction.NewLevel, "file.new", "New level", File),
        new(EditorAction.Open, "file.open", "Open", File),
        new(EditorAction.Save, "file.save", "Save", File),
        new(EditorAction.SaveAs, "file.saveas", "Save as", File),
        new(EditorAction.Export, "file.export", "Export mesh", File),

        new(EditorAction.FrameLevel, "view.framelevel", "Frame the level", View),
        new(EditorAction.FrameFocused, "view.framefocused", "Frame the focused object", View),
        new(EditorAction.ViewFront, "view.front", "Front view", View),
        new(EditorAction.ViewBack, "view.back", "Back view", View),
        new(EditorAction.ViewRight, "view.right", "Right view", View),
        new(EditorAction.ViewLeft, "view.left", "Left view", View),
        new(EditorAction.ViewTop, "view.top", "Top view", View),
        new(EditorAction.ViewBottom, "view.bottom", "Bottom view", View),
        new(EditorAction.ViewTurnRound, "view.turnround", "Turn the view round", View),
        new(EditorAction.ToggleOrthographic, "view.orthographic", "Orthographic", View),
        new(EditorAction.OrbitLeft, "view.orbitleft", "Step the view left", View),
        new(EditorAction.OrbitRight, "view.orbitright", "Step the view right", View),
        new(EditorAction.OrbitUp, "view.orbitup", "Step the view up", View),
        new(EditorAction.OrbitDown, "view.orbitdown", "Step the view down", View),
        new(EditorAction.ToggleGrid, "view.grid", "Ground grid", View),
        new(EditorAction.ToggleMeasurements, "view.measurements", "Measurements", View),
        new(EditorAction.ToggleSidebar, "view.sidebar", "Sidebar", View),
        new(EditorAction.ToggleOverlays, "view.overlays", "Overlays on or off", View),
        new(EditorAction.ToggleGizmos, "view.gizmos", "Gizmos on or off", View),
        new(EditorAction.ToggleXRay, "view.xray", "X-Ray", View),
        new(EditorAction.ToggleWireframe, "view.wireframe", "Wireframe, and back", View),

        new(EditorAction.RenderImage, "view.render", "Render an image of the level", View),
        new(EditorAction.ShortcutSheet, "help.shortcuts", "This list of shortcuts", Help),
        new(EditorAction.Preferences, "help.preferences", "Preferences", Help),
    ];

    public static ActionInfo Info(EditorAction action) => All[IndexOf(action)];

    public static ActionInfo? ById(string id)
    {
        foreach (ActionInfo info in All)
        {
            if (info.Id == id)
            {
                return info;
            }
        }

        return null;
    }

    private static int IndexOf(EditorAction action)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].Action == action)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(action), action, "Every action needs an entry in EditorActions.All.");
    }
}
