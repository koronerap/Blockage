using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// A linked copy given voxels of its own (Fullreleaseplan 6.2, Blender's Make Single User): from
/// here on an edit to it is an edit to it alone. Undone, it shares the grid it had again.
/// </summary>
public sealed class MakeSingleUserCommand(VoxelObject target) : ICommand
{
    private readonly VoxelWorld _shared = target.Grid;
    private VoxelWorld? _own;

    public string Name => $"Make {target.Name} a Single User";

    public int RetainedCells => _shared.SolidCount;

    public void Redo() => target.Grid = _own ??= _shared.Copy();

    public void Undo() => target.Grid = _shared;
}
