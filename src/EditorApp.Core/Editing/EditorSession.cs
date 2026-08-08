using EditorApp.Core.Commands;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// What the editor is currently doing: the world, the undo history, the active tool and its
/// sub-modes. Deliberately free of any windowing or GL dependency so tool behaviour can be tested
/// without a context.
/// </summary>
public sealed class EditorSession
{
    private VoxelEditCommand? _stroke;
    private VoxelEditCommand? _extrudePreview;

    /// <summary>
    /// The level: one or more independently placed objects sharing a palette. Starts empty — what
    /// a new level contains is the caller's decision, not the session's.
    /// </summary>
    public VoxelScene Scene { get; private set; } = new();

    /// <summary>
    /// The focused object's grid — what every tool edits. The scene guarantees an object always
    /// exists, so this never has to be null-checked at a call site.
    /// </summary>
    public VoxelWorld World => Scene.Focus?.Grid ?? EnsureFocus();

    public VoxelObject FocusObject => Scene.Focus ?? throw new InvalidOperationException("The scene has no object.");

    /// <summary>
    /// Moves focus to whatever the cursor is over. Refused while a gesture is running: focus
    /// changing mid-drag would hand the rest of the drag to a different object.
    /// </summary>
    public bool TryFocus(int objectId)
    {
        if (IsStrokeActive || IsExtruding)
        {
            return false;
        }

        if (objectId == Scene.FocusId)
        {
            return true;
        }

        if (!Scene.SetFocus(objectId))
        {
            return false;
        }

        // A selection belongs to the object it was made on.
        Selection = null;
        return true;
    }

    private VoxelWorld EnsureFocus()
    {
        VoxelObject created = Scene.Add(new VoxelWorld(), ObjectTransform.Identity);
        Scene.SetFocus(created.Id);
        return created.Grid;
    }

    public UndoStack History { get; } = new();

    /// <summary>Transform is the default: the tool that cannot destroy anything.</summary>
    public EditorTool ActiveTool { get; set; } = EditorTool.Transform;

    public TransformMode TransformMode { get; set; } = TransformMode.Move;

    public TransformSpace TransformSpace { get; set; } = TransformSpace.Global;

    public ExtrudeSelectionMode ExtrudeSelectionMode { get; set; } = ExtrudeSelectionMode.Box;

    /// <summary>When set, extruded voxels become a new independent object instead of joining this one.</summary>
    public bool ExtrudeCreatesObject { get; set; }

    public PaintMode PaintMode { get; set; } = PaintMode.Brush;

    /// <summary>Palette index the tools write. Never 0 — that is the empty marker.</summary>
    public byte ActiveColorIndex { get; set; } = 1;

    /// <summary>3D euclidean radius. 0 is exactly one voxel.</summary>
    public float BrushRadius { get; set; }

    /// <summary>How far two colours may differ and still be filled together.</summary>
    public int BucketThreshold { get; set; }

    /// <summary>True when the world has changed since the last save.</summary>
    public bool HasUnsavedChanges { get; set; }

    /// <summary>Path of the open project, or null for an unsaved one.</summary>
    public string? ProjectPath { get; set; }

    public string ProjectName => ProjectPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>Number of voxels changed by the stroke in progress. Drives the status line.</summary>
    public int StrokeCellCount => _stroke?.RetainedCells ?? 0;

    public bool IsStrokeActive => _stroke is not null;

    // ---- Strokes -----------------------------------------------------------------------------

    /// <summary>
    /// Starts a gesture. Everything applied until <see cref="EndStroke"/> becomes one undo step, so
    /// a whole click-and-drag reverses as a single action.
    /// </summary>
    public void BeginStroke()
    {
        // Bound to the focused grid here, once: focus is locked for the rest of the gesture, and
        // undo has to reach this object even if focus has moved on by then.
        _stroke ??= new VoxelEditCommand(ActiveTool.ToString(), World);
    }

    public void EndStroke()
    {
        if (_stroke is null)
        {
            return;
        }

        if (!_stroke.IsEmpty)
        {
            History.Push(_stroke);
            HasUnsavedChanges = true;
        }

        _stroke = null;
    }

    /// <summary>
    /// Runs an edit as exactly one undo step, independent of any stroke the mouse has open.
    /// Returns false when nothing changed, in which case no history entry is created.
    /// </summary>
    private bool RunStep(string name, Func<VoxelEditCommand, int> operation)
    {
        EndStroke();

        var command = new VoxelEditCommand(name, World);
        if (operation(command) == 0 || command.IsEmpty)
        {
            return false;
        }

        History.Push(command);
        HasUnsavedChanges = true;
        return true;
    }

    // ---- Extrude -----------------------------------------------------------------------------

    /// <summary>The surface Extrude is working on, or null when nothing is selected.</summary>
    public FaceSelection? Selection { get; private set; }

    /// <summary>Steps applied by the drag currently in progress. Positive pulls out, negative pushes in.</summary>
    public int ExtrudeSteps { get; private set; }

    public bool IsExtruding => _extrudePreview is not null;

    public bool HasSelection => Selection is { IsEmpty: false };

    public void ClearSelection()
    {
        CancelExtrude();
        Selection = null;
    }

    /// <summary>
    /// Replaces, adds to or subtracts from the selection. The operation is decided by the caller
    /// when the drag starts, never re-read mid-drag.
    /// </summary>
    public void SetSelection(FaceSelection selection, SelectionOperation operation = SelectionOperation.Replace)
    {
        Selection = Selection is null || operation == SelectionOperation.Replace
            ? selection
            : Selection.Combine(selection, operation);
    }

