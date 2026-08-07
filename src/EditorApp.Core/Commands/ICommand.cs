using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// A reversible edit (EditorApp.md §8). Commands carry the before/after state of everything they
/// touched, and report how many cells that costs so the undo stack can be trimmed by memory rather
/// than by operation count.
/// </summary>
public interface ICommand
{
    /// <summary>Shown in the UI next to Undo/Redo.</summary>
    string Name { get; }

    /// <summary>How many cells of state this command holds onto.</summary>
    int RetainedCells { get; }

    void Redo(VoxelWorld world);

    void Undo(VoxelWorld world);
}
