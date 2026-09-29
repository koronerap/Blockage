using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using EditorApp.Rendering;

namespace EditorApp.Ui;

/// <summary>What the search's commands reach into: the parts of the application its menus reach.</summary>
public sealed class SearchSources
{
    public required EditorSession Session { get; init; }

    public required ProjectController Project { get; init; }

    public required MimicraftController Mimicraft { get; init; }

    public required ViewActions View { get; init; }

    public required Preferences Preferences { get; init; }

    /// <summary>Does a keymap action, exactly as its key would.</summary>
    public required Action<EditorAction> Run { get; init; }

    /// <summary>Puts changed preferences — the theme — into effect.</summary>
    public required Action ApplyPreferences { get; init; }

    public required Action Exit { get; init; }
}

/// <summary>
/// Everything F3 finds: every action in the keymap, whether or not it has a key, then what only a
/// menu or a popover offers — the turns and mirrors, the overlays one by one, the lights to add, a
/// tool's modes. Built while the search is up, since some of it — the recent files, the objects to
/// join into — changes as the level does.
/// </summary>
public static class SearchCommands
{
    /// <summary>Not listed: the search itself, and keys that mean something only in the middle of a gesture.</summary>
    private static readonly HashSet<EditorAction> NotListed = [EditorAction.Search, EditorAction.Cancel, EditorAction.KeepExtrude];

    /// <summary>Other words the actions answer to — what someone after one might type instead of its name.</summary>
    private static readonly Dictionary<EditorAction, string> Keywords = new()
    {
        [EditorAction.ToolTransform] = "move rotate gizmo",
        [EditorAction.ToolExtrude] = "pull push add remove voxels",
        [EditorAction.ToolPaint] = "colour color brush",
        [EditorAction.ToolLoopCut] = "split slice",
        [EditorAction.ToolView] = "camera navigate",
        [EditorAction.ToggleSnap] = "magnet snapping",
        [EditorAction.Undo] = "back",
        [EditorAction.Duplicate] = "copy clone",
        [EditorAction.Delete] = "remove erase",
        [EditorAction.Hide] = "visibility invisible",
        [EditorAction.ShowAll] = "unhide reveal visibility",
        [EditorAction.Lock] = "freeze",
        [EditorAction.UnlockAll] = "unfreeze",
        [EditorAction.Rename] = "name",
        [EditorAction.Subdivide] = "finer smaller voxels resolution",
        [EditorAction.SetParent] = "child link attach hierarchy",
        [EditorAction.ClearParent] = "unparent free detach unlink hierarchy",
        [EditorAction.Export] = "obj gltf glb",
        [EditorAction.FrameLevel] = "view all zoom extents",
        [EditorAction.FrameFocused] = "view selected zoom",
        [EditorAction.ToggleOrthographic] = "perspective projection",
        [EditorAction.ToggleGrid] = "floor",
        [EditorAction.ToggleMeasurements] = "dimensions ruler size",
        [EditorAction.ToggleSidebar] = "outliner properties panel",
        [EditorAction.ToggleXRay] = "xray see through transparent",
        [EditorAction.ToggleWireframe] = "wire lattice shading",
        [EditorAction.ShortcutSheet] = "keys hotkeys keyboard help",
        [EditorAction.Preferences] = "settings options keymap theme",
    };

    private static readonly (string Name, LightKind Kind)[] Lights =
    [
        ("Sun", LightKind.Directional),
        ("Point light", LightKind.Point),
        ("Spot light", LightKind.Spot),
    ];

    private static readonly (string Name, RotateDirection Direction)[] Turns =
    [
        ("Turn left", RotateDirection.Left),
        ("Turn right", RotateDirection.Right),
        ("Tip up", RotateDirection.Up),
        ("Tip down", RotateDirection.Down),
    ];

    private static readonly (string Name, Axis Axis)[] Flips =
    [
        ("Flip X (left and right)", Axis.X),
        ("Flip Y (top and bottom)", Axis.Y),
        ("Flip Z (front and back)", Axis.Z),
    ];

