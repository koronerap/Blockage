using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>
/// Removes an object from the scene, reversibly. The object itself is kept rather than copied, so
/// undo puts back the very same instance and anything still referencing it stays correct — in the
/// same place in the list it was taken from.
/// </summary>
public sealed class DeleteObjectCommand(VoxelScene scene, VoxelObject target) : ICommand
{
    private int _previousFocusId;
    private int _index = -1;
    private bool _wasSelected;

    public string Name => $"Delete {target.Name}";

    public int RetainedCells { get; } = target.Grid.SolidCount;

    public void Redo()
    {
        _previousFocusId = scene.FocusId;
        _index = scene.IndexOf(target.Id);
        _wasSelected = scene.IsSelected(target.Id);
        scene.Remove(target.Id);
    }

    public void Undo()
    {
        scene.Restore(target, _index >= 0 ? _index : null);
        scene.SetFocus(_previousFocusId == 0 ? target.Id : _previousFocusId);

        // Back as it went: a selected object comes back selected.
        if (_wasSelected)
        {
            scene.Select(target.Id);
        }
    }
}
