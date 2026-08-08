using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>
/// Moves or rotates a whole object. Costs almost nothing to store — a placement, not voxels — so it
/// barely touches the undo stack's cell budget.
/// </summary>
public sealed class TransformCommand(
    VoxelObject target,
    ObjectTransform before,
    ObjectTransform after,
    string name) : ICommand
{
    public string Name { get; } = name;

    public int RetainedCells => 1;

    public void Redo() => target.Transform = after;

    public void Undo() => target.Transform = before;
}