    public static List<SearchCommand> Build(SearchSources sources)
    {
        EditorSession session = sources.Session;
        ViewActions view = sources.View;
        ViewportSettings viewport = view.Viewport;
        Action<EditorAction> run = sources.Run;
        var commands = new List<SearchCommand>();

        foreach (ActionInfo info in EditorActions.All)
        {
            if (NotListed.Contains(info.Action))
            {
                continue;
            }

            EditorAction action = info.Action;
            commands.Add(new SearchCommand(info.Id, info.Name, info.Category, () => run(action))
            {
                Action = action,
                Keywords = Keywords.GetValueOrDefault(action, string.Empty),
                Problem = ProblemOf(action, session),
            });
        }

        // ---- File
        commands.Add(new("file.recover", "Recover unsaved work...", EditorActions.File, sources.Project.OfferRecovery)
        {
            Keywords = "autosave crash restore",
            Problem = () => sources.Project.CanRecover ? null : "No unsaved work was left behind.",
        });
        commands.Add(new("file.mimicraft", "Save for Mimicraft...", EditorActions.File, sources.Mimicraft.Show) { Keywords = "character weapons game export" });
        commands.Add(new("file.recoverlast", "Recover last session", EditorActions.File, sources.Project.RecoverLastSession)
        {
            Keywords = "restore quit closed lost",
            Problem = () => sources.Project.LastSessionFound is null ? "Nothing has been kept yet." : null,
        });
        commands.Add(new("help.welcome", "Welcome screen", EditorActions.Help, WelcomeScreen.Open) { Keywords = "splash start recent templates" });

        foreach (LevelTemplate template in LevelTemplates.All)
        {
            commands.Add(new($"file.new.{template}".ToLowerInvariant(), LevelTemplates.NameOf(template), "New", () => sources.Project.NewProject(template))
            {
                Keywords = "template level file",
            });
        }
        commands.Add(new("file.exit", "Exit", EditorActions.File, sources.Exit) { Keywords = "quit close" });

        foreach (string path in sources.Project.Recent.Paths)
        {
            commands.Add(new($"file.recent:{path}", $"Open {Path.GetFileName(path)}", "Open Recent", () => sources.Project.OpenRecent(path))
            {
                Keywords = "recent file level",
            });
        }

        // ---- View
        commands.Add(new("view.center", "Move view to center", EditorActions.View, view.LookAtCenter) { Keywords = "origin look" });
        commands.Add(new("view.reset", "Reset camera", EditorActions.View, view.ResetCamera) { Keywords = "home default view" });
        commands.Add(new("view.statistics", "Statistics", EditorActions.View, view.ToggleStatistics) { Keywords = "fps triangles counts overlay" });
        commands.Add(new("view.solid", "Solid shading", EditorActions.View, () => viewport.Shading = ShadingMode.Unlit) { Keywords = "unlit studio" });
        commands.Add(new("view.lit", "Lit shading", EditorActions.View, () => viewport.Shading = ShadingMode.Lit) { Keywords = "lights lighting" });
        commands.Add(new("view.studio", "Studio lighting", "Shading", () => { viewport.Shading = ShadingMode.Unlit; viewport.SolidLighting = SolidLighting.Studio; }));
        commands.Add(new("view.flat", "Flat lighting", "Shading", () => { viewport.Shading = ShadingMode.Unlit; viewport.SolidLighting = SolidLighting.Flat; }) { Keywords = "unshaded" });
        commands.Add(new("view.colour.palette", "Colour from the palette", "Shading", () => viewport.Colour = ColourMode.Palette) { Keywords = "color" });
        commands.Add(new("view.colour.single", "One colour for everything", "Shading", () => viewport.Colour = ColourMode.Single) { Keywords = "single color clay" });
        commands.Add(new("view.colour.random", "A colour per object", "Shading", () => viewport.Colour = ColourMode.Random) { Keywords = "random color objects" });

        Toggle(commands, "view.overlay.lighticons", "Light icons", "Overlays", () => viewport.LightIcons = !viewport.LightIcons);
        Toggle(commands, "view.overlay.relationships", "Relationship lines", "Overlays", () => viewport.RelationshipLines = !viewport.RelationshipLines, "parent child");
        Toggle(commands, "view.overlay.origins", "Origins", "Overlays", () => viewport.Origins = !viewport.Origins, "pivot");
        Toggle(commands, "view.overlay.focus", "Focus highlight", "Overlays", () => viewport.FocusHighlight = !viewport.FocusHighlight);
        Toggle(commands, "view.overlay.textinfo", "Text info", "Overlays", () => viewport.TextInfo = !viewport.TextInfo);
        Toggle(commands, "view.overlay.wireframe", "Wireframe over the faces", "Overlays", () => viewport.Wireframe = !viewport.Wireframe, "lattice lines");
        Toggle(commands, "view.overlay.mirror", "Mirror planes", "Overlays", () => viewport.MirrorPlanes = !viewport.MirrorPlanes, "symmetry");
        Toggle(commands, "view.gizmo.navigate", "Navigation gizmo", "Gizmos", () => viewport.NavigateGizmo = !viewport.NavigateGizmo, "axis ball");
        Toggle(commands, "view.gizmo.tools", "Tool gizmos", "Gizmos", () => viewport.ToolGizmos = !viewport.ToolGizmos, "arrow transform extrude");
        Toggle(commands, "view.gizmo.lights", "Light aim lines", "Gizmos", () => viewport.LightGizmos = !viewport.LightGizmos);

        // ---- Object
        foreach ((string name, RotateDirection direction) in Turns)
        {
            commands.Add(new($"object.turn.{direction}".ToLowerInvariant(), name, "Object", () => session.RotateFocus(direction))
            {
                Keywords = "rotate quarter",
                Problem = () => NoVoxels(session),
            });
        }

        foreach ((string name, Axis axis) in Flips)
        {
            commands.Add(new($"object.flip.{axis}".ToLowerInvariant(), name, "Object", () => session.FlipFocus(axis))
            {
                Keywords = "mirror",
                Problem = () => NoVoxels(session),
            });
        }

        if (session.Scene.Focus is { } focus)
        {
            foreach (VoxelObject target in session.Scene.Objects)
            {
                if (target.Id != focus.Id)
                {
                    commands.Add(new($"object.join:{target.Id}", $"Join into {target.Name}", "Object", () => ClipboardActions.Join(session, focus, target, ReportLog.Shared))
                    {
                        Keywords = "merge combine",
                        Problem = () => session.JoinProblem(focus.Id, target.Id),
                    });
                }
            }
        }

        // ---- Add
        foreach ((string name, LightKind kind) in Lights)
        {
            commands.Add(new($"add.light.{kind}".ToLowerInvariant(), name, "Add", () => LightMenu.Add(session, kind)) { Keywords = "lamp light" });
        }

        // ---- Tools and their modes
        commands.Add(new("tool.transform.global", "Global axes", "Transform", () => { run(EditorAction.ToolTransform); session.TransformSpace = TransformSpace.Global; }) { Keywords = "world space" });
        commands.Add(new("tool.transform.local", "Local axes", "Transform", () => { run(EditorAction.ToolTransform); session.TransformSpace = TransformSpace.Local; }) { Keywords = "object space" });
        commands.Add(new("tool.extrude.box", "Box select", "Extrude", () => { run(EditorAction.ToolExtrude); session.ExtrudeSelectionMode = ExtrudeSelectionMode.Box; }) { Keywords = "rectangle selection" });
        commands.Add(new("tool.extrude.face", "Face select", "Extrude", () => { run(EditorAction.ToolExtrude); session.ExtrudeSelectionMode = ExtrudeSelectionMode.Face; }) { Keywords = "patch selection" });
        commands.Add(new("tool.extrude.newobject", "New object", "Extrude", () => { run(EditorAction.ToolExtrude); session.ExtrudeCreatesObject = !session.ExtrudeCreatesObject; }) { Keywords = "create separate" });
        commands.Add(new("tool.paint.brush", "Brush", "Paint", () => { run(EditorAction.ToolPaint); session.PaintMode = PaintMode.Brush; }) { Keywords = "colour color" });
        commands.Add(new("tool.paint.bucket", "Bucket fill", "Paint", () => { run(EditorAction.ToolPaint); session.PaintMode = PaintMode.Bucket; }) { Keywords = "colour color flood" });
        commands.Add(new("tool.paint.pattern", "Pattern", "Paint", () => { run(EditorAction.ToolPaint); session.PaintMode = PaintMode.Pattern; }) { Keywords = "colour color texture" });

        foreach (SnapTarget target in (SnapTarget[])[SnapTarget.Increment, SnapTarget.Corner, SnapTarget.EdgeCentre, SnapTarget.Surface])
        {
            commands.Add(new($"snap.{target}".ToLowerInvariant(), $"Snap to {SnapName(target)}", "Snap", () => session.Snap.Set(target, !session.Snap.Snaps(target)))
            {
                Keywords = "snapping magnet",
            });
        }

        // ---- Preferences
        foreach (PreferencesWindow.Page page in Enum.GetValues<PreferencesWindow.Page>())
        {
            commands.Add(new($"preferences.{page}".ToLowerInvariant(), $"{page} preferences", EditorActions.Edit, () =>
            {
                PreferencesWindow.Current = page;
                PreferencesWindow.Open();
            })
            {
                Keywords = "settings options",
            });
        }

        foreach (ThemeKind theme in Enum.GetValues<ThemeKind>())
        {
            commands.Add(new($"theme.{theme}".ToLowerInvariant(), $"{theme} theme", "Interface", () =>
            {
                sources.Preferences.Theme = theme;
                sources.ApplyPreferences();
            })
            {
                Keywords = "colours colors appearance",
            });
        }

        return commands;
    }

