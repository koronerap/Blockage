using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>How the Select tool picks voxels in Edit Mode.</summary>
public enum VoxelSelectMode
{
    /// <summary>A click takes one voxel, a drag the voxels in a box.</summary>
    Box,

    /// <summary>A click takes the voxels joined to it that are its colour: the magic wand.</summary>
    Wand,

    /// <summary>A click takes every voxel of its colour in the object.</summary>
    Colour,
}

/// <summary>
/// Edit Mode (Fullreleaseplan 0.3), Blender's Tab: inside the active object, where the Select tool
/// picks voxels rather than objects, the gizmo moves and turns them within the object's lattice, and
/// Delete, Fill and P act on them.
/// </summary>
public sealed partial class EditorSession
{
    private int _editObjectId;

    /// <summary>True while inside an object, working on its voxels.</summary>
    public bool InEditMode { get; private set; }

    /// <summary>The object Edit Mode is inside, or null outside it.</summary>
    public VoxelObject? EditObject => InEditMode ? Scene.Find(_editObjectId) : null;

    /// <summary>The voxels chosen in Edit Mode, of <see cref="EditObject"/>.</summary>
    public VoxelSelection VoxelSelection { get; } = new();

    public VoxelSelectMode VoxelSelectMode { get; set; } = VoxelSelectMode.Box;

    /// <summary>
    /// Goes into the active object. Refused when there is none that can be worked on — locked,
    /// hidden, a light — and mid-gesture.
    /// </summary>
    public bool EnterEditMode()
    {
        if (InEditMode)
        {
            return true;
        }

        if (IsStrokeActive || IsExtruding || SelectedLightId != 0 || Scene.Focus is not { Locked: false, Visible: true } focus)
        {
            return false;
        }

        Scene.Select(focus.Id);
        _editObjectId = focus.Id;
        VoxelSelection.Clear();
        InEditMode = true;
        return true;
    }

    /// <summary>Back out to the objects. A voxel drag still running is thrown away.</summary>
    public void ExitEditMode()
    {
        if (!InEditMode)
        {
            return;
        }

        CancelVoxelTransform();
        VoxelSelection.Clear();
        InEditMode = false;
        _editObjectId = 0;
    }

    public bool ToggleEditMode()
    {
        if (InEditMode)
        {
            ExitEditMode();
            return true;
        }

        return EnterEditMode();
    }

    /// <summary>
    /// Leaves Edit Mode if its object can no longer be edited — deleted, undone away, hidden, locked —
    /// and drops from the selection whatever is no longer a voxel.
    /// </summary>
    private void KeepEditModeHonest()
    {
        if (!InEditMode)
        {
            return;
        }

        if (Scene.Find(_editObjectId) is not { Locked: false, Visible: true } edited)
        {
            ExitEditMode();
            return;
        }

        if (Scene.FocusId != edited.Id)
        {
            Scene.SetFocus(edited.Id);
        }

        VoxelSelection.Retain(edited.Grid);
    }

    // ---- Choosing voxels -----------------------------------------------------------------------

    /// <summary>Replaces, adds to or takes from the voxel selection. Only solid cells of the edited object count.</summary>
    public void SelectVoxels(IEnumerable<Int3> cells, SelectionOperation operation)
    {
        if (EditObject is not { } edited || IsVoxelTransforming)
        {
            return;
        }

        VoxelSelection.Set(cells.Where(edited.Grid.IsSolid), operation);
    }

