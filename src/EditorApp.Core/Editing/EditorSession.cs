using System.Numerics;
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
public sealed partial class EditorSession
{
    private VoxelEditCommand? _stroke;
    private VoxelEditCommand? _extrudePreview;

    /// <summary>
    /// The level: one or more independently placed objects sharing a palette. Starts empty — what
    /// a new level contains is the caller's decision, not the session's.
    /// </summary>
    public VoxelScene Scene { get; private set; } = new();

    /// <summary>
    /// The focused object's grid — what every tool edits. A level may have no objects at all; then
    /// this is a grid that belongs to nothing, empty, so that reading it never has to be null-checked
    /// at a call site. The tools that write refuse before they get here.
    /// </summary>
    public VoxelWorld World => Scene.Focus?.Grid ?? _nothing;

    private readonly VoxelWorld _nothing = new();

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

        // In Edit Mode the tools stay inside the object being edited.
        if (InEditMode)
        {
            return false;
        }

        // A locked object is passed over: that is what locking it is for.
        if (Scene.Find(objectId) is { Locked: true })
        {
            return false;
        }

        if (!Scene.SetFocus(objectId))
        {
            return false;
        }

        // A selection belongs to the object it was made on.
        Selection = null;

        // Focus on something outside the selection makes it the selection: the one-object-at-a-time
        // way of the phone, which has no selecting of its own. On the desktop the tools only offer
        // what is selected already.
        if (!Scene.IsSelected(objectId))
        {
            DeselectAll();
            Scene.Select(objectId);
        }

        return true;
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

    /// <summary>The second colour, for the two-colour fills: where a gradient ends, what noise and dither mix in.</summary>
    public byte SecondaryColorIndex { get; set; } = 1;

    /// <summary>How much of the second colour noise and dither mix in, 0 to 1.</summary>
    public float MixAmount
    {
        get => _mixAmount;
        set => _mixAmount = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : _mixAmount;
    }

    private float _mixAmount = 0.5f;

    /// <summary>The colour in hand and the second colour trade places.</summary>
    public void SwapColours() => (ActiveColorIndex, SecondaryColorIndex) = (SecondaryColorIndex, ActiveColorIndex);

    /// <summary>
    /// The loaded image painted onto faces of the focused object, each at the point of the image it
    /// was seen through — the stencil — as one undo step. The host works out which faces are seen
    /// and where; see-through pixels paint nothing.
    /// </summary>
    public bool ProjectPattern(IEnumerable<(Int3 Cell, Face Face, float U, float V)> faces)
    {
        if (Pattern is not { } pattern)
        {
            return false;
        }

        List<(Int3 Cell, Face Face, float U, float V)> seen = [.. faces];
        return RunStep("Stencil", c =>
        {
            int changed = 0;
            foreach ((Int3 cell, Face face, float u, float v) in seen)
            {
                if (pattern.SampleAt(Scene.Palette, u, v) is { } index && c.ApplyFace(cell, face, index))
                {
                    changed++;
                }
            }

            return changed;
        });
    }

    /// <summary>A gradient dragged from one face to a cell, over the surface it starts on, as one undo step.</summary>
    public bool PaintGradient(RaycastHit start, Int3 end) =>
        RunStep("Gradient", c => PaintOperations.Gradient(start.Voxel, start.Face, end, ActiveColorIndex, SecondaryColorIndex, BucketThreshold, c));

    /// <summary>3D euclidean radius. 0 is exactly one voxel.</summary>
    public float BrushRadius { get; set; }

    /// <summary>How far two colours may differ and still be filled together.</summary>
    public int BucketThreshold { get; set; }

    /// <summary>
    /// Makes a bucket click recolour the whole object instead of the surface under the cursor.
    /// A mode of the bucket rather than a fourth tool: it is still "fill with this colour", only
    /// with a different idea of how far the fill reaches.
    /// </summary>
    public bool BucketWholeObject { get; set; }

    /// <summary>The tiled image Pattern samples, or null when none has been loaded.</summary>
    public PatternSource? Pattern { get; set; }

