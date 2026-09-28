using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>
/// Moves, rotates or resizes a whole object, or moves and aims a light. Costs almost nothing to
/// store — a placement, not voxels — so it barely touches the undo stack's cell budget.
/// </summary>
public sealed class TransformCommand(
    IPlaceable target,
    ObjectTransform before,
    ObjectTransform after,
    string name) : ICommand
{
    public string Name { get; } = name;

    public int RetainedCells => 1;

    public void Redo() => target.Transform = after;

    public void Undo() => target.Transform = before;
}