    /// <summary>
    /// A click on a voxel, by the Select tool's mode: the voxel, the wand's patch, or its colour.
    /// Shift adds — or, for one voxel, lets go of it if it was chosen — Ctrl takes away.
    /// </summary>
    public void ClickVoxel(Int3 cell, bool shift, bool control)
    {
        if (EditObject is not { } edited || !edited.Grid.IsSolid(cell))
        {
            return;
        }

        IEnumerable<Int3> picked = VoxelSelectMode switch
        {
            VoxelSelectMode.Wand => VoxelSelecting.Connected(edited.Grid, cell),
            VoxelSelectMode.Colour => VoxelSelecting.OfColour(edited.Grid, edited.Grid.GetVoxel(cell)),
            _ => [cell],
        };

        SelectionOperation operation = control
            ? SelectionOperation.Subtract
            : shift
                ? VoxelSelectMode == VoxelSelectMode.Box && VoxelSelection.Contains(cell) ? SelectionOperation.Subtract : SelectionOperation.Add
                : SelectionOperation.Replace;

        SelectVoxels(picked, operation);
    }

    public int SelectAllVoxels()
    {
        if (EditObject is { } edited)
        {
            SelectVoxels(ClipboardOperations.Everything(edited.Grid), SelectionOperation.Replace);
        }

        return VoxelSelection.Count;
    }

    public void DeselectAllVoxels() => VoxelSelection.Clear();

    public void InvertVoxelSelection()
    {
        if (EditObject is not { } edited)
        {
            return;
        }

        List<Int3> rest = [.. ClipboardOperations.Everything(edited.Grid).Where(cell => !VoxelSelection.Contains(cell))];
        VoxelSelection.Restore(rest);
    }

    /// <summary>Takes in the voxels touching the selection face to face, Blender's Select More.</summary>
    public int GrowVoxelSelection()
    {
        if (EditObject is not { } edited)
        {
            return 0;
        }

        List<Int3> border = VoxelSelecting.Border(edited.Grid, VoxelSelection);
        VoxelSelection.Set(border, SelectionOperation.Add);
        return border.Count;
    }

    /// <summary>Lets go of the voxels on the selection's edge, Blender's Select Less.</summary>
    public int ShrinkVoxelSelection()
    {
        List<Int3> rim = VoxelSelecting.Rim(VoxelSelection);
        VoxelSelection.Set(rim, SelectionOperation.Subtract);
        return rim.Count;
    }

    // ---- Editing the chosen voxels -------------------------------------------------------------

    /// <summary>
    /// A voxel edit made in Edit Mode, which puts back the voxel selection as it was on either side
    /// of it — undoing a move brings the selection back to where the voxels were.
    /// </summary>
    private sealed class EditModeCommand(EditorSession session, ICommand inner, Int3[] before, Int3[] after) : ICommand
    {
        public string Name => inner.Name;

        public int RetainedCells => inner.RetainedCells + before.Length + after.Length;

        public void Redo()
        {
            inner.Redo();
            session.VoxelSelection.Restore(after);
        }

        public void Undo()
        {
            inner.Undo();
            session.VoxelSelection.Restore(before);
        }
    }

    private void PushEdit(ICommand command, Int3[] before)
    {
        History.Push(new EditModeCommand(this, command, before, [.. VoxelSelection.Cells]));
        HasUnsavedChanges = true;
    }

    /// <summary>Empties the chosen voxels. One undo step; the selection goes with them.</summary>
    public int DeleteSelectedVoxels()
    {
        if (EditObject is not { } edited || VoxelSelection.IsEmpty)
        {
            return 0;
        }

        EndStroke();
        CancelExtrude();

        Int3[] before = [.. VoxelSelection.Cells];
        var command = new VoxelEditCommand("Delete voxels", edited.Grid);
        foreach (Int3 cell in before)
        {
            command.Apply(cell, Palette.EmptyIndex);
        }

        VoxelSelection.Clear();
        Selection = null;
        PushEdit(command, before);
        return before.Length;
    }

