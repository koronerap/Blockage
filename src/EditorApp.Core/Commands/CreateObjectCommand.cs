using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// Adds a new object to the scene, reversibly. Extrude's Create sub-mode uses it: the voxels it
/// pulled out become a separate object instead of joining the one they came from, which is the
/// "extrude and loop cut in one motion" the spec describes.
/// </summary>
public sealed class CreateObjectCommand(
    VoxelScene scene,
    VoxelWorld grid,
    ObjectTransform transform,
    string name) : ICommand
{
    private VoxelObject? _created;
    private int _previousFocusId;

    public string Name => "Create object";

    public int RetainedCells { get; } = grid.SolidCount;

    public VoxelObject? Created => _created;

    public void Redo()
    {
        _previousFocusId = scene.FocusId;

        if (_created is null)
        {
            _created = scene.Add(grid, transform, name);
        }
        else
        {
            scene.Restore(_created);
        }

        scene.SetFocus(_created.Id);
    }

    public void Undo()
    {
        if (_created is null)
        {
            return;
        }

        scene.Remove(_created.Id);
        scene.SetFocus(_previousFocusId);
    }
}
