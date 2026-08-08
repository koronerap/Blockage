using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>
/// Removes an object from the scene, reversibly. The object itself is kept rather than copied, so
/// undo puts back the very same instance and anything still referencing it stays correct.
/// </summary>
public sealed class DeleteObjectCommand(VoxelScene scene, VoxelObject target) : ICommand
{
    private int _previousFocusId;

    public string Name => $"Delete {target.Name}";

    public int RetainedCells { get; } = target.Grid.SolidCount;

    public void Redo()
    {
        _previousFocusId = scene.FocusId;
        scene.Remove(target.Id);
    }

    public void Undo()
    {
        scene.Restore(target);
        scene.SetFocus(_previousFocusId == 0 ? target.Id : _previousFocusId);
    }
}