    /// <summary>Colours the chosen voxels, faces and all, with the colour in hand. One undo step.</summary>
    public int FillSelectedVoxels()
    {
        if (EditObject is not { } edited || VoxelSelection.IsEmpty)
        {
            return 0;
        }

        EndStroke();
        byte colour = ActiveColorIndex == Palette.EmptyIndex ? Palette.WhiteIndex : ActiveColorIndex;
        var command = new VoxelEditCommand("Fill voxels", edited.Grid);

        foreach (Int3 cell in VoxelSelection.Cells)
        {
            byte under = edited.Grid.GetVoxel(cell);
            for (int f = 0; f < FaceInfo.Count; f++)
            {
                byte painted = edited.Grid.GetFaceColor(cell, (Face)f);
                if (painted != under)
                {
                    command.RecordFaceLost(cell, (Face)f, painted, colour);
                }
            }

            command.Apply(cell, colour);
        }

        if (command.IsEmpty)
        {
            return 0;
        }

        PushEdit(command, [.. VoxelSelection.Cells]);
        return VoxelSelection.Count;
    }

    /// <summary>
    /// Blender's P: the chosen voxels become an object of their own, where they were, and leave this
    /// one. The new object is selected beside it. One undo step.
    /// </summary>
    public VoxelObject? SeparateSelectedVoxels()
    {
        if (EditObject is not { } edited || VoxelSelection.IsEmpty)
        {
            return null;
        }

        EndStroke();
        CancelExtrude();

        Int3[] before = [.. VoxelSelection.Cells];
        VoxelWorld piece = ClipboardOperations.Extract(edited.Grid, before);

        var cut = new VoxelEditCommand("Separate", edited.Grid);
        foreach (Int3 cell in before)
        {
            cut.Apply(cell, Palette.EmptyIndex);
        }

        var create = new CreateObjectCommand(
            Scene,
            piece,
            edited.Transform,
            DuplicateName(edited.Name, Scene.Objects.Select(o => o.Name)),
            "Separate",
            Scene.IndexOf(edited.Id) + 1,
            Scene.ParentOf(edited)?.Id ?? 0);
        create.Redo();

        // Creating it took focus; Edit Mode stays in the object it was in.
        Scene.SetFocus(edited.Id);
        Scene.Select(create.Created!.Id);

        VoxelSelection.Clear();
        Selection = null;
        PushEdit(new CompositeCommand("Separate", [cut, create]), before);
        return create.Created;
    }

    /// <summary>The chosen voxels mirrored across one of the object's own axes, in place. One undo step.</summary>
    public bool MirrorSelectedVoxels(Axis axis)
    {
        (Int3 x, Int3 y, Int3 z) = VoxelSelecting.Mirror(axis);
        return TransformSelectedVoxels(x, y, z, Int3.Zero, $"Mirror voxels {axis}");
    }

    /// <summary>The chosen voxels turned a quarter at a time about one of the object's own axes, about their middle.</summary>
    public bool RotateSelectedVoxels(Axis axis, int quarterTurns)
    {
        (Int3 x, Int3 y, Int3 z) = VoxelSelecting.QuarterTurns(axis, quarterTurns);
        return TransformSelectedVoxels(x, y, z, Int3.Zero, "Turn voxels");
    }