    /// <summary>True when the world has changed since the last save.</summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set
        {
            _hasUnsavedChanges = value;
            if (value)
            {
                Revision++;
            }
        }
    }

    private bool _hasUnsavedChanges;

    /// <summary>
    /// Goes up by one every time the level is marked changed. Whatever keeps a copy of its own —
    /// autosave — compares this rather than the level itself to know whether its copy is stale.
    /// </summary>
    public long Revision { get; private set; }

    /// <summary>Path of the open project, or null for an unsaved one.</summary>
    public string? ProjectPath { get; set; }

    public string ProjectName => ProjectPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>Number of voxels changed by the stroke in progress. Drives the status line.</summary>
    public int StrokeCellCount => _stroke?.RetainedCells ?? 0;

    public bool IsStrokeActive => _stroke is not null;

    /// <summary>Mirror planes Paint and Extrude repeat their writes across. Off until switched on.</summary>
    public Symmetry Symmetry { get; } = new();

    /// <summary>How a Transform drag snaps: the header's magnet and what it lands on.</summary>
    public SnapSettings Snap { get; } = new();

    /// <summary>
    /// A new voxel edit on the focused object, repeated across the mirror planes when symmetry is on.
    /// Only for the tools symmetry is for: a loop cut or a turn is never mirrored.
    /// </summary>
    private VoxelEditCommand NewMirroredEdit(string name) =>
        new(name, World) { Mirrors = Symmetry.ImagesFor(Scene.Focus) };

    // ---- Strokes -----------------------------------------------------------------------------

    /// <summary>
    /// Starts a gesture. Everything applied until <see cref="EndStroke"/> becomes one undo step, so
    /// a whole click-and-drag reverses as a single action.
    /// </summary>
    public void BeginStroke()
    {
        // Bound to the focused grid here, once: focus is locked for the rest of the gesture, and
        // undo has to reach this object even if focus has moved on by then.
        if (Scene.Focus is not null)
        {
            _stroke ??= NewMirroredEdit(ActiveTool.ToString());
        }
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
        if (Scene.Focus is null)
        {
            return false;
        }

        VoxelEditCommand command = NewMirroredEdit(name);

        // Judged by what was written, not by the count the operation reports: with symmetry on, the
        // side the cursor is on can already be done while its mirror image still changes.
        operation(command);
        if (command.IsEmpty)
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

        VoxelEditCommand preview = NewMirroredEdit($"Extrude {steps:+0;-0}");
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
        var command = new CreateObjectCommand(
            Scene,
            grid,
            source.Transform,
            source.Name + " (extruded)",
            parentId: Scene.ParentOf(source)?.Id ?? 0);
        command.Redo();
        History.Push(command);

        // Selected alongside the one it came out of, so the tools can go on with either.
        Scene.Select(command.Created!.Id);
        SelectedLightId = 0;
        KeepEditModeHonest();

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
        if (_stroke is null)
        {
            return false;
        }

        return PaintMode switch
        {
            // The whole object rather than the surface under the cursor. Still a fill; only how far
            // it reaches differs.
            PaintMode.Bucket when BucketWholeObject =>
                PaintOperations.FillObject(ActiveColorIndex, _stroke!) > 0,

            PaintMode.Bucket => PaintOperations.Bucket(
                hit.Voxel, hit.Face, ActiveColorIndex, BucketThreshold, _stroke!) > 0,

            // With no pattern loaded there is nothing to sample, so Pattern falls back to a plain
            // fill rather than doing nothing and looking broken.
            PaintMode.Pattern => Pattern is { } pattern
                ? PaintOperations.Pattern(hit.Voxel, hit.Face, pattern, BucketThreshold, _stroke!) > 0
                : PaintOperations.Bucket(hit.Voxel, hit.Face, ActiveColorIndex, BucketThreshold, _stroke!) > 0,

            // A gradient is a drag, not a click: PaintGradient, once it is let go.
            PaintMode.Gradient => false,

            PaintMode.Noise => PaintOperations.Mix(hit.Voxel, hit.Face, BucketThreshold,
                cell => PaintOperations.Scatter(cell) < MixAmount ? SecondaryColorIndex : ActiveColorIndex, _stroke!) > 0,

            PaintMode.Dither => PaintOperations.Mix(hit.Voxel, hit.Face, BucketThreshold,
                cell => PaintOperations.Bayer(cell, hit.Face) < MixAmount ? SecondaryColorIndex : ActiveColorIndex, _stroke!) > 0,

            _ => PaintOperations.Brush(hit.Voxel, hit.Face, BrushRadius, ActiveColorIndex, _stroke!) > 0,
        };
    }

    /// <summary>
    /// Paints a straight line or a box between two cells, as one step. Shift and Ctrl drags commit
    /// their shape on release rather than as the cursor travels.
    ///
    /// Whether the box is filled or an outline follows the paint mode rather than being a setting of
    /// its own: a brush stroke around a shape leaves an outline, and filling is the whole of what a
    /// bucket does.
    /// </summary>
    public bool PaintShape(Int3 from, Int3 to, Face face, bool asBox) =>
        RunStep(
            asBox ? "Paint box" : "Paint line",
            c => asBox
                ? FillsSolid
                    ? PaintOperations.BoxFilled(from, to, face, ActiveColorIndex, c)
                    : PaintOperations.BoxFrame(from, to, face, BrushRadius, ActiveColorIndex, c)
                : PaintOperations.Line(from, to, face, BrushRadius, ActiveColorIndex, c));

    /// <summary>True when the active mode fills a shape rather than outlining it.</summary>
    public bool FillsSolid => PaintMode is PaintMode.Bucket or PaintMode.Pattern;


    // ---- Sculpt --------------------------------------------------------------------------------

    public SculptMode SculptMode { get; set; } = SculptMode.Add;

    public SculptShape SculptShape { get; set; } = SculptShape.Sphere;

    /// <summary>The brush's radius in voxels.</summary>
    public float SculptRadius
    {
        get => _sculptRadius;
        set => _sculptRadius = float.IsFinite(value) ? Math.Clamp(value, SculptOperations.MinRadius, SculptOperations.MaxRadius) : _sculptRadius;
    }

    private float _sculptRadius = 3f;

    /// <summary>
    /// One dab of the Sculpt brush at a surface point of the focused object, part of the open stroke
    /// — so a whole drag is one undo step — and mirrored as symmetry says. <paramref name="mode"/>
    /// stands in for the tool's own mode while a key turns it round.
    /// </summary>
    public bool Sculpt(RaycastHit hit, SculptMode? mode = null)
    {
        BeginStroke();
        if (_stroke is null)
        {
            return false;
        }

        byte colour = ActiveColorIndex == Palette.EmptyIndex ? Palette.WhiteIndex : ActiveColorIndex;
        return SculptOperations.Dab(mode ?? SculptMode, SculptShape, SculptRadius, hit, colour, _stroke) > 0;
    }

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
    public void ApplyTransform(IPlaceable target, ObjectTransform transform)
    {
        if (target is VoxelSelectionHandle handle)
        {
            DragSelectionHandle(handle, transform);
            return;
        }

        target.Transform = transform;
        HasUnsavedChanges = true;
    }

    /// <summary>Records a finished gizmo drag as one undo step.</summary>
    public bool PushTransformEdit(IPlaceable target, ObjectTransform before, string name)
    {
        if (target is VoxelSelectionHandle)
        {
            return EndSelectionDrag();
        }

        if (before == target.Transform)
        {
            return false;
        }

        History.Push(new TransformCommand(target, before, target.Transform, name));
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Removes an object, the last one included: Shift+A adds new ones to an empty level.
    /// </summary>
    public bool DeleteObject(int objectId)
    {
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
        KeepEditModeHonest();

        HasUnsavedChanges = true;
        return true;
    }

    // ---- Objects as a whole ------------------------------------------------------------------

    /// <summary>
    /// A deliberate choice of object — from a list, not the cursor passing over. Unlike
    /// <see cref="TryFocus"/>, a held Extrude selection does not pin focus against it: the selection
    /// belongs to the object being left, and is dropped. Still refused mid-gesture.
    /// </summary>
    public bool ChooseObject(int objectId)
    {
        // Refused before anything is let go: a click on a locked row should change nothing at all.
        if (Scene.Find(objectId) is not { Locked: false } target || IsStrokeActive || IsExtruding)
        {
            return false;
        }

        // A hidden object cannot be selected, but it can still be the one Properties shows.
        if (!target.Visible)
        {
            DeselectAll();
            MoveFocus(objectId);
            return true;
        }

        return ClickSelect(objectId);
    }

    // ---- Selecting objects and lights ----------------------------------------------------------

    /// <summary>
    /// What the last click made active, Blender's active object: the picked light, else the focused
    /// object; 0 for neither. Properties shows it, the gizmo takes its axes, and Ctrl+P and Ctrl+J
    /// gather the rest of the selection into it. It can be active without being selected.
    /// </summary>
    public int ActiveId => SelectedLightId != 0 ? SelectedLightId : Scene.FocusId;

    public bool IsSelected(int id) => Scene.IsSelected(id);

    public int SelectedCount => Scene.SelectedCount;

    /// <summary>The selected objects, in list order.</summary>
    public IEnumerable<VoxelObject> SelectedObjects => Scene.SelectedObjects;

    /// <summary>The selected lights, in list order.</summary>
    public IEnumerable<SceneLight> SelectedLights => Scene.SelectedLights;

    /// <summary>
    /// A click on something, Blender's way. On its own it becomes the whole selection and the active
    /// one. With <paramref name="extend"/> — Shift — something not selected is added and made active,
    /// something selected is made active, and the active one is let go. False when it cannot be
    /// selected, locked or hidden or gone, and then nothing changes; refused mid-gesture too.
    /// </summary>
    public bool ClickSelect(int id, bool extend = false)
    {
        if (IsStrokeActive || IsExtruding || !Scene.CanSelect(id))
        {
            return false;
        }

        // Choosing another object is leaving the one being edited, as clicking one in Blender's
        // outliner is.
        if (InEditMode && id != _editObjectId)
        {
            ExitEditMode();
        }

        if (!extend)
        {
            DeselectAll();
        }
        else if (Scene.IsSelected(id) && ActiveId == id)
        {
            Deselect(id);
            return true;
        }

        Scene.Select(id);
        MakeActive(id);
        return true;
    }

    /// <summary>Takes one thing out of the selection. An object stays active, as in Blender; a light does not.</summary>
    public bool Deselect(int id)
    {
        if (!Scene.Deselect(id))
        {
            return false;
        }

        if (id == SelectedLightId)
        {
            SelectedLightId = 0;
        }

        return true;
    }

    public void DeselectAll()
    {
        Scene.DeselectAll();
        SelectedLightId = 0;
    }

    /// <summary>Selects everything that can be: every shown, unlocked object, and every unlocked light.</summary>
    public int SelectAll()
    {
        foreach (VoxelObject o in Scene.Objects)
        {
            Scene.Select(o.Id);
        }

        foreach (SceneLight light in Scene.Lights)
        {
            Scene.Select(light.Id);
        }

        KeepActiveSelected();
        return Scene.SelectedCount;
    }

    /// <summary>Selects what was not selected, and lets go of what was.</summary>
    public void InvertSelection()
    {
        var before = new HashSet<int>(Scene.SelectedIds);
        DeselectAll();

        foreach (VoxelObject o in Scene.Objects.Where(o => !before.Contains(o.Id)))
        {
            Scene.Select(o.Id);
        }

        foreach (SceneLight light in Scene.Lights.Where(l => !before.Contains(l.Id)))
        {
            Scene.Select(light.Id);
        }

        KeepActiveSelected();
    }

    /// <summary>
    /// A box's worth at once: replaces the selection, adds to it or takes from it. What cannot be
    /// selected is passed over.
    /// </summary>
    public void SelectMany(IEnumerable<int> ids, SelectionOperation operation)
    {
        if (IsStrokeActive || IsExtruding)
        {
            return;
        }

        if (operation == SelectionOperation.Replace)
        {
            Scene.DeselectAll();
        }

        foreach (int id in ids)
        {
            if (operation == SelectionOperation.Subtract)
            {
                Scene.Deselect(id);
            }
            else
            {
                Scene.Select(id);
            }
        }

        if (SelectedLightId != 0 && !Scene.IsSelected(SelectedLightId))
        {
            SelectedLightId = 0;
        }

        KeepActiveSelected();
    }

    /// <summary>
    /// When the active thing has dropped out of a selection that still holds something, the first
    /// selected object — else light — takes over, so what Properties shows is one of the selected.
    /// </summary>
    private void KeepActiveSelected()
    {
        if (Scene.SelectedCount == 0 || Scene.IsSelected(ActiveId))
        {
            return;
        }

        if (Scene.SelectedObjects.FirstOrDefault() is { } first)
        {
            SelectedLightId = 0;
            MoveFocus(first.Id);
        }
        else if (Scene.SelectedLights.FirstOrDefault() is { } light)
        {
            SelectedLightId = light.Id;
        }
    }

    private void MakeActive(int id)
    {
        if (Scene.FindLight(id) is not null)
        {
            SelectedLightId = id;
            return;
        }

        SelectedLightId = 0;
        MoveFocus(id);
    }

    /// <summary>Focus to another object. The Extrude selection belongs to the one being left.</summary>
    private void MoveFocus(int id)
    {
        if (id != Scene.FocusId && Scene.SetFocus(id))
        {
            CancelExtrude();
            Selection = null;
        }
    }

    /// <summary>What was just made becomes the selection, and the first of it the active one.</summary>
    private void SelectOnly(IEnumerable<int> ids)
    {
        // Something new made is chosen in Object Mode, as Blender leaves you after adding.
        ExitEditMode();
        DeselectAll();
        int first = 0;
        foreach (int id in ids)
        {
            Scene.Select(id);
            first = first == 0 ? id : first;
        }

        if (first != 0)
        {
            MakeActive(first);
        }
    }

    /// <summary>
    /// What a Transform drag moves: every selected light, and every selected object with voxels in
    /// it, that is not locked — less whatever has a parent that is moving too, since it goes along
    /// with the parent anyway and would otherwise move twice. The active one first: the gizmo's axes
    /// are its axes.
    /// </summary>
    public IReadOnlyList<IPlaceable> TransformTargets
    {
        get
        {
            // Inside an object, the gizmo is on its chosen voxels.
            if (InEditMode)
            {
                return SelectionHandle is { } handle ? [handle] : [];
            }

            var targets = new List<IPlaceable>();
            targets.AddRange(Scene.SelectedObjects.Where(o => !o.Locked && !o.IsEmpty));
            targets.AddRange(Scene.SelectedLights.Where(l => !l.Locked));

            targets.RemoveAll(t => targets.Any(other => other.Id != t.Id && Scene.IsDescendantOf(t.Id, other.Id)));

            int active = targets.FindIndex(t => t.Id == ActiveId);
            if (active > 0)
            {
                (targets[0], targets[active]) = (targets[active], targets[0]);
            }

            return targets;
        }
    }

    /// <summary>What a drag of several things turns about. A move moves them all alike whatever it is.</summary>
    public TransformPivot Pivot { get; set; } = TransformPivot.MedianPoint;

    /// <summary>
    /// Where the gizmo stands for these targets under <see cref="Pivot"/>: the active one's centre, or
    /// the middle of all of them. Each's own centre for Individual Origins too — the gizmo has to
    /// stand somewhere, and the middle is where Blender puts it.
    /// </summary>
    public Vector3 PivotPoint(IReadOnlyList<IPlaceable> targets)
    {
        if (targets.Count == 0)
        {
            return Vector3.Zero;
        }

        if (Pivot == TransformPivot.ActiveElement && targets[0].Id == ActiveId)
        {
            return targets[0].WorldCentre();
        }

        Vector3 sum = Vector3.Zero;
        foreach (IPlaceable target in targets)
        {
            sum += target.WorldCentre();
        }

        return sum / targets.Count;
    }

    /// <summary>Records a finished drag of several things as one undo step.</summary>
    public bool PushTransformEdits(IReadOnlyList<(IPlaceable Target, ObjectTransform Before)> moved, string name)
    {
        if (moved.Any(m => m.Target is VoxelSelectionHandle))
        {
            return EndSelectionDrag();
        }

        List<ICommand> steps = [.. moved
            .Where(m => m.Before != m.Target.Transform)
            .Select(m => (ICommand)new TransformCommand(m.Target, m.Before, m.Target.Transform, name))];

        if (steps.Count == 0)
        {
            return false;
        }

        History.Push(CompositeCommand.Of(name, steps));
        HasUnsavedChanges = true;
        return true;
    }

    // ---- The selection as a whole ------------------------------------------------------------

    /// <summary>A name for an operation on these: the one thing's own name, else how many.</summary>
    private static string Several(string verb, IReadOnlyList<IPlaceable> things) =>
        things.Count == 1 ? $"{verb} {things[0].Name}" : $"{verb} {things.Count} {(things.All(t => t is SceneLight) ? "lights" : "objects")}";

    /// <summary>Deletes everything selected, as one undo step. Returns how many things went.</summary>
    public int DeleteSelected()
    {
        List<IPlaceable> doomed = [.. Scene.SelectedObjects.Where(o => !o.Locked), .. Scene.SelectedLights.Where(l => !l.Locked)];
        if (doomed.Count == 0)
        {
            return 0;
        }

        EndStroke();
        CancelExtrude();

        int focus = Scene.FocusId;
        var steps = new List<ICommand>();
        foreach (IPlaceable thing in doomed)
        {
            ICommand step = thing is VoxelObject o ? new DeleteObjectCommand(Scene, o) : new DeleteLightCommand(Scene, (SceneLight)thing);
            step.Redo();
            steps.Add(step);
        }

        if (Scene.FocusId != focus)
        {
            Selection = null;
        }

        if (SelectedLightId != 0 && Scene.FindLight(SelectedLightId) is null)
        {
            SelectedLightId = 0;
        }

        History.Push(CompositeCommand.Of(Several("Delete", doomed), steps));
        KeepEditModeHonest();
        HasUnsavedChanges = true;
        return doomed.Count;
    }

    /// <summary>
    /// Copies everything selected beside where it is — all by the same step, so they keep their places
    /// among each other — and selects the copies. A copy of something whose parent is copied too goes
    /// under the parent's copy; otherwise it keeps the parent. One undo step.
    /// </summary>
    public IReadOnlyList<IPlaceable> DuplicateSelected(Vector3 towards)
    {
        List<VoxelObject> objects = [.. Scene.SelectedObjects.Where(o => !o.IsEmpty)];
        List<SceneLight> lights = [.. Scene.SelectedLights];
        if (objects.Count + lights.Count == 0)
        {
            return [];
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        Vector3 offset = SelectionOffset(objects, lights, towards);
        var copyOf = new Dictionary<int, int>();
        var steps = new List<ICommand>();
        var copies = new List<IPlaceable>();

        // Parents before their children, so a child's copy has its parent's copy to go under.
        foreach (VoxelObject source in ParentsFirst(objects))
        {
            int parent = Scene.ParentOf(source)?.Id ?? 0;
            var command = new CreateObjectCommand(
                Scene,
                source.Grid.Copy(),
                source.Transform.Translated(offset),
                DuplicateName(source.Name, Scene.Objects.Select(o => o.Name)),
                $"Duplicate {source.Name}",
                Scene.IndexOf(source.Id) + 1,
                copyOf.GetValueOrDefault(parent, parent));

            command.Redo();
            if (source.Modifiers.Count > 0)
            {
                command.Created!.SetModifiers(source.Modifiers);
            }

            steps.Add(command);
            copies.Add(command.Created!);
            copyOf[source.Id] = command.Created!.Id;
        }

        foreach (SceneLight source in lights)
        {
            SceneLight copy = Scene.CreateLight(source.Kind, source.Name);
            copy.Apply(source.State with
            {
                Name = DuplicateName(source.Name, Scene.Lights.Select(l => l.Name)),
                Transform = source.Transform.Translated(offset),
            });

            int parent = Scene.ParentOf(source)?.Id ?? 0;
            copy.ParentId = copyOf.GetValueOrDefault(parent, parent);

            var command = new AddLightCommand(Scene, copy, $"Duplicate {source.Name}");
            command.Redo();
            steps.Add(command);
            copies.Add(copy);
        }

        // The active one's copy is the active copy.
        int active = ActiveId;
        List<int> ids = [.. copies.Select(c => c.Id)];
        if (copyOf.TryGetValue(active, out int activeCopy))
        {
            ids.Remove(activeCopy);
            ids.Insert(0, activeCopy);
        }

        SelectOnly(ids);

        History.Push(CompositeCommand.Of(Several("Duplicate", [.. objects, .. lights]), steps));
        HasUnsavedChanges = true;
        return copies;
    }

    /// <summary>Objects ordered so that each comes after any of its ancestors among them.</summary>
    private List<VoxelObject> ParentsFirst(List<VoxelObject> objects) =>
        [.. objects.OrderBy(o => Depth(o))];

    private int Depth(IPlaceable thing)
    {
        int depth = 0;
        for (VoxelObject? parent = Scene.ParentOf(thing); parent is not null && depth <= Scene.Objects.Count; parent = Scene.ParentOf(parent))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// How far a copy of the selection goes: <see cref="DuplicateOffset"/> for the whole of it, in
    /// voxels of the largest size among it, so that a copy of each lands on its own lattice as often
    /// as the sizes allow.
    /// </summary>
    private static Vector3 SelectionOffset(List<VoxelObject> objects, List<SceneLight> lights, Vector3 towards)
    {
        if (objects.Count == 1 && lights.Count == 0)
        {
            return DuplicateOffset(objects[0], towards);
        }

        bool any = false;
        Vector3 min = Vector3.Zero, max = Vector3.Zero;
        float voxel = 1f;

        foreach (VoxelObject o in objects)
        {
            if (o.TryGetWorldBounds(out Vector3 lo, out Vector3 hi))
            {
                min = any ? Vector3.Min(min, lo) : lo;
                max = any ? Vector3.Max(max, hi) : hi;
                voxel = any ? MathF.Max(voxel, o.VoxelSize) : o.VoxelSize;
                any = true;
            }
        }

        if (!any)
        {
            // Lights alone: a couple of units aside, which is where a copied light is still seen apart.
            bool x = MathF.Abs(towards.X) >= MathF.Abs(towards.Z);
            float s = (x ? towards.X : towards.Z) >= 0f ? 1f : -1f;
            return x ? new Vector3(2f * s, 0f, 0f) : new Vector3(0f, 0f, 2f * s);
        }

        bool alongX = MathF.Abs(towards.X) >= MathF.Abs(towards.Z);
        float sign = (alongX ? towards.X : towards.Z) >= 0f ? 1f : -1f;
        Vector3 size = (max - min) / voxel;
        float step = (MathF.Ceiling((alongX ? size.X : size.Z) - 1e-3f) + 1f) * voxel;
        return alongX ? new Vector3(sign * step, 0f, 0f) : new Vector3(0f, 0f, sign * step);
    }

    /// <summary>Hides the selected objects and switches the selected lights off or back on. Outside undo, as hiding one is.</summary>
    public int HideSelected()
    {
        int changed = 0;
        foreach (VoxelObject o in Scene.SelectedObjects.ToList())
        {
            changed += SetObjectVisible(o.Id, false) ? 1 : 0;
        }

        foreach (SceneLight light in Scene.SelectedLights.ToList())
        {
            changed += SetLightVisible(light.Id, !light.Visible) ? 1 : 0;
        }

        return changed;
    }

    /// <summary>Locks everything selected, which lets go of it. Returns how many things were locked.</summary>
    public int LockSelected()
    {
        int locked = 0;
        foreach (VoxelObject o in Scene.SelectedObjects.ToList())
        {
            locked += SetObjectLocked(o.Id, true) ? 1 : 0;
        }

        foreach (SceneLight light in Scene.SelectedLights.ToList())
        {
            locked += SetLightLocked(light.Id, true) ? 1 : 0;
        }

        return locked;
    }

    /// <summary>
    /// Blender's Ctrl+P: every other selected thing becomes the child of the active object, staying
    /// where it is. What cannot go under it — the active's own parent, say — is passed over. One undo
    /// step; returns how many were parented.
    /// </summary>
    public int ParentSelectedToActive()
    {
        if (SelectedLightId != 0 || Scene.Focus is not { } parent)
        {
            return 0;
        }

        EndStroke();
        CancelExtrude();

        var steps = new List<ICommand>();
        var children = new List<IPlaceable>();
        foreach (IPlaceable child in Scene.SelectedObjects.Cast<IPlaceable>().Concat(Scene.SelectedLights).ToList())
        {
            if (child.Id == parent.Id || Scene.ParentProblem(child.Id, parent.Id) is not null || Scene.ParentOf(child)?.Id == parent.Id)
            {
                continue;
            }

            var command = new ParentCommand(Scene, child, parent.Id, $"Parent {child.Name} to {parent.Name}");
            command.Redo();
            steps.Add(command);
            children.Add(child);
        }

        if (steps.Count == 0)
        {
            return 0;
        }

        History.Push(CompositeCommand.Of(children.Count == 1 ? $"Parent {children[0].Name} to {parent.Name}" : $"Parent {children.Count} to {parent.Name}", steps));
        HasUnsavedChanges = true;
        return steps.Count;
    }

    /// <summary>Frees everything selected from its parent, leaving each where it is. One undo step.</summary>
    public int ClearParentOfSelected()
    {
        List<IPlaceable> freed = [.. Scene.SelectedObjects.Cast<IPlaceable>().Concat(Scene.SelectedLights).Where(t => Scene.ParentOf(t) is not null)];
        if (freed.Count == 0)
        {
            return 0;
        }

        EndStroke();
        CancelExtrude();

        var steps = new List<ICommand>();
        foreach (IPlaceable child in freed)
        {
            var command = new ParentCommand(Scene, child, 0, $"Clear parent of {child.Name}");
            command.Redo();
            steps.Add(command);
        }

        History.Push(CompositeCommand.Of(Several("Clear parent of", freed), steps));
        HasUnsavedChanges = true;
        return steps.Count;
    }

    /// <summary>
    /// Blender's Ctrl+J: the voxels of every other selected object go into the active one, and those
    /// objects out of the level. Only objects on the active one's lattice can; the rest are left as
    /// they are and counted in <paramref name="refused"/>. One undo step; returns how many were joined.
    /// </summary>
    public int JoinSelectedIntoActive(out int refused)
    {
        refused = 0;
        if (SelectedLightId != 0 || Scene.Focus is not { Locked: false } target)
        {
            return 0;
        }

        EndStroke();
        CancelExtrude();

        var steps = new List<ICommand>();
        foreach (VoxelObject source in Scene.SelectedObjects.Where(o => o.Id != target.Id).ToList())
        {
            if (JoinProblem(source.Id, target.Id) is not null
                || LatticeMap.Between(source.Transform, target.Transform, out _) is not { } map)
            {
                refused++;
                continue;
            }

            var command = new JoinCommand(Scene, source, target, map);
            command.Redo();
            steps.Add(command);
        }

        if (steps.Count == 0)
        {
            return 0;
        }

        Selection = null;
        History.Push(CompositeCommand.Of(steps.Count == 1 ? steps[0].Name : $"Join {steps.Count} into {target.Name}", steps));
        HasUnsavedChanges = true;
        return steps.Count;
    }

    /// <summary>
    /// One voxel operation done to every selected object that can take it, as one undo step: a flip,
    /// a quarter turn, a subdivide. Returns how many objects it was done to.
    /// </summary>
    private int ForEachSelected(string name, Func<VoxelObject, bool> can, Func<VoxelObject, ICommand> make)
    {
        List<VoxelObject> targets = [.. Scene.SelectedObjects.Where(o => !o.IsEmpty && !o.Locked && can(o))];
        if (targets.Count == 0)
        {
            return 0;
        }

        // Any gesture in progress, and the Extrude selection, were in the cells as they were.
        EndStroke();
        CancelExtrude();
        Selection = null;

        var steps = new List<ICommand>();
        foreach (VoxelObject target in targets)
        {
            ICommand command = make(target);
            command.Redo();
            steps.Add(command);
        }

        History.Push(CompositeCommand.Of(targets.Count == 1 ? $"{name} {targets[0].Name}" : $"{name} {targets.Count} objects", steps));
        HasUnsavedChanges = true;
        return targets.Count;
    }

    public int FlipSelected(Axis axis) =>
        ForEachSelected("Mirror", _ => true, o => new FlipObjectCommand(o.Grid, axis));

    public int RotateSelected(RotateDirection direction) =>
        ForEachSelected("Turn", _ => true, o => new RotateObjectCommand(o.Grid, direction));

    public int SubdivideSelected() =>
        ForEachSelected("Subdivide", o => SubdivideProblem(o) is null, o => new SubdivideObjectCommand(o, Symmetry));

    // ---- Clipboard -----------------------------------------------------------------------------

    /// <summary>What was last copied or cut, or null: the first piece of it. Kept by the session, not the system clipboard.</summary>
    public VoxelClipboard? Clipboard => _clipboard.Count > 0 ? _clipboard[0] : null;

    /// <summary>Every piece of what was last copied — one per object, when several were selected.</summary>
    public IReadOnlyList<VoxelClipboard> ClipboardPieces => _clipboard;

    private List<VoxelClipboard> _clipboard = [];

    /// <summary>Pastes since the last copy, so each lands a step further along instead of on the last.</summary>
    private int _pasteCount;

    /// <summary>
    /// True when a copy would take the voxels behind the Extrude selection rather than the whole
    /// focused object — only while Extrude is the tool, since a selection outlives its tool.
    /// </summary>
    public bool CopiesSelection => ActiveTool == EditorTool.Extrude && Selection is { IsEmpty: false };

    /// <summary>
    /// Copies the voxels behind the Extrude selection — a wall's thickness behind each selected face —
    /// or, with nothing selected, the whole focused object. Painted faces come along. Returns how many
    /// voxels were copied.
    /// </summary>
    public int Copy()
    {
        // In Edit Mode, the chosen voxels.
        if (!CopiesSelection && EditObject is { } edited && !VoxelSelection.IsEmpty)
        {
            VoxelWorld chosen = ClipboardOperations.Extract(edited.Grid, VoxelSelection.Cells);
            _clipboard = [new VoxelClipboard(chosen, edited.Transform, edited.Name)];
            _pasteCount = 0;
            return chosen.SolidCount;
        }

        // Behind the Extrude selection: the voxels of the one object it is on.
        if (CopiesSelection && Scene.Focus is { } focus)
        {
            VoxelWorld region = ClipboardOperations.Extract(focus.Grid, ClipboardOperations.RegionBehind(Selection!, focus.Grid));
            if (region.SolidCount == 0)
            {
                return 0;
            }

            _clipboard = [new VoxelClipboard(region, focus.Transform, focus.Name)];
            _pasteCount = 0;
            return region.SolidCount;
        }

        // Otherwise every selected object, whole.
        List<VoxelClipboard> pieces = [.. CopySources()
            .Select(o => new VoxelClipboard(ClipboardOperations.Extract(o.Grid, ClipboardOperations.Everything(o.Grid)), o.Transform, o.Name))
            .Where(piece => piece.Grid.SolidCount > 0)];

        if (pieces.Count == 0)
        {
            return 0;
        }

        _clipboard = pieces;
        _pasteCount = 0;
        return pieces.Sum(piece => piece.Grid.SolidCount);
    }

    /// <summary>What a copy takes whole: the selected objects. The phone selects what it focuses, so it has them too.</summary>
    private IEnumerable<VoxelObject> CopySources() => Scene.SelectedObjects;

    /// <summary>
    /// Copies, then takes the copied voxels out, as one undo step. With no Extrude selection it is the
    /// selected objects that go, whole.
    /// </summary>
    public int Cut()
    {
        EndStroke();
        CancelExtrude();

        if (!CopiesSelection && InEditMode && !VoxelSelection.IsEmpty)
        {
            int taken = Copy();
            DeleteSelectedVoxels();
            return taken;
        }

        bool region = CopiesSelection;
        if (!region)
        {
            // A locked object is not to be changed; copying it is fine, taking it away is not.
            List<VoxelObject> sources = [.. CopySources()];
            if (sources.Count == 0 || sources.Any(o => o.Locked))
            {
                return 0;
            }

            int whole = Copy();
            if (whole == 0)
            {
                return 0;
            }

            var steps = new List<ICommand>();
            foreach (VoxelObject o in sources)
            {
                var delete = new DeleteObjectCommand(Scene, o);
                delete.Redo();
                steps.Add(delete);
            }

            Selection = null;
            History.Push(CompositeCommand.Of(sources.Count == 1 ? $"Cut {sources[0].Name}" : $"Cut {sources.Count} objects", steps));
            HasUnsavedChanges = true;
            return whole;
        }

        if (Scene.Focus is not { Locked: false } focus)
        {
            return 0;
        }

        int copied = Copy();
        if (copied == 0)
        {
            return 0;
        }

        var command = new VoxelEditCommand("Cut", focus.Grid);
        foreach (Int3 cell in ClipboardOperations.Everything(Clipboard!.Grid))
        {
            command.Apply(cell, Palette.EmptyIndex);
        }

        // The selected faces are gone with the voxels under them.
        Selection = null;

        History.Push(command);
        HasUnsavedChanges = true;
        return copied;
    }

    /// <summary>
    /// Pastes as a new object, on the lattice of the one the voxels came from, beside where they were
    /// copied from — further along with each paste. The new object is focused. One undo step.
    /// </summary>
    public VoxelObject? Paste(Vector3 towards)
    {
        if (_clipboard.Count == 0)
        {
            return null;
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        // All the pieces by one step, so they land as they were copied, among each other.
        _pasteCount++;
        List<VoxelObject> placed = [.. _clipboard.Select(piece => new VoxelObject(0, piece.Grid, piece.Transform, piece.Name))];
        Vector3 offset = SelectionOffset(placed, [], towards) * _pasteCount;

        var steps = new List<ICommand>();
        var pasted = new List<int>();
        foreach (VoxelClipboard piece in _clipboard)
        {
            var command = new CreateObjectCommand(
                Scene,
                piece.Grid.Copy(),
                piece.Transform.Translated(offset),
                DuplicateName(piece.Name, Scene.Objects.Select(o => o.Name)),
                "Paste");

            command.Redo();
            steps.Add(command);
            pasted.Add(command.Created!.Id);
        }

        SelectOnly(pasted);
        History.Push(CompositeCommand.Of("Paste", steps));
        HasUnsavedChanges = true;
        return Scene.Find(pasted[0]);
    }

    /// <summary>Why one object cannot be joined into another, or null when it can.</summary>
    public string? JoinProblem(int sourceId, int targetId)
    {
        if (sourceId == targetId || Scene.Find(sourceId) is not { } source || Scene.Find(targetId) is not { } target)
        {
            return "Pick another object to join into.";
        }

        return LatticeMap.Between(source.Transform, target.Transform, out string reason) is null ? reason : null;
    }

    /// <summary>
    /// Moves one object's voxels into another and takes the first out of the level, as one undo step.
    /// Only between objects on the same lattice, so nothing is resampled; where the two overlap, the
    /// one being joined wins.
    /// </summary>
    public bool JoinInto(int sourceId, int targetId)
    {
        if (JoinProblem(sourceId, targetId) is not null
            || Scene.Find(sourceId) is not { } source
            || Scene.Find(targetId) is not { } target
            || LatticeMap.Between(source.Transform, target.Transform, out _) is not { } map)
        {
            return false;
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        var command = new JoinCommand(Scene, source, target, map);
        command.Redo();
        History.Push(command);
        KeepEditModeHonest();
        HasUnsavedChanges = true;
        return true;
    }

    // ---- Adding --------------------------------------------------------------------------------

    /// <summary>
    /// The shape just added, and how it was made — what Blender's Adjust Last Operation works on — for
    /// as long as nothing has been done since, and it has not been locked. Null otherwise.
    /// </summary>
    public AddedShape? LastShape =>
        _lastShape is { } shape
        && ReferenceEquals(History.LastDone, shape.Command)
        && Scene.Find(shape.Object.Id) is { Locked: false }
            ? shape
            : null;

    private AddedShape? _lastShape;

    /// <summary>
    /// A new object made of <paramref name="grid"/>, set against the surface at <paramref name="point"/>
    /// facing <paramref name="normal"/> (see <see cref="Placement.Against"/>) with voxels of
    /// <paramref name="voxelSize"/>. Named <paramref name="name"/>, or the next free name after it.
    /// Focused; one undo step.
    /// </summary>
    public VoxelObject AddObject(VoxelWorld grid, string name, Vector3 point, Vector3 normal, float voxelSize)
    {
        EndStroke();
        CancelExtrude();
        Selection = null;
        SelectedLightId = 0;

        float size = ObjectTransform.ValidVoxelSize(voxelSize) ?? 1f;
        string unique = Scene.Objects.Any(o => o.Name == name) ? DuplicateName(name, Scene.Objects.Select(o => o.Name)) : name;

        var command = new CreateObjectCommand(Scene, grid, Placement.Against(grid, point, normal, size), unique, $"Add {name}");
        command.Redo();
        History.Push(command);
        SelectOnly([command.Created!.Id]);

        HasUnsavedChanges = true;
        return command.Created!;
    }

    /// <summary>A shape, in the colour in hand, added as <see cref="AddObject"/> adds — and kept for adjusting.</summary>
    public VoxelObject AddShape(ShapeSettings settings, Vector3 point, Vector3 normal, float voxelSize)
    {
        byte colour = ActiveColorIndex == Palette.EmptyIndex ? Palette.WhiteIndex : ActiveColorIndex;
        VoxelObject added = AddObject(Shapes.Build(settings, colour), Shapes.NameOf(settings.Kind), point, normal, voxelSize);

        _lastShape = new AddedShape(added, settings, colour, point, normal, History.LastDone!);
        return added;
    }

    /// <summary>
    /// Makes the shape just added again with other settings, in place: the same object, set against
    /// the same point, and still the one undo step it was added in. False when something has been done
    /// since, or for another kind of shape.
    /// </summary>
    public bool ReshapeLast(ShapeSettings settings)
    {
        if (LastShape is not { } last || last.Settings.Kind != settings.Kind)
        {
            return false;
        }

        VoxelWorld grid = Shapes.Build(settings, last.Colour);
        last.Object.Grid.ReplaceWith(grid);
        last.Object.Transform = Placement.Against(grid, last.Point, last.Normal, last.Object.VoxelSize);

        _lastShape = last with { Settings = settings };
        HasUnsavedChanges = true;
        return true;
    }

    // ---- Parents -------------------------------------------------------------------------------

    /// <summary>Why one thing cannot be put under another, or null when it can. 0 is no parent, which always can.</summary>
    public string? ParentProblem(int childId, int parentId) => Scene.ParentProblem(childId, parentId);

    /// <summary>
    /// Makes an object or a light the child of an object, which it then moves with; 0 for none. It
    /// stays where it is either way. One undo step; false when refused or when nothing would change.
    /// </summary>
    public bool SetParent(int childId, int parentId)
    {
        if (Scene.FindPlaceable(childId) is not { } child
            || Scene.ParentProblem(childId, parentId) is not null
            || (Scene.ParentOf(child)?.Id ?? 0) == parentId)
        {
            return false;
        }

        EndStroke();
        CancelExtrude();

        string name = Scene.Find(parentId) is { } parent ? $"Parent {child.Name} to {parent.Name}" : $"Clear parent of {child.Name}";
        var command = new ParentCommand(Scene, child, parentId, name);
        command.Redo();
        History.Push(command);
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>Frees something from its parent, leaving it where it is. One undo step.</summary>
    public bool ClearParent(int childId) => SetParent(childId, 0);

    // ---- Lights --------------------------------------------------------------------------------

    /// <summary>
    /// The light picked in the outliner or the viewport, or 0. While one is picked, the Transform
    /// tool moves and aims it instead of the focused object. The tools that edit voxels still work
    /// on the focused object, since a light has none.
    /// </summary>
    public int SelectedLightId { get; private set; }

    /// <summary>The picked light, or null, including when it has since been deleted or undone away.</summary>
    public SceneLight? SelectedLight => SelectedLightId == 0 ? null : Scene.FindLight(SelectedLightId);

    /// <summary>
    /// The one of <see cref="TransformTargets"/> the gizmo takes its axes from — the active one when it
    /// is among them — or null when nothing selected can be moved.
    /// </summary>
    public IPlaceable? TransformTarget => TransformTargets is [var first, ..] ? first : null;

    /// <summary>A click on a light's icon: the light becomes the selection, and active.</summary>
    public bool SelectLight(int lightId)
    {
        // A locked light is passed over, as a locked object is.
        if (Scene.FindLight(lightId) is not { Locked: false })
        {
            return false;
        }

        return ClickSelect(lightId);
    }

    /// <summary>Lets go of the active light, if one is.</summary>
    public void ClearLightSelection()
    {
        if (SelectedLightId != 0)
        {
            Deselect(SelectedLightId);
        }
    }

    /// <summary>
    /// Adds a light, picks it, and records it as one undo step. Named after its kind, numbered the
    /// way copies are when the name is taken.
    /// </summary>
    public SceneLight AddLight(LightKind kind, Vector3 position, Vector3 shining)
    {
        string name = kind switch
        {
            LightKind.Directional => "Sun",
            LightKind.Point => "Point",
            _ => "Spot",
        };

        if (Scene.Lights.Any(l => l.Name == name))
        {
            name = DuplicateName(name, Scene.Lights.Select(l => l.Name));
        }

        SceneLight light = Scene.CreateLight(kind, name);
        light.Transform = new ObjectTransform(position, SceneLight.Aiming(shining));

        if (kind == LightKind.Directional)
        {
            light.Intensity = SceneLight.SunIntensity;
        }

        var command = new AddLightCommand(Scene, light, $"Add {name}");
        command.Redo();
        History.Push(command);

        SelectOnly([light.Id]);
        HasUnsavedChanges = true;
        return light;
    }

    /// <summary>A copy of a light beside it, picked. One undo step.</summary>
    public SceneLight? DuplicateLight(int lightId, Vector3 offset)
    {
        if (Scene.FindLight(lightId) is not { } source)
        {
            return null;
        }

        SceneLight copy = Scene.CreateLight(source.Kind, source.Name);
        copy.Apply(source.State with
        {
            Name = DuplicateName(source.Name, Scene.Lights.Select(l => l.Name)),
            Transform = source.Transform.Translated(offset),
        });

        // Under the same parent; held where it lands once it is in the level.
        copy.ParentId = Scene.ParentOf(source)?.Id ?? 0;

        var command = new AddLightCommand(Scene, copy, $"Duplicate {source.Name}");
        command.Redo();
        History.Push(command);

        SelectOnly([copy.Id]);
        HasUnsavedChanges = true;
        return copy;
    }

    /// <summary>
    /// Takes a light out. Unlike objects, the last one can go: a level lit by nothing but its
    /// ambient floor is a strange choice, not a dead end.
    /// </summary>
    public bool DeleteLight(int lightId)
    {
        if (Scene.FindLight(lightId) is not { } light)
        {
            return false;
        }

        var command = new DeleteLightCommand(Scene, light);
        command.Redo();
        History.Push(command);

        if (SelectedLightId == lightId)
        {
            SelectedLightId = 0;
        }

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>Records a finished edit of a light's settings as one undo step.</summary>
    public bool PushLightEdit(SceneLight light, LightState before, string name)
    {
        if (before == light.State)
        {
            return false;
        }

        History.Push(new LightEditCommand(light, before, light.State, name));
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>Switches a light on or off. Like hiding an object: saved, but outside undo.</summary>
    public bool SetLightVisible(int lightId, bool visible)
    {
        if (Scene.FindLight(lightId) is not { } light || light.Visible == visible)
        {
            return false;
        }

        light.Visible = visible;
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Locks or unlocks a light. A locked light still shines; its icon is only no longer something to
    /// pick, move or aim. Picked when it is locked, it is let go.
    /// </summary>
    public bool SetLightLocked(int lightId, bool locked)
    {
        if (Scene.FindLight(lightId) is not { } light || light.Locked == locked)
        {
            return false;
        }

        light.Locked = locked;
        if (locked)
        {
            Deselect(lightId);
        }

        HasUnsavedChanges = true;
        return true;
    }

    public bool RenameLight(int lightId, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || Scene.FindLight(lightId) is not { } light)
        {
            return false;
        }

        if (light.Name != trimmed)
        {
            light.Name = trimmed;
            HasUnsavedChanges = true;
        }

        return true;
    }

    /// <summary>The ambient floor. Saved with the level; outside undo, like the lights' switches.</summary>
    public bool SetAmbient(float ambient)
    {
        float before = Scene.Ambient;
        Scene.Ambient = ambient;

        if (Scene.Ambient == before)
        {
            return false;
        }

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Renames an object. Not an undo step — a name is not part of what the voxels are, and the
    /// phone treats it the same way — but it is saved, so the level counts as changed. Refused for a
    /// blank name.
    /// </summary>
    public bool RenameObject(int objectId, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || Scene.Find(objectId) is not { } target)
        {
            return false;
        }

        if (target.Name != trimmed)
        {
            target.Name = trimmed;
            HasUnsavedChanges = true;
        }

        return true;
    }

    /// <summary>
    /// Shows or hides an object. Hidden objects are not drawn, picked or exported, so the state is
    /// saved with the level; but it is a way of looking rather than an edit to the voxels, so it goes
    /// around the undo stack, as it does on the phone.
    ///
    /// Hiding the focused object moves focus to the nearest visible one: every tool acts on the
    /// focused object, and a gizmo floating over nothing is no use to anybody.
    /// </summary>
    public bool SetObjectVisible(int objectId, bool visible)
    {
        if (Scene.Find(objectId) is not { } target || target.Visible == visible)
        {
            return false;
        }

        target.Visible = visible;
        if (!visible)
        {
            Scene.Deselect(target.Id);
            KeepEditModeHonest();
        }

        if (!visible && target.Id == Scene.FocusId)
        {
            EndStroke();
            CancelExtrude();
            Selection = null;

            if (NearestVisible(Scene.IndexOf(target.Id)) is { } next)
            {
                Scene.SetFocus(next.Id);
            }
        }

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Locks or unlocks an object: a locked one is drawn and exported, but cannot be picked, focused
    /// or edited from the viewport. Saved with the level and outside undo, like hiding.
    ///
    /// Locking the focused object moves focus on, as hiding it does: the tools act on whatever holds
    /// focus, and holding it is exactly what a locked object must not do.
    /// </summary>
    public bool SetObjectLocked(int objectId, bool locked)
    {
        if (Scene.Find(objectId) is not { } target || target.Locked == locked)
        {
            return false;
        }

        target.Locked = locked;
        if (locked)
        {
            Scene.Deselect(target.Id);
            KeepEditModeHonest();
        }

        if (locked && target.Id == Scene.FocusId)
        {
            EndStroke();
            CancelExtrude();
            Selection = null;

            if (NearestVisible(Scene.IndexOf(target.Id)) is { } next)
            {
                Scene.SetFocus(next.Id);
            }
        }

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>Unlocks every locked object and light. Returns how many there were.</summary>
    public int UnlockAll()
    {
        int unlocked = 0;
        foreach (VoxelObject o in Scene.Objects.Where(o => o.Locked))
        {
            o.Locked = false;
            unlocked++;
        }

        foreach (SceneLight light in Scene.Lights.Where(l => l.Locked))
        {
            light.Locked = false;
            unlocked++;
        }

        if (unlocked > 0)
        {
            HasUnsavedChanges = true;
        }

        return unlocked;
    }

    /// <summary>Shows every hidden object. Returns how many there were.</summary>
    public int ShowAllObjects()
    {
        int shown = 0;
        foreach (VoxelObject o in Scene.Objects)
        {
            if (!o.Visible)
            {
                o.Visible = true;
                shown++;
            }
        }

        if (shown > 0)
        {
            HasUnsavedChanges = true;
        }

        return shown;
    }

    /// <summary>The visible, unlocked object nearest a place in the list, looking down it first.</summary>
    private VoxelObject? NearestVisible(int index)
    {
        IReadOnlyList<VoxelObject> objects = Scene.Objects;

        static bool CanHoldFocus(VoxelObject o) => o.Visible && !o.Locked;

        for (int step = 1; step < objects.Count; step++)
        {
            if (index + step < objects.Count && CanHoldFocus(objects[index + step]))
            {
                return objects[index + step];
            }

            if (index - step >= 0 && CanHoldFocus(objects[index - step]))
            {
                return objects[index - step];
            }
        }

        return null;
    }

    /// <summary>
    /// Copies the focused object — voxels, painted faces, turn — and places the copy beside it, one
    /// voxel clear, on the side <paramref name="towards"/> points most along. The host passes the
    /// camera's right, so the copy turns up to the right of the original on screen rather than on
    /// top of it, where it would look as though nothing had happened.
    ///
    /// The copy is focused and goes straight after the original in the list. One undo step.
    /// </summary>
    public VoxelObject? DuplicateFocus(Vector3 towards)
    {
        if (Scene.Focus is not { IsEmpty: false } source)
        {
            return null;
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        ObjectTransform placed = source.Transform with
        {
            Position = source.Transform.Position + DuplicateOffset(source, towards),
        };

        var command = new CreateObjectCommand(
            Scene,
            source.Grid.Copy(),
            placed,
            DuplicateName(source.Name, Scene.Objects.Select(o => o.Name)),
            $"Duplicate {source.Name}",
            Scene.IndexOf(source.Id) + 1,
            Scene.ParentOf(source)?.Id ?? 0);

        command.Redo();
        if (source.Modifiers.Count > 0)
        {
            command.Created!.SetModifiers(source.Modifiers);
        }

        History.Push(command);
        SelectOnly([command.Created!.Id]);

        HasUnsavedChanges = true;
        return command.Created;
    }

    /// <summary>
    /// Where a copy goes relative to its original: along X or Z, whichever the direction leans on
    /// more, by the object's world width on that axis plus one voxel. Whole voxels of the object's
    /// own size, so a copy of an object on its lattice stays on it. Never up or down — the camera's
    /// right never points that way, and a copy stacked on top is rarely where it is wanted.
    /// </summary>
    public static Vector3 DuplicateOffset(VoxelObject source, Vector3 towards)
    {
        float voxel = source.VoxelSize;
        Vector3 size = source.TryGetWorldBounds(out Vector3 min, out Vector3 max) ? (max - min) / voxel : Vector3.One;

        bool alongX = MathF.Abs(towards.X) >= MathF.Abs(towards.Z);
        float sign = (alongX ? towards.X : towards.Z) >= 0f ? 1f : -1f;
        float step = (MathF.Ceiling((alongX ? size.X : size.Z) - 1e-3f) + 1f) * voxel;

        return alongX ? new Vector3(sign * step, 0f, 0f) : new Vector3(0f, 0f, sign * step);
    }

    /// <summary>
    /// Blender's naming: "Tree" becomes "Tree.001", and "Tree.001" becomes "Tree.002" rather than
    /// "Tree.001.001" — the first number not already taken.
    /// </summary>
    public static string DuplicateName(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.Ordinal);

        string stem = name;
        int dot = name.LastIndexOf('.');
        if (dot > 0 && name.Length - dot - 1 >= 3 && name.AsSpan(dot + 1).IndexOfAnyExceptInRange('0', '9') < 0)
        {
            stem = name[..dot];
        }

        for (int number = 1; ; number++)
        {
            string candidate = $"{stem}.{number:000}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
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
        KeepEditModeHonest();

        HasUnsavedChanges = true;
        PreviewCutPlane = null;
        return true;
    }

    // ---- History and palette -----------------------------------------------------------------

    public bool Undo()
    {
        CancelExtrude();
        EndStroke();
        CancelVoxelTransform();

        if (!History.Undo())
        {
            return false;
        }

        RetainSelection();
        ForgetGoneLight();
        KeepEditModeHonest();
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Drops whatever of the selection the world no longer shows. Undoing an extrude takes away the
    /// very faces the selection had advanced onto, and left as it was the arrow stood in empty air,
    /// still offering a pull.
    /// </summary>
    private void RetainSelection()
    {
        if (Selection is not { } selection)
        {
            return;
        }

        FaceSelection kept = selection.Retain(World);
        Selection = kept.IsEmpty ? null : kept;
    }

    /// <summary>An active light that an undo or redo took out of the level is active no longer.</summary>
    private void ForgetGoneLight()
    {
        if (SelectedLightId != 0 && !Scene.IsSelected(SelectedLightId))
        {
            SelectedLightId = 0;
        }
    }

    public bool Redo()
    {
        CancelExtrude();
        EndStroke();
        CancelVoxelTransform();

        if (!History.Redo())
        {
            return false;
        }

        RetainSelection();
        ForgetGoneLight();
        KeepEditModeHonest();
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
    /// Mirrors the focused object along one of its own axes, in its voxels rather than its transform,
    /// for the same reasons a quarter turn works that way.
    /// </summary>
    public bool FlipFocus(Axis axis)
    {
        if (Scene.Focus is not { IsEmpty: false, Locked: false } focus)
        {
            return false;
        }

        // Any gesture in progress was aimed at the model as it was a moment ago.
        EndStroke();
        CancelExtrude();
        Selection = null;

        var command = new FlipObjectCommand(focus.Grid, axis);
        command.Redo();
        History.Push(command);

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Why an object cannot be subdivided, in words for a tooltip; null when it can.
    /// </summary>
    public string? SubdivideProblem(VoxelObject? target)
    {
        if (target is not { IsEmpty: false })
        {
            return "There are no voxels to subdivide.";
        }

        if (target.Locked)
        {
            return "It is locked. Unlock it in the Outliner first.";
        }

        long after = (long)target.Grid.SolidCount * Subdivide.Factor * Subdivide.Factor * Subdivide.Factor;
        if (after > Subdivide.MaxVoxels)
        {
            return $"It would hold {after:N0} voxels, and {Subdivide.MaxVoxels:N0} is the most an object can.";
        }

        if (target.VoxelSize / Subdivide.Factor < ObjectTransform.MinVoxelSize)
        {
            return "Its voxels are already as small as they can be.";
        }

        return null;
    }

    /// <summary>
    /// Cuts every voxel of the focused object into 2 × 2 × 2 of half the size. It keeps its size and
    /// place in the world, and its colours, face by face; it holds eight times the voxels, so there
    /// is room for detail the coarse version had no cells for.
    /// </summary>
    public bool SubdivideFocus()
    {
        if (Scene.Focus is not { } focus || SubdivideProblem(focus) is not null)
        {
            return false;
        }

        // Any gesture in progress, and the selection, were in the cells as they were a moment ago.
        EndStroke();
        CancelExtrude();
        Selection = null;

        var command = new SubdivideObjectCommand(focus, Symmetry);
        command.Redo();
        History.Push(command);

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
        if (Scene.Focus is not { IsEmpty: false, Locked: false } focus)
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
    /// Sets how big one of an object's voxels is in the world. The object grows or shrinks about its
    /// own origin — for a model built from the corner up, that keeps it standing where it stood.
    ///
    /// An undo step now, where the level-wide size was not: it moves the object in the world and
    /// changes how it sits against its neighbours, which is an edit to the level.
    /// </summary>
    public bool SetObjectVoxelSize(int objectId, float size)
    {
        if (Scene.Find(objectId) is not { } target
            || ObjectTransform.ValidVoxelSize(size) is not { } valid
            || valid == target.VoxelSize)
        {
            return false;
        }

        ObjectTransform before = target.Transform;
        target.Transform = before with { VoxelSize = valid };
        History.Push(new TransformCommand(target, before, target.Transform, "Voxel size"));
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

    /// <summary>What a palette entry is made of, changed live — a drag — without an undo step until <see cref="PushMaterialEdit"/>.</summary>
    public void SetMaterial(int index, VoxelMaterial material)
    {
        if (index == Palette.EmptyIndex)
        {
            return;
        }

        int revision = Scene.Palette.MaterialRevision;
        Scene.Palette.SetMaterial(index, material);
        if (Scene.Palette.MaterialRevision != revision)
        {
            HasUnsavedChanges = true;
        }
    }

    /// <summary>Records a finished change to an entry's material as one undo step.</summary>
    public bool PushMaterialEdit(int index, VoxelMaterial before)
    {
        if (index == Palette.EmptyIndex || Scene.Palette.Material(index) == before)
        {
            return false;
        }

        History.Push(new MaterialEditCommand(Scene.Palette, index, before, Scene.Palette.Material(index)));
        HasUnsavedChanges = true;
        return true;
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
    /// <summary>A new level around one grid, and the sun every new level starts with.</summary>
    public void ReplaceWorld(VoxelWorld world, string? projectPath)
    {
        var scene = new VoxelScene();
        scene.ReplacePalette(world.Palette);
        scene.Add(world, ObjectTransform.Identity, "Object 1");
        scene.AddDefaultSun();
        ReplaceScene(scene, projectPath);
    }

    /// <summary>Replaces the whole level.</summary>
    public void ReplaceScene(VoxelScene scene, string? projectPath)
    {
        ExitEditMode();
        _stroke = null;
        _extrudePreview = null;
        ExtrudeSteps = 0;
        Selection = null;

        Scene = scene;
        SelectedLightId = 0;
        Symmetry.Forget();

        // A level saved with its first object locked would otherwise open with focus on the one
        // object the tools may not touch.
        if (Scene.Focus is { Locked: true } && Scene.Objects.FirstOrDefault(o => o.Visible && !o.Locked) is { } open)
        {
            Scene.SetFocus(open.Id);
        }

        // A level that comes with nothing selected opens with its active object selected, as
        // Blender's does, so the tools have something to work on from the first click.
        if (Scene.SelectedCount == 0)
        {
            Scene.Select(Scene.FocusId);
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
