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

        // A held Extrude selection pins focus too, even though no drag is running. Reaching for the
        // arrow means crossing whatever happens to be between the cursor and it, and losing the
        // selection to an object merely passed over makes the tool unusable in a crowded scene.
        // A deliberate click still moves focus — see ExtrudeInteraction.OnPress.
        if (ActiveTool == EditorTool.Extrude && HasSelection)
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

    /// <summary>The tiled image Pattern samples, or null when none has been loaded.</summary>
    public PatternSource? Pattern { get; set; }

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

            // Voxels now carry this colour, so the slot is no longer the picker's to reuse.
            // Without this, choosing the next colour would silently repaint what was just painted.
            if (WorkingSlot == ActiveColorIndex)
            {
                WorkingSlot = null;
            }
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

        // Same reasoning as EndStroke: once voxels carry the working colour it stops being scratch.
        if (WorkingSlot == ActiveColorIndex)
        {
            WorkingSlot = null;
        }

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

        // Create only makes sense pulling out; pushing in has nothing to hand to a new object, so
        // it behaves like a plain extrude.
        if (ExtrudeCreatesObject && ExtrudeSteps > 0 && Scene.Focus is { } source)
        {
            return ConfirmExtrudeAsNewObject(preview, source);
        }

        History.Push(preview);
        HasUnsavedChanges = true;

        Selection = ExtrudeOperation.Advance(selection, ExtrudeSteps);
        _extrudePreview = null;
        ExtrudeSteps = 0;
        return true;
    }

    private bool ConfirmExtrudeAsNewObject(VoxelEditCommand preview, VoxelObject source)
    {
        var grid = new VoxelWorld();
        foreach ((Int3 position, byte value) in preview.Written())
        {
            if (value != Palette.EmptyIndex)
            {
                grid.SetVoxel(position, value);
            }
        }

        // Take the voxels back out of the object they were pulled from; they belong to the new one.
        preview.Undo();
        _extrudePreview = null;
        ExtrudeSteps = 0;

        if (grid.SolidCount == 0)
        {
            return false;
        }

        // Same transform, and the voxels keep their coordinates, so the new object appears exactly
        // where the extrude drew it.
        var command = new CreateObjectCommand(Scene, grid, source.Transform, source.Name + " (extruded)");
        command.Redo();
        History.Push(command);

        Selection = null;
        HasUnsavedChanges = true;
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
                hit.Voxel, hit.Face, ActiveColorIndex, BucketThreshold, _stroke!) > 0,

            // With no pattern loaded there is nothing to sample, so Pattern falls back to a plain
            // fill rather than doing nothing and looking broken.
            PaintMode.Pattern => Pattern is { } pattern
                ? PaintOperations.Pattern(hit.Voxel, hit.Face, pattern, BucketThreshold, _stroke!) > 0
                : PaintOperations.Bucket(hit.Voxel, hit.Face, ActiveColorIndex, BucketThreshold, _stroke!) > 0,

            _ => PaintOperations.Brush(hit.Voxel, hit.Face, BrushRadius, ActiveColorIndex, _stroke!) > 0,
        };
    }

    /// <summary>
    /// Paints a straight line or a hollow box between two cells, as one step. Shift and Ctrl drags
    /// commit their shape on release rather than as the cursor travels.
    /// </summary>
    public bool PaintShape(Int3 from, Int3 to, Face face, bool asBox) =>
        RunStep(
            asBox ? "Paint box" : "Paint line",
            c => asBox
                ? PaintOperations.BoxFrame(from, to, face, BrushRadius, ActiveColorIndex, c)
                : PaintOperations.Line(from, to, face, BrushRadius, ActiveColorIndex, c));

    /// <summary>The eyedropper. Returns false when there is nothing to sample.</summary>
    public bool SampleColor(RaycastHit hit)
    {
        if (PaintOperations.Sample(World, hit.Voxel, hit.Face) is not { } index)
        {
            return false;
        }

        ActiveColorIndex = index;
        return true;
    }

    // ---- Transform ---------------------------------------------------------------------------

    /// <summary>
    /// Moves a specific object without recording history — used live during a gizmo drag, where
    /// every frame would otherwise become its own undo step.
    ///
    /// The target is explicit rather than "whatever has focus": a drag that passed over another
    /// object used to start moving that one instead, mid-gesture.
    /// </summary>
    public void ApplyTransform(VoxelObject target, ObjectTransform transform)
    {
        target.Transform = transform;
        HasUnsavedChanges = true;
    }

    /// <summary>Records a finished gizmo drag as one undo step.</summary>
    public bool PushTransformEdit(VoxelObject target, ObjectTransform before, string name)
    {
        if (before == target.Transform)
        {
            return false;
        }

        History.Push(new TransformCommand(target, before, target.Transform, name));
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Removes an object. Refused for the last one: with no Place tool, an empty scene has nothing
    /// left to extrude from.
    /// </summary>
    public bool DeleteObject(int objectId)
    {
        if (Scene.Objects.Count <= 1)
        {
            return false;
        }

        VoxelObject? target = null;
        foreach (VoxelObject candidate in Scene.Objects)
        {
            if (candidate.Id == objectId)
            {
                target = candidate;
                break;
            }
        }

        if (target is null)
        {
            return false;
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        var command = new DeleteObjectCommand(Scene, target);
        command.Redo();
        History.Push(command);

        HasUnsavedChanges = true;
        return true;
    }

    // ---- Loop Cut ----------------------------------------------------------------------------

    /// <summary>The plane the cursor is currently over, or null. Drawn as a preview.</summary>
    public CutPlane? PreviewCutPlane { get; set; }

    /// <summary>
    /// Splits the focused object at the plane. Refused when one side would be empty — a cut has to
    /// produce two objects, not one object and a ghost.
    /// </summary>
    public bool ApplyLoopCut(CutPlane plane)
    {
        if (Scene.Focus is not { } target)
        {
            return false;
        }

        (VoxelWorld low, VoxelWorld high) = LoopCut.Split(target.Grid, plane);
        if (low.SolidCount == 0 || high.SolidCount == 0)
        {
            return false;
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        var command = new LoopCutCommand(Scene, target, low, high);
        command.Redo();
        History.Push(command);

        HasUnsavedChanges = true;
        PreviewCutPlane = null;
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

        // A pattern caches which palette entry each of its colours matched; that answer just changed.
        Pattern?.InvalidateMatches();
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// The slot the picker is currently writing into, while that colour has not been painted with
    /// or saved. Reused as the picker is dragged, so one session of choosing a colour consumes one
    /// slot rather than one per frame.
    /// </summary>
    public byte? WorkingSlot { get; private set; }

    /// <summary>
    /// Makes an arbitrary colour the active one, live. If the palette already holds it exactly that
    /// entry is selected; otherwise it goes into a working slot, which is not a saved swatch until
    /// <see cref="SaveActiveColor"/> says so.
    ///
    /// This is the difference between picking a colour and editing the palette. Writing the picked
    /// colour over the active entry — which is what the editor used to do — repaints every voxel
    /// that shared the index, which is almost never what choosing a colour is meant to mean.
    /// </summary>
    public byte SelectColor(Color32 color)
    {
        color = color with { A = 255 };

        if (Scene.Palette.FindExact(color) is { } existing)
        {
            WorkingSlot = null;
            ActiveColorIndex = existing;
            return existing;
        }

        // Keep using the same working slot while it is still nobody's colour but the picker's.
        byte slot = WorkingSlot is { } reusable && !Scene.Palette.IsCustomSaved(reusable)
            ? reusable
            : AllocateCustomSlot();

        Scene.Palette[slot] = color;
        Scene.Palette.SetCustomSaved(slot, false);
        Scene.MarkAllDirty();
        Pattern?.InvalidateMatches();

        // No history entry: a working slot has nothing painted with it, so nothing visible changed.
        HasUnsavedChanges = true;

        WorkingSlot = slot;
        ActiveColorIndex = slot;
        return slot;
    }

    /// <summary>
    /// Keeps the active colour as a swatch. Only colours saved this way appear in the Custom row —
    /// otherwise every colour ever used would pile up there.
    /// </summary>
    public bool SaveActiveColor()
    {
        if (!Palette.IsCustomIndex(ActiveColorIndex) || Scene.Palette.IsCustomSlotFree(ActiveColorIndex))
        {
            return false;
        }

        if (Scene.Palette.IsCustomSaved(ActiveColorIndex))
        {
            return false;
        }

        Scene.Palette.SetCustomSaved(ActiveColorIndex, true);

        // The slot belongs to the swatch now; the next picker change starts a fresh one.
        WorkingSlot = null;
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Turns the focused object a quarter turn, in its voxels rather than in its transform.
    ///
    /// The distinction matters more than it looks. A transform rotation is a placement — the grid
    /// underneath is untouched, so everything that reads the voxels rather than the picture still
    /// sees the model the way it was built, and a file format with no field for rotation carries
    /// none of it. This turns the lattice, which is exact: a quarter turn of a cubic grid is a
    /// permutation, and nothing is resampled or lost.
    /// </summary>
    public bool RotateFocus(RotateDirection direction)
    {
        if (Scene.Focus is not { IsEmpty: false } focus)
        {
            return false;
        }

        // Any gesture in progress was aimed at the model as it was a moment ago.
        EndStroke();
        CancelExtrude();
        Selection = null;

        var command = new RotateObjectCommand(focus.Grid, direction);
        command.Redo();
        History.Push(command);

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Sets the world size of one voxel for the whole level.
    ///
    /// Not an undo step, unlike a palette edit. A palette edit repaints voxels — it changes the
    /// model, invisibly and everywhere at once, so it needs a way back. This changes no voxel and
    /// no placement; the number that was there is still on screen in the box you just typed into.
    /// </summary>
    public bool SetVoxelSize(float size)
    {
        float clamped = Math.Clamp(size, VoxelScene.MinVoxelSize, VoxelScene.MaxVoxelSize);
        if (!float.IsFinite(size) || clamped == Scene.VoxelSize)
        {
            return false;
        }

        Scene.VoxelSize = clamped;
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>Gives a custom slot back to the pool.</summary>
    public bool ClearCustomColor(int index)
    {
        if (!Palette.IsCustomIndex(index) || Scene.Palette.IsCustomSlotFree(index))
        {
            return false;
        }

        Color32 before = Scene.Palette[index];
        Scene.Palette.ClearCustomSlot(index);
        Scene.MarkAllDirty();
        Pattern?.InvalidateMatches();

        History.Push(new PaletteEditCommand(Scene, index, before, Color32.Transparent));
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// A free slot first; then an unsaved one no voxel is using, since a working colour nobody kept
    /// and nobody painted with is the cheapest thing to lose; then any unsaved one; and only as a
    /// last resort a saved swatch, because refusing to pick a colour is worse than dropping one.
    /// </summary>
    private byte AllocateCustomSlot()
    {
        for (int i = Palette.CustomStart; i < Palette.Size; i++)
        {
            if (Scene.Palette.IsCustomSlotFree(i))
            {
                return (byte)i;
            }
        }

        HashSet<byte> used = UsedPaletteIndices();

        for (int i = Palette.CustomStart; i < Palette.Size; i++)
        {
            if (!Scene.Palette.IsCustomSaved(i) && !used.Contains((byte)i))
            {
                return (byte)i;
            }
        }

        for (int i = Palette.CustomStart; i < Palette.Size; i++)
        {
            if (!Scene.Palette.IsCustomSaved(i))
            {
                return (byte)i;
            }
        }

        return Palette.CustomStart;
    }

    /// <summary>Every palette index referenced by a voxel anywhere in the scene.</summary>
    public HashSet<byte> UsedPaletteIndices()
    {
        var used = new HashSet<byte>();

        foreach (VoxelObject o in Scene.Objects)
        {
            foreach (Chunk chunk in o.Grid.Chunks.Values)
            {
                foreach (byte index in chunk.Indices)
                {
                    if (index != Palette.EmptyIndex)
                    {
                        used.Add(index);
                    }
                }
            }
        }

        return used;
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
    /// A new level starts as an 8x8x8 white cube. With no Place tool there has to be something to
    /// extrude from, and a cube gives every one of the six directions a real surface to pull on from
    /// the first click. White keeps the first thing on screen about shape, not colour.
    ///
    /// It is centred on the origin horizontally and rests on the ground plane, so it sits where the
    /// grid says the middle of the world is rather than off in one quadrant.
    /// </summary>
    public static VoxelWorld CreateStarterWorld(byte paletteIndex = Palette.WhiteIndex)
    {
        var world = new VoxelWorld();

        const int half = StarterCubeSize / 2;

        for (int x = -half; x < half; x++)
        {
            for (int y = 0; y < StarterCubeSize; y++)
            {
                for (int z = -half; z < half; z++)
                {
                    world.SetVoxel(x, y, z, paletteIndex);
                }
            }
        }

        return world;
    }
}