    /// <summary>The chosen voxels moved by whole voxels. One undo step.</summary>
    public bool MoveSelectedVoxels(Int3 by) =>
        TransformSelectedVoxels(new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1), by, "Move voxels");

    /// <summary>
    /// A copy of the chosen voxels beside them, clear of them along the object's own axis that
    /// <paramref name="towards"/> — a direction in the world, the camera's right — leans on most.
    /// The copy is what is chosen afterwards. One undo step.
    /// </summary>
    public bool DuplicateSelectedVoxels(Vector3 towards)
    {
        if (EditObject is not { } edited || !VoxelSelection.TryGetBounds(out Int3 min, out Int3 max))
        {
            return false;
        }

        Vector3 local = edited.Transform.InverseTransformDirection(towards);
        Int3 size = max - min + Int3.One;
        Int3 by = MathF.Abs(local.X) >= MathF.Abs(local.Y) && MathF.Abs(local.X) >= MathF.Abs(local.Z)
            ? new Int3(MathF.Sign(local.X) * size.X, 0, 0)
            : MathF.Abs(local.Y) >= MathF.Abs(local.Z)
                ? new Int3(0, MathF.Sign(local.Y) * size.Y, 0)
                : new Int3(0, 0, MathF.Sign(local.Z) * size.Z);

        return TransformSelectedVoxels(new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1), by, "Duplicate voxels", keepSource: true);
    }

    private bool TransformSelectedVoxels(Int3 x, Int3 y, Int3 z, Int3 shift, string name, bool keepSource = false)
    {
        if (EditObject is null || VoxelSelection.IsEmpty || IsVoxelTransforming)
        {
            return false;
        }

        EndStroke();
        CancelExtrude();

        LatticeMap map = VoxelSelecting.About(VoxelSelection.Centre(), x, y, z, shift);
        _voxelDrag = new VoxelDrag([.. VoxelSelection.Cells], ClipboardOperations.Extract(EditObject.Grid, VoxelSelection.Cells), EditObject)
        {
            KeepSource = keepSource,
        };
        PreviewVoxelMap(map, name);
        return ConfirmVoxelTransform();
    }

    // ---- The gizmo on the chosen voxels --------------------------------------------------------

    /// <summary>A move or turn of the chosen voxels being dragged: what they were, and the preview written so far.</summary>
    private sealed record VoxelDrag(Int3[] Cells, VoxelWorld Piece, VoxelObject Owner)
    {
        /// <summary>A copy rather than a move: the voxels stay where they were, too.</summary>
        public bool KeepSource { get; init; }

        public VoxelEditCommand? Preview { get; set; }

        public LatticeMap? Map { get; set; }
    }

    private VoxelDrag? _voxelDrag;
    private VoxelSelectionHandle? _handle;

    public bool IsVoxelTransforming => _voxelDrag is not null;

    /// <summary>
    /// What the Transform tool holds in Edit Mode: the chosen voxels, at their middle, with the
    /// object's turn. Null with nothing chosen.
    /// </summary>
    public VoxelSelectionHandle? SelectionHandle
    {
        get
        {
            if (EditObject is not { } edited || VoxelSelection.IsEmpty)
            {
                _handle = null;
                return null;
            }

            if (_voxelDrag is null)
            {
                ObjectTransform rest = edited.Transform with { Position = edited.Transform.TransformPoint(VoxelSelection.Centre()) };
                if (_handle is null || _handle.Owner != edited || _handle.Rest != rest)
                {
                    _handle = new VoxelSelectionHandle(edited, rest);
                }
            }

            return _handle;
        }
    }

    /// <summary>
    /// The gizmo moved the handle: into whole voxels along the object's own axes and quarter turns
    /// about them, previewed in the object — the last preview taken back first.
    /// </summary>
    private void DragSelectionHandle(VoxelSelectionHandle handle, ObjectTransform moved)
    {
        // The gizmo follows the pointer; the voxels follow it in whole steps.
        handle.Transform = moved;
        _handle = handle;
        VoxelObject owner = handle.Owner;
        ObjectTransform rest = handle.Rest;

        Vector3 local = owner.Transform.InverseTransformDirection(moved.Position - rest.Position) / owner.VoxelSize;
        var shift = new Int3((int)MathF.Round(local.X), (int)MathF.Round(local.Y), (int)MathF.Round(local.Z));

        // The turn in the object's own frame, to the nearest quarter about its nearest axis.
        Quaternion turn = Quaternion.Conjugate(owner.Transform.Rotation) * moved.Rotation * Quaternion.Conjugate(rest.Rotation) * owner.Transform.Rotation;
        (Int3 x, Int3 y, Int3 z) = NearestQuarterTurn(turn);
        bool turned = x != new Int3(1, 0, 0) || y != new Int3(0, 1, 0);

        // Back where it started: nothing is being moved, which is also how a cancelled drag ends.
        if (shift == Int3.Zero && !turned)
        {
            _voxelDrag?.Preview?.Undo();
            _voxelDrag = null;
            return;
        }

        _voxelDrag ??= new VoxelDrag([.. VoxelSelection.Cells], ClipboardOperations.Extract(owner.Grid, VoxelSelection.Cells), owner);
        PreviewVoxelMap(VoxelSelecting.About(VoxelSelection.Centre(), x, y, z, shift), turned ? "Turn voxels" : "Move voxels");
    }

    private static (Int3 X, Int3 Y, Int3 Z) NearestQuarterTurn(Quaternion turn)
    {
        turn = Quaternion.Normalize(turn);
        float angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(turn.W), 0f, 1f));
        int quarters = (int)MathF.Round(angle / (MathF.PI / 2f));
        if (quarters == 0)
        {
            return (new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1));
        }

        // The axis, signed so the angle is the short way round.
        Vector3 axis = new Vector3(turn.X, turn.Y, turn.Z) * MathF.Sign(turn.W == 0f ? 1f : turn.W);
        Axis nearest = MathF.Abs(axis.X) >= MathF.Abs(axis.Y) && MathF.Abs(axis.X) >= MathF.Abs(axis.Z)
            ? Axis.X
            : MathF.Abs(axis.Y) >= MathF.Abs(axis.Z) ? Axis.Y : Axis.Z;
        float sign = nearest switch
        {
            Axis.X => MathF.Sign(axis.X),
            Axis.Y => MathF.Sign(axis.Y),
            _ => MathF.Sign(axis.Z),
        };

        return VoxelSelecting.QuarterTurns(nearest, quarters * (int)sign);
    }

    private void PreviewVoxelMap(LatticeMap map, string name)
    {
        if (_voxelDrag is not { } drag)
        {
            return;
        }

        drag.Preview?.Undo();
        drag.Preview = null;
        drag.Map = null;

        bool identity = map.Origin == Int3.Zero && map.X == new Int3(1, 0, 0) && map.Y == new Int3(0, 1, 0) && map.Z == new Int3(0, 0, 1);
        if (identity)
        {
            return;
        }

        var command = new VoxelEditCommand(name, drag.Owner.Grid);
        if (!drag.KeepSource)
        {
            foreach (Int3 cell in drag.Cells)
            {
                command.Apply(cell, Palette.EmptyIndex);
            }
        }

        ClipboardOperations.WriteInto(drag.Piece, map, command);
        drag.Preview = command;
        drag.Map = map;
        HasUnsavedChanges = true;
    }

    /// <summary>Ends a voxel drag: keeps what it did as one undo step, the selection moved with the voxels.</summary>
    private bool ConfirmVoxelTransform()
    {
        if (_voxelDrag is not { } drag)
        {
            return false;
        }

        _voxelDrag = null;
        if (_handle is not null)
        {
            _handle.Transform = _handle.Rest;
        }

        if (drag.Preview is not { IsEmpty: false } preview || drag.Map is not { } map)
        {
            drag.Preview?.Undo();
            return false;
        }

        _handle = null;

        VoxelSelection.Restore(drag.Cells.Select(map.Cell));
        PushEdit(preview, drag.Cells);
        return true;
    }

    /// <summary>Throws a voxel drag away, putting the voxels back where they were.</summary>
    public void CancelVoxelTransform()
    {
        if (_handle is not null)
        {
            _handle.Transform = _handle.Rest;
        }

        if (_voxelDrag is not { } drag)
        {
            return;
        }

        drag.Preview?.Undo();
        _voxelDrag = null;
    }

    /// <summary>Ends a drag of the gizmo on the chosen voxels: what it did becomes one undo step.</summary>
    private bool EndSelectionDrag()
    {
        if (_voxelDrag is null)
        {
            CancelVoxelTransform();
            return false;
        }

        return ConfirmVoxelTransform();
    }
}