    private static void Toggle(List<SearchCommand> commands, string id, string name, string menu, Action flip, string keywords = "") =>
        commands.Add(new SearchCommand(id, name, menu, flip) { Keywords = $"show hide {keywords}".Trim() });

    private static string SnapName(SnapTarget target) => target switch
    {
        SnapTarget.Increment => "increment",
        SnapTarget.Corner => "corners",
        SnapTarget.EdgeCentre => "edge centres",
        _ => "surface",
    };

    private static string? NoVoxels(EditorSession session) =>
        session.Scene.Focus is { IsEmpty: false } ? null : "Nothing with voxels is focused.";

    /// <summary>What stands in the way of an action now, where anything can — shown on its row, greyed.</summary>
    private static Func<string?>? ProblemOf(EditorAction action, EditorSession session) => action switch
    {
        EditorAction.Undo => () => session.History.CanUndo ? null : "There is nothing to undo.",
        EditorAction.Redo => () => session.History.CanRedo ? null : "There is nothing to redo.",
        EditorAction.Paste => () => session.Clipboard is null ? "Nothing has been copied." : null,
        EditorAction.Copy or EditorAction.Cut => () => NoVoxels(session),
        EditorAction.Subdivide => () => session.SubdivideProblem(session.Scene.Focus),
        EditorAction.SetParent => () => session.Scene.Objects.Count > 1 || session.SelectedLight is not null
            ? null
            : "There is no other object to be its parent.",
        EditorAction.ClearParent => () => InHand(session) is { } held && session.Scene.ParentOf(held) is null
            ? $"{held.Name} has no parent."
            : null,
        _ => null,
    };

    private static IPlaceable? InHand(EditorSession session) => session.SelectedLight as IPlaceable ?? session.Scene.Focus;
}
