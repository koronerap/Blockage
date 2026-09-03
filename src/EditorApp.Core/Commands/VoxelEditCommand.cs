using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// A batch of voxel writes recorded as it happens. One command covers one user gesture — a click,
/// a whole brush drag, a bucket fill — so undo reverses what the user perceives as one action.
///
/// The grid is fixed when the command is created. Undo must put the voxels back where they came
/// from even if focus has moved to another object since.
/// </summary>
public sealed class VoxelEditCommand(string name, VoxelWorld target) : ICommand
{
    /// <summary>
    /// One recorded write. <paramref name="Face"/> is null for a whole-voxel change and set when a
    /// single face was painted, so undo puts back exactly what the gesture replaced.
    /// </summary>
    private readonly record struct CellChange(Int3 Position, Face? Face, byte Before, byte After);

    private readonly List<CellChange> _changes = [];
    private readonly HashSet<(Int3 Position, Face? Face)> _touched = [];

    public string Name { get; } = name;

    /// <summary>The grid this command reads and writes. Operations use it so the two cannot diverge.</summary>
    public VoxelWorld Target { get; } = target;

    public int RetainedCells => _changes.Count;

    public bool IsEmpty => _changes.Count == 0;

    /// <summary>
    /// The cells this command wrote and what it put there. Extrude's Create sub-mode uses it to
    /// lift the voxels it just made into an object of their own.
    /// </summary>
    public IEnumerable<(Int3 Position, byte Value)> Written()
    {
        foreach (CellChange change in _changes)
        {
            yield return (change.Position, change.After);
        }
    }

    /// <summary>
    /// Writes a voxel and records the change. Returns false when nothing changed. A cell touched
    /// more than once inside the same command keeps its original "before" value, so undoing a drag
    /// that crossed itself still restores the state from before the drag.
    /// </summary>
    public bool Apply(Int3 position, byte paletteIndex)
    {
        byte before = Target.GetVoxel(position);
        if (before == paletteIndex || !Target.SetVoxel(position, paletteIndex))
        {
            return false;
        }

        Record(position, face: null, before, paletteIndex);
        return true;
    }

    /// <summary>
    /// Paints a single face. Returns false when nothing changed.
    /// </summary>
    public bool ApplyFace(Int3 position, Face face, byte paletteIndex)
    {
        byte before = Target.GetFaceColor(position, face);
        if (before == paletteIndex || !Target.SetFaceColor(position, face, paletteIndex))
        {
            return false;
        }

        Record(position, face, before, paletteIndex);
        return true;
    }

    /// <summary>
    /// Records a face colour that is about to be lost because the voxel underneath is being
    /// recoloured, without writing anything itself.
    ///
    /// Setting a voxel's colour forgets what was painted on its faces, which is deliberate — the
    /// overrides described the colour it used to be. Undo still has to put them back, and by the
    /// time the write has happened they are gone, so they are recorded first. Undo replays in
    /// reverse, so recording the faces BEFORE the voxel is what makes it restore the voxel first and
    /// the faces after: the other order would have the voxel's own write wipe the faces it had just
    /// restored.
    /// </summary>
    public void RecordFaceLost(Int3 position, Face face, byte before, byte after)
    {
        if (before != after)
        {
            Record(position, face, before, after);
        }
    }

    private void Record(Int3 position, Face? face, byte before, byte after)
    {
        if (_touched.Add((position, face)))
        {
            _changes.Add(new CellChange(position, face, before, after));
            return;
        }

        // Already recorded: keep the original before, update the after, so undoing a drag that
        // crossed itself still restores the state from before the drag.
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (_changes[i].Position == position && _changes[i].Face == face)
            {
                _changes[i] = _changes[i] with { After = after };
                break;
            }
        }
    }

    public void Redo()
    {
        foreach (CellChange change in _changes)
        {
            Write(change, change.After);
        }
    }

    public void Undo()
    {
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            Write(_changes[i], _changes[i].Before);
        }
    }

    private void Write(CellChange change, byte value)
    {
        if (change.Face is { } face)
        {
            Target.SetFaceColor(change.Position, face, value);
        }
        else
        {
            Target.SetVoxel(change.Position, value);
        }
    }
}
