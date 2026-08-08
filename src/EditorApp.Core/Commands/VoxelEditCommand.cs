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
    private readonly record struct CellChange(Int3 Position, byte Before, byte After);

    private readonly List<CellChange> _changes = [];
    private readonly HashSet<Int3> _touched = [];

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
        if (before == paletteIndex)
        {
            return false;
        }

        if (!Target.SetVoxel(position, paletteIndex))
        {
            return false;
        }

        if (_touched.Add(position))
        {
            _changes.Add(new CellChange(position, before, paletteIndex));
        }
        else
        {
            // Already recorded: keep the original before, update the after.
            for (int i = _changes.Count - 1; i >= 0; i--)
            {
                if (_changes[i].Position == position)
                {
                    _changes[i] = _changes[i] with { After = paletteIndex };
                    break;
                }
            }
        }

        return true;
    }

    public void Redo()
    {
        foreach (CellChange change in _changes)
        {
            Target.SetVoxel(change.Position, change.After);
        }
    }

    public void Undo()
    {
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            CellChange change = _changes[i];
            Target.SetVoxel(change.Position, change.Before);
        }
    }
}