    /// <summary>Takes the whole connected coplanar patch under the cursor — Extrude's Face sub-mode.</summary>
    public void SelectPatch(RaycastHit hit, SelectionOperation operation = SelectionOperation.Replace) =>
        SetSelection(FaceSelection.ConnectedPatch(World, hit.Voxel, hit.Face), operation);

    /// <summary>
    /// Updates the live preview to a whole number of steps. The previous preview is undone first,
    /// so dragging back and forth never leaves anything behind.
    /// </summary>
    public void PreviewExtrude(int steps)
    {
        if (Selection is not { IsEmpty: false } selection || steps == ExtrudeSteps)
        {
            return;
        }

        _extrudePreview?.Undo();
        ExtrudeSteps = steps;

        if (steps == 0)
        {
            _extrudePreview = null;
            return;
        }

        var preview = new VoxelEditCommand($"Extrude {steps:+0;-0}", World);
        ExtrudeOperation.Apply(selection, steps, preview);
        _extrudePreview = preview.IsEmpty ? null : preview;
    }

    /// <summary>Commits the drag. The selection advances to the newly formed surface.</summary>
    public bool ConfirmExtrude()
    {
        if (_extrudePreview is not { } preview || Selection is not { } selection)
        {
            ExtrudeSteps = 0;
            return false;
        }

        History.Push(preview);
        HasUnsavedChanges = true;

        Selection = ExtrudeOperation.Advance(selection, ExtrudeSteps);
        _extrudePreview = null;
        ExtrudeSteps = 0;
        return true;
    }

    /// <summary>Throws the drag away and puts the world back exactly as it was.</summary>
    public void CancelExtrude()
    {
        _extrudePreview?.Undo();
        _extrudePreview = null;
        ExtrudeSteps = 0;
    }

    // ---- Paint -------------------------------------------------------------------------------

    /// <summary>Paints under the cursor with the active mode. Part of the open stroke.</summary>
    public bool Paint(RaycastHit hit)
    {
        BeginStroke();

        return PaintMode switch
        {
            PaintMode.Bucket => PaintOperations.Bucket(
                hit.Voxel, ActiveColorIndex, BucketThreshold, _stroke!) > 0,
            _ => PaintOperations.Brush(hit.Voxel, BrushRadius, ActiveColorIndex, _stroke!) > 0,
        };
    }

    /// <summary>The eyedropper. Returns false when there is nothing to sample.</summary>
    public bool SampleColor(RaycastHit hit)
    {
        if (PaintOperations.Sample(World, hit.Voxel) is not { } index)
        {
            return false;
        }

        ActiveColorIndex = index;
        return true;
    }

    // ---- History and palette -----------------------------------------------------------------

    public bool Undo()
    {
        CancelExtrude();
        EndStroke();

        if (!History.Undo())
        {
            return false;
        }

        HasUnsavedChanges = true;
        return true;
    }

    public bool Redo()
    {
        CancelExtrude();
        EndStroke();

        if (!History.Redo())
        {
            return false;
        }

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>Applies a palette color live, without recording history (used while dragging a picker).</summary>
    public void ApplyPaletteColor(int index, Color32 color)
    {
        Scene.Palette[index] = color;
        Scene.MarkAllDirty();
        HasUnsavedChanges = true;
    }

    /// <summary>Records a finished palette edit as one undo step.</summary>
    public void PushPaletteEdit(int index, Color32 before, Color32 after)
    {
        if (before == after)
        {
            return;
        }

        History.Push(new PaletteEditCommand(Scene, index, before, after));
        HasUnsavedChanges = true;
    }

    /// <summary>Replaces the level with a single-object scene — New, or opening a v1 project.</summary>
    public void ReplaceWorld(VoxelWorld world, string? projectPath)
    {
        var scene = new VoxelScene();
        scene.ReplacePalette(world.Palette);
        scene.Add(world, ObjectTransform.Identity, "Object 1");
        ReplaceScene(scene, projectPath);
    }

    /// <summary>Replaces the whole level.</summary>
    public void ReplaceScene(VoxelScene scene, string? projectPath)
    {
        _stroke = null;
        _extrudePreview = null;
        ExtrudeSteps = 0;
        Selection = null;

        Scene = scene;
        if (Scene.Objects.Count == 0)
        {
            EnsureFocus();
        }

        Scene.MarkAllDirty();
        History.Clear();
        ProjectPath = projectPath;
        HasUnsavedChanges = false;
    }

    /// <summary>Side of the cube a new level starts from.</summary>
    public const int StarterCubeSize = 8;

    /// <summary>
    /// A new level starts as an 8³ white cube at the origin. With no Place tool there has to be
    /// something to extrude from, and a cube gives every one of the six directions a real surface to
    /// pull on from the first click. White keeps the first thing on screen about shape, not colour.
    /// </summary>
    public static VoxelWorld CreateStarterWorld(byte paletteIndex = Palette.WhiteIndex)
    {
        var world = new VoxelWorld();

        for (int x = 0; x < StarterCubeSize; x++)
        {
            for (int y = 0; y < StarterCubeSize; y++)
            {
                for (int z = 0; z < StarterCubeSize; z++)
                {
                    world.SetVoxel(x, y, z, paletteIndex);
                }
            }
        }

        return world;
    }
}
