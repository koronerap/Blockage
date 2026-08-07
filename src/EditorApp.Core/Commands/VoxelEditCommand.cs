using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// A batch of voxel writes recorded as it happens. One command covers one user gesture — a click,
/// a whole brush drag, a bucket fill — so undo reverses what the user perceives as one action.
/// </summary>
public sealed class VoxelEditCommand(string name) : ICommand
{
    private readonly record struct CellChange(Int3 Position, byte Before, byte After);

    private readonly List<CellChange> _changes = [];
    private readonly HashSet<Int3> _touched = [];

    public string Name { get; } = name;

    public int RetainedCells => _changes.Count;

    public bool IsEmpty => _changes.Count == 0;

    /// <summary>
    /// Writes a voxel and records the change. Returns false when nothing changed. A cell touched
    /// more than once inside the same command keeps its original "before" value, so undoing a drag
    /// that crossed itself still restores the state from before the drag.
    /// </summary>
    public bool Apply(VoxelWorld world, Int3 position, byte paletteIndex)
    {
        byte before = world.GetVoxel(position);
        if (before == paletteIndex)
        {
            return false;
        }

        if (!world.SetVoxel(position, paletteIndex))
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

    public void Redo(VoxelWorld world)
    {
        foreach (CellChange change in _changes)
        {
            world.SetVoxel(change.Position, change.After);
        }
    }

    public void Undo(VoxelWorld world)
    {
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            CellChange change = _changes[i];
            world.SetVoxel(change.Position, change.Before);
        }
    }
}
