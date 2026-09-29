using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>
/// Makes something the child of an object, or of nothing, keeping it where it is. Undo puts back the
/// parent it had — even one deleted since, so undoing that delete finds its child again.
/// </summary>
public sealed class ParentCommand(VoxelScene scene, IPlaceable child, int parentId, string name) : ICommand
{
    private readonly ParentRecord _before = new();

    public string Name { get; } = name;

    public int RetainedCells => 1;

    public void Redo()
    {
        _before.Capture([child]);
        scene.SetParent(child.Id, parentId);
    }

    public void Undo() => _before.Restore();
}
